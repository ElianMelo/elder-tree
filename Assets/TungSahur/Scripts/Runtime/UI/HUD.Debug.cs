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
        // ================================================================== F1 debug menu: boss roster + test tools
        // Lists every boss configured on the BossEncounter. Up/Down + Select (or 1-9) spawns or
        // despawns one; the last row sends them all away. Pauses the game while open, like the
        // pause menu, so the menu keys never leak into gameplay.
        class DebugRow
        {
            public BombardiroBoss boss;     // null + no tool = "Despawn all"
            public VisualElement row;
            public Label status, action;
            // tool rows (masks, obstacles): run an action, show a live status
            public System.Action tool;
            public System.Func<string> toolStatus;
            public string toolAction;
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
            ApplySerif(Lbl("BOSSES & TESTS", "dbg__title", head));
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

            // standalone tests for the mask reward and the breakable obstacles
            void Tool(string name, string action, System.Action run, System.Func<string> status)
            {
                var r = new DebugRow { row = El("dbg__row", debugList), tool = run, toolStatus = status, toolAction = action };
                Lbl("", "dbg__key", r.row);
                var c = El("dbg__col", r.row);
                ApplySerif(Lbl(name, "dbg__name", c));
                r.status = Lbl("", "dbg__status", c);
                r.action = Lbl(action, "dbg__action", r.row);
                debugRows.Add(r);
            }
            var masks = Masks.MaskSystem.Instance;
            if (enc != null && enc.rewardMask != null)
            {
                var mk = enc.rewardMask;
                Tool($"{mk.displayName} (pickup)", "Spawn", () => enc.SpawnMaskForTest(),
                    () => masks != null && masks.Owns(mk) ? "Owned: putting it on is a quick transformation" : "Not owned: picking it up plays the cinematic");
                Tool("Forget all masks", "Forget", () => masks?.ForgetAll(),
                    () => masks != null && masks.Worn != null ? $"Wearing {masks.Worn.displayName}" : "Elder Tree form");
            }
            Tool("Restore breakables", "Restore", World.Breakable.RestoreAll, () =>
            {
                int broken = 0;
                foreach (var b in World.Breakable.All) if (b != null && b.Broken) broken++;
                return $"{broken} / {World.Breakable.All.Count} broken";
            });

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
            var tool = debugRows[i].tool;
            if (tool != null)
            {
                tool();
                AudioManager.Play2D(Sfx.UISelect, 0.7f);
                RefreshDebugRows();
                return;
            }
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
                if (r.tool != null) { r.status.text = r.toolStatus?.Invoke() ?? ""; r.action.text = r.toolAction; continue; }
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
                int bossRows = 0;
                foreach (var r in debugRows) if (r.boss != null) bossRows++;
                for (int i = 0; i < 9 && i < bossRows; i++)
                    if (kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.Numpad1 + i].wasPressedThisFrame) ActivateDebugRow(i);
                if (kb.digit0Key.wasPressedThisFrame || kb.numpad0Key.wasPressedThisFrame) ActivateDebugRow(debugRows.Count - 1);
            }
            if (input.CancelPressed) { SetDebugMenu(false); return; }
            RefreshDebugRows();
        }
    }
}
