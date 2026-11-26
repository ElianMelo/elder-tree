using System;
using System.Collections.Generic;
using ElderTree.Audio;
using ElderTree.InputHandling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ElderTree.UI
{
    /// <summary>
    /// A scrolling list of option rows (value pickers, sliders, key bindings, buttons) driven by pad, keyboard or
    /// mouse. Shared by the pause menu and the main menu so both edit the same settings the same way. Styles are the
    /// .setting / .slider / .bindrow classes in ElderTreeHUD.uss.
    /// </summary>
    public class OptionList
    {
        public enum RowKind { Value, Slider, Bind, Button }

        class Row
        {
            public RowKind kind;
            public VisualElement el;
            public Func<string> value;
            public Action<int> change;
            public Label valueLbl, nameLbl;
            public Func<float> get;
            public Action<float> set;
            public VisualElement track, fill, knob;
            public ControlBindings.Entry entry;
            public readonly Label[] cells = new Label[2];
            public Action press;
            public string help;
        }

        public readonly ScrollView Scroll;
        /// <summary>Selection or listening state changed: footers and help text should follow.</summary>
        public event Action Changed;
        /// <summary>A key binding was remapped or reset: glyphs elsewhere on screen should follow.</summary>
        public event Action BindingsChanged;

        readonly List<Row> rows = new();
        readonly Action<VisualElement> serif;
        int rowIndex, bindColumn;
        Row listening;
        float guardUntil, sliderSoundAt;

        public int Count => rows.Count;
        public bool Listening => listening != null;
        /// <summary>Help line of the highlighted row (empty if none).</summary>
        public string CurrentHelp => rows.Count > 0 ? rows[Mathf.Clamp(rowIndex, 0, rows.Count - 1)].help ?? "" : "";

        public OptionList(VisualElement parent, Action<VisualElement> applySerif, string extraClass = null)
        {
            serif = applySerif;
            Scroll = new ScrollView(ScrollViewMode.Vertical);
            Scroll.AddToClassList("pause__options");
            if (!string.IsNullOrEmpty(extraClass)) Scroll.AddToClassList(extraClass);
            Scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            Scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            Scroll.mouseWheelScrollSize = 60f;
            parent.Add(Scroll);
        }

        static VisualElement El(string cls, VisualElement parent)
        {
            var e = new VisualElement();
            foreach (var c in cls.Split(' ')) e.AddToClassList(c);
            e.pickingMode = PickingMode.Ignore;
            parent.Add(e);
            return e;
        }

        static Label Lbl(string text, string cls, VisualElement parent)
        {
            var l = new Label(text);
            foreach (var c in cls.Split(' ')) l.AddToClassList(c);
            l.pickingMode = PickingMode.Ignore;
            parent.Add(l);
            return l;
        }

        public static string OnOff(bool b) => b ? "On" : "Off";
        public static string Pct(float v) => $"{Mathf.RoundToInt(v * 100f)}%";

        // ================================================================== building
        public void Clear()
        {
            ControlBindings.CancelRebind();
            Scroll.Clear();
            rows.Clear();
            listening = null;
            rowIndex = 0;
            bindColumn = 0;
            Scroll.scrollOffset = Vector2.zero;
        }

        public void Section(string title)
        {
            var s = Lbl(title, "options__section", Scroll);
            serif?.Invoke(s);
        }

        Row NewRow(RowKind kind, VisualElement el, string help)
        {
            var r = new Row { kind = kind, el = el, help = help };
            int idx = rows.Count;
            rows.Add(r);
            el.pickingMode = PickingMode.Position;
            el.RegisterCallback<PointerEnterEvent>(_ => Hover(idx, -1));
            return r;
        }

        void Hover(int idx, int column)
        {
            if (ControlBindings.Rebinding) return;
            bool moved = idx != rowIndex || (column >= 0 && column != bindColumn);
            rowIndex = idx;
            if (column >= 0) bindColumn = column;
            if (moved) Refresh();
        }

        /// <summary>A value that steps left / right (or toggles). <paramref name="change"/> gets -1 or +1.</summary>
        public void AddValue(string name, Func<string> value, Action<int> change, string help = null)
        {
            var row = El("setting", Scroll);
            var r = NewRow(RowKind.Value, row, help);
            r.value = value;
            r.change = change;
            r.nameLbl = Lbl(name, "setting__name", row);
            var right = El("setting__value-box", row);
            var left = Lbl("‹", "setting__arrow", right);
            r.valueLbl = Lbl(value(), "setting__value", right);
            var rightArrow = Lbl("›", "setting__arrow", right);
            left.pickingMode = rightArrow.pickingMode = PickingMode.Position;
            left.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); Step(r, -1); });
            rightArrow.RegisterCallback<ClickEvent>(e => { e.StopPropagation(); Step(r, 1); });
            row.RegisterCallback<ClickEvent>(_ => Step(r, 1));
        }

        void Step(Row r, int d)
        {
            r.change(d);
            Settings.Apply();
            RefreshValues();
            AudioManager.Play2D(Sfx.UIMove, 0.6f, 1.1f);
        }

        /// <summary>A 0..1 slider. <paramref name="preview"/> plays while it moves (null: the menu tick).</summary>
        public void AddSlider(string name, Func<float> get, Action<float> set, Sfx? preview, string help = null)
        {
            var row = El("setting setting--slider", Scroll);
            var r = NewRow(RowKind.Slider, row, help);
            r.get = get;
            r.set = v =>
            {
                set(Mathf.Round(Mathf.Clamp01(v) * 100f) / 100f);
                Settings.Apply();
                if (Time.unscaledTime >= sliderSoundAt)
                {
                    sliderSoundAt = Time.unscaledTime + 0.09f;
                    if (preview.HasValue) AudioManager.Play2D(preview.Value, 0.9f);
                    else AudioManager.Play2D(Sfx.UIMove, 0.6f, 1.1f);
                }
            };
            r.nameLbl = Lbl(name, "setting__name", row);
            var right = El("setting__value-box", row);
            r.track = El("slider", right);
            r.track.pickingMode = PickingMode.Position;
            r.fill = El("slider__fill", r.track);
            r.knob = El("slider__knob", r.track);
            r.valueLbl = Lbl("", "slider__value", right);
            r.value = () => Pct(get());
            r.track.RegisterCallback<PointerDownEvent>(e =>
            {
                r.track.CapturePointer(e.pointerId);
                SlideTo(r, e.localPosition.x);
                e.StopPropagation();
            });
            r.track.RegisterCallback<PointerMoveEvent>(e => { if (r.track.HasPointerCapture(e.pointerId)) SlideTo(r, e.localPosition.x); });
            r.track.RegisterCallback<PointerUpEvent>(e => { if (r.track.HasPointerCapture(e.pointerId)) r.track.ReleasePointer(e.pointerId); });
            RefreshSlider(r);
        }

        void SlideTo(Row r, float x)
        {
            float w = r.track.resolvedStyle.width;
            if (w <= 1f) return;
            r.set(x / w);
            RefreshSlider(r);
        }

        static void RefreshSlider(Row r)
        {
            float v = r.get();
            r.fill.style.width = Length.Percent(v * 100f);
            r.knob.style.left = Length.Percent(v * 100f);
            r.valueLbl.text = r.value();
        }

        public void AddBindHeader()
        {
            var head = El("bindrow bindrow--head", Scroll);
            Lbl("Action", "bindrow__name", head);
            Lbl("Keyboard & Mouse", "bindrow__cell bindrow__cell--head", head);
            Lbl("Gamepad", "bindrow__cell bindrow__cell--head", head);
        }

        public void AddBind(ControlBindings.Entry e)
        {
            var row = El("bindrow", Scroll);
            var r = NewRow(RowKind.Bind, row, null);
            int idx = rows.Count - 1;
            r.entry = e;
            r.nameLbl = Lbl(e.label, "bindrow__name", row);
            for (int c = 0; c < 2; c++)
            {
                int col = c;
                bool fixedCell = (c == 0 ? e.kbm : e.pad) == null;
                var cell = Lbl("", fixedCell ? "bindrow__cell bindrow__cell--fixed" : "bindrow__cell", row);
                cell.pickingMode = PickingMode.Position;
                cell.RegisterCallback<PointerEnterEvent>(_ => Hover(idx, col));
                cell.RegisterCallback<ClickEvent>(ev =>
                {
                    ev.StopPropagation();
                    if (ControlBindings.Rebinding) return;
                    rowIndex = idx;
                    bindColumn = col;
                    StartBind(r, col);
                });
                r.cells[c] = cell;
            }
            RefreshBindRow(r);
        }

        static void RefreshBindRow(Row r)
        {
            var e = r.entry;
            r.cells[0].text = e.kbm != null ? ControlBindings.Glyph(e.kbm) : e.kbmFixed?.Invoke() ?? "—";
            r.cells[1].text = e.pad != null ? ControlBindings.Glyph(e.pad) : e.padFixed?.Invoke() ?? "—";
            r.cells[0].RemoveFromClassList("bindrow__cell--listening");
            r.cells[1].RemoveFromClassList("bindrow__cell--listening");
        }

        void StartBind(Row r, int col)
        {
            var slot = col == 0 ? r.entry.kbm : r.entry.pad;
            if (slot == null)
            {
                AudioManager.Play2D(Sfx.Knock, 0.5f);
                Refresh();
                return;
            }
            if (!ControlBindings.StartRebind(slot, ok =>
                {
                    listening = null;
                    guardUntil = Time.unscaledTime + 0.25f;
                    if (ok) NotifyBindingsChanged();
                    else RefreshBindRow(r);
                    AudioManager.Play2D(ok ? Sfx.UISelect : Sfx.UIBack, 0.6f);
                    Refresh();
                }))
            {
                AudioManager.Play2D(Sfx.Knock, 0.5f);
                return;
            }
            listening = r;
            r.cells[col].text = col == 0 ? "Press a key…" : "Press a button…";
            r.cells[col].AddToClassList("bindrow__cell--listening");
            AudioManager.Play2D(Sfx.UISelect, 0.6f);
            Refresh();
        }

        /// <summary>Bindings changed (remap or reset): every bind row and any listener follow.</summary>
        public void NotifyBindingsChanged()
        {
            foreach (var r in rows) if (r.kind == RowKind.Bind) RefreshBindRow(r);
            BindingsChanged?.Invoke();
        }

        public void AddButton(string name, Action press, string help = null)
        {
            var row = El("setting setting--button", Scroll);
            var r = NewRow(RowKind.Button, row, help);
            r.press = press;
            r.nameLbl = Lbl(name, "setting__name", row);
            row.RegisterCallback<ClickEvent>(_ => Press(r));
        }

        void Press(Row r)
        {
            AudioManager.Play2D(Sfx.UISelect, 0.7f);
            r.press();
        }

        // ================================================================== state
        /// <summary>Re-reads every value, slider and binding (after a reset).</summary>
        public void RefreshValues()
        {
            foreach (var r in rows)
            {
                if (r.kind == RowKind.Value) r.valueLbl.text = r.value();
                else if (r.kind == RowKind.Slider) RefreshSlider(r);
                else if (r.kind == RowKind.Bind) RefreshBindRow(r);
            }
        }

        public void Refresh()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                bool on = i == rowIndex;
                r.el.EnableInClassList(r.kind == RowKind.Bind ? "bindrow--active" : "setting--active", on);
                if (r.kind == RowKind.Bind)
                    for (int c = 0; c < 2; c++) r.cells[c].EnableInClassList("bindrow__cell--active", on && c == bindColumn);
            }
            Changed?.Invoke();
        }

        /// <summary>Footer text for the highlighted row (without tabs / close).</summary>
        public string FooterAction(bool pad)
        {
            if (listening != null)
                return bindColumn == 0 ? "Press a key or mouse button      Esc  Cancel" : "Press a button on the controller      Start  Cancel";
            if (rows.Count == 0) return "";
            var r = rows[Mathf.Clamp(rowIndex, 0, rows.Count - 1)];
            return r.kind switch
            {
                RowKind.Bind => pad ? "D-Pad ←→  Column      A  Remap" : "← →  Column      Enter  Remap",
                RowKind.Button => pad ? "A  Confirm" : "Enter  Confirm",
                _ => pad ? "D-Pad  Adjust" : "← →  Adjust",
            };
        }

        // ================================================================== input
        /// <summary>
        /// True while a rebind is listening or just finished: the caller must not read menu input this frame
        /// (the key that finished a rebind must not also drive the menu). Handles the rebind cancel keys.
        /// </summary>
        public bool Busy(PlayerInputReader input)
        {
            if (ControlBindings.Rebinding)
            {
                // either device's cancel key backs out, whichever column is listening
                if ((Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                    (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame))
                    ControlBindings.CancelRebind();
                input?.ConsumeNavigate();
                return true;
            }
            if (Time.unscaledTime < guardUntil) { input?.ConsumeNavigate(); return true; }
            return false;
        }

        /// <summary>Drives the list with a navigation step and the submit button.</summary>
        public void Navigate(Vector2Int nav, bool submit)
        {
            if (rows.Count == 0) return;
            if (nav.y != 0)
            {
                rowIndex = (rowIndex - nav.y + rows.Count) % rows.Count;
                Scroll.ScrollTo(rows[rowIndex].el);
                AudioManager.Play2D(Sfx.UIMove, 0.5f);
                Refresh();
            }
            var r = rows[rowIndex];
            if (nav.x != 0)
            {
                switch (r.kind)
                {
                    case RowKind.Value: Step(r, nav.x); break;
                    case RowKind.Slider: r.set(r.get() + nav.x * 0.05f); RefreshSlider(r); break;
                    case RowKind.Bind:
                        int c = Mathf.Clamp(bindColumn + nav.x, 0, 1);
                        if (c != bindColumn) { bindColumn = c; AudioManager.Play2D(Sfx.UIMove, 0.5f); Refresh(); }
                        break;
                }
            }
            if (submit)
            {
                switch (r.kind)
                {
                    case RowKind.Value: Step(r, 1); break;
                    case RowKind.Bind: StartBind(r, bindColumn); break;
                    case RowKind.Button: Press(r); break;
                }
            }
        }
    }
}
