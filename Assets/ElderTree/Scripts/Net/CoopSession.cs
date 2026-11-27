using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Managing.Transporting;
using FishNet.Transporting;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ElderTree.Net
{
    /// <summary>
    /// Online co-op for up to four players: Multiplayer Services sessions (create, join by code, browse) carried over
    /// Unity Relay, with FishNet as the netcode. One of these lives for the whole run (DontDestroyOnLoad) once the
    /// CO-OP page is opened.
    /// <para>The flow: a party forms in a lobby (the session); the host starts the vote; everyone votes on a stage (a
    /// first vote starts a 15 s countdown, a unanimous vote goes at once, the most votes win, ties and Random are
    /// drawn); everyone loads the stage; after the fight (and each player's own mask) the party votes again.</para>
    /// <para>The server side of the host only routes messages and keeps the session state (roster, phase, votes).
    /// Gameplay runs on the peers: see <see cref="NetStage"/>.</para>
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public partial class CoopSession : MonoBehaviour
    {
        public const int MaxPlayers = 4;
        public const float VoteSeconds = 15f;
        public const string SessionType = "eldertree-coop";

        /// <summary>
        /// Testing aid: Relay over plain UDP instead of DTLS (command line <c>-coopUdp</c>). DTLS needs Burst-compiled
        /// native callbacks, which a Unity Editor only has after a restart once Burst has been installed.
        /// </summary>
        public static bool UseUdpRelay = System.Array.IndexOf(Environment.GetCommandLineArgs(), "-coopUdp") >= 0;

        static NetworkOptions RelayOptions => new NetworkOptions { RelayProtocol = UseUdpRelay ? RelayProtocol.UDP : RelayProtocol.DTLS };

        public enum Phase : byte { None = 0, Lobby = 1, Vote = 2, Stage = 3 }
        public enum Busy { None, SigningIn, Hosting, Joining, Leaving }

        public static CoopSession Instance { get; private set; }

        // ------------------------------------------------------------------ what the menu reads
        public Busy Working { get; private set; }
        public string Error { get; private set; }
        public bool Connected => session != null && nm != null && nm.ClientManager.Started && LocalId >= 0;
        public bool IsHost => session != null && session.IsHost;
        public string Code => session?.Code ?? "";
        public string SessionName => session?.Name ?? "";
        public Phase CurrentPhase { get; private set; }
        public int LocalId => nm != null && nm.ClientManager.Connection != null ? nm.ClientManager.Connection.ClientId : -1;
        public string PlayerName { get; private set; } = "Tree";
        /// <summary>Raised whenever anything the menu shows changed (roster, phase, votes, errors).</summary>
        public event Action Changed;

        // vote state (as the server last said)
        public readonly Dictionary<int, int> Votes = new();
        /// <summary>Seconds left on the countdown (negative: not running).</summary>
        public float VoteRemaining => voteDeadline < 0f ? -1f : Mathf.Max(0f, voteDeadline - Time.realtimeSinceStartup);
        /// <summary>The stage the vote settled on (-1 none yet).</summary>
        public int VotePick { get; private set; } = -1;
        public int StageIndex { get; private set; } = -1;
        public const int VoteRandom = 200, VoteNone = 255;

        // ------------------------------------------------------------------ internals
        NetworkManager nm;
        RelayTransport transport;
        CoopNetworkHandler handler;
        ISession session;
        float voteDeadline = -1f;
        float clockOffset;               // server clock - local clock (for the countdown)
        readonly NetWriter w = new();

        StageCatalog catalog;
        List<StageCatalog.Stage> Stages => catalog != null ? catalog.stages : new List<StageCatalog.Stage>();

        // ================================================================== boot
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register()
        {
            UI.CoopMenuHook.Create = (root, host, cat) => new CoopMenu(root, host, cat);
        }

        /// <summary>The session manager (made on first use; lives across scenes).</summary>
        public static CoopSession Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("Coop Session");
            DontDestroyOnLoad(go);
            return go.AddComponent<CoopSession>();
        }

        void Awake()
        {
            Instance = this;
            catalog = StageCatalog.Instance;
            BuildNetworkManager();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this) Instance = null;
        }

        /// <summary>Resources prefab: NetworkManager + TransportManager + <see cref="RelayTransport"/> (tune FishNet there).</summary>
        public const string NetworkManagerPrefab = "CoopNetworkManager";

        void BuildNetworkManager()
        {
            var prefab = Resources.Load<GameObject>(NetworkManagerPrefab);
            if (prefab != null)
            {
                var inst = Instantiate(prefab);                      // a root object: FishNet keeps it across scenes itself
                inst.name = "Coop NetworkManager";
                nm = inst.GetComponent<NetworkManager>();
                transport = inst.GetComponent<RelayTransport>();
            }
            if (nm == null || transport == null)
            {
                // no prefab: build the same thing by hand
                var go = new GameObject("Coop NetworkManager");
                go.SetActive(false);
                transport = go.AddComponent<RelayTransport>();
                var tm = go.AddComponent<TransportManager>();
                tm.Transport = transport;
                nm = go.AddComponent<NetworkManager>();
                nm.SpawnablePrefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
                go.SetActive(true);
            }
            transport.maximumClients = MaxPlayers;
            nm.ServerManager.SetFrameRate(120);
            nm.ClientManager.SetFrameRate(120);
            handler = new CoopNetworkHandler(nm, transport);

            nm.ServerManager.RegisterBroadcast<CoopPacket>(OnServerPacket, false);
            nm.ClientManager.RegisterBroadcast<CoopPacket>(OnClientPacket);
            nm.ServerManager.OnRemoteConnectionState += OnRemoteConnection;
            nm.ClientManager.OnClientConnectionState += OnClientConnection;
            nm.ClientManager.OnAuthenticated += OnAuthenticated;
        }

        void Notify() => Changed?.Invoke();

        void Fail(string message)
        {
            Error = message;
            Working = Busy.None;
            Debug.LogWarning("[Coop] " + message);
            Notify();
        }

        // ================================================================== services
        static string Profile()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-coopProfile") return Clean(args[i + 1]);
#if UNITY_EDITOR
            // each editor (and each Multiplayer Play Mode player / project clone) signs in as its own player
            return "ed" + ((uint)Application.dataPath.GetHashCode()).ToString("x8");
#else
            return null;
#endif
        }

        static string Clean(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s) if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            return sb.Length == 0 ? null : sb.ToString(0, Math.Min(sb.Length, 30));
        }

        Task<bool> signingIn;

        /// <summary>Sign in once; callers that arrive while it runs share the same attempt.</summary>
        Task<bool> SignIn()
        {
            if (signingIn == null || (signingIn.IsCompleted && !signingIn.Result)) signingIn = SignInOnce();
            return signingIn;
        }

        async Task<bool> SignInOnce()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    var options = new InitializationOptions();
                    var profile = Profile();
                    if (profile != null) options.SetProfile(profile);
                    await UnityServices.InitializeAsync(options);
                }
                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                try
                {
                    var n = await AuthenticationService.Instance.GetPlayerNameAsync();
                    if (!string.IsNullOrEmpty(n)) PlayerName = n.Split('#')[0];
                }
                catch (Exception) { /* a name is nice to have */ }
                return true;
            }
            catch (Exception e)
            {
                Fail("Could not reach the online services. " + Short(e));
                return false;
            }
        }

        static string Short(Exception e)
        {
            Debug.LogException(e);
            // the session wraps what actually went wrong: show the innermost reason
            var inner = e;
            while (inner.InnerException != null) inner = inner.InnerException;
            var m = inner.Message;
            if (string.IsNullOrEmpty(m)) m = e.Message;
            if (string.IsNullOrEmpty(m)) return "";
            return m.Length > 140 ? m.Substring(0, 140) + "…" : m;
        }

        // ================================================================== create / join / browse / leave
        public async void Host()
        {
            if (Working != Busy.None || session != null) return;
            Error = null;
            Working = Busy.SigningIn; Notify();
            if (!await SignIn()) return;
            Working = Busy.Hosting; Notify();
            try
            {
                var options = new SessionOptions
                {
                    MaxPlayers = MaxPlayers,
                    Name = $"{PlayerName}'s grove",
                    Type = SessionType,
                    IsPrivate = false,
                }.WithRelayNetwork().WithNetworkOptions(RelayOptions).WithNetworkHandler(handler);
                session = await MultiplayerService.Instance.CreateSessionAsync(options);
                Hook(session);
                Working = Busy.None;
                Notify();
            }
            catch (Exception e)
            {
                await StopNetwork();
                session = null;
                Fail("Could not host a party. " + Short(e));
            }
        }

        public async void JoinByCode(string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length == 0) { Fail("Enter a party code."); return; }
            await Join(() => MultiplayerService.Instance.JoinSessionByCodeAsync(code, JoinOptions()));
        }

        public async void JoinById(string id) => await Join(() => MultiplayerService.Instance.JoinSessionByIdAsync(id, JoinOptions()));

        JoinSessionOptions JoinOptions() => new JoinSessionOptions { Type = SessionType }.WithNetworkOptions(RelayOptions).WithNetworkHandler(handler);

        async Task Join(Func<Task<ISession>> join)
        {
            if (Working != Busy.None || session != null) return;
            Error = null;
            Working = Busy.SigningIn; Notify();
            if (!await SignIn()) return;
            Working = Busy.Joining; Notify();
            try
            {
                session = await join();
                Hook(session);
                Working = Busy.None;
                Notify();
            }
            catch (Exception e)
            {
                await StopNetwork();
                session = null;
                Fail("Could not join that party. " + Short(e));
            }
        }

        /// <summary>The open parties are being listed (in the background: hosting and joining never wait on it).</summary>
        public bool Browsing { get; private set; }

        public async Task<IList<ISessionInfo>> Browse()
        {
            if (Browsing || session != null) return Array.Empty<ISessionInfo>();
            Browsing = true;
            Notify();
            try
            {
                if (!await SignIn()) return Array.Empty<ISessionInfo>();
                var q = new QuerySessionsOptions { Count = 20 };
                q.FilterOptions.Add(new FilterOption(FilterField.AvailableSlots, "0", FilterOperation.Greater));
                q.FilterOptions.Add(new FilterOption(FilterField.IsLocked, "false", FilterOperation.Equal));
                q.SortOptions.Add(new SortOption(SortOrder.Descending, SortField.CreationTime));
                var res = await MultiplayerService.Instance.QuerySessionsAsync(q);
                return res.Sessions;
            }
            catch (Exception e)
            {
                // a busy moment (rate limit, a blip): keep quiet, the page asks again shortly
                Debug.LogWarning("[Coop] listing parties failed: " + e.Message);
                return Array.Empty<ISessionInfo>();
            }
            finally
            {
                Browsing = false;
                Notify();
            }
        }

        void Hook(ISession s)
        {
            s.RemovedFromSession += () => OnLostSession("You were removed from the party.");
            s.Deleted += () => OnLostSession("The party was closed.");
            Coop.Active = true;
            Coop.IsHost = s.IsHost;
            Coop.Bridge = this;
            CurrentPhase = Phase.Lobby;
        }

        /// <summary>Leave the party (and the stage, if in one): back to the main menu, solo.</summary>
        public async void Leave(string reason = null)
        {
            if (Working == Busy.Leaving) return;
            Working = Busy.Leaving;
            Notify();
            var s = session;
            session = null;
            try { if (s != null) await s.LeaveAsync(); }
            catch (Exception e) { Debug.LogWarning("[Coop] leave: " + e.Message); }
            await StopNetwork();
            ResetState();
            Working = Busy.None;
            Error = reason;
            Notify();
            if (SceneManager.GetActiveScene().name != SceneFlow.MainMenu) SceneFlow.Load(SceneFlow.MainMenu);
        }

        void OnLostSession(string why)
        {
            if (session == null) return;
            Leave(why);
        }

        async Task StopNetwork()
        {
            try { await handler.StopAsync(); } catch (Exception) { }
        }

        void ResetState()
        {
            Coop.Active = false;
            Coop.InStage = false;
            Coop.IsHost = true;
            Coop.Bridge = null;
            Coop.Party.Clear();
            Coop.RaisePartyChanged();
            CurrentPhase = Phase.None;
            Votes.Clear();
            voteDeadline = -1f;
            VotePick = -1;
            StageIndex = -1;
            roster.Clear();
            loaded.Clear();
            done.Clear();
            svVotes.Clear();
            svPhase = Phase.Lobby;
            svStage = svPick = -1;
            svDeadline = svFirstDoneAt = -1f;
        }

        // ================================================================== connection events
        void OnAuthenticated()
        {
            // introduce ourselves: the server answers with the roster and the phase
            w.Reset();
            w.Str(PlayerName);
            SendToServer(Msg.Hello, w.ToArray(), Channel.Reliable);
        }

        void OnClientConnection(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Stopped && session != null && Working != Busy.Leaving && Working != Busy.Hosting && Working != Busy.Joining)
                Leave(IsHost ? "The party ended." : "Lost the connection to the host.");
        }

        void OnRemoteConnection(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            if (roster.RemoveAll(r => r.id == conn.ClientId) > 0)
            {
                svVotes.Remove(conn.ClientId);
                loaded.Remove(conn.ClientId);
                done.Remove(conn.ClientId);
                ServerBroadcastRoster();
                if (svPhase == Phase.Vote) { ServerEvaluateVote(false); ServerBroadcastVote(); }
                if (svPhase == Phase.Stage) ServerCheckDone();
            }
        }

        // ================================================================== sending
        void SendToServer(Msg kind, byte[] data, Channel ch, int to = -1)
        {
            if (nm == null || !nm.ClientManager.Started) return;
            nm.ClientManager.Broadcast(new CoopPacket { kind = (byte)kind, from = -1, to = to, data = data }, ch);
        }

        /// <summary>Send to the other players (or one of them): the server relays it.</summary>
        public void Send(Msg kind, byte[] data, bool reliable, int to = -1) =>
            SendToServer(kind, data, reliable ? Channel.Reliable : Channel.Unreliable, to);

        // ================================================================== client side: what the server says
        void OnClientPacket(CoopPacket p, Channel ch)
        {
            var r = new NetReader(p.data);
            switch ((Msg)p.kind)
            {
                case Msg.Roster: ReadRoster(ref r); break;
                case Msg.Phase: ReadPhase(ref r); break;
                case Msg.VoteState: ReadVote(ref r); break;
                default:
                    if (p.from == LocalId && p.from >= 0) return;          // never our own echo
                    NetStage.Instance?.Receive((Msg)p.kind, p.from, ref r);
                    break;
            }
        }

        void ReadRoster(ref NetReader r)
        {
            int n = r.Byte();
            var keep = new Dictionary<int, Coop.Member>();
            foreach (var m in Coop.Party) keep[m.id] = m;
            Coop.Party.Clear();
            for (int i = 0; i < n; i++)
            {
                int id = r.Int();
                int slot = r.Byte();
                string name = r.Str();
                bool host = r.Bool();
                if (!keep.TryGetValue(id, out var m)) m = new Coop.Member { id = id };
                m.slot = slot;
                m.name = name;
                m.isHost = host;
                m.isLocal = id == LocalId;
                Coop.Party.Add(m);
            }
            Coop.Party.Sort((a, b) => a.slot.CompareTo(b.slot));
            Coop.RaisePartyChanged();
            NetStage.Instance?.OnRosterChanged();
            Notify();
        }

        void ReadPhase(ref NetReader r)
        {
            var ph = (Phase)r.Byte();
            int stage = r.Byte();
            if (stage == 255) stage = -1;
            var prev = CurrentPhase;
            CurrentPhase = ph;
            switch (ph)
            {
                case Phase.Lobby:
                    Votes.Clear(); voteDeadline = -1f; VotePick = -1;
                    break;
                case Phase.Vote:
                    Votes.Clear(); voteDeadline = -1f; VotePick = -1;
                    if (prev == Phase.Stage || SceneManager.GetActiveScene().name != SceneFlow.MainMenu)
                    {
                        // back from the stage: everyone to the vote screen together
                        Coop.InStage = false;
                        SceneFlow.Load(SceneFlow.MainMenu);
                    }
                    break;
                case Phase.Stage:
                    StageIndex = stage;
                    if (stage >= 0 && stage < Stages.Count) StartCoroutine(GoToStage(Stages[stage]));
                    break;
            }
            Notify();
        }

        /// <summary>The party is leaving for a stage (the vote screen plays the travel card, then <see cref="Travel"/>).</summary>
        public event Action<StageCatalog.Stage> StageRequested;

        /// <summary>Load the stage the party is going to.</summary>
        public void Travel(StageCatalog.Stage s)
        {
            if (Coop.InStage) return;
            Coop.InStage = true;
            SceneFlow.Load(s.scene, s.bossArg);
        }

        /// <summary>The CO-OP page plays the travel card and then travels; if no page is showing, go anyway.</summary>
        System.Collections.IEnumerator GoToStage(StageCatalog.Stage s)
        {
            StageRequested?.Invoke(s);
            float t = 0f;
            while (!Coop.InStage && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            if (!Coop.InStage && CurrentPhase == Phase.Stage) Travel(s);
        }

        void ReadVote(ref NetReader r)
        {
            Votes.Clear();
            int n = r.Byte();
            for (int i = 0; i < n; i++)
            {
                int id = r.Int();
                int v = r.Byte();
                Votes[id] = v;
            }
            float remaining = r.Float();
            voteDeadline = remaining < 0f ? -1f : Time.realtimeSinceStartup + remaining;
            int pick = r.Byte();
            VotePick = pick == 255 ? -1 : pick;
            Notify();
        }

        // ================================================================== menu actions
        /// <summary>Host, from the party room: everyone to the stage vote.</summary>
        public void StartVote() { if (IsHost) SendToServer(Msg.StartVote, null, Channel.Reliable); }

        /// <summary>Vote for a stage (index), <see cref="VoteRandom"/>, or <see cref="VoteNone"/> to take the vote back.</summary>
        public void CastVote(int option)
        {
            w.Reset();
            w.Byte((byte)option);
            SendToServer(Msg.Vote, w.ToArray(), Channel.Reliable);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Coop.Active) return;
            if (Coop.InStage && scene.name != SceneFlow.MainMenu)
            {
                var go = new GameObject("Coop Stage");
                go.AddComponent<NetStage>().Init(this);
            }
        }

        /// <summary>The stage finished loading here.</summary>
        internal void ReportLoaded() => SendToServer(Msg.Loaded, null, Channel.Reliable);

        void Update()
        {
            if (nm != null && nm.ServerManager.Started) ServerTick();
        }
    }
}
