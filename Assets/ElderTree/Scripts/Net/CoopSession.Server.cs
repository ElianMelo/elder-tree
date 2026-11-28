using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;

namespace ElderTree.Net
{
    /// <summary>
    /// The host's server half: who is in the party (and in which slot), where the party is (lobby, vote, stage),
    /// the vote, and message routing. It runs no gameplay.
    /// <para>Vote rules: a player votes for a stage or Random, and may take the vote back. The first vote starts a
    /// <see cref="VoteSeconds"/> countdown. When every player has voted for the same thing, it is decided at once.
    /// When the countdown runs out, the option with the most votes wins (ties are drawn among the leaders); Random
    /// winning draws a boss stage. Taking back every vote stops the countdown.</para>
    /// </summary>
    public partial class CoopSession
    {
        class RosterEntry { public int id; public int slot; public string name; }

        readonly List<RosterEntry> roster = new();
        readonly Dictionary<int, int> svVotes = new();
        readonly HashSet<int> loaded = new(), done = new();
        Phase svPhase = Phase.Lobby;
        float svDeadline = -1f, svPickAt, svStageAt, svFirstDoneAt = -1f;
        int svPick = -1, svStage = -1;

        int HostConnId => nm.ClientManager.Connection != null ? nm.ClientManager.Connection.ClientId : -1;

        void OnServerPacket(NetworkConnection conn, CoopPacket p, Channel ch)
        {
            int from = conn.ClientId;
            var r = new NetReader(p.data);
            switch ((Msg)p.kind)
            {
                case Msg.Hello: ServerHello(conn, r.Str()); return;
                case Msg.StartVote:
                    if (from == HostConnId && svPhase == Phase.Lobby) ServerSetPhase(Phase.Vote);
                    return;
                case Msg.BackToVote:
                    // not while the party is claiming the spoils: some are done, others still have a mask to pick up
                    if (from == HostConnId && svPhase == Phase.Stage && (done.Count == 0 || ServerAllDone)) ServerSetPhase(Phase.Vote);
                    return;
                case Msg.Vote: ServerVote(from, r.Byte()); return;
                case Msg.Loaded: loaded.Add(from); return;
                case Msg.Done:
                    done.Add(from);
                    if (svFirstDoneAt < 0f) svFirstDoneAt = Time.realtimeSinceStartup;
                    ServerCheckDone();
                    return;
            }
            // gameplay: relay to the other players (or the one it is for)
            p.from = from;
            if (p.to >= 0)
            {
                if (nm.ServerManager.Clients.TryGetValue(p.to, out var target)) nm.ServerManager.Broadcast(target, p, false, ch);
            }
            else nm.ServerManager.BroadcastExcept(conn, p, false, ch);
        }

        void ServerHello(NetworkConnection conn, string name)
        {
            if (roster.Exists(e => e.id == conn.ClientId)) return;
            if (roster.Count >= MaxPlayers || svPhase == Phase.Stage)
            {
                conn.Disconnect(false);
                return;
            }
            int slot = 0;
            while (roster.Exists(e => e.slot == slot)) slot++;
            if (string.IsNullOrWhiteSpace(name)) name = "Tree";
            if (name.Length > 24) name = name.Substring(0, 24);
            roster.Add(new RosterEntry { id = conn.ClientId, slot = slot, name = name });
            ServerBroadcastRoster();
            // tell the newcomer where the party is
            w.Reset();
            w.Byte((byte)svPhase);
            w.Byte(svStage < 0 ? (byte)255 : (byte)svStage);
            nm.ServerManager.Broadcast(conn, Packet(Msg.Phase, w.ToArray()), false, Channel.Reliable);
            if (svPhase == Phase.Vote) ServerBroadcastVote();
        }

        static CoopPacket Packet(Msg kind, byte[] data) => new CoopPacket { kind = (byte)kind, from = -1, to = -1, data = data };

        void ServerBroadcastRoster()
        {
            roster.Sort((a, b) => a.slot.CompareTo(b.slot));
            w.Reset();
            w.Byte((byte)roster.Count);
            int host = HostConnId;
            foreach (var e in roster)
            {
                w.Int(e.id);
                w.Byte((byte)e.slot);
                w.Str(e.name);
                w.Bool(e.id == host);
            }
            nm.ServerManager.Broadcast(Packet(Msg.Roster, w.ToArray()), false, Channel.Reliable);
        }

        void ServerSetPhase(Phase ph, int stage = -1)
        {
            svPhase = ph;
            svVotes.Clear();
            svDeadline = -1f;
            svPick = -1;
            svStage = stage;
            loaded.Clear();
            done.Clear();
            svFirstDoneAt = -1f;
            if (ph == Phase.Stage) svStageAt = Time.realtimeSinceStartup;
            // newcomers may join in the lobby and the vote; the door closes while the party is out fighting
            LockSession(ph == Phase.Stage);
            w.Reset();
            w.Byte((byte)ph);
            w.Byte(stage < 0 ? (byte)255 : (byte)stage);
            nm.ServerManager.Broadcast(Packet(Msg.Phase, w.ToArray()), false, Channel.Reliable);
            if (ph == Phase.Vote) ServerBroadcastVote();
        }

        async void LockSession(bool locked)
        {
            var host = session?.AsHost();
            if (host == null || host.IsLocked == locked) return;
            try
            {
                host.IsLocked = locked;
                await host.SavePropertiesAsync();
            }
            catch (System.Exception e) { Debug.LogWarning("[Coop] lock: " + e.Message); }
        }

        void ServerVote(int from, int option)
        {
            if (svPhase != Phase.Vote || svPick >= 0 || !roster.Exists(e => e.id == from)) return;
            if (option == VoteNone) svVotes.Remove(from);
            else if (option == VoteRandom || (option >= 0 && option < Stages.Count)) svVotes[from] = option;
            else return;
            if (svVotes.Count == 0) svDeadline = -1f;                                     // every vote taken back
            else if (svDeadline < 0f) svDeadline = Time.realtimeSinceStartup + VoteSeconds;  // the first vote starts the clock
            ServerEvaluateVote(false);
            ServerBroadcastVote();
        }

        void ServerEvaluateVote(bool timeUp)
        {
            if (svPick >= 0) return;
            var counts = new Dictionary<int, int>();
            foreach (var e in roster)
                if (svVotes.TryGetValue(e.id, out int v)) counts[v] = counts.TryGetValue(v, out int c) ? c + 1 : 1;
            if (counts.Count == 0) { svDeadline = -1f; return; }
            int voters = 0;
            foreach (var e in roster) if (svVotes.ContainsKey(e.id)) voters++;
            bool unanimous = voters == roster.Count && counts.Count == 1;
            if (!unanimous && !timeUp) return;
            int best = 0;
            foreach (var kv in counts) best = Mathf.Max(best, kv.Value);
            var leaders = new List<int>();
            foreach (var kv in counts) if (kv.Value == best) leaders.Add(kv.Key);
            int option = leaders[Random.Range(0, leaders.Count)];
            svPick = option == VoteRandom ? RandomStage() : option;
            svPickAt = Time.realtimeSinceStartup;
            svDeadline = -1f;
        }

        int RandomStage()
        {
            var pool = new List<int>();
            for (int i = 0; i < Stages.Count; i++) if (Stages[i].HasBoss) pool.Add(i);
            if (pool.Count == 0) for (int i = 0; i < Stages.Count; i++) pool.Add(i);
            return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : 0;
        }

        void ServerBroadcastVote()
        {
            w.Reset();
            int n = 0;
            foreach (var e in roster) if (svVotes.ContainsKey(e.id)) n++;
            w.Byte((byte)n);
            foreach (var e in roster)
                if (svVotes.TryGetValue(e.id, out int v)) { w.Int(e.id); w.Byte((byte)v); }
            w.Float(svDeadline < 0f ? -1f : Mathf.Max(0f, svDeadline - Time.realtimeSinceStartup));
            w.Byte(svPick < 0 ? (byte)255 : (byte)svPick);
            nm.ServerManager.Broadcast(Packet(Msg.VoteState, w.ToArray()), false, Channel.Reliable);
        }

        bool ServerAllDone
        {
            get
            {
                foreach (var e in roster) if (!done.Contains(e.id)) return false;
                return true;
            }
        }

        void ServerCheckDone()
        {
            if (svPhase != Phase.Stage || !ServerAllDone) return;
            if (roster.Count > 0) ServerSetPhase(Phase.Vote);
        }

        /// <summary>Has everyone loaded the stage (or has the host waited long enough)?</summary>
        bool ServerEveryoneLoaded
        {
            get
            {
                if (svPhase != Phase.Stage) return true;
                if (Time.realtimeSinceStartup - svStageAt > 25f) return true;
                foreach (var e in roster) if (!loaded.Contains(e.id)) return false;
                return true;
            }
        }

        void ServerTick()
        {
            float now = Time.realtimeSinceStartup;
            switch (svPhase)
            {
                case Phase.Vote:
                    if (svPick < 0 && svDeadline > 0f && now >= svDeadline)
                    {
                        ServerEvaluateVote(true);
                        ServerBroadcastVote();
                    }
                    // a beat to show the pick, then everyone goes
                    if (svPick >= 0 && now - svPickAt > 2.2f) ServerSetPhase(Phase.Stage, svPick);
                    break;
                case Phase.Stage:
                    // someone is slow to come back from the spoils: don't hold the party forever
                    if (svFirstDoneAt > 0f && now - svFirstDoneAt > 120f) ServerSetPhase(Phase.Vote);
                    break;
            }
        }
    }
}
