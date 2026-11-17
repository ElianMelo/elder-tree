using System.Collections.Generic;
using TungSahur.Audio;
using TungSahur.Enemies;
using TungSahur.InputHandling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace TungSahur.UI
{
    public partial class HUD
    {
        // ================================================================== F1 debug menu: boss roster
        // Lists every boss configured on the BossEncounter. Up/Down + Select (or 1-9) spawns or
        // despawns one; the last row sends them all away. Pauses the game while open, like the
        // pause menu, so the menu keys never leak into gameplay.
        class DebugRow
        {
            public BombardiroBoss boss;     // null = "Despawn all"
            public VisualElement row;
            public Label status, action;
        }

        VisualElement debugRoot, debugList;
        readonly List<DebugRow> debugRows = new();
        int debugIndex;

        public bool DebugMenuOpen => debugRoot != null && debugRoot.style.display == DisplayStyle.Flex;

        void BuildDebugMenu()
        {
            debugRoot = El("dbg", root);
            debugRoot.style.display = DisplayStyle.None;
            var frame = El("dbg__frame", debugRoot);
            var head = El("dbg__head", frame);
            ApplySerif(Lbl("BOSSES", "dbg__title", head));
            Lbl("DEBUG · F1", "dbg__tag", head);
            debugList = El("dbg__list", frame);
            Lbl("", "dbg__foot", frame).name = "dbg-foot";
        }

        void RebuildDebugRows()
        {
            debugList.Clear();
            debugRows.Clear();
            var enc = BossEncounter.Instance;
            if (enc != null)
            {
                for (int i = 0; i < enc.bosses.Count; i++)
                {
                    var b = enc.bosses[i];
                    if (b == null) continue;
                    var r = new DebugRow { boss = b, row = El("dbg__row", debugList) };
                    Lbl(i < 9 ? (i + 1).ToString() : "", "dbg__key", r.row);
                    var col = El("dbg__col", r.row);
                    ApplySerif(Lbl(b.displayName, "dbg__name", col));
                    r.status = Lbl("", "dbg__status", col);
                    r.action = Lbl("", "dbg__action", r.row);
                    debugRows.Add(r);
                }
            }
            if (debugRows.Count == 0) Lbl("No bosses configured on the BossEncounter.", "dbg__empty", debugList);

            var all = new DebugRow { row = El("dbg__row dbg__row--all", debugList) };
            Lbl("0", "dbg__key", all.row);
            var allCol = El("dbg__col", all.row);
            Lbl("Despawn all", "dbg__name", allCol);
            all.status = Lbl("", "dbg__status", allCol);
            all.action = Lbl("", "dbg__action", all.row);
            debugRows.Add(all);
            debugIndex = Mathf.Clamp(debugIndex, 0, debugRows.Count - 1);
        }

        void SetDebugMenu(bool open)
        {
            if (open == DebugMenuOpen) return;
            if (open && (TitleShowing || PauseOpen)) return;
            if (open) RebuildDebugRows();
            debugRoot.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            AudioManager.Play2D(open ? Sfx.UISelect : Sfx.UIBack, 0.7f);
            PauseChanged?.Invoke(open);
            if (open)
            {
                debugRoot.style.opacity = 0f;
                StartCoroutine(FadeElement(debugRoot, 0f, 1f, 0.15f));
                RefreshDebugRows();
            }
        }

        void ActivateDebugRow(int i)
        {
            var enc = BossEncounter.Instance;
            if (enc == null || i < 0 || i >= debugRows.Count) return;
            debugIndex = i;
            var b = debugRows[i].boss;
            if (b == null) enc.DespawnAll();
            else if (BossEncounter.IsSpawned(b)) enc.Despawn(b);
            else enc.Spawn(b);
            AudioManager.Play2D(Sfx.UISelect, 0.7f);
            RefreshDebugRows();
        }

        static string BossStatus(BombardiroBoss b)
        {
            if (!BossEncounter.IsSpawned(b)) return "Not spawned";
            if (b.State == BombardiroBoss.Mode.Dead) return "Felled";
            return $"{b.State} · {Mathf.CeilToInt(b.Health)} / {Mathf.RoundToInt(b.maxHealth)} HP{(b.PhaseTwo ? " · Phase 2" : "")}";
        }

        void RefreshDebugRows()
        {
            int spawned = 0;
            for (int i = 0; i < debugRows.Count; i++)
            {
                var r = debugRows[i];
                r.row.EnableInClassList("dbg__row--active", i == debugIndex);
                if (r.boss == null) continue;
                bool on = BossEncounter.IsSpawned(r.boss);
                if (on) spawned++;
                r.status.text = BossStatus(r.boss);
                r.action.text = on ? "Despawn" : "Spawn";
                r.row.EnableInClassList("dbg__row--on", on);
            }
            var last = debugRows[debugRows.Count - 1];
            last.status.text = spawned == 0 ? "Nothing spawned" : $"{spawned} spawned";
            last.action.text = spawned == 0 ? "" : "Despawn";

            bool pad = input != null && input.Device == InputDeviceKind.Gamepad;
            debugRoot.Q<Label>("dbg-foot").text = pad
                ? "D-Pad  Choose      A  Spawn / despawn      B  Close"
                : "↑ ↓  Choose      Enter / 1-9  Spawn / despawn      F1 / Esc  Close";
        }

        void TickDebugMenu(float dt)
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) { SetDebugMenu(!DebugMenuOpen); return; }
            if (!DebugMenuOpen || input == null) return;

            var nav = input.ConsumeNavigate();
            if (nav.y != 0)
            {
                debugIndex = (debugIndex - nav.y + debugRows.Count) % debugRows.Count;
                AudioManager.Play2D(Sfx.UIMove, 0.5f);
            }
            if (input.SubmitPressed) ActivateDebugRow(debugIndex);
            if (kb != null)
            {
                for (int i = 0; i < 9 && i < debugRows.Count - 1; i++)
                    if (kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.Numpad1 + i].wasPressedThisFrame) ActivateDebugRow(i);
                if (kb.digit0Key.wasPressedThisFrame || kb.numpad0Key.wasPressedThisFrame) ActivateDebugRow(debugRows.Count - 1);
            }
            if (input.CancelPressed) { SetDebugMenu(false); return; }
            RefreshDebugRows();
        }
    }
}
