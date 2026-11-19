using System.Collections.Generic;
using ElderTree.Audio;
using ElderTree.Combat;
using ElderTree.Enemies;
using ElderTree.InputHandling;
using UnityEngine;
using UnityEngine.UIElements;

namespace ElderTree.UI
{
    public partial class HUD
    {
        // ================================================================== enemy bars + damage
        class EnemyBar
        {
            public TrainingDummy dummy;
            public VisualElement box, fill, chip;
            public Label dmg, tag;
            public float shown = 1f, chipV = 1f, chipHold, lastHit = -99f, accum, accumAt = -99f, tagUntil = -99f;
        }

        readonly Dictionary<TrainingDummy, EnemyBar> enemyBars = new();
        VisualElement enemyLayer;

        void BuildEnemyBars() => enemyLayer = El("enemy-layer", root);

        EnemyBar GetBar(TrainingDummy d)
        {
            if (enemyBars.TryGetValue(d, out var b)) return b;
            b = new EnemyBar { dummy = d };
            b.box = El("ebar", enemyLayer);
            var frame = El("ebar__frame", b.box);
            b.chip = El("ebar__chip", frame);
            b.fill = El("ebar__fill", frame);
            b.dmg = Lbl("", "ebar__dmg", b.box);
            b.tag = Lbl("", "ebar__tag", b.box);
            ApplySerif(b.tag);
            b.box.style.opacity = 0f;
            enemyBars[d] = b;
            return b;
        }

        void OnDummyDamaged(TrainingDummy d, DamageInfo info, bool poiseBroken)
        {
            var b = GetBar(d);
            if (Time.unscaledTime - b.accumAt > 1.6f) b.accum = 0f;
            b.accum += info.damage;
            b.accumAt = Time.unscaledTime;
            b.lastHit = Time.unscaledTime;
            b.chipHold = Time.unscaledTime + 0.5f;
            b.dmg.text = Mathf.RoundToInt(b.accum).ToString();
            b.dmg.RemoveFromClassList("ebar__dmg--pop");
            b.dmg.schedule.Execute(() => b.dmg.AddToClassList("ebar__dmg--pop")).StartingIn(0);
            b.dmg.style.scale = new Scale(new Vector3(1.35f, 1.35f, 1));
            if (poiseBroken || info.charged || info.critical)
            {
                b.tag.text = info.critical ? "CRITICAL" : poiseBroken ? "STAGGER" : "CHARGED";
                b.tagUntil = Time.unscaledTime + 1.1f;
            }
        }

        void TickEnemyBars(float dt)
        {
            if (cam == null) return;
            var size = root.layout.size;
            foreach (var b in enemyBars.Values)
            {
                if (b.dummy == null) continue;
                bool locked = lockOn != null && lockOn.Current != null && lockOn.Current.transform == b.dummy.transform;
                bool visible = b.dummy.IsAlive && (locked || Time.unscaledTime - b.lastHit < 4.5f);
                float targetAlpha = visible ? 1f : 0f;
                b.box.style.opacity = Mathf.MoveTowards(b.box.resolvedStyle.opacity, targetAlpha, dt * 4f);

                float n = b.dummy.Health / b.dummy.maxHealth;
                b.shown = Mathf.Lerp(b.shown, n, Ease.Damp(20f, dt));
                if (n > b.chipV) b.chipV = n;
                else if (Time.unscaledTime > b.chipHold) b.chipV = Mathf.MoveTowards(b.chipV, n, dt * 0.7f);
                b.fill.style.width = Length.Percent(b.shown * 100f);
                b.chip.style.width = Length.Percent(b.chipV * 100f);

                // accumulated damage number (fades after the combo ends)
                float since = Time.unscaledTime - b.accumAt;
                b.dmg.style.opacity = since < 1.6f ? 1f : Mathf.Clamp01(1f - (since - 1.6f) / 0.4f);
                var sc = b.dmg.resolvedStyle.scale.value.x;
                float ns = Mathf.Lerp(sc, 1f, Ease.Damp(14f, dt));
                b.dmg.style.scale = new Scale(new Vector3(ns, ns, 1));
                b.tag.style.opacity = Time.unscaledTime < b.tagUntil ? 1f : Mathf.MoveTowards(b.tag.resolvedStyle.opacity, 0f, dt * 3f);

                Vector3 head = b.dummy.transform.position + Vector3.up * 2.35f;
                Vector3 vp = cam.WorldToViewportPoint(head);
                if (vp.z <= 0f) { b.box.style.opacity = 0f; continue; }
                b.box.style.left = vp.x * size.x - 80f;
                b.box.style.top = (1f - vp.y) * size.y - 10f;
            }
        }

        // ================================================================== banner ("FELLED")
        VisualElement banner;
        Label bannerText;
        float bannerT = -1f;

        void BuildBanner()
        {
            banner = El("banner", root);
            El("banner__band", banner);
            bannerText = Lbl("", "banner__text", banner);
            ApplySerif(bannerText);
            banner.style.opacity = 0f;
        }

        void OnDummyFelled(TrainingDummy d) => ShowBanner($"{d.displayName.ToUpperInvariant()} FELLED");

        public void ShowBanner(string text)
        {
            bannerText.text = text;
            bannerT = 0f;
        }

        void TickBanner(float dt)
        {
            if (bannerT < 0f) return;
            bannerT += dt;
            const float inT = 0.45f, hold = 2.2f, outT = 0.9f;
            float a, s;
            if (bannerT < inT) { float k = bannerT / inT; a = Ease.OutCubic(k); s = Mathf.Lerp(1.12f, 1f, Ease.OutCubic(k)); }
            else if (bannerT < inT + hold) { a = 1f; s = 1f - (bannerT - inT) * 0.012f; }
            else if (bannerT < inT + hold + outT) { float k = (bannerT - inT - hold) / outT; a = 1f - k; s = 0.97f - k * 0.02f; }
            else { a = 0f; s = 1f; bannerT = -1f; }
            banner.style.opacity = a;
            bannerText.style.scale = new Scale(new Vector3(s, s, 1));
            bannerText.style.letterSpacing = Mathf.Lerp(18f, 10f, Mathf.Clamp01(bannerT / (inT + hold)));
        }

        // ================================================================== title card
        VisualElement title;
        Label titlePrompt;
        float titleFade = -1f;

        void BuildTitle()
        {
            title = El("title", root);
            var col = El("title__col", title);
            var kicker = Lbl("A Night Watch Soulslike", "title__kicker", col);
            ApplySerif(kicker);
            var main = Lbl("ELDER", "title__main", col);
            ApplySerif(main);
            var sub = Lbl("TREE", "title__sub", col);
            ApplySerif(sub);
            El("title__rule", col);
            titlePrompt = Lbl("Press any button", "title__prompt", col);
            TitleShowing = true;
            input?.SetGameplayEnabled(false);
            foreach (var e in new[] { statusBox, equipBox, equipName, hintsBox }) e.style.opacity = 0f;
        }

        void TickTitle(float dt)
        {
            if (TitleShowing && titleFade < 0f)
            {
                titlePrompt.style.opacity = 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.6f));
                if (Time.unscaledTime > 1.2f && input != null && input.AnyGameplayButtonPressed)
                {
                    titleFade = 0f;
                    AudioManager.Play2D(Sfx.Title, 0.9f);
                }
            }
            if (titleFade >= 0f)
            {
                titleFade += dt;
                float k = Mathf.Clamp01(titleFade / 1.1f);
                title.style.opacity = 1f - Ease.InOutCubic(k);
                title.style.scale = new Scale(Vector3.one * (1f + k * 0.04f));
                if (k >= 1f)
                {
                    title.style.display = DisplayStyle.None;
                    titleFade = -1f;
                    TitleShowing = false;
                    input?.ClearAll();
                    input?.SetGameplayEnabled(true);
                    SetHints(true);
                    foreach (var e in new[] { statusBox, equipBox, equipName, hintsBox }) StartCoroutine(FadeElement(e, 0f, 1f, 0.6f));
                }
            }
        }

        // ================================================================== pause menu
        VisualElement pauseRoot, pauseMenuCol, pausePanel;
        readonly List<Label> menuItems = new();
        int menuIndex;
        int panelIndex = -1;           // -1 = focus on left column
        readonly List<VisualElement> settingRows = new();
        int settingIndex;
        public System.Action ResetTrainingRequested;
        public System.Action QuitRequested;
        public System.Action<bool> PauseChanged;

        static readonly string[] MenuNames = { "Resume", "Controls", "Settings", "Summon Crocodile", "Summon Shark", "Summon Druid", "Reset Training", "Quit" };

        void BuildPause()
        {
            pauseRoot = El("pause", root);
            pauseRoot.style.display = DisplayStyle.None;
            var frame = El("pause__frame", pauseRoot);
            var head = Lbl("SYSTEM", "pause__head", frame);
            ApplySerif(head);
            var body = El("pause__body", frame);
            pauseMenuCol = El("pause__menu", body);
            foreach (var n in MenuNames)
            {
                var l = Lbl(n, "pause__item", pauseMenuCol);
                ApplySerif(l);
                menuItems.Add(l);
            }
            pausePanel = El("pause__panel", body);
            var foot = El("pause__foot", frame);
            Lbl("", "pause__footnote", foot).name = "footnote";
        }

        public void TogglePause() => SetPause(!PauseOpen);

        public void SetPause(bool open)
        {
            if (TitleShowing || DebugMenuOpen) return;
            pauseRoot.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            menuIndex = 0;
            panelIndex = -1;
            ShowPanel();
            RefreshMenu();
            AudioManager.Play2D(open ? Sfx.UISelect : Sfx.UIBack, 0.7f);
            PauseChanged?.Invoke(open);
            if (open) pauseRoot.style.opacity = 0f;
            if (open) StartCoroutine(FadeElement(pauseRoot, 0f, 1f, 0.18f));
        }

        void RefreshMenu()
        {
            for (int i = 0; i < menuItems.Count; i++)
                menuItems[i].EnableInClassList("pause__item--active", i == menuIndex);
            for (int i = 0; i < settingRows.Count; i++)
                settingRows[i].EnableInClassList("setting--active", panelIndex >= 0 && i == settingIndex);
            var foot = pauseRoot.Q<Label>("footnote");
            bool pad = input != null && input.Device == InputDeviceKind.Gamepad;
            foot.text = panelIndex >= 0
                ? (pad ? "D-Pad  Adjust      B  Back" : "← →  Adjust      Esc  Back")
                : (pad ? "A  Select      B  Close" : "Enter  Select      Esc  Close");
        }

        void ShowPanel()
        {
            pausePanel.Clear();
            settingRows.Clear();
            switch (menuIndex)
            {
                case 0:
                {
                    var t = Lbl("The night is young. Dawn is near.", "panel__flavor", pausePanel);
                    ApplySerif(t);
                    Lbl("Strike the training dummies to learn the weight of the club. Light attacks chain into a four-hit combo; " +
                        "heavy attacks can be held to charge. Lock on to strafe, and roll through danger.", "panel__text", pausePanel);
                    break;
                }
                case 1:
                {
                    var grid = El("controls-grid", pausePanel);
                    var hdr = El("controls-row controls-row--head", grid);
                    Lbl("Action", "controls-cell controls-cell--label", hdr);
                    Lbl("Gamepad", "controls-cell", hdr);
                    Lbl("Keyboard & Mouse", "controls-cell", hdr);
                    foreach (var h in Hints)
                    {
                        var row = El("controls-row", grid);
                        Lbl(h.label, "controls-cell controls-cell--label", row);
                        var c1 = El("controls-cell", row); Lbl(h.pad, "glyph", c1);
                        var c2 = El("controls-cell", row); Lbl(h.kbm, "glyph", c2);
                    }
                    break;
                }
                case 2:
                    AddSetting("Camera sensitivity", () => Settings.CameraSensitivity.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), d => Settings.CameraSensitivity = Mathf.Clamp(Settings.CameraSensitivity + d * 0.1f, 0.2f, 3f));
                    AddSetting("Invert vertical", () => Settings.InvertY ? "On" : "Off", _ => Settings.InvertY = !Settings.InvertY);
                    AddSetting("Master volume", () => $"{Mathf.RoundToInt(Settings.MasterVolume * 100)}%", d => Settings.MasterVolume = Mathf.Clamp01(Settings.MasterVolume + d * 0.05f));
                    AddSetting("Music volume", () => $"{Mathf.RoundToInt(Settings.MusicVolume * 100)}%", d => Settings.MusicVolume = Mathf.Clamp01(Settings.MusicVolume + d * 0.05f));
                    AddSetting("Effects volume", () => $"{Mathf.RoundToInt(Settings.EffectsVolume * 100)}%", d => Settings.EffectsVolume = Mathf.Clamp01(Settings.EffectsVolume + d * 0.05f));
                    AddSetting("Camera shake", () => $"{Mathf.RoundToInt(Settings.ShakeScale * 100)}%", d => Settings.ShakeScale = Mathf.Clamp(Settings.ShakeScale + d * 0.1f, 0f, 1.5f));
                    AddSetting("Controller vibration", () => Settings.Rumble ? "On" : "Off", _ => Settings.Rumble = !Settings.Rumble);
                    AddSetting("Show control hints", () => hintsExpanded ? "On" : "Off", _ => SetHints(!hintsExpanded));
                    break;
                case 3:
                {
                    var t = Lbl("A drone of four engines rolls in over the treeline.", "panel__flavor", pausePanel);
                    ApplySerif(t);
                    Lbl("Crocodile circles overhead raining bombs: watch the red rings on the ground and get out before they fill. " +
                        "Shoot its walking bombs before they reach you. When it lands and charges, dodge sideways; then it sits " +
                        "on the field for a few seconds. That's your chance to club it. Arrows break its stance in the air.", "panel__text", pausePanel);
                    Lbl("Press Select to summon.", "panel__text panel__text--dim", pausePanel);
                    break;
                }
                case 4:
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
                case 5:
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
                case 6:
                    Lbl("Stand every dummy back up at full health, send any boss away and restore your HP, FP and stamina.", "panel__text", pausePanel);
                    Lbl("Press Select to reset.", "panel__text panel__text--dim", pausePanel);
                    break;
                case 7:
                    Lbl("Return to the desktop?", "panel__text", pausePanel);
                    Lbl("Press Select to quit.", "panel__text panel__text--dim", pausePanel);
                    break;
            }
        }

        void AddSetting(string name, System.Func<string> value, System.Action<int> change)
        {
            var row = El("setting", pausePanel);
            Lbl(name, "setting__name", row);
            var right = El("setting__value-box", row);
            Lbl("‹", "setting__arrow", right);
            var v = Lbl(value(), "setting__value", right);
            Lbl("›", "setting__arrow", right);
            row.userData = (value, change, v);
            settingRows.Add(row);
        }

        void TickPause()
        {
            if (input == null) return;
            if (!TitleShowing && input.ConsumePause()) { TogglePause(); return; }
            if (!PauseOpen) return;

            var nav = input.ConsumeNavigate();
            if (panelIndex < 0)
            {
                if (nav.y != 0)
                {
                    menuIndex = (menuIndex - nav.y + menuItems.Count) % menuItems.Count;
                    AudioManager.Play2D(Sfx.UIMove, 0.5f);
                    ShowPanel();
                    RefreshMenu();
                }
                if (input.SubmitPressed)
                {
                    AudioManager.Play2D(Sfx.UISelect, 0.7f);
                    switch (menuIndex)
                    {
                        case 0: SetPause(false); return;
                        case 2: if (settingRows.Count > 0) { panelIndex = 0; settingIndex = 0; RefreshMenu(); } break;
                        case 3: SetPause(false); SummonBossRequested?.Invoke(0); return;
                        case 4: SetPause(false); SummonBossRequested?.Invoke(1); return;
                        case 5: SetPause(false); SummonBossRequested?.Invoke(2); return;
                        case 6: ResetTrainingRequested?.Invoke(); SetPause(false); return;
                        case 7: QuitRequested?.Invoke(); return;
                    }
                }
                if (input.CancelPressed) { SetPause(false); return; }
            }
            else
            {
                if (nav.y != 0)
                {
                    settingIndex = (settingIndex - nav.y + settingRows.Count) % settingRows.Count;
                    AudioManager.Play2D(Sfx.UIMove, 0.5f);
                    RefreshMenu();
                }
                if (nav.x != 0 || input.SubmitPressed)
                {
                    var (value, change, lbl) = ((System.Func<string>, System.Action<int>, Label))settingRows[settingIndex].userData;
                    change(nav.x != 0 ? nav.x : 1);
                    lbl.text = value();
                    Settings.Apply();
                    AudioManager.Play2D(Sfx.UIMove, 0.6f, 1.1f);
                }
                if (input.CancelPressed) { panelIndex = -1; AudioManager.Play2D(Sfx.UIBack, 0.6f); RefreshMenu(); }
            }
        }
    }
}
