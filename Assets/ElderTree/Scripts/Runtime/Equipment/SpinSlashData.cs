using UnityEngine;

namespace ElderTree.Equipment
{
    /// <summary>
    /// "Shark Tornado": the wearer coils with the blade low, then whirls on the spot several turns,
    /// drifting with the stick (or toward the lock target). Every turn the blade sweeps a full circle:
    /// each enemy in reach is cut once per pass, vines and other cuttables in the ring are severed, and
    /// the last turn ends in a wider, heavier finishing cut. Triggered by light + heavy, paid with mana.
    /// </summary>
    [CreateAssetMenu(menuName = "Elder Tree/Equipment/Special Attack (Spin Slash)", fileName = "Special_")]
    public class SpinSlashData : SpecialData
    {
        void Reset() { displayName = "Shark Tornado"; manaCost = 40f; staminaCost = 30f; }

        [Header("Wind-up")]
        [Tooltip("Coil with the blade drawn back low before the first turn.")]
        public float windupTime = 0.32f;

        [Header("Spin")]
        public int turns = 4;
        [Tooltip("Seconds for all the turns (they accelerate into the middle and slow on the last).")]
        public float spinTime = 1.25f;
        public float radius = 3.3f;
        public float damagePerPass = 36f;
        public float poisePerPass = 11f;
        [Tooltip("Drift speed while spinning (stick direction, or toward the lock target).")]
        public float driftSpeed = 3.4f;
        [Tooltip("Steering responsiveness of the drift.")]
        public float driftSharpness = 5f;

        [Header("Finisher (end of the last turn)")]
        public float finisherRadius = 4.1f;
        public float finisherDamage = 95f;
        public float finisherPoise = 40f;

        [Header("Cutting")]
        [Tooltip("Cuttables (vines, reeds...) whose requiredCut is at most this are severed by the spin.")]
        public int cutPower = 1;

        [Header("Recovery")]
        public float recoverTime = 0.5f;
        [Tooltip("Hits during the spin don't interrupt it (damage still lands).")]
        public bool hyperArmour = true;

        [Header("Feel")]
        public float shakePerPass = 0.22f;
        public float finisherShake = 0.8f;
        public float fovKick = 4f;
    }
}
