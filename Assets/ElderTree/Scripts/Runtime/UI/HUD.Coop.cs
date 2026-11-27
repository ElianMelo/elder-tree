using System.Collections.Generic;
using ElderTree.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace ElderTree.UI
{
    /// <summary>
    /// Co-op on the HUD: your allies' health under your own bars (a slot-coloured gem, name, a thin health line, a
    /// cross once fallen), each ally's name floating over their tree, and under the boss bar the player the boss is
    /// hunting ("hunts you" flares when it turns on you). Hidden outside co-op.
    /// </summary>
    public partial class HUD
    {
        VisualElement partyRoot, plateLayer;
        Label huntLabel;
        readonly List<(VisualElement row, VisualElement fill, Label name, VisualElement gem)> partyRows = new();
        readonly Dictionary<int, Label> plates = new();
        int partyShown = -1;
        int huntShownId = int.MinValue;
        float huntFlash;

        void BuildCoop()
        {
            partyRoot = El("party", root);
            plateLayer = El("plates", root);
            huntLabel = Lbl("", "bossbar__hunt", bossBox);
            ApplySerif(huntLabel);
        }

        void TickCoop(float dt)
        {
            bool on = Coop.InStage;
            partyRoot.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            plateLayer.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            huntLabel.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            TickPartyPanel();
            TickPlates();
            TickHunt(dt);
        }

        void TickPartyPanel()
        {
            // allies only: your own bars are already on screen
            var allies = new List<Coop.Member>();
            foreach (var m in Coop.Party) if (!m.isLocal) allies.Add(m);
            if (allies.Count != partyShown)
            {
                partyShown = allies.Count;
                partyRoot.Clear();
                partyRows.Clear();
                foreach (var _ in allies)
                {
                    var row = El("party__row", partyRoot);
                    var gem = El("party__gem", row);
                    var col = El("party__col", row);
                    var name = Lbl("", "party__name", col);
                    var bar = El("party__bar", col);
                    var fill = El("party__fill", bar);
                    partyRows.Add((row, fill, name, gem));
                }
            }
            for (int i = 0; i < allies.Count && i < partyRows.Count; i++)
            {
                var m = allies[i];
                var (row, fill, name, gem) = partyRows[i];
                name.text = m.dead ? m.name.ToUpperInvariant() + "   ✝" : m.name.ToUpperInvariant();
                gem.style.backgroundColor = Coop.SlotColor(m.slot);
                fill.style.width = new Length(Mathf.Clamp01(m.health01) * 100f, LengthUnit.Percent);
                row.EnableInClassList("party__row--dead", m.dead);
            }
        }

        void TickPlates()
        {
            if (cam == null) cam = Camera.main;
            var seen = new HashSet<int>();
            foreach (var m in Coop.Party)
            {
                if (m.isLocal || m.avatar == null) continue;
                seen.Add(m.id);
                if (!plates.TryGetValue(m.id, out var plate))
                {
                    plate = Lbl("", "plate", plateLayer);
                    ApplySerif(plate);
                    plates[m.id] = plate;
                }
                plate.text = m.name;
                plate.style.color = Coop.SlotColor(m.slot);
                Vector3 head = m.avatar.transform.position + Vector3.up * 2.55f;
                Vector3 vp = cam != null ? cam.WorldToViewportPoint(head) : Vector3.back;
                bool show = vp.z > 0.3f && vp.x > -0.05f && vp.x < 1.05f && vp.y > -0.05f && vp.y < 1.05f;
                plate.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) continue;
                var size = root.layout.size;
                plate.style.left = vp.x * size.x;
                plate.style.top = (1f - vp.y) * size.y;
                // far allies' names shrink and fade a little
                float k = Mathf.InverseLerp(40f, 6f, vp.z);
                plate.style.opacity = Mathf.Lerp(0.45f, 1f, k) * (m.dead ? 0.5f : 1f);
                plate.style.scale = new Scale(Vector3.one * Mathf.Lerp(0.75f, 1f, k));
            }
            var gone = new List<int>();
            foreach (var kv in plates) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone) { plates[id].RemoveFromHierarchy(); plates.Remove(id); }
        }

        void TickHunt(float dt)
        {
            var b = BossBarTarget;
            int id = -1;
            if (b != null && b.IsAlive)
            {
                if (b.Mirror != null) id = b.Mirror.aggroId;
                else if (b.Aggro != null) id = b.Aggro.NetId;
            }
            if (id != huntShownId)
            {
                huntShownId = id;
                var m = Coop.Find(id);
                if (m == null || b == null) huntLabel.text = "";
                else if (m.isLocal) { huntLabel.text = $"{b.DisplayName} hunts you"; huntFlash = 1f; }
                else huntLabel.text = $"{b.DisplayName} hunts {m.name}";
                huntLabel.style.color = m != null ? (m.isLocal ? new Color(1f, 0.72f, 0.55f) : Coop.SlotColor(m.slot)) : Color.white;
            }
            huntFlash = Mathf.MoveTowards(huntFlash, 0f, dt * 1.2f);
            huntLabel.style.scale = new Scale(Vector3.one * (1f + Ease.OutCubic(huntFlash) * 0.18f));
        }
    }
}
