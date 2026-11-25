using System.Collections;
using ElderTree.Combat;
using ElderTree.Enemies;
using UnityEngine;
using UnityEngine.UIElements;

namespace ElderTree.UI
{
    public partial class HUD
    {
        // ================================================================== boss bar (Elden Ring: bottom centre)
        /// <summary>Pause menu: summon roster boss N (0 = Crocodile, 1 = Shark, 2 = Druid, 3 = Fairy).</summary>
        public System.Action<int> SummonBossRequested;

        VisualElement bossBox, bossFill, bossChip;
        Label bossName, bossDmg;
        BossBase boss;
        float bossShown = 1f, bossChipV = 1f, bossChipHold, bossAccum, bossAccumAt = -99f, bossAlpha;
        bool bossVisible;

        VisualElement deathRoot, fadeRoot;
        Label deathText;
        float deathT = -1f;

        void BuildBossUI()
        {
            bossBox = El("bossbar", root);
            bossName = Lbl("", "bossbar__name", bossBox);
            ApplySerif(bossName);
            var frame = El("bossbar__frame", bossBox);
            bossChip = El("bossbar__chip", frame);
            bossFill = El("bossbar__fill", frame);
            El("bossbar__shine", frame);
            bossDmg = Lbl("", "bossbar__dmg", bossBox);
            bossBox.style.opacity = 0f;

            deathRoot = El("death", root);
            El("death__band", deathRoot);
            deathText = Lbl("YOU DIED", "death__text", deathRoot);
            ApplySerif(deathText);
            deathRoot.style.opacity = 0f;

            fadeRoot = El("screenfade", root);
            fadeRoot.style.opacity = 0f;
        }

        public void ShowBossBar(BossBase b)
        {
            if (boss != null) boss.Damaged -= OnBossDamaged;
            boss = b;
            boss.Damaged += OnBossDamaged;
            bossName.text = b.DisplayName;
            bossShown = bossChipV = 1f;
            bossAccum = 0f;
            bossDmg.text = "";
            bossVisible = true;
        }

        public void HideBossBar() => bossVisible = false;

        /// <summary>The boss whose bar is on screen (null when none has been shown yet).</summary>
        public BossBase BossBarTarget => bossVisible ? boss : null;

        void OnBossDamaged(BossBase b, DamageInfo info, bool poiseBroken)
        {
            if (Time.unscaledTime - bossAccumAt > 1.6f) bossAccum = 0f;
            bossAccum += info.damage;
            bossAccumAt = Time.unscaledTime;
            bossChipHold = Time.unscaledTime + 0.5f;
            bossDmg.text = Mathf.RoundToInt(bossAccum).ToString();
            bossDmg.style.scale = new Scale(new Vector3(1.3f, 1.3f, 1));
        }

        void TickBossBar(float dt)
        {
            bossAlpha = Mathf.MoveTowards(bossAlpha, bossVisible ? 1f : 0f, dt * (bossVisible ? 2f : 1f));
            bossBox.style.opacity = bossAlpha;
            if (boss != null)
            {
                float n = boss.Health / boss.MaxHealth;
                bossShown = Mathf.Lerp(bossShown, n, Ease.Damp(20f, dt));
                if (n > bossChipV) bossChipV = n;
                else if (Time.unscaledTime > bossChipHold) bossChipV = Mathf.MoveTowards(bossChipV, n, dt * 0.35f);
                bossFill.style.width = Length.Percent(bossShown * 100f);
                bossChip.style.width = Length.Percent(bossChipV * 100f);
                float since = Time.unscaledTime - bossAccumAt;
                bossDmg.style.opacity = since < 1.8f ? 1f : Mathf.Clamp01(1f - (since - 1.8f) / 0.5f);
                float sc = bossDmg.resolvedStyle.scale.value.x;
                float ns = Mathf.Lerp(sc, 1f, Ease.Damp(14f, dt));
                bossDmg.style.scale = new Scale(new Vector3(ns, ns, 1));
            }

            // YOU DIED: slow swell in, long hold, slow fade
            if (deathT >= 0f)
            {
                deathT += dt;
                const float inT = 1.4f, hold = 2.6f, outT = 1.2f;
                float a = deathT < inT ? Ease.OutCubic(deathT / inT)
                    : deathT < inT + hold ? 1f
                    : 1f - Mathf.Clamp01((deathT - inT - hold) / outT);
                deathRoot.style.opacity = a;
                float s = 0.94f + 0.08f * Mathf.Clamp01(deathT / (inT + hold + outT));
                deathText.style.scale = new Scale(new Vector3(s, s, 1));
                if (deathT > inT + hold + outT) deathT = -1f;
            }
        }

        public void ShowDeath()
        {
            deathT = 0f;
            deathRoot.style.opacity = 0f;
        }

        public IEnumerator FadeScreen(float to, float duration)
        {
            float from = fadeRoot.resolvedStyle.opacity;
            yield return Ease.Tween(duration, t => fadeRoot.style.opacity = Mathf.Lerp(from, to, t), Ease.InOutCubic);
        }
    }
}
