using System.Collections.Generic;
using ElderTree.Masks;
using ElderTree.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace ElderTree.UI
{
    /// <summary>
    /// Points at every mask waiting to be claimed: over the mask, its name, how far it is and an arrowhead in the mask's
    /// colour bobbing down at it; off screen (or behind you) the marker clings to the screen edge with the arrow turned
    /// toward it. Once the mask is about to fly to you by itself, the marker counts it down.
    /// </summary>
    public partial class HUD
    {
        class MaskMark
        {
            public VisualElement box;
            public Label name, info;
            public MarkArrow arrow;
        }

        /// <summary>A filled arrowhead pointing down (rotated to point elsewhere).</summary>
        class MarkArrow : VisualElement
        {
            public Color tint = Color.white;

            public MarkArrow()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext ctx)
            {
                float w = contentRect.width, h = contentRect.height;
                var p = ctx.painter2D;
                p.fillColor = tint;
                p.strokeColor = new Color(0.02f, 0.01f, 0.02f, 0.85f);
                p.lineWidth = 2f;
                p.lineJoin = LineJoin.Miter;
                p.BeginPath();
                p.MoveTo(new Vector2(1f, 1f));
                p.LineTo(new Vector2(w * 0.5f, h * 0.34f));
                p.LineTo(new Vector2(w - 1f, 1f));
                p.LineTo(new Vector2(w * 0.5f, h - 1f));
                p.ClosePath();
                p.Fill();
                p.Stroke();
            }
        }

        VisualElement markLayer;
        readonly Dictionary<MaskPickup, MaskMark> marks = new();
        readonly List<MaskPickup> markGone = new();

        void BuildMaskMarks()
        {
            markLayer = El("maskmarks", root);
        }

        void TickMaskMarks()
        {
            if (markLayer == null) return;
            if (cam == null) cam = Camera.main;
            markGone.Clear();
            foreach (var kv in marks) if (kv.Key == null || !MaskPickup.Active.Contains(kv.Key)) markGone.Add(kv.Key);
            foreach (var p in markGone) { marks[p].box.RemoveFromHierarchy(); marks.Remove(p); }
            bool show = !cinematicHud && cam != null && !MenuOpen;
            markLayer.style.display = show && MaskPickup.Active.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;

            var size = root.layout.size;
            if (size.x <= 1f || size.y <= 1f) return;
            var pc = PlayerController.Instance;
            foreach (var p in MaskPickup.Active)
            {
                if (p == null || p.mask == null) continue;
                if (!marks.TryGetValue(p, out var m)) marks[p] = m = MakeMark(p);

                Vector3 world = p.MarkerPoint;
                Vector3 vp = cam.WorldToViewportPoint(world);
                bool behind = vp.z < 0f;
                if (behind) { vp.x = 1f - vp.x; vp.y = 1f - vp.y; }
                const float Edge = 0.07f;
                bool onScreen = !behind && vp.x > Edge && vp.x < 1f - Edge && vp.y > Edge && vp.y < 1f - Edge;
                Vector2 sp = new Vector2(vp.x, vp.y);
                float angle = 0f;
                if (!onScreen)
                {
                    // slide out from the centre along the way to the mask until the marker meets the edge margin
                    Vector2 d = sp - new Vector2(0.5f, 0.5f);
                    if (d.sqrMagnitude < 1e-5f) d = Vector2.down;
                    float k = Mathf.Min((0.5f - Edge) / Mathf.Max(1e-4f, Mathf.Abs(d.x)), (0.5f - Edge) / Mathf.Max(1e-4f, Mathf.Abs(d.y)));
                    sp = new Vector2(0.5f, 0.5f) + d * k;
                    // screen space is y-down; the arrow is drawn pointing down
                    angle = Mathf.Atan2(-d.y * size.y, d.x * size.x) * Mathf.Rad2Deg - 90f;
                }
                m.box.style.left = sp.x * size.x;
                m.box.style.top = (1f - sp.y) * size.y;
                m.box.style.translate = onScreen ? new Translate(Length.Percent(-50), Length.Percent(-100)) : new Translate(Length.Percent(-50), Length.Percent(-50));
                m.arrow.style.rotate = new Rotate(angle);
                float bob = onScreen ? Mathf.Sin(Time.unscaledTime * 4f) * 4f : 0f;
                m.arrow.style.translate = new Translate(0, bob);
                m.name.style.display = onScreen ? DisplayStyle.Flex : DisplayStyle.None;

                float dist = pc != null ? Vector3.Distance(pc.transform.position, p.transform.position) : 0f;
                if (p.Homing) m.info.text = "COMING TO YOU";
                else if (p.SecondsLeft <= 5.5f) m.info.text = $"COMES TO YOU IN {Mathf.CeilToInt(p.SecondsLeft)}";
                else m.info.text = $"{dist:0} M";
                // far away: a little smaller and dimmer, never gone
                float near = Mathf.InverseLerp(45f, 6f, dist);
                m.box.style.opacity = Mathf.Lerp(0.7f, 1f, near);
                m.box.style.scale = new Scale(Vector3.one * Mathf.Lerp(0.85f, 1f, near));
            }
        }

        MaskMark MakeMark(MaskPickup p)
        {
            var m = new MaskMark();
            m.box = El("maskmark", markLayer);
            m.name = Lbl(p.mask.displayName.ToUpperInvariant(), "maskmark__name", m.box);
            ApplySerif(m.name);
            m.info = Lbl("", "maskmark__info", m.box);
            ApplySerif(m.info);
            m.arrow = new MarkArrow { tint = p.Tint };
            m.arrow.AddToClassList("maskmark__arrow");
            m.box.Add(m.arrow);
            Color c = p.Tint;
            m.name.style.color = Color.Lerp(c, Color.white, 0.55f);
            return m;
        }
    }
}
