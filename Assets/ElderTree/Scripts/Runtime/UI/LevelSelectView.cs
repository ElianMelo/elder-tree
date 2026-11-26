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
    /// The level select: a full-screen page listing every stage of the <see cref="StageCatalog"/>, the highlighted
    /// stage's vista drifting behind it, and its name, ruler, portrait (a silhouette until felled), description and
    /// boss / mask state. Choosing a stage fades through a title card and loads it. One view for the main menu and the
    /// in-game level select (F2, the pause menu, the return after a victory). Styles: LevelSelect.uss.
    /// </summary>
    public class LevelSelectView
    {
        public readonly VisualElement Root;
        public bool IsOpen { get; private set; }
        public bool Travelling { get; private set; }
        /// <summary>Back / cancel / the back arrow: the owner decides where that leads.</summary>
        public event Action BackRequested;

        readonly MonoBehaviour host;
        readonly List<StageCatalog.Stage> stages;
        readonly string backLabel;
        readonly VisualElement info, portrait, chips, fade, card;
        VisualElement bgFront, bgBack;
        readonly Label region, ruler, epithet, desc, footLeft, footKeys, cardTitle, cardSub;
        readonly List<VisualElement> rows = new();
        int index;
        Texture2D shown;
        float frontBorn, backBorn, inputAfter;
        Coroutine vistaFade;

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

        /// <param name="backLabel">What the back button does here ("Back", "Close").</param>
        public LevelSelectView(VisualElement parent, MonoBehaviour host, StageCatalog catalog, string backLabel = "Back")
        {
            this.host = host;
            this.backLabel = backLabel;
            stages = catalog != null ? catalog.stages : new List<StageCatalog.Stage>();

            Root = El("ls-root ls-off", parent);
            Root.pickingMode = PickingMode.Position;            // blocks clicks to whatever is underneath
            Root.style.display = DisplayStyle.None;
            if (catalog != null && catalog.style != null) Root.styleSheets.Add(catalog.style);

            bgBack = El("ls-vista", Root);
            bgFront = El("ls-vista", Root);
            Stretch(El("ls-fill ls-grade", Root), MenuTextures.Shade);
            Stretch(El("ls-fill ls-grade", Root), MenuTextures.Vignette);
            Stretch(El("ls-side", Root), MenuTextures.Side);

            var page = El("ls-page", Root);
            var head = El("ls-head", page);
            var back = Lbl("‹", "ls-back", head);
            back.pickingMode = PickingMode.Position;
            back.RegisterCallback<ClickEvent>(_ => { if (IsOpen && !Travelling) Back(); });
            var titles = El("ls-titles", head);
            Lbl("TRAVEL", "ls-kicker", titles);
            Lbl("LEVEL SELECT", "ls-title", titles, true);
            El("ls-rule", page).style.backgroundImage = MenuTextures.Line;

            var list = El("ls-list", page);
            for (int i = 0; i < stages.Count; i++)
            {
                int idx = i;
                var row = El("ls-stage", list);
                Stretch(El("ls-stage__glow", row), MenuTextures.Glow);
                El("ls-stage__gem", row);
                var txt = El("ls-stage__text", row);
                Lbl("", "ls-stage__name", txt, true);
                Lbl("", "ls-stage__sub", txt);
                row.pickingMode = PickingMode.Position;
                row.RegisterCallback<PointerEnterEvent>(_ => { if (IsOpen && !Travelling && index != idx) Select(idx, true); });
                row.RegisterCallback<ClickEvent>(_ => { if (IsOpen && !Travelling) { Select(idx, false); Travel(stages[idx]); } });
                rows.Add(row);
            }

            info = El("ls-info", Root);
            portrait = El("ls-portrait", info);
            ruler = Lbl("", "ls-info__kicker", info);
            region = Lbl("", "ls-info__name", info, true);
            epithet = Lbl("", "ls-info__epithet", info, true);
            El("ls-info__rule", info).style.backgroundImage = MenuTextures.Line;
            desc = Lbl("", "ls-info__desc", info);
            chips = El("ls-chips", info);

            var foot = El("ls-foot", Root);
            El("ls-foot__rule", foot).style.backgroundImage = MenuTextures.Line;
            var frow = El("ls-foot__row", foot);
            footLeft = Lbl("", "ls-foot__left", frow);
            footKeys = Lbl("", "ls-foot__keys", frow);

            fade = El("ls-fill ls-fade", Root);
            fade.style.opacity = 0f;
            card = El("ls-fill ls-card", Root);
            card.style.opacity = 0f;
            cardTitle = Lbl("", "ls-card__title", card, true);
            El("ls-card__rule", card).style.backgroundImage = MenuTextures.Line;
            cardSub = Lbl("", "ls-card__sub", card);
        }

        // ================================================================== open / close
        /// <summary>Shows the view with <paramref name="scene"/>'s stage highlighted (else the last stage entered).</summary>
        public void Open(string scene = null)
        {
            if (stages.Count == 0) return;
            if (scene == null && SceneFlow.LastStage(out var last, out _)) scene = last;
            int at = stages.FindIndex(s => s.scene == scene);
            if (at >= 0) index = at;
            IsOpen = true;
            inputAfter = Time.unscaledTime + 0.15f;
            Root.style.display = DisplayStyle.Flex;
            Root.schedule.Execute(() => { if (IsOpen) Root.RemoveFromClassList("ls-off"); }).StartingIn(20);
            Refresh();
            shown = null;
            ShowVista(stages[index].vista, true);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Root.AddToClassList("ls-off");
            Root.schedule.Execute(() => { if (!IsOpen) Root.style.display = DisplayStyle.None; }).StartingIn(280);
        }

        void Back()
        {
            AudioManager.Play2D(Sfx.UIBack, 0.6f);
            BackRequested?.Invoke();
        }

        /// <summary>Re-reads progress (felled bosses, masks) into every row and the info block.</summary>
        public void Refresh()
        {
            int bosses = 0, felled = 0;
            for (int i = 0; i < stages.Count; i++)
            {
                var s = stages[i];
                bool down = s.Felled;
                if (s.HasBoss) { bosses++; if (down) felled++; }
                var row = rows[i];
                row.Q<Label>(className: "ls-stage__name").text = s.region.ToUpperInvariant();
                row.Q<Label>(className: "ls-stage__sub").text = !s.HasBoss ? "Training grounds" : down ? $"{s.boss}, felled" : "? ? ?";
                row.EnableInClassList("ls-stage--felled", down);
                row.EnableInClassList("ls-stage--training", !s.HasBoss);
            }
            footLeft.text = $"BOSSES FELLED   {felled} / {bosses}";
            Select(Mathf.Clamp(index, 0, stages.Count - 1), false);
        }

        void Select(int idx, bool sound)
        {
            index = idx;
            for (int i = 0; i < rows.Count; i++) rows[i].EnableInClassList("ls-stage--active", i == index);
            var s = stages[index];
            bool down = s.Felled;
            region.text = s.region.ToUpperInvariant();
            ruler.text = !s.HasBoss ? "TRAINING GROUNDS" : down ? $"DOMAIN OF THE {s.boss.ToUpperInvariant()}" : "DOMAIN OF AN UNKNOWN FOE";
            epithet.text = s.epithet;
            desc.text = s.description;
            var face = !s.HasBoss || down ? s.portrait : s.silhouette != null ? s.silhouette : s.portrait;
            portrait.style.backgroundImage = face;
            portrait.style.display = face != null ? DisplayStyle.Flex : DisplayStyle.None;
            chips.Clear();
            if (s.HasBoss)
            {
                Chip(down ? "BOSS FELLED" : "BOSS UNVANQUISHED", down);
                if (!string.IsNullOrEmpty(s.maskName))
                {
                    bool mask = Progress.OwnsMask(s.maskId);
                    Chip(mask ? s.maskName.ToUpperInvariant() + " CLAIMED" : "MASK UNCLAIMED", mask);
                }
            }
            else Chip("NO BOSS", false);
            if (IsOpen)
            {
                ShowVista(s.vista);
                if (sound)
                {
                    AudioManager.Play2D(Sfx.UIMove, 0.5f);
                    info.RemoveFromClassList("ls-info--in");
                    info.schedule.Execute(() => info.AddToClassList("ls-info--in")).StartingIn(16);
                }
                else info.AddToClassList("ls-info--in");
            }
        }

        void Chip(string text, bool done) => Lbl(text, "ls-chip " + (done ? "ls-chip--done" : "ls-chip--open"), chips);

        // ================================================================== tick
        /// <summary>Call every frame (unscaled). Drives the drift, and while open the list.</summary>
        public void Tick(PlayerInputReader input)
        {
            float t = Time.unscaledTime;
            Drift(bgFront, t - frontBorn);
            Drift(bgBack, t - backBorn);
            if (!IsOpen) return;
            bool pad = input != null && input.Device == InputDeviceKind.Gamepad;
            footKeys.text = Travelling ? "" : $"{(pad ? "D-Pad" : "↑ ↓")}  Choose      {(pad ? "A" : "Enter")}  Travel      {(pad ? "B" : "Esc")}  {backLabel}";
            if (Travelling || input == null) return;
            if (t < inputAfter) { input.ConsumeNavigate(); return; }
            var nav = input.ConsumeNavigate();
            if (nav.y != 0 && stages.Count > 0) Select((index - nav.y + stages.Count) % stages.Count, true);
            if (input.SubmitPressed && stages.Count > 0) Travel(stages[index]);
            else if (input.CancelPressed) Back();
        }

        // ================================================================== travel
        public void Travel(StageCatalog.Stage s)
        {
            if (Travelling) return;
            AudioManager.Play2D(Sfx.UISelect, 0.8f);
            string sub = !s.HasBoss ? "Training grounds" : s.Felled ? $"{s.boss}, felled" : s.epithet;
            host.StartCoroutine(TravelRoutine(s.scene, s.bossArg, s.region.ToUpperInvariant(), sub));
        }

        /// <summary>Fade through a title card into a scene (also used for Continue).</summary>
        public IEnumerator TravelRoutine(string scene, string boss, string title, string sub)
        {
            Travelling = true;
            // from outside the list (Continue): only the fade and the card, not the list flashing up under them
            Root.EnableInClassList("ls-bare", !IsOpen);
            Root.style.display = DisplayStyle.Flex;
            Root.RemoveFromClassList("ls-off");
            cardTitle.text = title;
            cardSub.text = sub;
            yield return Ease.Tween(0.7f, k => fade.style.opacity = k, Ease.InOutSine);
            AudioManager.Play2D(Sfx.Title, 0.7f);
            yield return Ease.Tween(0.6f, k => card.style.opacity = k, Ease.OutCubic);
            yield return new WaitForSecondsRealtime(1.3f);
            yield return Ease.Tween(0.5f, k => card.style.opacity = 1f - k, Ease.InCubic);
            SceneFlow.Load(scene, boss);
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
            if (instant) { bgBack.style.opacity = 1f; FinishFade(); return; }
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

        /// <summary>Slow drift: a vista zooms in and slides a little over its life.</summary>
        public static void Drift(VisualElement e, float age)
        {
            float k = Mathf.Clamp01(age / 30f);
            e.style.scale = new Scale(Vector3.one * Mathf.Lerp(1.06f, 1.14f, Ease.InOutSine(k)));
            e.style.translate = new Translate(new Length(Mathf.Lerp(-1.2f, 1.2f, k), LengthUnit.Percent), new Length(Mathf.Sin(age * 0.07f) * 0.6f, LengthUnit.Percent));
        }
    }
}
