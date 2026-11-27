using System;
using System.Collections;
using System.Collections.Generic;
using ElderTree.Audio;
using ElderTree.InputHandling;
using UnityEngine;
using UnityEngine.UIElements;

namespace ElderTree.UI
{
    /// <summary>
    /// The title and main menu, in the restrained Elden Ring manner: a stage vista drifting slowly behind a dark
    /// grade, rising embers, a tree-ring emblem behind the title, a short centred list whose highlighted entry sits
    /// in a soft gold glow, and a footer with the highlighted entry's description and the button prompts.
    /// Pages: Level Select (the shared <see cref="LevelSelectView"/> over the <see cref="StageCatalog"/>), Options (game + camera), Settings (graphics,
    /// audio, controls in tabs), Reset (progress, masks, introductions, settings, controls; each one confirmed).
    /// Pad, keyboard and mouse all drive everything.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MainMenuScreen : MonoBehaviour
    {
        public StyleSheet hudStyle;          // option rows (.setting, .slider, .bindrow)
        public StyleSheet menuStyle;
        public string kicker = "A NIGHT WATCH SOULSLIKE";
        public string titleText = "ELDER TREE";
        public string version = "v0.1";
        [Tooltip("Seconds each vista holds behind the title before the next one fades in.")]
        public float vistaHold = 11f;

        enum Page { Title, Main, Levels, Coop, Options, Settings, Reset }

        class Item
        {
            public string label, help;
            public Action act;
            public VisualElement el;
            public Label text;
            public bool visible = true;
        }

        class Ember
        {
            public VisualElement el;
            public float x, y, speed, sway, phase, life, maxLife, size;
        }

        Page page = Page.Title;
        VisualElement root, bgFront, bgBack, embersLayer, titleGroup, mainCol, modal, fade;
        Label pressAny, footLeft, footHelp, footKeys, toast;
        RingEmblem emblem;
        readonly Dictionary<Page, VisualElement> pages = new();
        PlayerInputReader input;
        bool locked;
        float inputAfter;

        readonly List<Item> items = new();
        int itemIndex;

        StageCatalog catalog;
        List<StageCatalog.Stage> stages = new();
        LevelSelectView levels;
        ICoopMenu coop;
        Item coopItem;

        OptionList optionsList, settingsList, resetList;
        readonly List<Label> settingsTabs = new();
        int settingsTab;
        Label tabPrev, tabNext;
        static readonly string[] SettingsTabNames = { "GRAPHICS", "AUDIO", "CONTROLS" };

        Label modalTitle, modalBody;
        readonly Label[] modalBtns = new Label[2];
        int modalIndex;
        Action modalConfirm;
        bool ModalOpen => modal != null && modal.style.display == DisplayStyle.Flex;

        int vistaIndex = -1;
        float vistaAt;
        Coroutine vistaFade, toastRoutine;
        Texture2D shownVista;
        float frontBorn, backBorn;

        readonly List<Ember> embers = new();

        static readonly Color Gold = new(0.85f, 0.71f, 0.42f), Ember1 = new(1f, 0.62f, 0.28f), Ember2 = new(1f, 0.84f, 0.52f);

        // ================================================================== lifecycle
        void Start()
        {
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 0;
            Time.timeScale = 1f;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            input = PlayerInputReader.Instance;
            input?.SetGameplayEnabled(false);
            if (input != null) input.DeviceChanged += _ => RefreshFooter();
            Settings.Load();
            catalog = StageCatalog.Instance;
            if (catalog != null) stages = catalog.stages;

            Build();
            RefreshProgress();
            NextVista(true);
            // back from a co-op stage (or the session is still running): straight to the party
            if (Coop.Active && coop != null)
            {
                SceneFlow.OpenLevelSelect = false;
                GoTo(Page.Main, false);
                GoTo(Page.Coop, false);
            }
            // back from a stage (a victory, or the level select asked for): straight to the stage list
            else if (SceneFlow.OpenLevelSelect)
            {
                SceneFlow.OpenLevelSelect = false;
                GoTo(Page.Main, false);
                GoTo(Page.Levels, false);
            }
            else GoTo(Page.Title, false);
            AudioManager.SetMusic(MusicMood.Explore);
            StartCoroutine(FadeIn());
        }

        void OnDestroy()
        {
            ControlBindings.CancelRebind();
        }

        // ================================================================== helpers
        static VisualElement El(string cls, VisualElement parent)
        {
            var e = new VisualElement();
            if (!string.IsNullOrEmpty(cls)) foreach (var c in cls.Split(' ')) e.AddToClassList(c);
            e.pickingMode = PickingMode.Ignore;
            parent?.Add(e);
            return e;
        }

        static Label Lbl(string text, string cls, VisualElement parent)
        {
            var l = new Label(text);
            if (!string.IsNullOrEmpty(cls)) foreach (var c in cls.Split(' ')) l.AddToClassList(c);
            l.pickingMode = PickingMode.Ignore;
            parent?.Add(l);
            return l;
        }

        static void Serif(VisualElement e)
        {
            var f = UIFonts.Serif;
            if (f != null) e.style.unityFontDefinition = FontDefinition.FromSDFFont(f);
        }

        Label SerifLbl(string text, string cls, VisualElement parent)
        {
            var l = Lbl(text, cls, parent);
            Serif(l);
            return l;
        }

        static void Image(VisualElement e, Texture2D t, bool stretch = true)
        {
            e.style.backgroundImage = t;
            if (stretch) e.style.backgroundSize = new BackgroundSize(new Length(100, LengthUnit.Percent), new Length(100, LengthUnit.Percent));
        }

        /// <summary>Fade an element in or out (USS transition on .mm-off), taking it out of layout and picking once hidden.</summary>
        static void Show(VisualElement e, bool on)
        {
            if (on)
            {
                e.style.display = DisplayStyle.Flex;
                e.schedule.Execute(() => e.RemoveFromClassList("mm-off")).StartingIn(20);
            }
            else
            {
                e.AddToClassList("mm-off");
                e.schedule.Execute(() => { if (e.ClassListContains("mm-off")) e.style.display = DisplayStyle.None; }).StartingIn(280);
            }
        }

        bool Pad => input != null && input.Device == InputDeviceKind.Gamepad;

        // ================================================================== build
        void Build()
        {
            var doc = GetComponent<UIDocument>();
            root = doc.rootVisualElement;
            root.Clear();
            if (hudStyle) root.styleSheets.Add(hudStyle);
            if (menuStyle) root.styleSheets.Add(menuStyle);
            root.AddToClassList("mm-root");

            bgBack = El("mm-vista", root);
            bgFront = El("mm-vista", root);
            Image(El("mm-fill", root), MenuTextures.Shade);
            Image(El("mm-fill", root), MenuTextures.Vignette);
            embersLayer = El("mm-fill", root);
            for (int i = 0; i < 70; i++) embers.Add(NewEmber(true));

            BuildTitle();
            BuildMain();
            BuildOptions();
            BuildSettings();
            BuildReset();
            BuildFooter();
            levels = new LevelSelectView(root, this, catalog);
            levels.BackRequested += () => GoTo(Page.Main);
            coop = CoopMenuHook.Create?.Invoke(root, this, catalog);
            if (coop != null) coop.BackRequested += () => GoTo(Page.Main);
            coopItem.visible = coop != null;
            coopItem.el.style.display = coop != null ? DisplayStyle.Flex : DisplayStyle.None;
            BuildModal();

            toast = Lbl("", "mm-toast mm-off", root);
            toast.style.display = DisplayStyle.None;

            fade = El("mm-fill mm-fade", root);
        }

        void BuildTitle()
        {
            titleGroup = El("mm-title mm-fadeable", root);
            emblem = new RingEmblem();
            emblem.AddToClassList("mm-emblem");
            emblem.pickingMode = PickingMode.Ignore;
            titleGroup.Add(emblem);
            Lbl(kicker, "mm-title__kicker", titleGroup);
            SerifLbl(titleText, "mm-title__main", titleGroup);
            var orn = El("mm-ornament", titleGroup);
            El("mm-ornament__line", orn).style.backgroundImage = MenuTextures.Line;
            El("mm-ornament__gem", orn);
            El("mm-ornament__line", orn).style.backgroundImage = MenuTextures.Line;

            pressAny = SerifLbl("PRESS ANY BUTTON", "mm-press mm-fadeable", root);
            pressAny.pickingMode = PickingMode.Position;
        }

        void BuildMain()
        {
            mainCol = El("mm-main mm-fadeable", root);
            AddItem("CONTINUE", ContinueHelp(), Continue);
            AddItem("LEVEL SELECT", "Travel to any stage: the training grounds, or the domain of a boss.", () => GoTo(Page.Levels));
            AddItem("CO-OP", "Fight alongside up to three other players online: host a party or join one, then vote on where to go.", () => GoTo(Page.Coop));
            coopItem = items[items.Count - 1];
            AddItem("OPTIONS", "Boss introductions, control hints, camera shake and the camera.", () => GoTo(Page.Options));
            AddItem("SETTINGS", "Graphics, display, audio and controls.", () => GoTo(Page.Settings));
            AddItem("RESET", "Reset boss progress, masks or introductions, or restore the default settings and controls.", () => GoTo(Page.Reset));
            AddItem("QUIT GAME", "Return to the desktop.", () => Confirm("Quit the game?", "Your progress is saved.", "Quit", Quit));
        }

        void AddItem(string label, string help, Action act)
        {
            var it = new Item { label = label, help = help, act = act };
            int idx = items.Count;
            it.el = El("mm-item", mainCol);
            var glow = El("mm-item__glow", it.el);
            Image(glow, MenuTextures.Glow);
            El("mm-item__line mm-item__line--top", it.el).style.backgroundImage = MenuTextures.Line;
            El("mm-item__line mm-item__line--bottom", it.el).style.backgroundImage = MenuTextures.Line;
            it.text = SerifLbl(label, "mm-item__text", it.el);
            it.el.pickingMode = PickingMode.Position;
            it.el.RegisterCallback<PointerEnterEvent>(_ => { if (CanPoint(Page.Main) && itemIndex != idx) SelectItem(idx, true); });
            it.el.RegisterCallback<ClickEvent>(_ => { if (CanPoint(Page.Main)) { SelectItem(idx, false); Activate(); } });
            items.Add(it);
        }

        bool CanPoint(Page p) => !locked && page == p && !ModalOpen;

        VisualElement NewPage(Page p, string title, string kick)
        {
            var pg = El("mm-page mm-fadeable mm-off", root);
            pg.style.display = DisplayStyle.None;
            var head = El("mm-page__head", pg);
            var back = Lbl("‹", "mm-back", head);
            back.pickingMode = PickingMode.Position;
            back.RegisterCallback<ClickEvent>(_ => { if (CanPoint(p)) Back(); });
            var titles = El("mm-page__titles", head);
            Lbl(kick, "mm-page__kicker", titles);
            SerifLbl(title, "mm-page__title", titles);
            var rule = El("mm-page__rule", pg);
            rule.style.backgroundImage = MenuTextures.Line;
            pages[p] = pg;
            return pg;
        }

        // ------------------------------------------------------------------ options / settings / reset
        void BuildOptions()
        {
            var pg = NewPage(Page.Options, "OPTIONS", "GAME & CAMERA");
            var panel = El("mm-panel", pg);
            optionsList = new OptionList(panel, Serif, "mm-options");
            optionsList.Changed += RefreshFooter;
        }

        void BuildSettings()
        {
            var pg = NewPage(Page.Settings, "SETTINGS", "SYSTEM");
            var panel = El("mm-panel", pg);
            var bar = El("mm-tabs", panel);
            tabPrev = Lbl("Q", "pause__tabkey", bar);
            for (int i = 0; i < SettingsTabNames.Length; i++)
            {
                int idx = i;
                var t = SerifLbl(SettingsTabNames[i], "pause__tab", bar);
                t.pickingMode = PickingMode.Position;
                t.RegisterCallback<ClickEvent>(_ => { if (CanPoint(Page.Settings) && !ControlBindings.Rebinding && idx != settingsTab) SetSettingsTab(idx, true); });
                settingsTabs.Add(t);
            }
            tabNext = Lbl("E", "pause__tabkey", bar);
            settingsList = new OptionList(panel, Serif, "mm-options");
            settingsList.Changed += RefreshFooter;
        }

        void SetSettingsTab(int tab, bool sound)
        {
            settingsTab = (tab + SettingsTabNames.Length) % SettingsTabNames.Length;
            for (int i = 0; i < settingsTabs.Count; i++) settingsTabs[i].EnableInClassList("pause__tab--active", i == settingsTab);
            settingsList.Clear();
            switch (settingsTab)
            {
                case 0: SettingsPages.Graphics(settingsList); break;
                case 1: SettingsPages.Audio(settingsList); break;
                case 2: SettingsPages.Controls(settingsList); break;
            }
            settingsList.Refresh();
            if (sound) AudioManager.Play2D(Sfx.UIMove, 0.6f, 0.9f);
        }

        void BuildReset()
        {
            var pg = NewPage(Page.Reset, "RESET", "DATA");
            var panel = El("mm-panel mm-panel--narrow", pg);
            resetList = new OptionList(panel, Serif, "mm-options");
            resetList.Changed += RefreshFooter;
            resetList.Section("PROGRESS");
            resetList.AddButton("Reset boss progress", () => Confirm("Reset boss progress?",
                    "Every boss returns unfelled and unmet. Portraits turn back to silhouettes, masks are lost, and each introduction plays again.",
                    "Reset", () => { Progress.ResetAll(); SceneFlow.ForgetLastStage(); Done("Boss progress reset."); }),
                "Every boss returns unfelled: portraits become silhouettes again, masks are lost and introductions replay.");
            resetList.AddButton("Forget masks", () => Confirm("Forget every mask?",
                    "Felled bosses stay felled, but their masks are gone until you fell them again. The transformation plays anew.",
                    "Forget", () => { Progress.ForgetMasks(); Done("Masks forgotten."); }),
                "Lose every mask but keep the bosses felled. Each mask's transformation plays again when it is reclaimed.");
            resetList.AddButton("Replay boss introductions", () => Confirm("Replay boss introductions?",
                    "The next meeting with each boss plays its introduction again.",
                    "Replay", () => { Progress.ForgetIntros(); Done("Introductions will play again."); }),
                "The next meeting with each boss plays its introduction cinematic again.");
            resetList.Section("SYSTEM");
            resetList.AddButton("Restore default options & settings", () => Confirm("Restore default options and settings?",
                    "Graphics, audio, camera and game options return to their defaults. Resolution and display mode are kept.",
                    "Restore", () => { Settings.ResetDefaults(); Done("Options and settings restored."); }),
                "Graphics, audio, camera and game options back to their defaults. Resolution and display mode are kept.");
            resetList.AddButton("Restore default controls", () => Confirm("Restore default controls?",
                    "Every remapped key and button returns to its default.",
                    "Restore", () => { ControlBindings.ResetAll(); Done("Controls restored."); }),
                "Every remapped key and button back to its default.");
            resetList.Section("EVERYTHING");
            resetList.AddButton("Erase all data", () => Confirm("Erase all data?",
                    "Boss progress, masks, introductions, options, settings and controls are all erased. This cannot be undone.",
                    "Erase", () =>
                    {
                        Progress.ResetAll();
                        SceneFlow.ForgetLastStage();
                        Settings.ResetDefaults();
                        ControlBindings.ResetAll();
                        Done("All data erased.");
                    }),
                "Start over: progress, masks, introductions, options, settings and controls.");
        }

        // ------------------------------------------------------------------ footer, modal
        void BuildFooter()
        {
            var foot = El("mm-foot", root);
            El("mm-foot__rule", foot).style.backgroundImage = MenuTextures.Line;
            var row = El("mm-foot__row", foot);
            footLeft = Lbl("", "mm-foot__left", row);
            footHelp = Lbl("", "mm-foot__help", row);
            footKeys = Lbl("", "mm-foot__keys", row);
        }

        void BuildModal()
        {
            modal = El("mm-fill mm-modal", root);
            modal.pickingMode = PickingMode.Position;
            modal.style.display = DisplayStyle.None;
            var box = El("mm-modal__box", modal);
            El("mm-modal__rule", box).style.backgroundImage = MenuTextures.Line;
            modalTitle = SerifLbl("", "mm-modal__title", box);
            modalBody = Lbl("", "mm-modal__body", box);
            var btns = El("mm-modal__btns", box);
            for (int i = 0; i < 2; i++)
            {
                int idx = i;
                var b = SerifLbl("", "mm-modal__btn", btns);
                b.pickingMode = PickingMode.Position;
                b.RegisterCallback<PointerEnterEvent>(_ => { if (modalIndex != idx) { modalIndex = idx; RefreshModal(); AudioManager.Play2D(Sfx.UIMove, 0.4f); } });
                b.RegisterCallback<ClickEvent>(_ => { modalIndex = idx; ModalChoose(); });
                modalBtns[i] = b;
            }
            El("mm-modal__rule", box).style.backgroundImage = MenuTextures.Line;
        }

        void Confirm(string title, string body, string yes, Action onYes)
        {
            modalTitle.text = title;
            modalBody.text = body;
            modalBtns[0].text = yes.ToUpperInvariant();
            modalBtns[1].text = "CANCEL";
            modalIndex = 1;                      // the safe answer is the default
            modalConfirm = onYes;
            modal.style.display = DisplayStyle.Flex;
            modal.style.opacity = 0f;
            StartCoroutine(Ease.Tween(0.18f, k => modal.style.opacity = k, Ease.OutCubic));
            inputAfter = Time.unscaledTime + 0.12f;
            AudioManager.Play2D(Sfx.UISelect, 0.6f);
            RefreshModal();
            RefreshFooter();
        }

        void RefreshModal()
        {
            for (int i = 0; i < 2; i++) modalBtns[i].EnableInClassList("mm-modal__btn--active", i == modalIndex);
        }

        void ModalChoose()
        {
            var act = modalIndex == 0 ? modalConfirm : null;
            CloseModal(act == null);
            act?.Invoke();
        }

        void CloseModal(bool backSound)
        {
            modal.style.display = DisplayStyle.None;
            modalConfirm = null;
            inputAfter = Time.unscaledTime + 0.12f;
            if (backSound) AudioManager.Play2D(Sfx.UIBack, 0.6f);
            RefreshFooter();
        }

        /// <summary>A reset went through: refresh everything that shows progress or settings and say so.</summary>
        void Done(string message)
        {
            AudioManager.Play2D(Sfx.Knock, 0.6f);
            RefreshProgress();
            optionsList.RefreshValues();
            settingsList.RefreshValues();
            Toast(message);
        }

        void Toast(string message)
        {
            if (toastRoutine != null) StopCoroutine(toastRoutine);
            toastRoutine = StartCoroutine(ToastRoutine(message));
        }

        IEnumerator ToastRoutine(string message)
        {
            toast.text = message;
            Show(toast, true);
            yield return new WaitForSecondsRealtime(2.4f);
            Show(toast, false);
        }

        // ================================================================== state
        string ContinueHelp() => SceneFlow.LastStage(out var scene, out _) ? $"Return to {catalog?.Find(scene)?.region ?? scene}." : "";

        void RefreshProgress()
        {
            // continue only once a stage has been entered
            var cont = items[0];
            cont.visible = SceneFlow.LastStage(out _, out _);
            cont.help = ContinueHelp();
            cont.el.style.display = cont.visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (coopItem != null)
            {
                coopItem.visible = coop != null;
                coopItem.el.style.display = coopItem.visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (!items[itemIndex].visible) itemIndex = NextVisible(itemIndex, 1);
            SelectItem(itemIndex, false);

            int bosses = 0, felled = 0;
            foreach (var s in stages)
                if (s.HasBoss) { bosses++; if (s.Felled) felled++; }
            footLeft.text = $"BOSSES FELLED   {felled} / {bosses}          {version}";
            levels?.Refresh();
        }

        int NextVisible(int from, int dir)
        {
            for (int k = 1; k <= items.Count; k++)
            {
                int i = ((from + dir * k) % items.Count + items.Count) % items.Count;
                if (items[i].visible) return i;
            }
            return from;
        }

        void SelectItem(int idx, bool sound)
        {
            itemIndex = idx;
            for (int i = 0; i < items.Count; i++) items[i].el.EnableInClassList("mm-item--active", i == itemIndex);
            if (sound) AudioManager.Play2D(Sfx.UIMove, 0.5f);
            RefreshFooter();
        }

        void Activate()
        {
            var it = items[itemIndex];
            if (!it.visible) return;
            AudioManager.Play2D(Sfx.UISelect, 0.8f);
            it.act?.Invoke();
        }

        void GoTo(Page p, bool sound = true)
        {
            var from = page;
            page = p;
            inputAfter = Time.unscaledTime + 0.15f;
            bool titleish = p == Page.Title || p == Page.Main;
            Show(titleGroup, titleish);
            Show(pressAny, p == Page.Title);
            Show(mainCol, p == Page.Main);
            foreach (var kv in pages) if (kv.Key == p || kv.Key == from) Show(kv.Value, kv.Key == p);
            if (p == Page.Levels) levels.Open();
            else if (from == Page.Levels) levels.Close();
            if (p == Page.Coop) coop?.Open();
            else if (from == Page.Coop) coop?.Close();
            switch (p)
            {
                case Page.Options:
                    optionsList.Clear();
                    SettingsPages.Game(optionsList);
                    optionsList.Refresh();
                    break;
                case Page.Settings:
                    SetSettingsTab(settingsTab, false);
                    break;
                case Page.Reset:
                    resetList.Refresh();
                    break;
                case Page.Main:
                    if (from == Page.Levels) vistaAt = Time.unscaledTime;   // hold the stage just browsed a while
                    break;
            }
            if (from == Page.Options || from == Page.Settings) ControlBindings.CancelRebind();
            if (sound && from != p) AudioManager.Play2D(p == Page.Title || (int)p < (int)from && p == Page.Main ? Sfx.UIBack : Sfx.UISelect, 0.6f);
            RefreshFooter();
        }

        /// <summary>Show the CO-OP page (from anywhere in the menu).</summary>
        public void OpenCoop()
        {
            if (coop == null || page == Page.Coop) return;
            if (page == Page.Title) GoTo(Page.Main, false);
            GoTo(Page.Coop, false);
        }

        void Back()
        {
            if (page == Page.Main) GoTo(Page.Title);
            else if (page != Page.Title) GoTo(Page.Main);
        }

        void RefreshFooter()
        {
            if (footKeys == null) return;
            bool pad = Pad;
            string ok = pad ? "A" : "Enter", back = pad ? "B" : "Esc";
            string keys = "", help = "";
            if (ModalOpen)
            {
                keys = $"{(pad ? "D-Pad" : "← →")}  Choose      {ok}  Confirm      {back}  Cancel";
                help = "";
            }
            else switch (page)
            {
                case Page.Title: keys = ""; break;
                case Page.Main:
                    keys = $"{ok}  Select      {back}  Title";
                    help = items[itemIndex].help;
                    break;
                case Page.Options:
                    keys = optionsList.Listening ? optionsList.FooterAction(pad) : $"{optionsList.FooterAction(pad)}      {back}  Back";
                    help = optionsList.CurrentHelp;
                    break;
                case Page.Settings:
                    keys = settingsList.Listening ? settingsList.FooterAction(pad)
                        : $"{(pad ? "LB RB" : "Q E")}  Tabs      {settingsList.FooterAction(pad)}      {back}  Back";
                    help = settingsList.Listening ? "" : settingsList.CurrentHelp;
                    break;
                case Page.Reset:
                    keys = $"{ok}  Choose      {back}  Back";
                    help = resetList.CurrentHelp;
                    break;
                case Page.Coop:
                    keys = coop != null ? coop.Keys(pad) : "";
                    help = coop != null ? coop.Help : "";
                    break;
            }
            if (tabPrev != null) { tabPrev.text = pad ? "LB" : "Q"; tabNext.text = pad ? "RB" : "E"; }
            footKeys.text = keys;
            footHelp.text = help;
        }

        // ================================================================== travel
        void Continue()
        {
            if (!SceneFlow.LastStage(out var scene, out var boss)) return;
            locked = true;
            var s = catalog?.Find(scene);
            if (s != null) levels.Travel(s);
            else StartCoroutine(levels.TravelRoutine(scene, boss, scene.ToUpperInvariant(), ""));
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        IEnumerator FadeIn()
        {
            fade.style.opacity = 1f;
            yield return null;
            yield return Ease.Tween(1.4f, k => fade.style.opacity = 1f - k, Ease.InOutSine);
        }

        // ================================================================== vistas
        void NextVista(bool instant)
        {
            int n = stages.Count;
            for (int k = 1; k <= n; k++)
            {
                int i = (vistaIndex + k) % n;
                if (stages[i].vista == null) continue;
                vistaIndex = i;
                ShowVista(stages[i].vista, instant);
                break;
            }
            vistaAt = Time.unscaledTime;
        }

        void ShowVista(Texture2D tex, bool instant = false)
        {
            if (tex == null || tex == shownVista) return;
            shownVista = tex;
            if (vistaFade != null) { StopCoroutine(vistaFade); FinishFade(); }
            bgBack.style.backgroundImage = tex;
            bgBack.PlaceInFront(bgFront);          // the incoming vista fades in over the current one
            backBorn = Time.unscaledTime;
            if (instant) { bgBack.style.opacity = 1f; FinishFade(); return; }
            bgBack.style.opacity = 0f;
            vistaFade = StartCoroutine(VistaFade());
        }

        IEnumerator VistaFade()
        {
            yield return Ease.Tween(1.6f, k => bgBack.style.opacity = k, Ease.InOutSine);
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

        // ================================================================== embers
        Ember NewEmber(bool anywhere)
        {
            var e = new Ember { el = El("mm-ember", embersLayer) };
            Respawn(e, anywhere);
            return e;
        }

        static void Respawn(Ember e, bool anywhere)
        {
            e.x = UnityEngine.Random.value;
            e.y = anywhere ? UnityEngine.Random.value : 1.02f + UnityEngine.Random.value * 0.1f;
            e.speed = UnityEngine.Random.Range(0.012f, 0.045f);
            e.sway = UnityEngine.Random.Range(0.004f, 0.018f);
            e.phase = UnityEngine.Random.value * 10f;
            e.maxLife = UnityEngine.Random.Range(9f, 22f);
            e.life = anywhere ? UnityEngine.Random.value * e.maxLife : 0f;
            e.size = UnityEngine.Random.value < 0.2f ? UnityEngine.Random.Range(4.5f, 7f) : UnityEngine.Random.Range(2.5f, 4f);
            e.el.style.width = e.size;
            e.el.style.height = e.size;
            e.el.style.backgroundColor = Color.Lerp(Ember1, Ember2, UnityEngine.Random.value);
        }

        void TickEmbers(float dt, float t)
        {
            float w = embersLayer.resolvedStyle.width, h = embersLayer.resolvedStyle.height;
            if (float.IsNaN(w) || w < 2f) return;
            foreach (var e in embers)
            {
                e.life += dt;
                e.y -= e.speed * dt;
                if (e.life > e.maxLife || e.y < -0.05f) { Respawn(e, false); continue; }
                float x = e.x + Mathf.Sin(t * 0.6f + e.phase) * e.sway;
                float fadeIn = Mathf.Clamp01(e.life / 2f), fadeOut = Mathf.Clamp01((e.maxLife - e.life) / 3f);
                float flicker = 0.65f + 0.35f * Mathf.Sin(t * (3f + e.phase % 3f) + e.phase * 7f);
                e.el.style.left = x * w;
                e.el.style.top = e.y * h;
                e.el.style.opacity = fadeIn * fadeOut * flicker * Mathf.Lerp(0.35f, 1f, e.y);
            }
        }

        // ================================================================== tick
        void Update()
        {
            float dt = Time.unscaledDeltaTime, t = Time.unscaledTime;
            TickEmbers(dt, t);
            LevelSelectView.Drift(bgFront, t - frontBorn);
            LevelSelectView.Drift(bgBack, t - backBorn);
            levels.Tick(page == Page.Levels && !ModalOpen && !locked ? input : null);
            if (coop != null)
            {
                coop.Tick(page == Page.Coop && !ModalOpen && !locked && t >= inputAfter ? input : null);
                if (page == Page.Coop && footKeys != null) { footKeys.text = coop.Keys(Pad); footHelp.text = coop.Help; }
            }
            if (emblem != null) emblem.Spin(dt);
            if (page == Page.Title) pressAny.style.opacity = 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(t * 2.2f));
            else pressAny.style.opacity = StyleKeyword.Null;
            if ((page == Page.Title || page == Page.Main) && !locked && t - vistaAt > vistaHold) NextVista(false);

            if (locked || input == null || t < inputAfter) { input?.ConsumeNavigate(); return; }
            if (ModalOpen) { TickModal(); return; }
            switch (page)
            {
                case Page.Title:
                    if (input.AnyGameplayButtonPressed) { AudioManager.Play2D(Sfx.Title, 0.6f); GoTo(Page.Main, false); }
                    break;
                case Page.Main:
                {
                    var nav = input.ConsumeNavigate();
                    if (nav.y != 0) SelectItem(NextVisible(itemIndex, -nav.y), true);
                    if (input.SubmitPressed) Activate();
                    else if (input.CancelPressed) Back();
                    break;
                }
                case Page.Options: TickList(optionsList); break;
                case Page.Settings:
                    if (settingsList.Busy(input)) break;
                    if (input.TabLeftPressed) { SetSettingsTab(settingsTab - 1, true); break; }
                    if (input.TabRightPressed) { SetSettingsTab(settingsTab + 1, true); break; }
                    TickList(settingsList);
                    break;
                case Page.Reset: TickList(resetList); break;
                case Page.Coop: break;               // the co-op page reads its own input (Tick above)
            }
        }

        void TickList(OptionList l)
        {
            if (l.Busy(input)) return;
            l.Navigate(input.ConsumeNavigate(), input.SubmitPressed);
            if (!ModalOpen && input.CancelPressed) Back();
        }

        void TickModal()
        {
            var nav = input.ConsumeNavigate();
            if (nav.x != 0) { modalIndex = Mathf.Clamp(modalIndex + nav.x, 0, 1); RefreshModal(); AudioManager.Play2D(Sfx.UIMove, 0.4f); }
            if (input.SubmitPressed) ModalChoose();
            else if (input.CancelPressed) CloseModal(true);
        }
    }

    /// <summary>
    /// The emblem behind the title: the growth rings of a felled trunk, slightly eccentric, cracked in places,
    /// turning very slowly (alternate rings the other way).
    /// </summary>
    public class RingEmblem : VisualElement
    {
        struct Ring { public float r, w, a, speed, seed; public Vector2[] gaps; }

        static readonly Ring[] Rings =
        {
            new() { r = 0.16f, w = 1.4f, a = 0.35f, speed = 4f, seed = 1.3f, gaps = new[] { new Vector2(40, 70) } },
            new() { r = 0.27f, w = 1.2f, a = 0.3f, speed = -3f, seed = 2.1f, gaps = new[] { new Vector2(200, 230) } },
            new() { r = 0.36f, w = 1.8f, a = 0.42f, speed = 2.2f, seed = 3.7f, gaps = new[] { new Vector2(10, 22), new Vector2(150, 185) } },
            new() { r = 0.43f, w = 1f, a = 0.25f, speed = -1.6f, seed = 4.4f, gaps = new[] { new Vector2(300, 340) } },
            new() { r = 0.49f, w = 2.4f, a = 0.55f, speed = 1.1f, seed = 5.9f, gaps = new[] { new Vector2(80, 96), new Vector2(255, 262) } },
        };

        float t;

        public RingEmblem() => generateVisualContent += Draw;

        public void Spin(float dt)
        {
            t += dt;
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext mgc)
        {
            var rect = contentRect;
            if (rect.width < 4f) return;
            var c = rect.center;
            float size = Mathf.Min(rect.width, rect.height);
            var p = mgc.painter2D;
            p.lineCap = LineCap.Round;
            const int Seg = 160;
            foreach (var ring in Rings)
            {
                p.strokeColor = new Color(0.86f, 0.72f, 0.45f, ring.a);
                p.lineWidth = ring.w;
                float rot = t * ring.speed;
                bool drawing = false;
                p.BeginPath();
                for (int i = 0; i <= Seg; i++)
                {
                    float deg = i * 360f / Seg;
                    bool gap = false;
                    foreach (var g in ring.gaps) if (deg >= g.x && deg <= g.y) gap = true;
                    if (gap) { drawing = false; continue; }
                    float th = (deg + rot) * Mathf.Deg2Rad;
                    // tree rings are never quite round
                    float r = ring.r * size * (1f + 0.025f * Mathf.Sin(3f * th + ring.seed) + 0.014f * Mathf.Sin(5f * th + ring.seed * 2f));
                    var pt = c + new Vector2(Mathf.Cos(th), Mathf.Sin(th)) * r + new Vector2(size * 0.012f, size * 0.006f) * (ring.r * 2f);
                    if (!drawing) { p.MoveTo(pt); drawing = true; }
                    else p.LineTo(pt);
                }
                p.Stroke();
            }
            // the pith
            p.fillColor = new Color(0.96f, 0.87f, 0.65f, 0.6f);
            p.BeginPath();
            p.Arc(c + new Vector2(size * 0.004f, size * 0.002f), 3f, Angle.Degrees(0), Angle.Degrees(360));
            p.Fill();
        }
    }
}
