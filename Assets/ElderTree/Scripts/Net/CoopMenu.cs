using System;
using System.Collections;
using System.Collections.Generic;
using ElderTree.Audio;
using ElderTree.InputHandling;
using ElderTree.UI;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

namespace ElderTree.Net
{
    /// <summary>
    /// The main menu's CO-OP page, in the level select's manner (a drifting vista, a list on a dark band, the chosen
    /// stage at the bottom right). Three views, following the session:
    /// <list type="bullet">
    /// <item>Browser: host a party, join one by its code, or pick one of the open parties listed.</item>
    /// <item>Party room: the code to share, who is in, and (for the host) Begin the Vote.</item>
    /// <item>Vote: every stage plus Random, with each player's mark on the stage they chose and the countdown; the
    /// pick plays the travel card and everyone loads in together.</item>
    /// </list>
    /// Styles: LevelSelect.uss (shared rows, info, card) + Resources/CoopMenu.uss.
    /// </summary>
    public class CoopMenu : ICoopMenu
    {
        enum View { Browser, Party, Vote }

        class Row
        {
            public VisualElement el, chips;
            public Label name, sub;
            public Action act;
            public int stage = -1;          // vote rows: stage index or VoteRandom
            public bool header;
        }

        public bool IsOpen { get; private set; }
        public event Action BackRequested;
        public string Help { get; private set; } = "";

        readonly MonoBehaviour host;
        readonly List<StageCatalog.Stage> stages;
        readonly VisualElement root, list, info, portrait, chips, partyBox, fade, card;
        VisualElement bgFront, bgBack;
        readonly Label kicker, title, codeLbl, timerLbl, statusLbl, region, ruler, epithet, desc, cardTitle, cardSub;
        readonly TextField codeField;
        readonly List<Row> rows = new();
        int index;
        View view;
        IList<ISessionInfo> found = Array.Empty<ISessionInfo>();
        float browsedAt = -99f, inputAfter;
        bool travelling, dirty = true;
        Texture2D shown;
        float frontBorn, backBorn;
        Coroutine vistaFade;

        CoopSession S => CoopSession.Ensure();

        // ================================================================== build
        static VisualElement El(string cls, VisualElement parent)
        {
            var e = new VisualElement();
            if (!string.IsNullOrEmpty(cls)) foreach (var c in cls.Split(' ')) e.AddToClassList(c);
            e.pickingMode = PickingMode.Ignore;
            parent?.Add(e);
            return e;
        }

        static Label Lbl(string text, string cls, VisualElement parent, bool serif = false)
        {
            var l = new Label(text);
            if (!string.IsNullOrEmpty(cls)) foreach (var c in cls.Split(' ')) l.AddToClassList(c);
            l.pickingMode = PickingMode.Ignore;
            if (serif && UIFonts.Serif != null) l.style.unityFontDefinition = FontDefinition.FromSDFFont(UIFonts.Serif);
            parent?.Add(l);
            return l;
        }

        static void Stretch(VisualElement e, Texture2D t)
        {
            e.style.backgroundImage = t;
            e.style.backgroundSize = new BackgroundSize(new Length(100, LengthUnit.Percent), new Length(100, LengthUnit.Percent));
        }

        public CoopMenu(VisualElement parent, MonoBehaviour host, StageCatalog catalog)
        {
            this.host = host;
            stages = catalog != null ? catalog.stages : new List<StageCatalog.Stage>();

            root = El("ls-root co-root ls-off", parent);
            root.pickingMode = PickingMode.Position;
            root.style.display = DisplayStyle.None;
            if (catalog != null && catalog.style != null) root.styleSheets.Add(catalog.style);
            var own = Resources.Load<StyleSheet>("CoopMenu");
            if (own != null) root.styleSheets.Add(own);

            bgBack = El("ls-vista", root);
            bgFront = El("ls-vista", root);
            Stretch(El("ls-fill ls-grade", root), MenuTextures.Shade);
            Stretch(El("ls-fill ls-grade", root), MenuTextures.Vignette);
            Stretch(El("ls-side", root), MenuTextures.Side);

            var page = El("ls-page", root);
            var head = El("ls-head", page);
            var back = Lbl("‹", "ls-back", head);
            back.pickingMode = PickingMode.Position;
            back.RegisterCallback<ClickEvent>(_ => { if (IsOpen && !travelling) Back(); });
            var titles = El("ls-titles", head);
            kicker = Lbl("FIGHT TOGETHER", "ls-kicker", titles);
            title = Lbl("CO-OP", "ls-title", titles, true);
            timerLbl = Lbl("", "co-timer", head, true);
            El("ls-rule", page).style.backgroundImage = MenuTextures.Line;

            codeLbl = Lbl("", "co-code", page, true);
            partyBox = El("co-party", page);
            list = El("ls-list co-list", page);

            codeField = new TextField { maxLength = 8 };
            codeField.AddToClassList("co-field");
            codeField.isDelayed = false;
            codeField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { codeField.Blur(); S.JoinByCode(codeField.value); }
                else if (e.keyCode == KeyCode.Escape) codeField.Blur();
            }, TrickleDown.TrickleDown);
            codeField.RegisterValueChangedCallback(e => { var up = (e.newValue ?? "").ToUpperInvariant(); if (up != e.newValue) codeField.SetValueWithoutNotify(up); });

            statusLbl = Lbl("", "co-status", page);

            info = El("ls-info", root);
            portrait = El("ls-portrait", info);
            ruler = Lbl("", "ls-info__kicker", info);
            region = Lbl("", "ls-info__name", info, true);
            epithet = Lbl("", "ls-info__epithet", info, true);
            El("ls-info__rule", info).style.backgroundImage = MenuTextures.Line;
            desc = Lbl("", "ls-info__desc", info);
            chips = El("ls-chips", info);

            fade = El("ls-fill ls-fade", root);
            fade.style.opacity = 0f;
            card = El("ls-fill ls-card", root);
            card.style.opacity = 0f;
            cardTitle = Lbl("", "ls-card__title", card, true);
            El("ls-card__rule", card).style.backgroundImage = MenuTextures.Line;
            cardSub = Lbl("", "ls-card__sub", card);

            // the network stack only starts once the page is opened (or a session is already running)
            if (CoopSession.Instance != null) Hook();
            ShowVista(FirstVista(), true);
        }

        bool hooked;

        void Hook()
        {
            if (hooked) return;
            hooked = true;
            S.Changed += OnChanged;
            S.StageRequested += OnStageRequested;
        }

        Texture2D FirstVista()
        {
            foreach (var s in stages) if (s.vista != null && s.HasBoss) return s.vista;
            return stages.Count > 0 ? stages[0].vista : null;
        }

        bool Alive => host != null && root.panel != null;

        void OnChanged()
        {
            if (!Alive) { Detach(); return; }
            dirty = true;
        }

        void Detach()
        {
            hooked = false;
            if (CoopSession.Instance == null) return;
            CoopSession.Instance.Changed -= OnChanged;
            CoopSession.Instance.StageRequested -= OnStageRequested;
        }

        // ================================================================== open / close
        public void Open()
        {
            Hook();
            IsOpen = true;
            inputAfter = Time.unscaledTime + 0.15f;
            root.style.display = DisplayStyle.Flex;
            root.schedule.Execute(() => { if (IsOpen) root.RemoveFromClassList("ls-off"); }).StartingIn(20);
            dirty = true;
            Rebuild();
            if (view == View.Browser) Browse();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            codeField.Blur();
            root.AddToClassList("ls-off");
            root.schedule.Execute(() => { if (!IsOpen) root.style.display = DisplayStyle.None; }).StartingIn(280);
        }

        void Back()
        {
            AudioManager.Play2D(Sfx.UIBack, 0.6f);
            if (view == View.Browser) { BackRequested?.Invoke(); return; }
            // inside a party: the Leave row does the leaving; back just walks to it
            for (int i = rows.Count - 1; i >= 0; i--) if (rows[i].act == LeaveAct) { Select(i, true); return; }
        }

        async void Browse()
        {
            browsedAt = Time.unscaledTime;
            found = await S.Browse();
            dirty = true;
        }

        // ================================================================== rows
        View CurrentView
        {
            get
            {
                var s = CoopSession.Instance;
                if (s == null || !Coop.Active) return View.Browser;
                return s.CurrentPhase == CoopSession.Phase.Vote || s.CurrentPhase == CoopSession.Phase.Stage ? View.Vote : View.Party;
            }
        }

        void Rebuild()
        {
            dirty = false;
            var before = view;
            view = CurrentView;
            if (before != view) index = 0;
            rows.Clear();
            list.Clear();
            partyBox.Clear();
            var s = S;
            switch (view)
            {
                case View.Browser: BuildBrowser(s); break;
                case View.Party: BuildParty(s); break;
                case View.Vote: BuildVote(s); break;
            }
            index = Mathf.Clamp(index, 0, Mathf.Max(0, rows.Count - 1));
            if (rows.Count > 0 && rows[index].header) index = Next(index, 1);
            Select(index, false);
            RefreshStatus();
        }

        Row AddRow(string name, string sub, Action act, string extra = null)
        {
            var r = new Row { act = act };
            int idx = rows.Count;
            r.el = El("ls-stage co-row" + (extra != null ? " " + extra : ""), list);
            Stretch(El("ls-stage__glow", r.el), MenuTextures.Glow);
            El("ls-stage__gem", r.el);
            var txt = El("ls-stage__text", r.el);
            r.name = Lbl(name, "ls-stage__name", txt, true);
            r.sub = Lbl(sub, "ls-stage__sub", txt);
            r.chips = El("co-marks", r.el);
            r.el.pickingMode = PickingMode.Position;
            r.el.RegisterCallback<PointerEnterEvent>(_ => { if (IsOpen && !travelling && index != idx) Select(idx, true); });
            r.el.RegisterCallback<ClickEvent>(_ => { if (IsOpen && !travelling) { Select(idx, false); Activate(); } });
            rows.Add(r);
            return r;
        }

        void AddHeader(string text)
        {
            var r = new Row { header = true };
            r.el = Lbl(text, "co-section", list);
            rows.Add(r);
        }

        void BuildBrowser(CoopSession s)
        {
            kicker.text = "FIGHT TOGETHER";
            title.text = "CO-OP";
            codeLbl.style.display = DisplayStyle.None;
            timerLbl.text = "";
            AddRow("HOST A PARTY", "Up to four players. Share your code, or wait for others to find you.", () => S.Host());
            var join = AddRow("JOIN BY CODE", "Type the code your host shares, then press Enter.", FocusCode, "co-row--code");
            join.el.Add(codeField);
            AddRow("REFRESH", "Look again for open parties.", Browse);
            AddHeader(found.Count == 0 ? "NO OPEN PARTIES FOUND" : "OPEN PARTIES");
            foreach (var f in found)
            {
                var id = f.Id;
                int inside = f.MaxPlayers - f.AvailableSlots;
                AddRow(f.Name.ToUpperInvariant(), $"{inside} / {f.MaxPlayers} players", () => S.JoinById(id));
            }
            ShowInfo(null, "ONLINE", "Fight together", "",
                "Each player keeps their own save and their own masks. A felled boss drops its mask for every player who has yet to claim it. Bosses grow stronger with every player, and turn on one of you at a time.");
        }

        void BuildParty(CoopSession s)
        {
            kicker.text = "THE PARTY";
            title.text = s.SessionName.ToUpperInvariant();
            codeLbl.style.display = DisplayStyle.Flex;
            codeLbl.text = "CODE   " + s.Code;
            timerLbl.text = "";
            BuildMembers();
            if (s.IsHost) AddRow("BEGIN THE VOTE", "Everyone moves to the stage vote. Others can still join until you set out.", () => S.StartVote());
            else AddRow("WAITING FOR THE HOST", "The host begins the vote when the party is ready.", null, "co-row--idle");
            AddRow("LEAVE PARTY", s.IsHost ? "Close the party for everyone." : "Return to the main menu alone.", LeaveAct);
            ShowInfo(null, "PARTY OF " + Coop.Party.Count, "Gather", "",
                $"Share the code {s.Code} with your friends, or they can find this party in the list. Up to {CoopSession.MaxPlayers} players.");
        }

        void BuildMembers()
        {
            foreach (var m in Coop.Party)
            {
                var row = El("co-member", partyBox);
                var gem = El("co-member__gem", row);
                gem.style.backgroundColor = Coop.SlotColor(m.slot);
                Lbl(m.name.ToUpperInvariant(), "co-member__name", row, true);
                string tags = (m.isHost ? "HOST" : "") + (m.isLocal ? (m.isHost ? "  ·  YOU" : "YOU") : "");
                Lbl(tags, "co-member__tag", row);
            }
        }

        void BuildVote(CoopSession s)
        {
            kicker.text = "THE PARTY VOTES";
            title.text = "CHOOSE THE BATTLEFIELD";
            codeLbl.style.display = DisplayStyle.None;
            BuildMembers();
            for (int i = 0; i < stages.Count; i++)
            {
                var st = stages[i];
                var r = AddRow(st.region.ToUpperInvariant(), !st.HasBoss ? "Training grounds" : st.Felled ? $"{st.boss}, felled" : "? ? ?", null);
                r.stage = i;
                int idx = i;
                r.act = () => Vote(idx);
                r.el.EnableInClassList("ls-stage--felled", st.Felled);
                r.el.EnableInClassList("ls-stage--training", !st.HasBoss);
            }
            var rnd = AddRow("RANDOM", "Let fate choose a boss's domain.", () => Vote(CoopSession.VoteRandom));
            rnd.stage = CoopSession.VoteRandom;
            AddRow("LEAVE PARTY", s.IsHost ? "Close the party for everyone." : "Return to the main menu alone.", LeaveAct);
            RefreshMarks();
        }

        int MyVote => CoopSession.Instance != null && CoopSession.Instance.Votes.TryGetValue(CoopSession.Instance.LocalId, out int v) ? v : CoopSession.VoteNone;

        void Vote(int option)
        {
            if (S.VotePick >= 0) return;
            S.CastVote(MyVote == option ? CoopSession.VoteNone : option);     // the same choice again takes it back
        }

        /// <summary>Each player's mark on the stage they chose.</summary>
        void RefreshMarks()
        {
            var s = CoopSession.Instance;
            if (s == null) return;
            int mine = MyVote;
            foreach (var r in rows)
            {
                if (r.stage < 0 || r.chips == null) continue;
                r.chips.Clear();
                foreach (var m in Coop.Party)
                {
                    if (!s.Votes.TryGetValue(m.id, out int v) || v != r.stage) continue;
                    var mark = Lbl(Initial(m.name), "co-mark", r.chips);
                    mark.style.backgroundColor = Coop.SlotColor(m.slot) * new Color(1f, 1f, 1f, 0.9f);
                }
                r.el.EnableInClassList("co-row--mine", r.stage == mine);
                r.el.EnableInClassList("co-row--picked", s.VotePick >= 0 && r.stage == s.VotePick);
            }
        }

        static string Initial(string name) => string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();

        void LeaveAct() => S.Leave();

        void FocusCode()
        {
            codeField.Focus();
            codeField.SelectAll();
        }

        // ================================================================== selection + info
        int Next(int from, int dir)
        {
            for (int k = 1; k <= rows.Count; k++)
            {
                int i = ((from + dir * k) % rows.Count + rows.Count) % rows.Count;
                if (!rows[i].header) return i;
            }
            return from;
        }

        void Select(int idx, bool sound)
        {
            if (rows.Count == 0) return;
            index = Mathf.Clamp(idx, 0, rows.Count - 1);
            for (int i = 0; i < rows.Count; i++) if (!rows[i].header) rows[i].el.EnableInClassList("ls-stage--active", i == index);
            var r = rows[index];
            Help = r.sub != null ? r.sub.text : "";
            if (view == View.Vote && r.stage >= 0 && r.stage < stages.Count)
            {
                var st = stages[r.stage];
                bool down = st.Felled;
                var face = !st.HasBoss || down ? st.portrait : st.silhouette != null ? st.silhouette : st.portrait;
                ShowInfo(face, !st.HasBoss ? "TRAINING GROUNDS" : down ? $"DOMAIN OF THE {st.boss.ToUpperInvariant()}" : "DOMAIN OF AN UNKNOWN FOE",
                    st.region, st.epithet, st.description);
                chips.Clear();
                if (st.HasBoss)
                {
                    Chip(down ? "BOSS FELLED" : "BOSS UNVANQUISHED", down);
                    if (!string.IsNullOrEmpty(st.maskName))
                    {
                        bool mask = Progress.OwnsMask(st.maskId);
                        Chip(mask ? st.maskName.ToUpperInvariant() + " CLAIMED" : "MASK UNCLAIMED", mask);
                    }
                }
                ShowVista(st.vista);
            }
            else if (view == View.Vote && r.stage == CoopSession.VoteRandom)
                ShowInfo(null, "FATE DECIDES", "Random", "", "If Random carries the vote, the party is sent to a boss's domain drawn by lot.");
            if (sound) AudioManager.Play2D(Sfx.UIMove, 0.5f);
        }

        void Chip(string text, bool done) => Lbl(text, "ls-chip " + (done ? "ls-chip--done" : "ls-chip--open"), chips);

        void ShowInfo(Texture2D face, string k, string name, string epi, string body)
        {
            portrait.style.backgroundImage = face;
            portrait.style.display = face != null ? DisplayStyle.Flex : DisplayStyle.None;
            ruler.text = k;
            region.text = name.ToUpperInvariant();
            epithet.text = epi;
            desc.text = body;
            chips.Clear();
            info.AddToClassList("ls-info--in");
        }

        void Activate()
        {
            var r = rows[index];
            if (r.header || r.act == null) return;
            AudioManager.Play2D(Sfx.UISelect, 0.8f);
            r.act();
        }

        void RefreshStatus()
        {
            var s = CoopSession.Instance;
            string text = "";
            if (s != null)
            {
                text = s.Working switch
                {
                    CoopSession.Busy.SigningIn => "Reaching the online services…",
                    CoopSession.Busy.Hosting => "Opening a party…",
                    CoopSession.Busy.Joining => "Joining the party…",
                    CoopSession.Busy.Leaving => "Leaving…",
                    _ => !string.IsNullOrEmpty(s.Error) ? s.Error : s.Browsing && view == View.Browser ? "Looking for open parties…" : "",
                };
            }
            statusLbl.text = text;
            statusLbl.EnableInClassList("co-status--error", s != null && s.Working == CoopSession.Busy.None && !string.IsNullOrEmpty(s.Error));
        }

        // ================================================================== tick
        public void Tick(PlayerInputReader input)
        {
            float t = Time.unscaledTime;
            LevelSelectView.Drift(bgFront, t - frontBorn);
            LevelSelectView.Drift(bgBack, t - backBorn);
            if (!IsOpen) return;
            // while a code is being typed nothing may rebuild the list under the field
            bool typing = codeField.focusController != null && codeField.focusController.focusedElement == codeField;
            if ((dirty || CurrentView != view) && !typing) Rebuild();
            RefreshStatus();
            if (view == View.Vote) TickVoteClock();
            else if (view == View.Browser && !typing && Time.unscaledTime - browsedAt > 12f && S.Working == CoopSession.Busy.None && !S.Browsing) Browse();

            if (travelling || input == null) return;
            if (typing) { input.ConsumeNavigate(); return; }
            if (t < inputAfter) { input.ConsumeNavigate(); return; }
            var nav = input.ConsumeNavigate();
            if (nav.y != 0 && rows.Count > 0) Select(Next(index, -nav.y), true);
            if (input.SubmitPressed && rows.Count > 0) Activate();
            else if (input.CancelPressed)
            {
                // in the vote, back takes your vote back first
                if (view == View.Vote && MyVote != CoopSession.VoteNone && S.VotePick < 0) { S.CastVote(CoopSession.VoteNone); AudioManager.Play2D(Sfx.UIBack, 0.6f); }
                else Back();
            }
        }

        void TickVoteClock()
        {
            var s = S;
            RefreshMarks();
            if (s.VotePick >= 0 && s.VotePick < stages.Count) timerLbl.text = "TO " + stages[s.VotePick].region.ToUpperInvariant();
            else if (s.VoteRemaining >= 0f) timerLbl.text = Mathf.CeilToInt(s.VoteRemaining).ToString();
            else timerLbl.text = "";
            timerLbl.EnableInClassList("co-timer--urgent", s.VoteRemaining >= 0f && s.VoteRemaining < 5f);
        }

        public string Keys(bool pad)
        {
            string ok = pad ? "A" : "Enter", back = pad ? "B" : "Esc", nav = pad ? "D-Pad" : "↑ ↓";
            if (travelling) return "";
            return view switch
            {
                View.Vote => $"{nav}  Choose      {ok}  Vote / Take back      {back}  {(MyVote != CoopSession.VoteNone ? "Take back vote" : "Leave")}",
                View.Party => $"{nav}  Choose      {ok}  Select",
                _ => $"{nav}  Choose      {ok}  Select      {back}  Back",
            };
        }

        // ================================================================== travel
        void OnStageRequested(StageCatalog.Stage s)
        {
            if (!Alive) { Detach(); return; }
            if (travelling) return;
            host.StartCoroutine(TravelRoutine(s));
        }

        IEnumerator TravelRoutine(StageCatalog.Stage s)
        {
            travelling = true;
            if (!IsOpen) Open();
            cardTitle.text = s.region.ToUpperInvariant();
            cardSub.text = Coop.Party.Count > 1 ? $"A party of {Coop.Party.Count}" : (!s.HasBoss ? "Training grounds" : s.epithet);
            ShowVista(s.vista);
            yield return Ease.Tween(0.7f, k => fade.style.opacity = k, Ease.InOutSine);
            AudioManager.Play2D(Sfx.Title, 0.7f);
            yield return Ease.Tween(0.6f, k => card.style.opacity = k, Ease.OutCubic);
            yield return new WaitForSecondsRealtime(1.1f);
            yield return Ease.Tween(0.5f, k => card.style.opacity = 1f - k, Ease.InCubic);
            S.Travel(s);
        }

        // ================================================================== vistas
        void ShowVista(Texture2D tex, bool instant = false)
        {
            if (tex == null || tex == shown) return;
            shown = tex;
            if (vistaFade != null) { host.StopCoroutine(vistaFade); FinishFade(); }
            bgBack.style.backgroundImage = tex;
            bgBack.PlaceInFront(bgFront);
            backBorn = Time.unscaledTime;
            if (instant || host == null) { bgBack.style.opacity = 1f; FinishFade(); return; }
            bgBack.style.opacity = 0f;
            vistaFade = host.StartCoroutine(VistaFade());
        }

        IEnumerator VistaFade()
        {
            yield return Ease.Tween(0.45f, k => bgBack.style.opacity = k, Ease.InOutSine);
            FinishFade();
        }

        void FinishFade()
        {
            vistaFade = null;
            (bgFront, bgBack) = (bgBack, bgFront);
            (frontBorn, backBorn) = (backBorn, frontBorn);
            bgFront.style.opacity = 1f;
            bgBack.style.opacity = 0f;
        }
    }
}
