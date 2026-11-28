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
    /// Esc / Start menu: Game (resume, summons, reset, level select, main menu, quit), then the same pages as the
    /// main menu (<see cref="SettingsPages"/>): Options (cinematics, hints, shake, camera), Graphics, Audio and
    /// Controls (remapping for keyboard &amp; mouse and gamepad). Tabs switch with Q / E or LB / RB; everything
    /// also works with the mouse.
    /// </summary>
    public partial class HUD
    {
        VisualElement pauseRoot, pauseBody, pausePanel, pauseMenuCol;
        OptionList options;
        Label pauseFoot, pauseHelp, tabPrevKey, tabNextKey;
        readonly List<Label> menuItems = new();
        readonly List<Label> tabLabels = new();
        int menuIndex, tabIndex;

        public Action ResetTrainingRequested;
        public Action QuitRequested;
        public Action MainMenuRequested;
        public Action<bool> PauseChanged;

        const int TabGame = 0, TabOptions = 1, TabGraphics = 2, TabAudio = 3, TabControls = 4;
        static readonly string[] TabNames = { "GAME", "OPTIONS", "GRAPHICS", "AUDIO", "CONTROLS" };
        static readonly string[] MenuNames = { "Resume", "Summon Crocodile", "Summon Shark", "Summon Druid", "Summon Fairy", "Reset Training", "Level Select", "Main Menu", "Quit" };

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

            options = new OptionList(frame, ApplySerif);
            options.Changed += RefreshMenu;
            options.BindingsChanged += OnBindingsChanged;

            var foot = El("pause__foot", frame);
            pauseHelp = Lbl("", "pause__help", foot);
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
            options.Scroll.style.display = game ? DisplayStyle.None : DisplayStyle.Flex;
            if (game) ShowPanel();
            else BuildOptions();
            if (sound) AudioManager.Play2D(Sfx.UIMove, 0.6f, 0.9f);
            RefreshMenu();
        }

        void RefreshMenu()
        {
            if (pauseRoot == null) return;
            for (int i = 0; i < menuItems.Count; i++)
            {
                menuItems[i].EnableInClassList("pause__item--active", i == menuIndex);
                menuItems[i].style.display = ItemVisible(i) ? DisplayStyle.Flex : DisplayStyle.None;
            }
            // co-op: the stage list is the party vote, and leaving means leaving the party
            if (menuItems.Count > 7)
            {
                menuItems[6].text = Coop.InStage ? "Return to Vote" : MenuNames[6];
                menuItems[7].text = Coop.InStage ? "Leave Party" : MenuNames[7];
            }

            bool pad = input != null && input.Device == InputDeviceKind.Gamepad;
            tabPrevKey.text = pad ? "LB" : "Q";
            tabNextKey.text = pad ? "RB" : "E";
            string tabs = pad ? "LB RB  Tabs" : "Q E  Tabs";
            string close = pad ? "B  Close" : "Esc  Close";
            string text;
            if (options.Listening) text = options.FooterAction(pad);
            else if (tabIndex == TabGame || options.Count == 0)
                text = $"{tabs}      {(pad ? "A  Select" : "Enter  Select")}      {close}";
            else text = $"{tabs}      {options.FooterAction(pad)}      {close}";
            pauseHelp.text = tabIndex == TabGame || options.Listening ? "" : options.CurrentHelp;
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
                    Lbl("Options, graphics, audio and controls are in the tabs above.", "panel__text panel__text--dim", pausePanel);
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
                {
                    var t = Lbl("Cold light, and the hum of wings.", "panel__flavor", pausePanel);
                    ApplySerif(t);
                    Lbl("Fairy hovers above the ground and fights from afar: fans of homing motes, a halo of motes fired one by one, " +
                        "and columns of light that fall on rings opening under your feet. Get close and she won't stay long: after a " +
                        "moment she takes off for the far side of the field. Hit her while she's spent after a big spell, and use your " +
                        "bow. Break her stance and she drops to her knees.", "panel__text", pausePanel);
                    Lbl("Press Select to summon.", "panel__text panel__text--dim", pausePanel);
                    break;
                }
                case 5:
                    Lbl("Stand every dummy back up at full health, send any boss away and restore your HP, FP and stamina.", "panel__text", pausePanel);
                    Lbl("Press Select to reset.", "panel__text panel__text--dim", pausePanel);
                    break;
                case 6:
                {
                    var t = Lbl("Choose your next foe.", "panel__flavor", pausePanel);
                    ApplySerif(t);
                    Lbl("Travel to another stage: the training grounds, or the domain of a boss. Felled bosses show their true face there.", "panel__text", pausePanel);
                    Lbl("Press Select to choose. F2 opens it from anywhere.", "panel__text panel__text--dim", pausePanel);
                    break;
                }
                case 7:
                {
                    var t = Lbl("Rest a while.", "panel__flavor", pausePanel);
                    ApplySerif(t);
                    Lbl("Leave this place and return to the title screen. Your felled bosses and masks are kept.", "panel__text", pausePanel);
                    Lbl("Press Select to leave.", "panel__text panel__text--dim", pausePanel);
                    break;
                }
                case 8:
                    Lbl("Return to the desktop?", "panel__text", pausePanel);
                    Lbl("Press Select to quit.", "panel__text panel__text--dim", pausePanel);
                    break;
            }
        }

        /// <summary>Co-op: only the host calls bosses, resets the yard or takes the party back to the vote.</summary>
        bool ItemVisible(int i) => !Coop.InStage || Coop.IsHost || !(i >= 1 && i <= 6);

        int NextVisibleItem(int from, int dir)
        {
            for (int k = 1; k <= menuItems.Count; k++)
            {
                int i = ((from + dir * k) % menuItems.Count + menuItems.Count) % menuItems.Count;
                if (ItemVisible(i)) return i;
            }
            return from;
        }

        void ActivateMenuItem()
        {
            AudioManager.Play2D(Sfx.UISelect, 0.7f);
            if (!ItemVisible(menuIndex)) return;
            if (menuIndex == 6 && Enemies.BossArena.ReturnHeld is string held) { SetPause(false); ShowBanner(held); return; }
            if (Coop.InStage && menuIndex == 6) { SetPause(false); Coop.ReturnToVote(); return; }
            if (Coop.InStage && menuIndex == 7) { SetPause(false); Coop.Leave(); return; }
            switch (menuIndex)
            {
                case 0: SetPause(false); return;
                case 1: SetPause(false); SummonBossRequested?.Invoke(0); return;
                case 2: SetPause(false); SummonBossRequested?.Invoke(1); return;
                case 3: SetPause(false); SummonBossRequested?.Invoke(2); return;
                case 4: SetPause(false); SummonBossRequested?.Invoke(3); return;
                case 5: ResetTrainingRequested?.Invoke(); SetPause(false); return;
                case 6: SetLevelSelect(true); return;
                case 7: SetPause(false); MainMenuRequested?.Invoke(); return;
                case 8: QuitRequested?.Invoke(); return;
            }
        }

        // ================================================================== option tabs
        void BuildOptions()
        {
            options.Clear();
            switch (tabIndex)
            {
                case TabOptions: SettingsPages.Game(options); break;
                case TabGraphics: SettingsPages.Graphics(options); break;
                case TabAudio: SettingsPages.Audio(options); break;
                case TabControls: SettingsPages.Controls(options); break;
            }
            options.Refresh();
        }

        /// <summary>Bindings changed (remap or reset): every glyph on screen follows.</summary>
        void OnBindingsChanged()
        {
            RefreshHints();
            RefreshMaskBar();
        }

        // ================================================================== tick
        void TickPause()
        {
            if (input == null) return;
            if (!TitleShowing && input.ConsumePause()) { TogglePause(); return; }
            if (!PauseOpen) return;

            if (options.Busy(input)) return;

            if (input.TabLeftPressed) { SetTab(tabIndex - 1, true); return; }
            if (input.TabRightPressed) { SetTab(tabIndex + 1, true); return; }

            var nav = input.ConsumeNavigate();
            if (tabIndex == TabGame)
            {
                if (nav.y != 0)
                {
                    menuIndex = NextVisibleItem(menuIndex, -nav.y);
                    AudioManager.Play2D(Sfx.UIMove, 0.5f);
                    ShowPanel();
                    RefreshMenu();
                }
                if (input.SubmitPressed) { ActivateMenuItem(); return; }
            }
            else options.Navigate(nav, input.SubmitPressed);
            if (input.CancelPressed) SetPause(false);
        }
    }
}
