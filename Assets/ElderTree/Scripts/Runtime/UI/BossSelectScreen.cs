using System.Collections;
using System.Collections.Generic;
using ElderTree.Audio;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace ElderTree.UI
{
    /// <summary>
    /// Megaman-style stage select. A 3x3 grid of portrait frames: the Elder Tree sits in the middle (the
    /// training yard), the bosses around it, sealed slots for the bosses still to come. A boss you have not
    /// felled shows only its silhouette; once felled, its in-game portrait. Picking a boss plays the
    /// stage-intro band (portrait + name typed out) and loads its arena.
    /// Pad: D-pad / left stick + A, Start. Keyboard: arrows / WASD + Enter / Space. Mouse: hover + click.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class BossSelectScreen : MonoBehaviour
    {
        [System.Serializable]
        public class Entry
        {
            public string id;                 // progress id ("crocodile"); empty = a sealed slot
            public string displayName;
            public string epithet;
            public string maskId;             // owning this mask also counts as felled
            public Texture2D portrait;        // in-game portrait (shown once felled)
            public Texture2D silhouette;      // shown until then
            public string scene;
            public string bossArg;            // SceneFlow.PendingBoss for shared arenas
            public bool alwaysRevealed;       // the Elder Tree in the middle
            [Range(0, 8)] public int cell;
        }

        public List<Entry> entries = new();
        public Texture2D background;
        public Texture2D fogLayer;
        public Texture2D vignette;

        VisualElement root, grid, band, fade;
        readonly VisualElement[] frames = new VisualElement[9];
        readonly Entry[] byCell = new Entry[9];
        Label infoName, infoSub, infoState, bandName, bandSub;
        VisualElement bandPortrait, fogA, fogB;
        FontAsset serif;
        int cursor = 4;
        bool locked;
        float navCooldown;
        Vector2Int lastNav;

        static readonly Color Gold = new(0.85f, 0.71f, 0.42f), GoldBright = new(0.96f, 0.87f, 0.65f), Dim = new(0.6f, 0.57f, 0.5f);
        static readonly Color Frame = new(0.22f, 0.2f, 0.16f), Sickly = new(0.55f, 0.75f, 0.35f);

        void Start()
        {
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            Time.timeScale = 1f;
            serif = LoadSerif();
            foreach (var e in entries) if (e != null && e.cell >= 0 && e.cell < 9) byCell[e.cell] = e;
            Build();
            Select(cursor, false);
            AudioManager.SetMusic(MusicMood.Explore);
            StartCoroutine(FadeIn());
        }

        static FontAsset LoadSerif()
        {
            foreach (var fam in new[] { "Cinzel", "Trajan Pro", "Palatino Linotype", "Book Antiqua", "Georgia", "Cambria", "Times New Roman" })
            {
                try { var fa = FontAsset.CreateFontAsset(fam, "Regular", 90); if (fa != null) return fa; }
                catch { /* not installed */ }
            }
            return null;
        }

        Label Text(string s, VisualElement parent, int size, Color c, bool serifFont = true, FontStyle style = FontStyle.Normal)
        {
            var l = new Label(s);
            l.style.fontSize = size;
            l.style.color = c;
            l.style.unityFontStyleAndWeight = style;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            l.style.marginBottom = 0; l.style.marginTop = 0; l.style.paddingBottom = 0; l.style.paddingTop = 0;
            if (serifFont && serif != null) l.style.unityFontDefinition = FontDefinition.FromSDFFont(serif);
            parent?.Add(l);
            return l;
        }

        static void Border(IStyle s, float w, Color c)
        {
            s.borderTopWidth = w; s.borderBottomWidth = w; s.borderLeftWidth = w; s.borderRightWidth = w;
            s.borderTopColor = c; s.borderBottomColor = c; s.borderLeftColor = c; s.borderRightColor = c;
        }

        static void Fill(IStyle s)
        {
            s.position = Position.Absolute; s.left = 0; s.right = 0; s.top = 0; s.bottom = 0;
        }

        // ------------------------------------------------------------------ layout
        void Build()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear();
            Fill(root.style);
            root.style.backgroundColor = new Color(0.02f, 0.03f, 0.025f);

            var bg = new VisualElement();
            Fill(bg.style);
            if (background != null)
            {
                bg.style.backgroundImage = background;
                bg.style.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat);
                bg.style.backgroundSize = new BackgroundSize(new Length(512, LengthUnit.Pixel), new Length(512, LengthUnit.Pixel));
            }
            root.Add(bg);
            fogA = FogSheet(0.16f); fogB = FogSheet(0.1f);

            var vig = new VisualElement();
            Fill(vig.style);
            if (vignette != null)
            {
                vig.style.backgroundImage = vignette;
                vig.style.backgroundSize = new BackgroundSize(new Length(100, LengthUnit.Percent), new Length(100, LengthUnit.Percent));
            }
            vig.pickingMode = PickingMode.Ignore;
            root.Add(vig);

            var col = new VisualElement();
            Fill(col.style);
            col.style.alignItems = Align.Center;
            col.style.justifyContent = Justify.Center;
            root.Add(col);

            Text("SELECT YOUR FOE", col, 46, Gold).style.letterSpacing = 10;
            var rule = new VisualElement();
            rule.style.width = 520; rule.style.height = 2; rule.style.marginTop = 8; rule.style.marginBottom = 18;
            rule.style.backgroundColor = new Color(Gold.r, Gold.g, Gold.b, 0.55f);
            col.Add(rule);

            grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.width = 3 * 252;
            col.Add(grid);
            for (int i = 0; i < 9; i++) grid.Add(Cell(i));

            var info = new VisualElement();
            info.style.marginTop = 6; info.style.alignItems = Align.Center; info.style.height = 104;
            col.Add(info);
            infoName = Text("", info, 38, GoldBright);
            infoName.style.letterSpacing = 6;
            infoSub = Text("", info, 20, Dim);
            infoSub.style.unityFontStyleAndWeight = FontStyle.Italic;
            infoState = Text("", info, 16, Dim, false);
            infoState.style.marginTop = 8; infoState.style.letterSpacing = 3;

            var hint = Text("D-Pad / Arrows: choose     A / Enter: challenge", root, 15, new Color(Dim.r, Dim.g, Dim.b, 0.8f), false);
            hint.style.position = Position.Absolute; hint.style.bottom = 18; hint.style.left = 0; hint.style.right = 0;
            hint.style.letterSpacing = 2;

            BuildBand();

            fade = new VisualElement();
            Fill(fade.style);
            fade.style.backgroundColor = Color.black;
            fade.pickingMode = PickingMode.Ignore;
            root.Add(fade);
        }

        VisualElement FogSheet(float alpha)
        {
            var f = new VisualElement();
            f.style.position = Position.Absolute;
            f.style.left = new Length(-50, LengthUnit.Percent); f.style.top = new Length(-10, LengthUnit.Percent);
            f.style.width = new Length(200, LengthUnit.Percent); f.style.height = new Length(120, LengthUnit.Percent);
            if (fogLayer != null)
            {
                f.style.backgroundImage = fogLayer;
                f.style.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat);
                f.style.backgroundSize = new BackgroundSize(new Length(900, LengthUnit.Pixel), new Length(900, LengthUnit.Pixel));
            }
            f.style.unityBackgroundImageTintColor = new Color(0.6f, 0.8f, 0.6f, alpha);
            f.pickingMode = PickingMode.Ignore;
            root.Add(f);
            return f;
        }

        VisualElement Cell(int i)
        {
            var e = byCell[i];
            var cell = new VisualElement();
            cell.style.width = 252; cell.style.height = 246;
            cell.style.alignItems = Align.Center;
            cell.style.justifyContent = Justify.FlexStart;

            var frame = new VisualElement();
            frame.style.width = 196; frame.style.height = 196;
            Border(frame.style, 4, Frame);
            frame.style.backgroundColor = new Color(0.03f, 0.04f, 0.035f);
            frame.style.paddingLeft = 4; frame.style.paddingRight = 4; frame.style.paddingTop = 4; frame.style.paddingBottom = 4;
            cell.Add(frame);
            frames[i] = frame;

            var pic = new VisualElement();
            pic.style.flexGrow = 1;
            Border(pic.style, 1, new Color(0, 0, 0, 0.9f));
            pic.style.alignItems = Align.Center; pic.style.justifyContent = Justify.Center;
            frame.Add(pic);

            string label;
            if (e == null || string.IsNullOrEmpty(e.scene))
            {
                pic.style.backgroundColor = new Color(0.05f, 0.055f, 0.05f);
                var q = Text("?", pic, 96, new Color(0.3f, 0.32f, 0.28f));
                q.style.unityFontStyleAndWeight = FontStyle.Bold;
                label = "SEALED";
            }
            else
            {
                var tex = Revealed(e) ? e.portrait : (e.silhouette != null ? e.silhouette : e.portrait);
                if (tex != null)
                {
                    pic.style.backgroundImage = tex;
                    pic.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
                }
                label = Revealed(e) || e.alwaysRevealed ? e.displayName.ToUpperInvariant() : "? ? ?";
                if (!e.alwaysRevealed && Revealed(e))
                {
                    var tag = Text("FELLED", pic, 13, new Color(0.85f, 0.25f, 0.2f), false, FontStyle.Bold);
                    tag.style.position = Position.Absolute; tag.style.right = 6; tag.style.bottom = 4; tag.style.letterSpacing = 2;
                }
            }
            var name = Text(label, cell, 18, e != null && !string.IsNullOrEmpty(e.scene) ? Gold : new Color(0.35f, 0.34f, 0.3f));
            name.style.marginTop = 6; name.style.letterSpacing = 4;

            int idx = i;
            cell.RegisterCallback<PointerEnterEvent>(_ => { if (!locked && cursor != idx) Select(idx, true); });
            cell.RegisterCallback<ClickEvent>(_ => { if (!locked) { Select(idx, false); Confirm(); } });
            return cell;
        }

        void BuildBand()
        {
            band = new VisualElement();
            band.style.position = Position.Absolute;
            band.style.left = 0; band.style.right = 0;
            band.style.top = new Length(50, LengthUnit.Percent);
            band.style.height = 300; band.style.marginTop = -150;
            band.style.backgroundColor = new Color(0.015f, 0.025f, 0.02f, 0.97f);
            band.style.borderTopWidth = 3; band.style.borderBottomWidth = 3;
            band.style.borderTopColor = Gold; band.style.borderBottomColor = Gold;
            band.style.flexDirection = FlexDirection.Row;
            band.style.alignItems = Align.Center;
            band.style.justifyContent = Justify.Center;
            band.style.display = DisplayStyle.None;
            root.Add(band);
            bandPortrait = new VisualElement();
            bandPortrait.style.width = 250; bandPortrait.style.height = 250;
            Border(bandPortrait.style, 3, Gold);
            bandPortrait.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            band.Add(bandPortrait);
            var txt = new VisualElement();
            txt.style.marginLeft = 50; txt.style.width = 640;
            band.Add(txt);
            bandName = Text("", txt, 72, GoldBright);
            bandName.style.unityTextAlign = TextAnchor.MiddleLeft;
            bandName.style.letterSpacing = 12;
            bandSub = Text("", txt, 24, Sickly);
            bandSub.style.unityTextAlign = TextAnchor.MiddleLeft;
            bandSub.style.unityFontStyleAndWeight = FontStyle.Italic;
        }

        bool Revealed(Entry e) => e != null && (e.alwaysRevealed || Progress.IsFelled(e.id, e.maskId));

        // ------------------------------------------------------------------ selection
        void Select(int i, bool sound)
        {
            cursor = i;
            if (sound) AudioManager.Play2D(Sfx.UIMove, 0.5f);
            var e = byCell[i];
            if (e == null || string.IsNullOrEmpty(e.scene))
            {
                infoName.text = "SEALED";
                infoSub.text = "A foe not yet awoken.";
                infoState.text = "";
                return;
            }
            bool rev = Revealed(e);
            infoName.text = rev ? e.displayName.ToUpperInvariant() : "? ? ?";
            infoSub.text = e.epithet;
            infoState.text = e.alwaysRevealed ? "TRAINING GROUNDS" : rev ? "FELLED  ·  CHALLENGE AGAIN" : "UNKNOWN";
        }

        void Update()
        {
            float t = Time.unscaledTime;
            // Megaman blink on the chosen frame
            for (int i = 0; i < 9; i++)
            {
                var f = frames[i];
                if (f == null) continue;
                bool on = i == cursor;
                bool blink = on && (locked ? Mathf.Repeat(t * 12f, 1f) < 0.5f : Mathf.Repeat(t * 2.4f, 1f) < 0.62f);
                Border(f.style, on ? 5 : 4, blink ? GoldBright : on ? Gold * 0.6f : Frame);
                f.style.scale = new Scale(Vector3.one * (on ? 1.05f : 1f));
            }
            if (fogA != null) fogA.style.translate = new Translate(new Length(Mathf.Repeat(t * 14f, 900f), LengthUnit.Pixel), 0);
            if (fogB != null) fogB.style.translate = new Translate(new Length(-Mathf.Repeat(t * 9f, 900f), LengthUnit.Pixel), new Length(Mathf.Sin(t * 0.2f) * 20f, LengthUnit.Pixel));
            if (locked) return;

            // one step per push; holding repeats after a short delay
            Vector2Int nav = ReadNav();
            navCooldown -= Time.unscaledDeltaTime;
            bool fresh = nav != lastNav;
            lastNav = nav;
            if (nav != Vector2Int.zero && (fresh || navCooldown <= 0f))
            {
                int x = cursor % 3, y = cursor / 3;
                x = (x + nav.x + 3) % 3;
                y = (y - nav.y + 3) % 3;
                Select(y * 3 + x, true);
                navCooldown = fresh ? 0.42f : 0.16f;
            }
            if (ConfirmPressed()) Confirm();
        }

        static Vector2Int ReadNav()
        {
            Vector2 v = Vector2.zero;
            var k = Keyboard.current;
            if (k != null)
            {
                if (k.leftArrowKey.isPressed || k.aKey.isPressed) v.x -= 1;
                if (k.rightArrowKey.isPressed || k.dKey.isPressed) v.x += 1;
                if (k.upArrowKey.isPressed || k.wKey.isPressed) v.y += 1;
                if (k.downArrowKey.isPressed || k.sKey.isPressed) v.y -= 1;
            }
            var g = Gamepad.current;
            if (g != null)
            {
                v += g.dpad.ReadValue();
                var s = g.leftStick.ReadValue();
                if (s.magnitude > 0.6f) v += s;
            }
            return new Vector2Int(Mathf.Abs(v.x) > 0.5f ? (int)Mathf.Sign(v.x) : 0, Mathf.Abs(v.y) > 0.5f ? (int)Mathf.Sign(v.y) : 0);
        }

        static bool ConfirmPressed()
        {
            var k = Keyboard.current;
            var g = Gamepad.current;
            return (k != null && (k.enterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame)) ||
                   (g != null && (g.buttonSouth.wasPressedThisFrame || g.startButton.wasPressedThisFrame));
        }

        void Confirm()
        {
            var e = byCell[cursor];
            if (e == null || string.IsNullOrEmpty(e.scene))
            {
                AudioManager.Play2D(Sfx.Knock, 0.7f);
                frames[cursor].experimental.animation.Start(0f, 1f, 260, (el, k) =>
                    el.style.translate = new Translate(Mathf.Sin(k * Mathf.PI * 6f) * 8f * (1f - k), 0));
                return;
            }
            locked = true;
            AudioManager.Play2D(Sfx.UISelect, 0.9f);
            StartCoroutine(Intro(e));
        }

        // ------------------------------------------------------------------ stage intro + load
        IEnumerator Intro(Entry e)
        {
            yield return new WaitForSecondsRealtime(0.55f);
            bool rev = Revealed(e);
            var tex = rev ? e.portrait : (e.silhouette != null ? e.silhouette : e.portrait);
            bandPortrait.style.backgroundImage = tex;
            bandName.text = "";
            bandSub.text = "";
            band.style.display = DisplayStyle.Flex;
            // band wipes in from the left
            float t = 0f;
            while (t < 0.35f)
            {
                t += Time.unscaledDeltaTime;
                float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.35f), 3f);
                band.style.translate = new Translate(new Length((1f - k) * -110f, LengthUnit.Percent), 0);
                yield return null;
            }
            band.style.translate = new Translate(0, 0);
            AudioManager.Play2D(Sfx.Title, 0.8f);
            string full = rev || e.alwaysRevealed ? e.displayName.ToUpperInvariant() : e.displayName.ToUpperInvariant();
            for (int i = 1; i <= full.Length; i++)
            {
                bandName.text = full.Substring(0, i);
                if (full[i - 1] != ' ') AudioManager.Play2D(Sfx.UIMove, 0.35f, 1.3f);
                yield return new WaitForSecondsRealtime(0.07f);
            }
            bandSub.text = e.epithet;
            yield return new WaitForSecondsRealtime(1.6f);
            yield return FadeTo(1f, 0.7f);
            SceneFlow.Load(e.scene, e.bossArg);
        }

        IEnumerator FadeIn()
        {
            fade.style.opacity = 1f;
            yield return null;
            yield return FadeTo(0f, 0.8f);
        }

        IEnumerator FadeTo(float to, float dur)
        {
            float from = fade.style.opacity.value, t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                fade.style.opacity = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / dur));
                yield return null;
            }
            fade.style.opacity = to;
        }
    }
}
