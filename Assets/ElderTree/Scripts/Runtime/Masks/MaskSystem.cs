using System.Collections.Generic;
using ElderTree.Audio;
using ElderTree.Equipment;
using ElderTree.InputHandling;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree.Masks
{
    /// <summary>
    /// Majora's Mask style transformations. Four mask slots on 1-4 / D-pad (up, right, down, left).
    /// Pressing a slot puts that mask on (quick transformation); pressing the slot of the mask you
    /// are wearing takes it off and returns to the Elder Tree. Owned masks are saved in PlayerPrefs.
    /// The first time a mask is obtained, <see cref="MaskCinematic"/> plays the transformation.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class MaskSystem : MonoBehaviour
    {
        public PlayerController player;
        [Tooltip("Slot 0 = 1 / D-pad up, 1 = 2 / right, 2 = 3 / down, 3 = 4 / left.")]
        public MaskData[] slots = new MaskData[4];
        public MaskCinematic cinematic;

        public const string OwnedKey = "ElderTree.Masks.Owned";
        public static MaskSystem Instance { get; private set; }
        public MaskData Worn { get; private set; }
        public event System.Action Changed;
        /// <summary>A slot was pressed that holds no owned mask (HUD shakes the slot).</summary>
        public event System.Action<int> SlotDenied;

        readonly HashSet<string> owned = new();
        PlayerInputReader input;

        void Awake()
        {
            Instance = this;
            if (!player) player = GetComponent<PlayerController>();
            input = GetComponent<PlayerInputReader>();
            Load();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Load()
        {
            owned.Clear();
            foreach (var id in PlayerPrefs.GetString(OwnedKey, "").Split(','))
                if (!string.IsNullOrWhiteSpace(id)) owned.Add(id.Trim());
        }

        void Save()
        {
            PlayerPrefs.SetString(OwnedKey, string.Join(",", owned));
            PlayerPrefs.Save();
        }

        public bool Owns(MaskData m) => m != null && owned.Contains(m.id);
        public int SlotOf(MaskData m) => System.Array.IndexOf(slots, m);

        /// <summary>Give a mask. The first time, the transformation cinematic plays and ends wearing it.</summary>
        public void Acquire(MaskData m)
        {
            if (m == null) return;
            if (SlotOf(m) < 0)
            {
                // the slots are fixed data; a mask that isn't slotted goes to the first free one
                int free = System.Array.IndexOf(slots, null);
                if (free >= 0) slots[free] = m;
            }
            bool first = !owned.Contains(m.id);
            owned.Add(m.id);
            Save();
            Changed?.Invoke();
            if (first && cinematic != null) cinematic.Play(m, () => SetWorn(m, true));
            else if (Worn != m) TryWear(m);
        }

        /// <summary>Debug: forget every mask (the next pickup plays the cinematic again) and return to base form.</summary>
        public void ForgetAll()
        {
            owned.Clear();
            Save();
            if (Worn != null) SetWorn(null, true);
            Changed?.Invoke();
        }

        void Update()
        {
            if (input == null || player == null) return;
            int slot = input.ConsumeMaskSlot();
            if (slot < 0 || !input.GameplayEnabled) return;
            if (cinematic != null && cinematic.Playing) return;
            var m = slot < slots.Length ? slots[slot] : null;
            if (m == null || !Owns(m))
            {
                AudioManager.Play2D(Sfx.UIBack, 0.6f, 0.9f);
                SlotDenied?.Invoke(slot);
                return;
            }
            if (Worn == m) TryWear(null);
            else TryWear(m);
        }

        /// <summary>Quick transformation into a mask's form (null = back to the Elder Tree).</summary>
        public bool TryWear(MaskData m)
        {
            var form = m != null ? m.form : player.baseForm;
            float time = m != null ? m.quickTransformTime : (Worn != null ? Worn.quickTransformTime : 0.6f);
            if (!player.BeginQuickTransform(form, time)) { AudioManager.Play2D(Sfx.UIBack, 0.4f, 1.1f); return false; }
            Worn = m;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Wear immediately with no animation (cinematic end, respawn).</summary>
        public void SetWorn(MaskData m, bool apply)
        {
            Worn = m;
            if (apply) player.ApplyForm(m != null ? m.form : player.baseForm);
            Changed?.Invoke();
        }
    }
}
