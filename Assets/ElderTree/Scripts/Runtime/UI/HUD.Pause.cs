using System;
using System.Collections.Generic;
using System.Globalization;
using ElderTree.Audio;
using ElderTree.InputHandling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ElderTree.UI
{
    /// <summary>
    /// Esc / Start menu: Game (resume, summons, reset, boss select, quit), Graphics (PSX effect,
    /// pixelation, resolution, display mode, shake, hints), Audio (master / effects / music /
    /// ambience / footsteps) and Controls (camera options and remapping for keyboard &amp; mouse and
    /// gamepad). Tabs switch with Q / E or LB / RB; everything also works with the mouse.
    /// </summary>
    public partial class HUD
    {
        VisualElement pauseRoot, pauseBody, pausePanel, pauseMenuCol;
        ScrollView optionsScroll;
        Label pauseFoot, tabPrevKey, tabNextKey;
        readonly List<Label> menuItems = new();
        readonly List<Label> tabLabels = new();
        int menuIndex, tabIndex;
        float inputGuardUntil, sliderSoundAt;

        public Action ResetTrainingRequested;
        public Action QuitRequested;
        public Action BossSelectRequested;
        public Action<bool> PauseChanged;

        const int TabGame = 0, TabGraphics = 1, TabAudio = 2, TabControls = 3;
        static readonly string[] TabNames = { "GAME", "GRAPHICS", "AUDIO", "CONTROLS" };
        static readonly string[] MenuNames = { "Resume", "Summon Crocodile", "Summon Shark", "Summon Druid", "Reset Training", "Boss Select", "Quit" };

        enum RowKind { Value, Slider, Bind, Button }

        class OptRow
        {
            public RowKind kind;
            public VisualElement el;
            public Func<string> value;
            public Action<int> change;
            public Label valueLbl;
            public Func<float> get;
            public Action<float> set;
            public VisualElement track, fill, knob;
            public ControlBindings.Entry entry;
            public readonly Label[] cells = new Label[2];
            public Action press;
        }

        readonly List<OptRow> rows = new();
        int rowIndex, bindColumn;
        OptRow listeningRow;

        // display options are edited locally (Screen.* only updates a frame after a change)
        List<Vector2Int> resList;
        int resIndex, modeIndex;

        // ================================================================== build
        void BuildPause()
        {
            pauseRoot = El("pause", root);
            pauseRoot.pickingMode = PickingMode.Position;
            pauseRoot.style.display = DisplayStyle.None;
            var frame = El("pause__frame", pauseRoot);

            var top = El("pause__top", frame);
            var head = Lbl("SYSTEM", "pause__head", top);
            ApplySerif(head);
            var bar = El("pause__tabs", top);
            tabPrevKey = Lbl("Q", "pause__tabkey", bar);
            for (int i = 0; i < TabNames.Length; i++)
            {
                int idx = i;
                var t = Lbl(TabNames[i], "pause__tab", bar);
                ApplySerif(t);
                t.pickingMode = PickingMode.Position;
                t.RegisterCallback<ClickEvent>(_ => { if (!ControlBindings.Rebinding && idx != tabIndex) SetTab(idx, true); });
                tabLabels.Add(t);
            }
            tabNextKey = Lbl("E", "pause__tabkey", bar);

            pauseBody = El("pause__body", frame);
            pauseMenuCol = El("pause__menu", pauseBody);
            for (int i = 0; i < MenuNames.Length; i++)
            {
                int idx = i;
                var l = Lbl(MenuNames[i], "pause__item", pauseMenuCol);
                ApplySerif(l);
                l.pickingMode = PickingMode.Position;
                l.RegisterCallback<PointerEnterEvent>(_ => { if (menuIndex != idx) { menuIndex = idx; AudioManager.Play2D(Sfx.UIMove, 0.4f); ShowPanel(); RefreshMenu(); } });
                l.RegisterCallback<ClickEvent>(_ => { menuIndex = idx; ActivateMenuItem(); });
                menuItems.Add(l);
            }
            pausePanel = El("pause__panel", pauseBody);

            optionsScroll = new ScrollView(ScrollViewMode.Vertical);
            optionsScroll.AddToClassList("pause__options");
            optionsScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            optionsScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            optionsScroll.mouseWheelScrollSize = 60f;
            frame.Add(optionsScroll);

            var foot = El("pause__foot", frame);
            pauseFoot = Lbl("", "pause__footnote", foot);
        }

        // ================================================================== open / close / tabs
        public void TogglePause() => SetPause(!PauseOpen);

        public void SetPause(bool open)
        {
            if (TitleShowing || DebugMenuOpen) return;
            if (!open) ControlBindings.CancelRebind();
            pauseRoot.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            menuIndex = 0;
            SetTab(TabGame, false);
            AudioManager.Play2D(open ? Sfx.UISelect : Sfx.UIBack, 0.7f);
            PauseChanged?.Invoke(open);
            if (open)
            {
                pauseRoot.style.opacity = 0f;
                StartCoroutine(FadeElement(pauseRoot, 0f, 1f, 0.18f));
            }
        }

        void SetTab(int tab, bool sound)
        {
            tabIndex = (tab + TabNames.Length) % TabNames.Length;
            for (int i = 0; i < tabLabels.Count; i++) tabLabels[i].EnableInClassList("pause__tab--active", i == tabIndex);
            bool game = tabIndex == TabGame;
            pauseBody.style.display = game ? DisplayStyle.Flex : DisplayStyle.None;
            optionsScroll.style.display = game ? DisplayStyle.None : DisplayStyle.Flex;
            if (game) ShowPanel();
            else BuildOptions();
            if (sound) AudioManager.Play2D(Sfx.UIMove, 0.6f, 0.9f);
            RefreshMenu();
        }

        void RefreshMenu()
        {
            if (pauseRoot == null) return;
            for (int i = 0; i < menuItems.Count; i++)
                menuItems[i].EnableInClassList("pause__item--active", i == menuIndex);
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                bool on = i == rowIndex;
                r.el.EnableInClassList(r.kind == RowKind.Bind ? "bindrow--active" : "setting--active", on);
                if (r.kind == RowKind.Bind)
                    for (int c = 0; c < 2; c++) r.cells[c].EnableInClassList("bindrow__cell--active", on && c == bindColumn);
            }

            bool pad = input != null && input.Device == InputDeviceKind.Gamepad;
            tabPrevKey.text = pad ? "LB" : "Q";
            tabNextKey.text = pad ? "RB" : "E";
            string tabs = pad ? "LB RB  Tabs" : "Q E  Tabs";
            string close = pad ? "B  Close" : "Esc  Close";
            string text;
            if (listeningRow != null)
                text = bindColumn == 0 ? "Press a key or mouse button      Esc  Cancel" : "Press a button on the controller      Start  Cancel";
            else if (tabIndex == TabGame || rows.Count == 0)
                text = $"{tabs}      {(pad ? "A  Select" : "Enter  Select")}      {close}";
            else
            {
                var r = rows[Mathf.Clamp(rowIndex, 0, rows.Count - 1)];
                string act = r.kind switch
                {
                    RowKind.Bind => (pad ? "D-Pad ←→  Column      A  Remap" : "← →  Column      Enter  Remap"),
                    RowKind.Button => (pad ? "A  Confirm" : "Enter  Confirm"),
                    _ => (pad ? "D-Pad  Adjust" : "← →  Adjust"),
                };
                text = $"{tabs}      {act}      {close}";
            }
            pauseFoot.text = text;
        }

        // ================================================================== game tab
        void ShowPanel()
        {
            pausePanel.Clear();
            switch (menuIndex)
            {
                case 0:
                {
                    var t = Lbl("The night is young. Dawn is near.", "panel__flavor", pausePanel);
                    ApplySerif(t);
                    Lbl("Strike the training dummies to learn the weight of the club. Light attacks chain into a four-hit combo; " +
                        "heavy attacks can be held to charge. Lock on to strafe, and roll through danger.", "panel__text", pausePanel);
                    Lbl("Graphics, audio and control options are in the tabs above.", "panel__text panel__text--dim", pausePanel);
                    break;
                }
                case 1:
                {
                    var t = Lbl("A drone of four engines rolls in over the treeline.", "panel__flavor", pausePanel);
                    ApplySerif(t);
                    Lbl("Crocodile circles overhead raining bombs: watch the red rings on the ground and get out before they fill. " +
                        "Shoot its walking bombs before they reach you. When it lands and charges, dodge sideways; then it sits " +
                        "on the field for a few seconds. That's your chance to club it. Arrows break its stance in the air.", "panel__text", pausePanel);
                    Lbl("Press Select to summon.", "panel__text panel__text--dim", pausePanel);
                    break;
                }
                case 2:
                {
                    var t = Lbl("Squeaking rubber, then a fin cutting through the dirt.", "panel__flavor", pausePanel);
                    ApplySerif(t);
                    Lbl("Shark runs the arena on three sneakered legs. It sprints in and lunges with its jaws: roll through the bite, " +
                        "not away. It whips its tail at anyone on its flank or behind it, and spins like a top when you hug it; it reels " +
                        "afterwards. When it dives into the ground, watch the fin. Where the dirt starts to boil, it erupts jaws first. " +
                        "It is open after a skid, a spin, and every belly flop.", "panel__text", pausePanel);
                    Lbl("Press Select to summon.", "panel__text panel__text--dim", pausePanel);
                    break;
                }
                case 3:
                {
                    var t = Lbl("The treeline takes a step toward you.", "panel__flavor", pausePanel);
                    ApplySerif(t);
                    Lbl("Druid is a giant of bark and leaves on feet of roots. It stamps where you stand (jump the shockwave or be " +
                        "out of range), draws a leg back to punt you, and sinks low with its arms spread to grab you: roll through the " +
                        "sweep. Chop at its roots. When they crack and glow and it starts to sway, keep going: it topples onto its back " +
                        "and its face is open to criticals.", "panel__text", pausePanel);
                    Lbl("Press Select to summon.", "panel__text panel__text--dim", pausePanel);
                    break;
                }
                case 4:
                    Lbl("Stand every dummy back up at full health, send any boss away and restore your HP, FP and stamina.", "panel__text", pausePanel);
                    Lbl("Press Select to reset.", "panel__text panel__text--dim", pausePanel);
                    break;
                case 5:
                {
                    var t = Lbl("Choose your next foe.", "panel__flavor", pausePanel);
                    ApplySerif(t);
                    Lbl("Leave this place and return to the boss select screen. Felled bosses show their true face there.", "panel__text", pausePanel);
                    Lbl("Press Select to leave.", "panel__text panel__text--dim", pausePanel);
                    break;
                }
                case 6:
                    Lbl("Return to the desktop?", "panel__text", pausePanel);
                    Lbl("Press Select to quit.", "panel__text panel__text--dim", pausePanel);
                    break;
            }
        }

        void ActivateMenuItem()
        {
            AudioManager.Play2D(Sfx.UISelect, 0.7f);
            switch (menuIndex)
            {
                case 0: SetPause(false); return;
                case 1: SetPause(false); SummonBossRequested?.Invoke(0); return;
                case 2: SetPause(false); SummonBossRequested?.Invoke(1); return;
                case 3: SetPause(false); SummonBossRequested?.Invoke(2); return;
                case 4: ResetTrainingRequested?.Invoke(); SetPause(false); return;
                case 5: SetPause(false); BossSelectRequested?.Invoke(); return;
                case 6: QuitRequested?.Invoke(); return;
            }
        }

        // ================================================================== option tabs
        static string OnOff(bool b) => b ? "On" : "Off";
        static string Pct(float v) => $"{Mathf.RoundToInt(v * 100f)}%";

        void BuildOptions()
        {
            optionsScroll.Clear();
            rows.Clear();
            listeningRow = null;
            rowIndex = 0;
            switch (tabIndex)
            {
                case TabGraphics:
                {
                    Section("PRESENTATION");
                    AddValue("PSX effect", () => OnOff(Settings.PsxEffect), _ => Settings.PsxEffect = !Settings.PsxEffect);
                    AddValue("Pixelation", () => OnOff(Settings.Pixelation), _ => Settings.Pixelation = !Settings.Pixelation);
                    AddValue("Camera shake", () => Pct(Settings.ShakeScale), d => Settings.ShakeScale = Mathf.Clamp(Settings.ShakeScale + d * 0.1f, 0f, 1.5f));
                    AddValue("Control hints", () => OnOff(hintsExpanded), _ => SetHints(!hintsExpanded));

                    Section("DISPLAY");
                    resList = Settings.Resolutions();
                    resIndex = Mathf.Max(0, resList.IndexOf(new Vector2Int(Screen.width, Screen.height)));
                    modeIndex = Array.IndexOf(Settings.DisplayModes, Screen.fullScreenMode);
                    if (modeIndex < 0) modeIndex = Settings.DisplayModes.Length - 1;
                    AddValue("Resolution", () => $"{resList[resIndex].x} × {resList[resIndex].y}", d =>
                    {
                        resIndex = Mathf.Clamp(resIndex + d, 0, resList.Count - 1);
                        Settings.SetDisplay(resList[resIndex], Settings.DisplayModes[modeIndex]);
                    });
                    AddValue("Display mode", () => Settings.DisplayModeName(Settings.DisplayModes[modeIndex]), d =>
                    {
                        modeIndex = (modeIndex + d + Settings.DisplayModes.Length) % Settings.DisplayModes.Length;
                        Settings.SetDisplay(resList[resIndex], Settings.DisplayModes[modeIndex]);
                    });
                    break;
                }
                case TabAudio:
                    Section("VOLUME");
                    AddSlider("Master", () => Settings.MasterVolume, v => Settings.MasterVolume = v, null);
                    AddSlider("Sound effects", () => Settings.SfxVolume, v => Settings.SfxVolume = v, Sfx.WhooshLight);
                    AddSlider("Music", () => Settings.MusicVolume, v => Settings.MusicVolume = v, null);
                    AddSlider("Ambience", () => Settings.AmbienceVolume, v => Settings.AmbienceVolume = v, null);
                    AddSlider("Footsteps", () => Settings.FootstepVolume, v => Settings.FootstepVolume = v, Sfx.Footstep);
                    break;
                case TabControls:
                {
                    Section("CAMERA");
                    AddValue("Camera sensitivity", () => Settings.CameraSensitivity.ToString("0.0", CultureInfo.InvariantCulture),
                        d => Settings.CameraSensitivity = Mathf.Clamp(Settings.CameraSensitivity + d * 0.1f, 0.2f, 3f));
                    AddValue("Invert vertical", () => OnOff(Settings.InvertY), _ => Settings.InvertY = !Settings.InvertY);
                    AddValue("Controller vibration", () => OnOff(Settings.Rumble), _ => Settings.Rumble = !Settings.Rumble);

                    Section("BINDINGS");
                    var head = El("bindrow bindrow--head", optionsScroll);
                    Lbl("Action", "bindrow__name", head);
                    Lbl("Keyboard & Mouse", "bindrow__cell bindrow__cell--head", head);
                    Lbl("Gamepad", "bindrow__cell bindrow__cell--head", head);
                    foreach (var e in ControlBindings.Entries) AddBind(e);
                    AddButton("Restore default bindings", () =>
                    {
                        ControlBindings.ResetAll();
                        OnBindingsChanged();
                    });
                    break;
                }
            }
        }

        void Section(string title)
        {
            var s = Lbl(title, "options__section", optionsScroll);
            ApplySerif(s);
        }

        OptRow NewRow(RowKind kind, VisualElement el)
        {
            var r = new OptRow { kind = kind, el = el };
            int idx = rows.Count;
            rows.Add(r);
            el.pickingMode = PickingMode.Position;
            el.RegisterCallback<PointerEnterEvent>(_ => HoverRow(idx, -1));
            return r;
        }

        void HoverRow(int idx, int column)
        {
            if (ControlBindings.Rebinding) return;
            bool moved = idx != rowIndex || (column >= 0 && column != bindColumn);
            rowIndex = idx;
            if (column >= 0) bindColumn = column;
            if (moved) RefreshMenu();
        }

        void AddValue(string name, Func<string> value, Action<int> change)
        {
            var row = El("setting", optionsScroll);
            var r = NewRow(RowKind.Value, row);
            r.value = value;
            r.change = change;
            Lbl(name, "setting__name", row);
            var right = El("setting__value-box", row);
            var left = Lbl("‹", "setting__arrow", right);
            r.valueLbl = Lbl(value(), "setting__value", right);
            var rightArrow = Lbl("›", "setting__arrow", right);
            left.pickingMode = rightArrow.pickingMode = PickingMode.Position;
            left.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); ChangeValue(r, -1); });
            rightArrow.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); ChangeValue(r, 1); });
            row.RegisterCallback<ClickEvent>(_ => ChangeValue(r, 1));
        }

        void ChangeValue(OptRow r, int d)
        {
            r.change(d);
            r.valueLbl.text = r.value();
            Settings.Apply();
            AudioManager.Play2D(Sfx.UIMove, 0.6f, 1.1f);
        }

        void AddSlider(string name, Func<float> get, Action<float> set, Sfx? preview)
        {
            var row = El("setting setting--slider", optionsScroll);
            var r = NewRow(RowKind.Slider, row);
            r.get = get;
            r.set = v =>
            {
                set(Mathf.Round(Mathf.Clamp01(v) * 100f) / 100f);
                Settings.Apply();
                if (Time.unscaledTime >= sliderSoundAt)
                {
                    sliderSoundAt = Time.unscaledTime + 0.09f;
                    if (preview.HasValue) AudioManager.Play2D(preview.Value, 0.9f);
                    else AudioManager.Play2D(Sfx.UIMove, 0.6f, 1.1f);
                }
            };
            Lbl(name, "setting__name", row);
            var right = El("setting__value-box", row);
            r.track = El("slider", right);
            r.track.pickingMode = PickingMode.Position;
            r.fill = El("slider__fill", r.track);
            r.knob = El("slider__knob", r.track);
            r.valueLbl = Lbl("", "slider__value", right);
            r.value = () => Pct(get());
            r.track.RegisterCallback<PointerDownEvent>(e =>
            {
                r.track.CapturePointer(e.pointerId);
                SlideTo(r, e.localPosition.x);
                e.StopPropagation();
            });
            r.track.RegisterCallback<PointerMoveEvent>(e => { if (r.track.HasPointerCapture(e.pointerId)) SlideTo(r, e.localPosition.x); });
            r.track.RegisterCallback<PointerUpEvent>(e => { if (r.track.HasPointerCapture(e.pointerId)) r.track.ReleasePointer(e.pointerId); });
            RefreshSlider(r);
        }

        void SlideTo(OptRow r, float x)
        {
            float w = r.track.resolvedStyle.width;
            if (w <= 1f) return;
            r.set(x / w);
            RefreshSlider(r);
        }

        void NudgeSlider(OptRow r, int d)
        {
            r.set(r.get() + d * 0.05f);
            RefreshSlider(r);
        }

        static void RefreshSlider(OptRow r)
        {
            float v = r.get();
            r.fill.style.width = Length.Percent(v * 100f);
            r.knob.style.left = Length.Percent(v * 100f);
            r.valueLbl.text = r.value();
        }

        void AddBind(ControlBindings.Entry e)
        {
            var row = El("bindrow", optionsScroll);
            var r = NewRow(RowKind.Bind, row);
            int idx = rows.Count - 1;
            r.entry = e;
            Lbl(e.label, "bindrow__name", row);
            for (int c = 0; c < 2; c++)
            {
                int col = c;
                bool fixedCell = (c == 0 ? e.kbm : e.pad) == null;
                var cell = Lbl("", fixedCell ? "bindrow__cell bindrow__cell--fixed" : "bindrow__cell", row);
                cell.pickingMode = PickingMode.Position;
                cell.RegisterCallback<PointerEnterEvent>(_ => HoverRow(idx, col));
                cell.RegisterCallback<ClickEvent>(ev =>
                {
                    ev.StopPropagation();
                    if (ControlBindings.Rebinding) return;
                    rowIndex = idx;
                    bindColumn = col;
                    StartBind(r, col);
                });
                r.cells[c] = cell;
            }
            RefreshBindRow(r);
        }

        static void RefreshBindRow(OptRow r)
        {
            var e = r.entry;
            r.cells[0].text = e.kbm != null ? ControlBindings.Glyph(e.kbm) : e.kbmFixed?.Invoke() ?? "—";
            r.cells[1].text = e.pad != null ? ControlBindings.Glyph(e.pad) : e.padFixed?.Invoke() ?? "—";
            r.cells[0].RemoveFromClassList("bindrow__cell--listening");
            r.cells[1].RemoveFromClassList("bindrow__cell--listening");
        }

        void StartBind(OptRow r, int col)
        {
            var slot = col == 0 ? r.entry.kbm : r.entry.pad;
            if (slot == null)
            {
                AudioManager.Play2D(Sfx.Knock, 0.5f);
                RefreshMenu();
                return;
            }
            if (!ControlBindings.StartRebind(slot, ok =>
                {
                    listeningRow = null;
                    inputGuardUntil = Time.unscaledTime + 0.25f;
                    if (ok) OnBindingsChanged();
                    else RefreshBindRow(r);
                    AudioManager.Play2D(ok ? Sfx.UISelect : Sfx.UIBack, 0.6f);
                    RefreshMenu();
                }))
            {
                AudioManager.Play2D(Sfx.Knock, 0.5f);
                return;
            }
            listeningRow = r;
            r.cells[col].text = col == 0 ? "Press a key…" : "Press a button…";
            r.cells[col].AddToClassList("bindrow__cell--listening");
            AudioManager.Play2D(Sfx.UISelect, 0.6f);
            RefreshMenu();
        }

        /// <summary>Bindings changed (remap or reset): every glyph on screen follows.</summary>
        void OnBindingsChanged()
        {
            foreach (var r in rows) if (r.kind == RowKind.Bind) RefreshBindRow(r);
            RefreshHints();
            RefreshMaskBar();
        }

        void AddButton(string name, Action press)
        {
            var row = El("setting setting--button", optionsScroll);
            var r = NewRow(RowKind.Button, row);
            r.press = press;
            Lbl(name, "setting__name", row);
            row.RegisterCallback<ClickEvent>(_ => { press(); AudioManager.Play2D(Sfx.UISelect, 0.7f); });
        }

        // ================================================================== tick
        void TickPause()
        {
            if (input == null) return;
            if (!TitleShowing && input.ConsumePause()) { TogglePause(); return; }
            if (!PauseOpen) return;

            if (ControlBindings.Rebinding)
            {
                // either device's cancel key backs out, whichever column is listening
                if ((Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                    (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame))
                    ControlBindings.CancelRebind();
                input.ConsumeNavigate();
                return;
            }
            // the key that finished a rebind must not also drive the menu
            if (Time.unscaledTime < inputGuardUntil) { input.ConsumeNavigate(); return; }

            if (input.TabLeftPressed) { SetTab(tabIndex - 1, true); return; }
            if (input.TabRightPressed) { SetTab(tabIndex + 1, true); return; }

            var nav = input.ConsumeNavigate();
            if (tabIndex == TabGame)
            {
                if (nav.y != 0)
                {
                    menuIndex = (menuIndex - nav.y + menuItems.Count) % menuItems.Count;
                    AudioManager.Play2D(Sfx.UIMove, 0.5f);
                    ShowPanel();
                    RefreshMenu();
                }
                if (input.SubmitPressed) { ActivateMenuItem(); return; }
            }
            else if (rows.Count > 0)
            {
                if (nav.y != 0)
                {
                    rowIndex = (rowIndex - nav.y + rows.Count) % rows.Count;
                    optionsScroll.ScrollTo(rows[rowIndex].el);
                    AudioManager.Play2D(Sfx.UIMove, 0.5f);
                    RefreshMenu();
                }
                var r = rows[rowIndex];
                if (nav.x != 0)
                {
                    switch (r.kind)
                    {
                        case RowKind.Value: ChangeValue(r, nav.x); break;
                        case RowKind.Slider: NudgeSlider(r, nav.x); break;
                        case RowKind.Bind:
                            int c = Mathf.Clamp(bindColumn + nav.x, 0, 1);
                            if (c != bindColumn) { bindColumn = c; AudioManager.Play2D(Sfx.UIMove, 0.5f); RefreshMenu(); }
                            break;
                    }
                }
                if (input.SubmitPressed)
                {
                    switch (r.kind)
                    {
                        case RowKind.Value: ChangeValue(r, 1); break;
                        case RowKind.Bind: StartBind(r, bindColumn); break;
                        case RowKind.Button: r.press(); AudioManager.Play2D(Sfx.UISelect, 0.7f); break;
                    }
                }
            }
            if (input.CancelPressed) SetPause(false);
        }
    }
}
