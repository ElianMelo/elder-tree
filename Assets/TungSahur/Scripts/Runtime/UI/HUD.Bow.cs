using TungSahur.CameraSystem;
using TungSahur.Combat;
using UnityEngine;
using UnityEngine.UIElements;

namespace TungSahur.UI
{
    /// <summary>
    /// Bow HUD: a centre crosshair whose four ticks close in as the string comes back (gold and a
    /// pulse at full draw, warm tint over a target), a hit marker when an arrow lands, and the bow
    /// slot (left hand) with an infinite-ammo counter in the equipment cross.
    /// </summary>
    public partial class HUD
    {
        public Texture2D bowIcon;

        VisualElement crosshair, chDot, chHit;
        readonly VisualElement[] chTicks = new VisualElement[4];
        VisualElement bowSlot, clubSlot;
        Label ammoLabel;
        float fullDrawPulse, hitFlash;
        bool hitWasFull;

        static readonly Vector2[] TickDirs = { Vector2.up, Vector2.right, Vector2.down, Vector2.left };

        void BuildCrosshair()
        {
            crosshair = El("crosshair", root);
            for (int i = 0; i < 4; i++)
            {
                chTicks[i] = El(i % 2 == 0 ? "crosshair__tick crosshair__tick--v" : "crosshair__tick crosshair__tick--h", crosshair);
            }
            chDot = El("crosshair__dot", crosshair);
            chHit = El("crosshair__hit", crosshair);
            for (int i = 0; i < 4; i++)
            {
                var arm = El("crosshair__hit-arm", chHit);
                arm.style.rotate = new Rotate(45f + 90f * i);
            }
            crosshair.style.opacity = 0f;
            chHit.style.opacity = 0f;

            if (player != null)
            {
                player.FullDrawReached += () => fullDrawPulse = 1f;
            }
            ArrowProjectile.HitTarget += OnArrowHit;
        }

        void OnDisable() => ArrowProjectile.HitTarget -= OnArrowHit;

        void OnArrowHit(ArrowProjectile a, IDamageable target, DamageInfo info)
        {
            hitFlash = 1f;
            hitWasFull = a != null && a.FullDraw;
        }

        void BuildBowSlot(VisualElement cross)
        {
            bowSlot = cross.Q(className: "equip__slot--left");
            clubSlot = cross.Q(className: "equip__slot--right");
            if (bowSlot == null) return;
            if (bowIcon) { var img = El("equip__icon", bowSlot); img.style.backgroundImage = bowIcon; }
            ammoLabel = Lbl("∞", "equip__ammo", cross);
        }

        void TickBow(float dt)
        {
            if (crosshair == null) return;
            var cd = CameraDirector.Instance;
            float aim = cd != null ? cd.AimWeight : 0f;
            bool raised = player != null && player.BowRaised;
            crosshair.style.opacity = raised ? aim : Mathf.Min(aim, crosshair.resolvedStyle.opacity);

            float draw = player != null ? player.BowDraw : 0f;
            bool full = player != null && player.FullDraw;
            bool onTarget = player != null && player.AimOnTarget;

            // spread: wide while nocking, tight at the anchor
            float spread = Mathf.Lerp(38f, 11f, Ease.OutCubic(draw));
            if (player != null && player.CurrentBowPhase == Player.BowPhase.Renock) spread = 38f;
            fullDrawPulse = Mathf.MoveTowards(fullDrawPulse, 0f, dt * 4f);
            float pop = 1f + Ease.OutCubic(fullDrawPulse) * 0.35f;
            Color tick = full ? Palette.UIGoldBright : new Color(1f, 0.98f, 0.93f, Mathf.Lerp(0.7f, 1f, draw));
            for (int i = 0; i < 4; i++)
            {
                var d = TickDirs[i] * spread * pop;
                chTicks[i].style.translate = new Translate(d.x, -d.y);
                chTicks[i].style.backgroundColor = tick;
            }
            chDot.style.backgroundColor = onTarget ? Palette.Lantern : new Color(1f, 0.98f, 0.93f, 0.95f);
            float ds = onTarget ? 1.25f : 1f;
            chDot.style.scale = new Scale(new Vector3(ds, ds, 1f));

            hitFlash = Mathf.MoveTowards(hitFlash, 0f, dt * 3.2f);
            chHit.style.opacity = hitFlash;
            float hs = 1f + (1f - hitFlash) * 0.5f;
            chHit.style.scale = new Scale(new Vector3(hs, hs, 1f));
            foreach (var arm in chHit.Children())
                arm.style.backgroundColor = hitWasFull ? Palette.Lantern : new Color(1f, 0.97f, 0.9f);

            // the lock-on reticle steps back while the bow's own sign is up
            if (reticle != null) reticle.style.opacity = (1f - reticlePop * 0.7f) * (1f - aim * 0.85f);

            // equipment: the raised weapon's slot lights up, the name follows it
            if (bowSlot != null)
            {
                bowSlot.EnableInClassList("equip__slot--active", raised);
                clubSlot?.EnableInClassList("equip__slot--active", !raised);
                if (equipName is Label l) l.text = raised ? "Sahur Bow" : "Sahur Club";
            }
        }
    }
}
