using System.Collections.Generic;
using ElderTree.Audio;
using ElderTree.Enemies;
using ElderTree.InputHandling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ElderTree.UI
{
    public partial class HUD
    {
        // ================================================================== F1 debug menu: bosses, masks, world, consumables
        // Four tabs (Q / E, LB / RB or Left / Right switch them). Up/Down + Select (or 1-9) runs the chosen row; on the
        // Boss tab, 0 sends every boss away. Pauses the game while open, like the pause menu, so the menu keys never
        // leak into gameplay. It sits above every other HUD layer.
        enum DebugTab { Boss, Mask, World, Consumable }
        static readonly string[] DebugTabNames = { "Boss", "Mask", "World", "Consumable" };

        class DebugRow
        {
            public BossBase boss;     // null + no tool = "Despawn all"
            public VisualElement row;
            public Label status, action;
            // tool rows (masks, obstacles, potions): run an action, show a live status
            public System.Action tool;
            public System.Func<string> toolStatus;
            public string toolAction;
        }

        VisualElement debugRoot, debugList, debugTabBar;
        Label debugTabPrev, debugTabNext;
        readonly List<Label> debugTabLabels = new();
        readonly List<DebugRow> debugRows = new();
        int debugIndex;
        DebugTab debugTab;

        public bool DebugMenuOpen => debugRoot != null && debugRoot.style.display == DisplayStyle.Flex;

        void BuildDebugMenu()
        {
            debugRoot = El("dbg", root);
            debugRoot.style.display = DisplayStyle.None;
            var frame = El("dbg__frame", debugRoot);
            var head = El("dbg__head", frame);
            ApplySerif(Lbl("DEBUG MENU", "dbg__title", head));
            Lbl("F1", "dbg__tag", head);

            debugTabBar = El("dbg__tabs", frame);
            debugTabPrev = Lbl("Q", "dbg__tabkey", debugTabBar);
            for (int i = 0; i < DebugTabNames.Length; i++)
            {
                var tab = Lbl(DebugTabNames[i], "dbg__tab", debugTabBar);
                ApplySerif(tab);
                debugTabLabels.Add(tab);
            }
            debugTabNext = Lbl("E", "dbg__tabkey", debugTabBar);

            debugList = El("dbg__list", frame);
            Lbl("", "dbg__foot", frame).name = "dbg-foot";
        }

        void RebuildDebugRows()
        {
            debugList.Clear();
            debugRows.Clear();
            for (int i = 0; i < debugTabLabels.Count; i++)
                debugTabLabels[i].EnableInClassList("dbg__tab--active", i == (int)debugTab);

            var enc = BossEncounter.Instance;
            switch (debugTab)
            {
                case DebugTab.Boss: BuildBossRows(enc); break;
                case DebugTab.Mask: BuildMaskRows(enc); break;
                case DebugTab.World: BuildWorldRows(); break;
                case DebugTab.Consumable: BuildConsumableRows(); break;
            }
            if (debugRows.Count == 0) Lbl("Nothing to show here.", "dbg__empty", debugList);
            debugIndex = Mathf.Clamp(debugIndex, 0, Mathf.Max(0, debugRows.Count - 1));
        }

        /// <summary>A row that runs <paramref name="run"/> and shows a live status line.</summary>
        void DebugTool(string name, string action, System.Action run, System.Func<string> status)
        {
            var r = new DebugRow { row = El("dbg__row", debugList), tool = run, toolStatus = status, toolAction = action };
            int n = debugRows.Count;
            Lbl(n < 9 ? (n + 1).ToString() : "", "dbg__key", r.row);
            var c = El("dbg__col", r.row);
            ApplySerif(Lbl(name, "dbg__name", c));
            r.status = Lbl("", "dbg__status", c);
            r.action = Lbl(action, "dbg__action", r.row);
            debugRows.Add(r);
        }

        void BuildBossRows(BossEncounter enc)
        {
            if (enc == null) return;
            for (int i = 0; i < enc.bosses.Count; i++)
            {
                var b = enc.bosses[i];
                if (b == null) continue;
                var r = new DebugRow { boss = b, row = El("dbg__row", debugList) };
                int n = debugRows.Count;
                Lbl(n < 9 ? (n + 1).ToString() : "", "dbg__key", r.row);
                var col = El("dbg__col", r.row);
                ApplySerif(Lbl(b.DisplayName, "dbg__name", col));
                r.status = Lbl("", "dbg__status", col);
                r.action = Lbl("", "dbg__action", r.row);
                debugRows.Add(r);
            }
            if (debugRows.Count == 0) return;
            var all = new DebugRow { row = El("dbg__row dbg__row--all", debugList) };
            Lbl("0", "dbg__key", all.row);
            var allCol = El("dbg__col", all.row);
            Lbl("Despawn all", "dbg__name", allCol);
            all.status = Lbl("", "dbg__status", allCol);
            all.action = Lbl("", "dbg__action", all.row);
            debugRows.Add(all);
        }

        void BuildMaskRows(BossEncounter enc)
        {
            var masks = Masks.MaskSystem.Instance;
            bool anyMask = false;
            if (enc != null)
            {
                foreach (var reward in enc.AllRewards())
                {
                    var mk = reward;
                    anyMask = true;
                    DebugTool($"{mk.displayName} (pickup)", "Spawn", () => enc.SpawnMaskForTest(mk),
                        () => masks != null && masks.Owns(mk) ? "Owned: putting it on is a quick transformation" : "Not owned: picking it up plays the cinematic");
                }
            }
            if (anyMask)
            {
                DebugTool("Forget all masks", "Forget", () => masks?.ForgetAll(),
                    () => masks != null && masks.Worn != null ? $"Wearing {masks.Worn.displayName}" : "Elder Tree form");
            }
        }

        void BuildWorldRows()
        {
            DebugTool("Restore breakables + vines", "Restore", () => { World.Breakable.RestoreAll(); World.Cuttable.RestoreAll(); }, () =>
            {
                int broken = 0, cut = 0;
                foreach (var b in World.Breakable.All) if (b != null && b.Broken) broken++;
                foreach (var c in World.Cuttable.All) if (c != null && c.Cleared) cut++;
                return $"{broken} / {World.Breakable.All.Count} broken, {cut} / {World.Cuttable.All.Count} vines cleared";
            });
        }

        void BuildConsumableRows()
        {
            if (stats == null) return;
            DebugTool("Restore potion flasks", "Restore", () => stats.RefillPotions(),
                () => $"{stats.Potions} / {stats.maxPotions} health potions");
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
                debugRoot.BringToFront();
                debugRoot.style.opacity = 0f;
                StartCoroutine(FadeElement(debugRoot, 0f, 1f, 0.15f));
                RefreshDebugRows();
            }
        }

        void SwitchDebugTab(int dir)
        {
            int n = DebugTabNames.Length;
            debugTab = (DebugTab)(((int)debugTab + dir + n) % n);
            debugIndex = 0;
            AudioManager.Play2D(Sfx.UIMove, 0.6f);
            RebuildDebugRows();
            RefreshDebugRows();
        }

        void ActivateDebugRow(int i)
        {
            if (i < 0 || i >= debugRows.Count) return;
            debugIndex = i;
            var tool = debugRows[i].tool;
            if (tool != null)
            {
                tool();
                AudioManager.Play2D(Sfx.UISelect, 0.7f);
                RefreshDebugRows();
                return;
            }
            var enc = BossEncounter.Instance;
            if (enc == null) return;
            var b = debugRows[i].boss;
            if (b == null) enc.DespawnAll();
            else if (BossEncounter.IsSpawned(b)) enc.Despawn(b);
            else enc.Spawn(b);
            AudioManager.Play2D(Sfx.UISelect, 0.7f);
            RefreshDebugRows();
        }

        static string BossStatus(BossBase b)
        {
            if (!BossEncounter.IsSpawned(b)) return "Not spawned";
            if (b.IsDead) return "Felled";
            return $"{b.StateLabel} · {Mathf.CeilToInt(b.Health)} / {Mathf.RoundToInt(b.MaxHealth)} HP{(b.PhaseTwo ? " · Phase 2" : "")}";
        }

        void RefreshDebugRows()
        {
            bool pad = input != null && input.Device == InputDeviceKind.Gamepad;
            debugTabPrev.text = pad ? "LB" : "Q";
            debugTabNext.text = pad ? "RB" : "E";

            int spawned = 0;
            DebugRow allRow = null;
            for (int i = 0; i < debugRows.Count; i++)
            {
                var r = debugRows[i];
                r.row.EnableInClassList("dbg__row--active", i == debugIndex);
                if (r.tool != null) { r.status.text = r.toolStatus?.Invoke() ?? ""; r.action.text = r.toolAction; continue; }
                if (r.boss == null) { allRow = r; continue; }
                bool on = BossEncounter.IsSpawned(r.boss);
                if (on) spawned++;
                r.status.text = BossStatus(r.boss);
                r.action.text = on ? "Despawn" : "Spawn";
                r.row.EnableInClassList("dbg__row--on", on);
            }
            if (allRow != null)
            {
                allRow.status.text = spawned == 0 ? "Nothing spawned" : $"{spawned} spawned";
                allRow.action.text = spawned == 0 ? "" : "Despawn";
            }

            string run = debugTab == DebugTab.Boss ? "Spawn / despawn" : "Run";
            debugRoot.Q<Label>("dbg-foot").text = pad
                ? $"LB RB  Tab      D-Pad  Choose      A  {run}      B  Close"
                : $"Q E  Tab      ↑ ↓  Choose      Enter / 1-9  {run}      F1 / Esc  Close      F2  Boss select";
        }

        bool leavingToBossSelect;

        void TickDebugMenu(float dt)
        {
            var kb = Keyboard.current;
            // F2 (debug): straight back to the boss select screen from any arena, paused or not
            if (kb != null && kb.f2Key.wasPressedThisFrame && !leavingToBossSelect)
            {
                leavingToBossSelect = true;
                if (DebugMenuOpen) SetDebugMenu(false);
                AudioManager.Play2D(Sfx.UISelect, 0.7f);
                StartCoroutine(SceneFlow.FadeAndLoad(this, SceneFlow.BossSelect));
                return;
            }
            if (kb != null && kb.f1Key.wasPressedThisFrame) { SetDebugMenu(!DebugMenuOpen); return; }
            if (!DebugMenuOpen || input == null) return;

            if (input.TabLeftPressed) { SwitchDebugTab(-1); return; }
            if (input.TabRightPressed) { SwitchDebugTab(1); return; }
            var nav = input.ConsumeNavigate();
            if (nav.x != 0) { SwitchDebugTab(nav.x); return; }
            if (nav.y != 0 && debugRows.Count > 0)
            {
                debugIndex = (debugIndex - nav.y + debugRows.Count) % debugRows.Count;
                AudioManager.Play2D(Sfx.UIMove, 0.5f);
            }
            if (input.SubmitPressed) ActivateDebugRow(debugIndex);
            if (kb != null)
            {
                int numbered = 0;
                foreach (var r in debugRows) if (r.boss != null || r.tool != null) numbered++;
                for (int i = 0; i < 9 && i < numbered; i++)
                    if (kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.Numpad1 + i].wasPressedThisFrame) ActivateDebugRow(i);
                if (debugTab == DebugTab.Boss && debugRows.Count > 0 && (kb.digit0Key.wasPressedThisFrame || kb.numpad0Key.wasPressedThisFrame))
                    ActivateDebugRow(debugRows.Count - 1);
            }
            if (input.CancelPressed) { SetDebugMenu(false); return; }
            RefreshDebugRows();
        }
    }
}
