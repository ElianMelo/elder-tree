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
            var kicker = Lbl(titleKicker, "title__kicker", col);
            ApplySerif(kicker);
            var main = Lbl(titleMain, "title__main", col);
            ApplySerif(main);
            var sub = Lbl(titleSub, "title__sub", col);
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
                    SetHints(HintsHere);
                    foreach (var e in new[] { statusBox, equipBox, equipName, hintsBox }) StartCoroutine(FadeElement(e, 0f, 1f, 0.6f));
                }
            }
        }
    }
}
