using UnityEngine;
using UnityEngine.UIElements;

namespace ElderTree.UI
{
    public partial class HUD
    {
        // ------------------------------------------------------------------ special charges
        // Two slots under the stamina bar. Light / heavy hits fill them left to right; a full slot
        // glows gold and can pay for one special. In a boss fight a spent slot goes dark for the
        // rest of the fight (only PlayerStats.specialsPerBoss specials per boss).
        VisualElement chargeRow;
        VisualElement[] chargeSlots, chargeFills;
        float[] chargeShown, chargeFlash;
        float chargeDeny;

        static readonly Color ChargeFilling = new Color(0.62f, 0.5f, 0.28f, 1f);
        static readonly Color ChargeFull = new Color(0.98f, 0.8f, 0.4f, 1f);
        static readonly Color ChargeFrame = new Color(0.85f, 0.7f, 0.42f, 0.55f);

        void BuildCharges()
        {
            int n = Mathf.Max(1, stats.maxSpecialCharges);
            chargeRow = El("charges", statusBox);
            chargeSlots = new VisualElement[n];
            chargeFills = new VisualElement[n];
            chargeShown = new float[n];
            chargeFlash = new float[n];
            for (int i = 0; i < n; i++)
            {
                var slot = chargeSlots[i] = El("charge", chargeRow);
                var inner = El("charge__inner", slot);
                chargeFills[i] = El("charge__fill", inner);
                El("charge__gem", slot);
            }
            stats.SpecialDenied += () => chargeDeny = 1f;
            stats.SpecialChargeReady += () =>
            {
                int k = Mathf.Clamp(stats.SpecialChargesReady - 1, 0, chargeFlash.Length - 1);
                chargeFlash[k] = 1f;
            };
        }

        void TickCharges(float dt)
        {
            if (chargeSlots == null) return;
            chargeDeny = Mathf.MoveTowards(chargeDeny, 0f, dt * 2.5f);
            chargeRow.style.translate = new Translate(Mathf.Sin(chargeDeny * 40f) * 5f * chargeDeny, 0f);

            float charge = stats.SpecialCharge;
            // slots past what this boss fight still allows are spent for good
            int spent = stats.InBossFight ? Mathf.Clamp(stats.SpecialsUsedThisFight, 0, chargeSlots.Length) : 0;
            int live = chargeSlots.Length - spent;
            for (int i = 0; i < chargeSlots.Length; i++)
            {
                bool dead = i >= live;
                float target = dead ? 0f : Mathf.Clamp01(charge - i);
                chargeShown[i] = Mathf.Lerp(chargeShown[i], target, Ease.Damp(14f, dt));
                bool full = target >= 0.999f;
                chargeFlash[i] = Mathf.MoveTowards(chargeFlash[i], 0f, dt * 1.8f);

                var fill = chargeFills[i];
                fill.style.width = Length.Percent(chargeShown[i] * 100f);
                Color c = full ? ChargeFull : ChargeFilling;
                if (full) c = Color.Lerp(c, Color.white, 0.12f + 0.12f * Mathf.Sin(Time.unscaledTime * 4f + i));
                fill.style.backgroundColor = Color.Lerp(c, Color.white, chargeFlash[i]);

                var slot = chargeSlots[i];
                slot.EnableInClassList("charge--full", full);
                slot.EnableInClassList("charge--spent", dead);
                Color frame = dead ? new Color(0.35f, 0.32f, 0.28f, 0.4f)
                    : Color.Lerp(full ? new Color(1f, 0.82f, 0.45f, 0.9f) : ChargeFrame, new Color(0.55f, 0.75f, 1f, 1f),
                        chargeDeny * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 30f)));
                slot.style.borderTopColor = slot.style.borderBottomColor = slot.style.borderLeftColor = slot.style.borderRightColor = frame;
                float s = 1f + Ease.OutCubic(chargeFlash[i]) * 0.18f;
                slot.style.scale = new Scale(new Vector3(s, s, 1f));
            }
        }
    }
}
