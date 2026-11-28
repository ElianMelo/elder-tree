using System.Collections;
using System.Collections.Generic;
using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.Combat;
using ElderTree.Enemies;
using ElderTree.InputHandling;
using ElderTree.Player;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace ElderTree.UI
{
    /// <summary>
    /// Runtime HUD built in code on UI Toolkit (styles in ElderTreeHUD.uss): status bars with chip
    /// damage, equipment cross, lock-on reticle, enemy bar with accumulating damage (Elden Ring
    /// style), device-adaptive control hints, title card, "FELLED" banner and a pause menu that is
    /// fully navigable with a controller.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public partial class HUD : MonoBehaviour
    {
        public StyleSheet styleSheet;
        public Texture2D clubIcon;
        public PlayerStats stats;
        public PlayerController player;
        public LockOnSystem lockOn;

        public static HUD Instance { get; private set; }
        /// <summary>Any menu that takes the cursor and pauses the game (pause, level select or F1 debug).</summary>
        public bool MenuOpen => PauseOpen || DebugMenuOpen || LevelSelectOpen;
        public bool PauseOpen => pauseRoot != null && pauseRoot.style.display == DisplayStyle.Flex;
        public bool TitleShowing { get; private set; }
        [Header("Title card (per scene)")]
        public string titleKicker = "A Night Watch Soulslike";
        public string titleMain = "ELDER";
        public string titleSub = "TREE";

        VisualElement root;
        Bar hp, fp, st, spirit;
        VisualElement spiritPips;
        float spiritFlash;
        int spiritLevelShown = -1;
        VisualElement reticle, statusBox, equipBox, equipName;
        Label hintsTitle;
        VisualElement hintsBox, hintsList;
        float hintsShownAt;
        bool hintsExpanded = true;
        PlayerInputReader input;
        Camera cam;
        FontAsset serif;

        class Bar
        {
            public VisualElement frame, chip, fill;
            public float shown = 1f, chipValue = 1f, chipHoldUntil;
            public float maxWidthPx;
        }

        void Awake() => Instance = this;
        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Settings.Changed -= OnSettingsChanged;
        }

        Settings.HintsMode hintsPref;

        /// <summary>
        /// Does this stage show the control list on arrival? By default only the training grounds (the tutorial) do;
        /// elsewhere the list stays hidden until the Help button asks for it.
        /// </summary>
        bool HintsHere => Settings.ControlHints switch
        {
            Settings.HintsMode.EveryStage => true,
            Settings.HintsMode.Off => false,
            _ => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == SceneFlow.Training,
        };

        /// <summary>The Control hints option was changed in a menu: show or hide the list right away.</summary>
        void OnSettingsChanged()
        {
            if (hintsBox == null || Settings.ControlHints == hintsPref) return;
            hintsPref = Settings.ControlHints;
            SetHints(HintsHere);
        }

        void Start()
        {
            cam = Camera.main;
            input = PlayerInputReader.Instance;
            var doc = GetComponent<UIDocument>();
            root = doc.rootVisualElement;
            root.Clear();
            if (styleSheet) root.styleSheets.Add(styleSheet);
            root.AddToClassList("hud-root");
            root.pickingMode = PickingMode.Ignore;
            serif = LoadSerif();

            BuildStatus();
            BuildEquipment();
            BuildReticle();
            BuildCrosshair();
            BuildEnemyBars();
            BuildHints();
            BuildBanner();
            BuildBossUI();
            BuildCoop();
            BuildMaskMarks();
            BuildPause();
            BuildDebugMenu();
            BuildMasks();
            BuildTitle();
            BuildLevelSelect();
            hintsPref = Settings.ControlHints;
            SetHints(HintsHere);
            Settings.Changed += OnSettingsChanged;

            if (input != null) input.DeviceChanged += _ => { RefreshHints(); RefreshMaskBar(); };
            if (lockOn != null) lockOn.Changed += OnLockChanged;
            foreach (var d in FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None))
            {
                d.Damaged += OnDummyDamaged;
                d.Felled += OnDummyFelled;
            }
        }

        static FontAsset LoadSerif() => UIFonts.Serif;

        void ApplySerif(VisualElement e)
        {
            if (serif != null) e.style.unityFontDefinition = FontDefinition.FromSDFFont(serif);
        }

        static VisualElement El(string cls, VisualElement parent = null)
        {
            var e = new VisualElement();
            if (!string.IsNullOrEmpty(cls)) foreach (var c in cls.Split(' ')) e.AddToClassList(c);
            e.pickingMode = PickingMode.Ignore;
            parent?.Add(e);
            return e;
        }

        static Label Lbl(string text, string cls, VisualElement parent = null)
        {
            var l = new Label(text);
            foreach (var c in cls.Split(' ')) l.AddToClassList(c);
            l.pickingMode = PickingMode.Ignore;
            parent?.Add(l);
            return l;
        }

        // ------------------------------------------------------------------ status bars
        void BuildStatus()
        {
            statusBox = El("status", root);
            hp = MakeBar(statusBox, "bar--hp", stats.maxHealth * 0.62f, 19);
            fp = MakeBar(statusBox, "bar--fp", stats.maxMana * 2.2f, 13);
            st = MakeBar(statusBox, "bar--st", stats.maxStamina * 2.6f, 13);
            // long sword spirit gauge: only shown while the current weapon has one
            spirit = MakeBar(statusBox, "bar--spirit", stats.maxStamina * 1.9f, 11);
            spiritPips = El("spirit__pips", spirit.frame);
            for (int i = 0; i < 3; i++) El("spirit__pip", spiritPips);
            spirit.frame.style.display = DisplayStyle.None;
            BuildCharges();
        }

        void TickSpirit(float dt)
        {
            bool on = player != null && player.HasSpiritGauge;
            spirit.frame.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            TickBar(spirit, player.Spirit / 100f, dt);
            int lvl = player.SpiritLevel;
            if (lvl != spiritLevelShown)
            {
                if (lvl > spiritLevelShown && spiritLevelShown >= 0) spiritFlash = 1f;
                spiritLevelShown = lvl;
                var m = player.Melee;
                Color c = lvl > 0 && m != null && m.spiritLevelColors.Length > 0 ? m.spiritLevelColors[Mathf.Min(lvl, m.spiritLevelColors.Length) - 1] : new Color(0.62f, 0.78f, 0.86f);
                spirit.fill.style.backgroundColor = new Color(c.r, c.g, c.b, 1f);
                for (int i = 0; i < spiritPips.childCount; i++)
                    spiritPips[i].style.backgroundColor = i < lvl ? new Color(c.r, c.g, c.b, 1f) : new Color(0.1f, 0.1f, 0.12f, 0.8f);
            }
            spiritFlash = Mathf.MoveTowards(spiritFlash, 0f, dt * 2f);
            // the current level's time drains along the frame
            float k = player.SpiritLevelTime01;
            spirit.frame.style.borderBottomColor = Color.Lerp(new Color(0.85f, 0.7f, 0.42f, 0.55f), Color.white, spiritFlash);
            spiritPips.style.opacity = player.SpiritLevel > 0 ? 0.55f + 0.45f * k : 0.6f;
        }

        Bar MakeBar(VisualElement parent, string mod, float widthPx, float heightPx)
        {
            var b = new Bar { maxWidthPx = widthPx };
            b.frame = El($"bar {mod}", parent);
            b.frame.style.width = widthPx;
            b.frame.style.height = heightPx;
            var inner = El("bar__inner", b.frame);
            b.chip = El("bar__chip", inner);
            b.fill = El("bar__fill", inner);
            El("bar__shine", inner);
            return b;
        }

        void TickBar(Bar b, float normalized, float dt)
        {
            normalized = Mathf.Clamp01(normalized);
            float prev = b.shown;
            b.shown = Mathf.Lerp(b.shown, normalized, Ease.Damp(18f, dt));
            if (normalized < prev - 0.001f) b.chipHoldUntil = Time.unscaledTime + 0.55f;
            if (normalized > b.chipValue) b.chipValue = normalized;
            else if (Time.unscaledTime > b.chipHoldUntil) b.chipValue = Mathf.MoveTowards(b.chipValue, normalized, dt * 0.6f);
            b.fill.style.width = Length.Percent(b.shown * 100f);
            b.chip.style.width = Length.Percent(b.chipValue * 100f);
        }

        // ------------------------------------------------------------------ equipment cross
        void BuildEquipment()
        {
            var cross = equipBox = El("equip", root);
            El("equip__slot equip__slot--up", cross);
            El("equip__slot equip__slot--left", cross);
            var right = El("equip__slot equip__slot--right equip__slot--active", cross);
            if (clubIcon) { var img = El("equip__icon", right); img.style.backgroundImage = clubIcon; }
            var down = El("equip__slot equip__slot--down", cross);
            Lbl("—", "equip__empty", down);
            BuildBowSlot(cross);
            BuildPotionSlot(cross);
            var name = Lbl("Elder Club", "equip__name", root);
            ApplySerif(name);
            equipName = name;
        }

        // ------------------------------------------------------------------ lock-on reticle
        void BuildReticle()
        {
            reticle = El("reticle", root);
            El("reticle__ring", reticle);
            El("reticle__dot", reticle);
            reticle.style.display = DisplayStyle.None;
        }

        float reticlePop;

        void OnLockChanged(Targetable t)
        {
            reticle.style.display = t != null ? DisplayStyle.Flex : DisplayStyle.None;
            reticlePop = 1f;
        }

        // ------------------------------------------------------------------ main update
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (cam == null) cam = Camera.main;
            if (stats != null)
            {
                TickBar(hp, stats.Health / stats.maxHealth, dt);
                TickBar(fp, stats.Mana / stats.maxMana, dt);
                TickBar(st, Mathf.Max(0f, stats.Stamina) / stats.maxStamina, dt);
                TickSpirit(dt);
                TickCharges(dt);
                TickPotion(dt);
            }

            // reticle
            if (lockOn != null && lockOn.Current != null && cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(lockOn.Current.LockPosition);
                if (vp.z > 0)
                {
                    var size = root.layout.size;
                    reticle.style.left = vp.x * size.x;
                    reticle.style.top = (1 - vp.y) * size.y;
                    reticlePop = Mathf.MoveTowards(reticlePop, 0f, dt * 5f);
                    float s = 1f + Ease.OutCubic(reticlePop) * 1.2f;
                    reticle.style.scale = new Scale(new Vector3(s, s, 1));
                    reticle.style.opacity = 1f - reticlePop * 0.7f;
                }
            }

            TickBow(dt);
            TickEnemyBars(dt);
            TickHints();
            TickBanner(dt);
            TickBossBar(dt);
            TickCoop(dt);
            TickMaskMarks();
            TickLevelSelect();
            TickDebugMenu(dt);
            TickMasks(dt);
            TickPause();
            TickTitle(dt);
        }

        // ------------------------------------------------------------------ hints
        struct Hint { public string pad, kbm, label; }

        /// <summary>Control hints built from the live bindings, so remaps show up everywhere.</summary>
        static List<Hint> Hints()
        {
            static string K(string a, bool pad, int nth = 0) => ControlBindings.Key(a, pad, nth);
            return new List<Hint>
            {
                new Hint { pad = K("Light", true), kbm = K("Light", false), label = "Light attack" },
                new Hint { pad = K("Heavy", true), kbm = $"{K("Modifier", false)} + {K("Light", false)}", label = "Heavy attack (hold to charge)" },
                new Hint { pad = $"{K("Light", true)} + {K("Heavy", true)}", kbm = $"{K("Modifier", false)} + {K("Aim", false)}", label = "Special attack (1 charge, from hits)" },
                new Hint { pad = K("Aim", true), kbm = K("Aim", false), label = "Ranged: hold to aim" },
                new Hint { pad = $"{K("Aim", true)} + {K("Heavy", true)}", kbm = $"{K("Aim", false)} + {K("Light", false)}", label = "Shoot (mana per shot)" },
                new Hint { pad = K("Guard", true), kbm = K("Guard", false), label = "Guard" },
                new Hint { pad = K("UseItem", true), kbm = K("UseItem", false), label = "Drink health potion" },
                new Hint { pad = K("Dodge", true), kbm = K("Dodge", false), label = "Roll / backstep · hold: sprint" },
                new Hint { pad = K("Jump", true), kbm = K("Jump", false), label = "Jump" },
                new Hint { pad = K("LockOn", true), kbm = $"{K("LockOn", false)} / {K("LockOn", false, 1)}", label = "Lock on · reset camera" },
                new Hint { pad = "R-Stick ←→", kbm = "Mouse flick / wheel", label = "Switch target" },
                new Hint { pad = ControlBindings.MaskRange(true), kbm = ControlBindings.MaskRange(false), label = "Masks: wear · again to remove" },
                new Hint { pad = K("Walk", true), kbm = K("Walk", false), label = "Walk toggle" },
                new Hint { pad = "Start", kbm = "Esc", label = "Menu" },
            };
        }

        void BuildHints()
        {
            hintsBox = El("hints", root);
            hintsTitle = Lbl("Controls", "hints__title", hintsBox);
            ApplySerif(hintsTitle);
            hintsList = El("hints__list", hintsBox);
            RefreshHints();
            hintsShownAt = float.MaxValue;
        }

        public void RefreshHints()
        {
            if (hintsList == null) return;
            hintsList.Clear();
            bool pad = input != null && input.Device == InputDeviceKind.Gamepad;
            foreach (var h in Hints())
            {
                var row = El("hint", hintsList);
                Lbl(pad ? h.pad : h.kbm, "glyph", row);
                Lbl(h.label, "hint__label", row);
            }
            var toggle = El("hint hint--toggle", hintsList);
            Lbl(ControlBindings.Key("Help", pad), "glyph glyph--small", toggle);
            Lbl("Show / hide controls", "hint__label hint__label--dim", toggle);
        }

        void TickHints()
        {
            if (input != null && input.ConsumeHelp()) SetHints(!hintsExpanded);
            if (hintsExpanded && Time.unscaledTime - hintsShownAt > 28f) SetHints(false);
        }

        /// <summary>Open or fold the control list. Folded, it keeps a one-line "show controls" tab on a stage that shows
        /// hints, and disappears entirely on one that doesn't (the Help button still brings it back).</summary>
        void SetHints(bool expanded)
        {
            hintsExpanded = expanded;
            hintsBox.style.display = expanded || HintsHere ? UnityEngine.UIElements.DisplayStyle.Flex : UnityEngine.UIElements.DisplayStyle.None;
            hintsShownAt = Time.unscaledTime;
            hintsBox.EnableInClassList("hints--collapsed", !expanded);
        }

        // ------------------------------------------------------------------ helpers
        public static IEnumerator FadeElement(VisualElement e, float from, float to, float dur)
        {
            yield return Ease.Tween(dur, t => e.style.opacity = Mathf.Lerp(from, to, t), Ease.OutCubic);
        }
    }
}
