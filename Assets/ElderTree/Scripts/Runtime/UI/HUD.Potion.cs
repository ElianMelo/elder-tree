using UnityEngine;
using UnityEngine.UIElements;

namespace ElderTree.UI
{
    public partial class HUD
    {
        // ------------------------------------------------------------------ health potions
        // The down slot of the equipment cross (the quick item, as in Elden Ring): a flask drawn in code and the
        // count beside it. Drinking pulses it; pressing with none left shakes it and flashes the count.
        VisualElement potionSlot, potionIcon;
        Label potionCount;
        float potionPulse, potionDeny;
        int potionShown = -1;

        static readonly Color PotionLiquid = new Color(0.72f, 0.12f, 0.11f, 1f);
        static readonly Color PotionLiquidDark = new Color(0.36f, 0.05f, 0.06f, 1f);
        static readonly Color PotionGlass = new Color(0.86f, 0.8f, 0.7f, 0.55f);
        static readonly Color PotionCork = new Color(0.5f, 0.36f, 0.22f, 1f);

        void BuildPotionSlot(VisualElement cross)
        {
            potionSlot = cross.Q(className: "equip__slot--down");
            if (potionSlot == null) return;
            potionSlot.Clear();
            potionIcon = El("equip__potion", potionSlot);
            potionIcon.generateVisualContent += DrawFlask;
            potionCount = Lbl("", "equip__potion-count", cross);
            ApplySerif(potionCount);
            stats.PotionUsed += () => potionPulse = 1f;
            stats.PotionDenied += () => potionDeny = 1f;
        }

        void TickPotion(float dt)
        {
            if (potionSlot == null) return;
            int n = stats.Potions;
            if (n != potionShown)
            {
                potionShown = n;
                potionCount.text = n.ToString();
                potionIcon.MarkDirtyRepaint();
            }
            potionPulse = Mathf.MoveTowards(potionPulse, 0f, dt * 2.6f);
            potionDeny = Mathf.MoveTowards(potionDeny, 0f, dt * 2.5f);

            float s = 1f + Ease.OutCubic(potionPulse) * 0.14f;
            potionSlot.style.scale = new Scale(new Vector3(s, s, 1f));
            float shake = Mathf.Sin(potionDeny * 40f) * 5f * potionDeny;
            potionSlot.style.translate = new Translate(shake, 0f);
            potionCount.style.translate = new Translate(shake, 0f);

            bool empty = n <= 0;
            var countColor = empty ? new Color(0.62f, 0.58f, 0.52f, 0.7f) : new Color(0.96f, 0.87f, 0.65f, 1f);
            potionCount.style.color = Color.Lerp(countColor, new Color(1f, 0.35f, 0.3f, 1f), potionDeny);
            var frame = Color.Lerp(empty ? new Color(0.85f, 0.7f, 0.42f, 0.2f) : new Color(0.85f, 0.7f, 0.42f, 0.35f),
                                   new Color(1f, 0.92f, 0.75f, 0.9f), potionPulse);
            potionSlot.style.borderTopColor = potionSlot.style.borderBottomColor =
                potionSlot.style.borderLeftColor = potionSlot.style.borderRightColor = frame;
            potionIcon.style.opacity = empty ? 0.45f : 1f;
        }

        /// <summary>A round-bellied flask with a cork: crimson while potions remain, a dry dark stain when empty.</summary>
        void DrawFlask(MeshGenerationContext ctx)
        {
            var r = potionIcon.contentRect;
            float w = r.width, h = r.height;
            if (w < 1f || h < 1f) return;
            float cx = w * 0.5f;
            var p = ctx.painter2D;
            bool empty = stats != null && stats.Potions <= 0;

            Vector2 belly = new Vector2(cx, h * 0.64f);
            float br = w * 0.3f;
            float neckW = w * 0.09f, neckTop = h * 0.2f, neckBottom = belly.y - br * 0.82f;

            // glass outline: belly + neck
            p.fillColor = new Color(0.08f, 0.06f, 0.06f, 0.85f);
            p.BeginPath();
            p.Arc(belly, br + 2.5f, 0f, 360f);
            p.Fill();
            p.BeginPath();
            p.MoveTo(new Vector2(cx - neckW - 2.5f, neckTop));
            p.LineTo(new Vector2(cx + neckW + 2.5f, neckTop));
            p.LineTo(new Vector2(cx + neckW + 2.5f, neckBottom + 4f));
            p.LineTo(new Vector2(cx - neckW - 2.5f, neckBottom + 4f));
            p.ClosePath();
            p.Fill();

            // liquid: fills the belly (a low dregs line when empty)
            p.fillColor = empty ? new Color(0.2f, 0.06f, 0.06f, 0.9f) : PotionLiquidDark;
            p.BeginPath();
            p.Arc(belly, br, 0f, 360f);
            p.Fill();
            if (!empty)
            {
                p.fillColor = PotionLiquid;
                p.BeginPath();
                p.Arc(belly + new Vector2(-br * 0.12f, -br * 0.12f), br * 0.72f, 0f, 360f);
                p.Fill();
                // liquid in the neck
                p.fillColor = PotionLiquidDark;
                p.BeginPath();
                p.MoveTo(new Vector2(cx - neckW, neckTop + h * 0.08f));
                p.LineTo(new Vector2(cx + neckW, neckTop + h * 0.08f));
                p.LineTo(new Vector2(cx + neckW, neckBottom + 4f));
                p.LineTo(new Vector2(cx - neckW, neckBottom + 4f));
                p.ClosePath();
                p.Fill();
            }

            // glint
            p.strokeColor = PotionGlass;
            p.lineWidth = 2f;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.Arc(belly, br * 0.72f, 200f, 250f);
            p.Stroke();

            // cork
            p.fillColor = PotionCork;
            p.BeginPath();
            p.MoveTo(new Vector2(cx - neckW - 1.5f, neckTop - h * 0.1f));
            p.LineTo(new Vector2(cx + neckW + 1.5f, neckTop - h * 0.1f));
            p.LineTo(new Vector2(cx + neckW, neckTop + h * 0.04f));
            p.LineTo(new Vector2(cx - neckW, neckTop + h * 0.04f));
            p.ClosePath();
            p.Fill();
        }
    }
}
