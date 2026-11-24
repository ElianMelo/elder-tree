using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ElderTree.InputHandling
{
    /// <summary>
    /// Player control remapping on top of ElderTreeControls.inputactions: the list of remappable
    /// bindings shown in the Controls tab (keyboard &amp; mouse and gamepad), short glyph names for HUD
    /// prompts, interactive rebinding (a new key that is already used swaps with the old one) and
    /// persistence of the overrides in PlayerPrefs. Pause / Menu stays fixed so the menu is always reachable.
    /// </summary>
    public static class ControlBindings
    {
        public const string KbmGroup = "Keyboard&Mouse";
        public const string PadGroup = "Gamepad";
        const string PrefKey = "eldertree.bindings";

        /// <summary>One binding of an action: the nth plain binding of a device group, or a composite part.</summary>
        public sealed class Slot
        {
            public string action, part;
            public int nth;
            public bool pad;
            public Slot(string action, bool pad, int nth = 0, string part = null) { this.action = action; this.pad = pad; this.nth = nth; this.part = part; }
        }

        /// <summary>A row of the Controls tab. A null slot shows <c>kbmFixed</c> / <c>padFixed</c> instead and can't be remapped.</summary>
        public sealed class Entry
        {
            public string label;
            public Slot kbm, pad;
            public Func<string> kbmFixed, padFixed;
        }

        static InputActionAsset Asset => PlayerInputReader.Instance != null ? PlayerInputReader.Instance.Actions : null;

        static Slot K(string a, int nth = 0, string part = null) => new Slot(a, false, nth, part);
        static Slot P(string a, int nth = 0) => new Slot(a, true, nth);

        public static readonly Entry[] Entries =
        {
            new Entry { label = "Move forward", kbm = K("Move", part: "up"), padFixed = () => "L-Stick" },
            new Entry { label = "Move back", kbm = K("Move", part: "down"), padFixed = () => "L-Stick" },
            new Entry { label = "Move left", kbm = K("Move", part: "left"), padFixed = () => "L-Stick" },
            new Entry { label = "Move right", kbm = K("Move", part: "right"), padFixed = () => "L-Stick" },
            new Entry { label = "Camera", kbmFixed = () => "Mouse", padFixed = () => "R-Stick" },
            new Entry { label = "Light attack", kbm = K("Light"), pad = P("Light") },
            new Entry { label = "Heavy attack", kbmFixed = () => $"{Key("Modifier", false)} + {Key("Light", false)}", pad = P("Heavy") },
            new Entry { label = "Heavy / special modifier", kbm = K("Modifier"), padFixed = () => "—" },
            new Entry { label = "Special attack", kbmFixed = () => $"{Key("Modifier", false)} + {Key("Aim", false)}", padFixed = () => $"{Key("Light", true)} + {Key("Heavy", true)}" },
            new Entry { label = "Aim bow", kbm = K("Aim"), pad = P("Aim") },
            new Entry { label = "Guard", kbm = K("Guard"), pad = P("Guard") },
            new Entry { label = "Roll · hold to sprint", kbm = K("Dodge"), pad = P("Dodge") },
            new Entry { label = "Jump", kbm = K("Jump"), pad = P("Jump") },
            new Entry { label = "Drink health potion", kbm = K("UseItem"), pad = P("UseItem") },
            new Entry { label = "Lock on", kbm = K("LockOn"), pad = P("LockOn") },
            new Entry { label = "Lock on (alternate)", kbm = K("LockOn", 1), padFixed = () => "—" },
            new Entry { label = "Switch target", kbmFixed = () => "Flick / Wheel", padFixed = () => "R-Stick ←→" },
            new Entry { label = "Walk toggle", kbm = K("Walk"), pad = P("Walk") },
            new Entry { label = "Mask slot 1", kbm = K("Mask1"), pad = P("Mask1") },
            new Entry { label = "Mask slot 2", kbm = K("Mask2"), pad = P("Mask2") },
            new Entry { label = "Mask slot 3", kbm = K("Mask3"), pad = P("Mask3") },
            new Entry { label = "Mask slot 4", kbm = K("Mask4"), pad = P("Mask4") },
            new Entry { label = "Show / hide control hints", kbm = K("Help"), pad = P("Help") },
            new Entry { label = "Menu", kbmFixed = () => "Esc", padFixed = () => "Start" },
        };

        // ------------------------------------------------------------------ lookup
        static bool InGroup(InputBinding b, string group) =>
            !string.IsNullOrEmpty(b.groups) && Array.IndexOf(b.groups.Split(InputBinding.Separator), group) >= 0;

        /// <summary>Binding index of a slot in its action, or -1.</summary>
        public static int IndexOf(InputAction action, Slot s)
        {
            if (action == null) return -1;
            string group = s.pad ? PadGroup : KbmGroup;
            int seen = 0;
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.isComposite || !InGroup(b, group)) continue;
                if (s.part != null ? !(b.isPartOfComposite && string.Equals(b.name, s.part, StringComparison.OrdinalIgnoreCase)) : b.isPartOfComposite) continue;
                if (seen++ == s.nth) return i;
            }
            return -1;
        }

        static bool Resolve(Slot s, out InputAction action, out int index)
        {
            action = Asset?.FindAction(s.action);
            index = IndexOf(action, s);
            return index >= 0;
        }

        public static string PathOf(Slot s) => Resolve(s, out var a, out int i) ? a.bindings[i].effectivePath : null;

        /// <summary>Glyph for a slot ("LMB", "Space", "RB", "D-Pad ↑").</summary>
        public static string Glyph(Slot s) => s == null ? "—" : PathGlyph(PathOf(s));

        /// <summary>Glyph for the nth binding of an action on a device family.</summary>
        public static string Key(string action, bool pad, int nth = 0) => Glyph(new Slot(action, pad, nth));

        /// <summary>Short key for the mask strip: the D-pad arrow alone, or the key.</summary>
        public static string MaskKey(int slot, bool pad)
        {
            string g = Key($"Mask{slot + 1}", pad);
            return g.StartsWith("D-Pad ") ? g.Substring(6) : g;
        }

        /// <summary>"1 - 4" / "D-Pad" while the masks keep their default layout, otherwise the four keys.</summary>
        public static string MaskRange(bool pad)
        {
            var keys = new string[4];
            for (int i = 0; i < 4; i++) keys[i] = Key($"Mask{i + 1}", pad);
            if (!pad && keys[0] == "1" && keys[1] == "2" && keys[2] == "3" && keys[3] == "4") return "1 - 4";
            if (pad && keys[0] == "D-Pad ↑" && keys[1] == "D-Pad →" && keys[2] == "D-Pad ↓" && keys[3] == "D-Pad ←") return "D-Pad";
            if (pad) for (int i = 0; i < 4; i++) keys[i] = MaskKey(i, true);
            return string.Join(" ", keys);
        }

        public static string PathGlyph(string path)
        {
            if (string.IsNullOrEmpty(path)) return "—";
            string p = path.ToLowerInvariant();
            int slash = p.IndexOf('/');
            string device = slash > 0 ? p.Substring(0, slash) : p;
            string ctrl = slash > 0 ? p.Substring(slash + 1) : "";
            if (device.Contains("gamepad") || device.Contains("xinput") || device.Contains("dualshock") || device.Contains("dualsense") || device.Contains("switchpro"))
            {
                switch (ctrl)
                {
                    case "buttonsouth": return "A";
                    case "buttoneast": return "B";
                    case "buttonwest": return "X";
                    case "buttonnorth": return "Y";
                    case "leftshoulder": return "LB";
                    case "rightshoulder": return "RB";
                    case "lefttrigger": return "LT";
                    case "righttrigger": return "RT";
                    case "leftstickpress": return "L3";
                    case "rightstickpress": return "R3";
                    case "start": return "Start";
                    case "select": return "View";
                    case "dpad/up": return "D-Pad ↑";
                    case "dpad/right": return "D-Pad →";
                    case "dpad/down": return "D-Pad ↓";
                    case "dpad/left": return "D-Pad ←";
                }
            }
            else if (device.Contains("mouse"))
            {
                switch (ctrl)
                {
                    case "leftbutton": return "LMB";
                    case "rightbutton": return "RMB";
                    case "middlebutton": return "MMB";
                    case "backbutton": return "Mouse 4";
                    case "forwardbutton": return "Mouse 5";
                }
            }
            else if (device.Contains("keyboard"))
            {
                switch (ctrl)
                {
                    case "leftshift": return "Shift";
                    case "rightshift": return "R-Shift";
                    case "leftctrl": return "Ctrl";
                    case "rightctrl": return "R-Ctrl";
                    case "leftalt": return "Alt";
                    case "rightalt": return "R-Alt";
                    case "uparrow": return "↑";
                    case "downarrow": return "↓";
                    case "leftarrow": return "←";
                    case "rightarrow": return "→";
                    case "escape": return "Esc";
                    case "backquote": return "`";
                }
            }
            string s = InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
            return string.IsNullOrEmpty(s) ? path : s;
        }

        // ------------------------------------------------------------------ rebinding
        static InputActionRebindingExtensions.RebindingOperation op;
        public static bool Rebinding => op != null;

        /// <summary>
        /// Wait for the next key / button of the slot's device family and bind it. Esc (keyboard) or Start (pad)
        /// cancels. <paramref name="done"/> gets true when a new binding was taken.
        /// </summary>
        public static bool StartRebind(Slot s, Action<bool> done)
        {
            CancelRebind();
            if (s == null || !Resolve(s, out var action, out int index)) return false;
            string oldPath = action.bindings[index].effectivePath;
            bool wasEnabled = action.enabled;
            if (wasEnabled) action.Disable();

            var o = action.PerformInteractiveRebinding(index)
                .WithExpectedControlType("Button")
                .WithCancelingThrough(s.pad ? "<Gamepad>/start" : "<Keyboard>/escape")
                .WithControlsExcluding("<Keyboard>/escape")
                .WithControlsExcluding("<Keyboard>/anyKey")
                .WithControlsExcluding("<Gamepad>/start")
                .WithControlsExcluding("<Gamepad>/leftStick")
                .WithControlsExcluding("<Gamepad>/rightStick")
                .WithControlsExcluding("<Gamepad>/leftStick/*")
                .WithControlsExcluding("<Gamepad>/rightStick/*")
                .WithControlsExcluding("<Pointer>/position")
                .WithControlsExcluding("<Pointer>/delta")
                .WithControlsExcluding("<Pointer>/press")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithControlsExcluding("<Mouse>/scroll/*")
                .WithControlsExcluding("<Mouse>/press")
                .WithControlsExcluding("<Mouse>/clickCount")
                .OnMatchWaitForAnother(0.05f);
            if (s.pad) o.WithControlsHavingToMatchPath("<Gamepad>");
            else o.WithControlsHavingToMatchPath("<Keyboard>").WithControlsHavingToMatchPath("<Mouse>");

            void Finish(bool ok)
            {
                if (ok)
                {
                    string newPath = action.bindings[index].effectivePath;
                    if (!string.Equals(newPath, oldPath, StringComparison.OrdinalIgnoreCase)) SwapConflicts(s, newPath, oldPath);
                    Save();
                }
                o.Dispose();
                if (op == o) op = null;
                if (wasEnabled) action.Enable();
                done?.Invoke(ok);
            }
            o.OnComplete(_ => Finish(true)).OnCancel(_ => Finish(false));
            op = o;
            o.Start();
            return true;
        }

        public static void CancelRebind() => op?.Cancel();

        /// <summary>Another remappable slot of the same device family on the new key takes the old key instead.</summary>
        static void SwapConflicts(Slot changed, string newPath, string oldPath)
        {
            foreach (var e in Entries)
            {
                var other = changed.pad ? e.pad : e.kbm;
                if (other == null || (other.action == changed.action && other.nth == changed.nth && other.part == changed.part)) continue;
                if (!Resolve(other, out var a, out int i)) continue;
                if (!string.Equals(a.bindings[i].effectivePath, newPath, StringComparison.OrdinalIgnoreCase)) continue;
                a.ApplyBindingOverride(i, oldPath);
            }
        }

        public static void ResetAll()
        {
            CancelRebind();
            var asset = Asset;
            if (asset == null) return;
            asset.RemoveAllBindingOverrides();
            Save();
        }

        static void Save()
        {
            var asset = Asset;
            if (asset == null) return;
            PlayerPrefs.SetString(PrefKey, asset.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        /// <summary>Called by PlayerInputReader before its maps are enabled.</summary>
        public static void Load(InputActionAsset asset)
        {
            if (asset == null) return;
            asset.RemoveAllBindingOverrides();
            string json = PlayerPrefs.GetString(PrefKey, "");
            if (string.IsNullOrEmpty(json)) return;
            try { asset.LoadBindingOverridesFromJson(json); }
            catch (Exception e) { Debug.LogWarning($"[ControlBindings] ignoring saved bindings: {e.Message}"); }
        }
    }
}
