using ElderTree.Equipment;
using ElderTree.InputHandling;
using ElderTree.Masks;
using UnityEngine;
using UnityEngine.UIElements;

namespace ElderTree.UI
{
    public partial class HUD
    {
        // ================================================================== mask slots, forms, cinematic overlays
        // Four small diamonds above the equipment cross: 1-4 on keyboard, D-pad up/right/down/left on a pad.
        // An owned mask shows its icon, the worn one glows; pressing an empty slot shakes it.
        // The equipment cross follows the current form (club/bow for the Elder Tree, hammer/cannon for
        // the alligator), and the ranged slot shows its mana cost per shot.
        VisualElement maskBar;
        readonly VisualElement[] maskSlots = new VisualElement[4];
        readonly VisualElement[] maskIcons = new VisualElement[4];
        readonly Label[] maskKeys = new Label[4];
        readonly float[] maskShake = new float[4];
        VisualElement letterTop, letterBottom, flashRoot, cardRoot;
        Label cardTitle, cardLine, cardHint;
        float letterbox, letterTarget, flash, cardT = -1f, manaDeny;
        bool cinematicHud, maskAny;
        static readonly string[] PadArrows = { "↑", "→", "↓", "←" };

        void BuildMasks()
        {
            maskBar = El("maskbar", root);
            for (int i = 0; i < 4; i++)
            {
                var slot = El("maskbar__slot", maskBar);
                maskSlots[i] = slot;
                maskIcons[i] = El("maskbar__icon", slot);
                maskKeys[i] = Lbl("", "maskbar__key", maskBar);
                maskKeys[i].style.left = 6 + i * 52;
            }

            letterTop = El("letterbox letterbox--top", root);
            letterBottom = El("letterbox letterbox--bottom", root);
            flashRoot = El("whiteflash", root);
            flashRoot.style.opacity = 0f;

            cardRoot = El("maskcard", root);
            El("maskcard__band", cardRoot);
            cardTitle = Lbl("", "maskcard__title", cardRoot);
            ApplySerif(cardTitle);
            cardLine = Lbl("", "maskcard__line", cardRoot);
            cardHint = Lbl("", "maskcard__hint", cardRoot);
            cardRoot.style.opacity = 0f;

            if (stats != null) stats.ManaDenied += () => manaDeny = 1f;
            if (player != null) player.FormChanged += _ => RefreshEquipment();
            var ms = MaskSystem.Instance;
            if (ms != null)
            {
                ms.Changed += RefreshMaskBar;
                ms.SlotDenied += i => { if (i >= 0 && i < 4) maskShake[i] = 1f; };
            }
            RefreshMaskBar();
            RefreshEquipment();
        }

        void RefreshMaskBar()
        {
            var ms = MaskSystem.Instance;
            bool pad = input != null && input.Device == InputDeviceKind.Gamepad;
            for (int i = 0; i < 4; i++)
            {
                var m = ms != null && i < ms.slots.Length ? ms.slots[i] : null;
                bool own = ms != null && ms.Owns(m);
                maskIcons[i].style.backgroundImage = own && m.icon != null ? new StyleBackground(m.icon) : new StyleBackground(StyleKeyword.None);
                maskSlots[i].EnableInClassList("maskbar__slot--owned", own);
                maskSlots[i].EnableInClassList("maskbar__slot--worn", own && ms.Worn == m);
                maskKeys[i].text = pad ? PadArrows[i] : (i + 1).ToString();
            }
            // nothing owned yet: keep the strip out of the way
            bool any = false;
            if (ms != null) foreach (var m in ms.slots) any |= ms.Owns(m);
            maskAny = any;
        }

        /// <summary>Weapon icons, names and the mana cost follow the current form.</summary>
        void RefreshEquipment()
        {
            if (player == null) return;
            var f = player.CurrentForm;
            Texture2D meleeIcon = f != null && f.magic != null && f.magic.icon != null ? f.magic.icon
                : f != null && f.melee != null && f.melee.icon != null ? f.melee.icon : clubIcon;
            Texture2D rangedIcon = f != null && f.rangedKind == RangedKind.Orb && f.orb != null && f.orb.icon != null ? f.orb.icon
                : f != null && f.rangedKind == RangedKind.Cannon && f.cannon != null && f.cannon.icon != null ? f.cannon.icon
                : f != null && f.rangedKind == RangedKind.Crossbow && f.crossbow != null && f.crossbow.icon != null ? f.crossbow.icon
                : f != null && f.bow != null && f.bow.icon != null ? f.bow.icon : bowIcon;
            var ci = clubSlot?.Q(className: "equip__icon");
            if (ci != null) ci.style.backgroundImage = meleeIcon;
            var bi = bowSlot?.Q(className: "equip__icon");
            if (bi != null) bi.style.backgroundImage = rangedIcon;
            if (ammoLabel != null) ammoLabel.text = $"◆ {Mathf.RoundToInt(player.RangedManaCost)}";
        }

        string MeleeName => player != null && player.CurrentForm != null && player.CurrentForm.magic != null ? player.CurrentForm.magic.displayName
            : player != null && player.CurrentForm != null && player.CurrentForm.melee != null ? player.CurrentForm.melee.displayName : "Elder Club";
        string RangedName
        {
            get
            {
                var f = player != null ? player.CurrentForm : null;
                if (f != null && f.rangedKind == RangedKind.Orb && f.orb != null) return f.orb.displayName;
                if (f != null && f.rangedKind == RangedKind.Cannon && f.cannon != null) return f.cannon.displayName;
                if (f != null && f.rangedKind == RangedKind.Crossbow && f.crossbow != null) return f.crossbow.displayName;
                if (f != null && f.bow != null) return f.bow.displayName;
                return "Elder Bow";
            }
        }

        // ------------------------------------------------------------------ cinematic overlays
        public void SetCinematic(bool on)
        {
            cinematicHud = on;
            letterTarget = on ? 1f : 0f;
            if (!on) { cardT = cardT >= 0f ? Mathf.Max(cardT, 4.5f) : -1f; }
        }

        public void Flash(float strength) => flash = Mathf.Max(flash, strength);

        public void ShowMaskReceived(MaskData m)
        {
            cardTitle.text = m.receivedTitle;
            cardLine.text = m.receivedLine;
            var ms = MaskSystem.Instance;
            int slot = ms != null ? Mathf.Max(0, ms.SlotOf(m)) : 0;
            bool pad = input != null && input.Device == InputDeviceKind.Gamepad;
            string key = pad ? $"D-Pad {PadArrows[slot]}" : (slot + 1).ToString();
            cardHint.text = $"Press {key} to wear it · press it again to return to the Elder Tree";
            cardT = 0f;
        }

        void TickMasks(float dt)
        {
            // letterbox + the rest of the HUD steps aside during cinematics
            letterbox = Mathf.MoveTowards(letterbox, letterTarget, dt * 2.2f);
            float lb = Ease.InOutCubic(letterbox);
            letterTop.style.height = Length.Percent(11f * lb);
            letterBottom.style.height = Length.Percent(11f * lb);
            float hudA = 1f - lb;
            foreach (var e in new[] { statusBox, equipBox, equipName, hintsBox })
                if (e != null) e.style.opacity = hudA;
            maskBar.style.opacity = maskAny ? hudA : 0f;

            flash = Mathf.MoveTowards(flash, 0f, dt * 2.6f);
            flashRoot.style.opacity = flash * flash;

            // mask received card: fade in, hold, fade out
            if (cardT >= 0f)
            {
                cardT += dt;
                const float inT = 0.6f, hold = 4.4f, outT = 0.9f;
                float a = cardT < inT ? Ease.OutCubic(cardT / inT) : cardT < inT + hold ? 1f : 1f - Mathf.Clamp01((cardT - inT - hold) / outT);
                cardRoot.style.opacity = a;
                cardTitle.style.letterSpacing = Mathf.Lerp(26f, 12f, Ease.OutCubic(Mathf.Clamp01(cardT / (inT + hold))));
                if (cardT > inT + hold + outT) { cardT = -1f; cardRoot.style.opacity = 0f; }
            }

            for (int i = 0; i < 4; i++)
            {
                maskShake[i] = Mathf.MoveTowards(maskShake[i], 0f, dt * 3f);
                maskSlots[i].style.translate = new Translate(Mathf.Sin(maskShake[i] * 40f) * 6f * maskShake[i], 0f);
            }

            // mana bar blinks when a shot / spell is refused
            manaDeny = Mathf.MoveTowards(manaDeny, 0f, dt * 2.5f);
            if (fp != null) fp.frame.style.borderTopColor = fp.frame.style.borderBottomColor = fp.frame.style.borderLeftColor =
                fp.frame.style.borderRightColor = Color.Lerp(new Color(0.85f, 0.7f, 0.42f, 0.55f), new Color(0.55f, 0.75f, 1f, 1f), manaDeny * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 30f)));
        }
    }
}
