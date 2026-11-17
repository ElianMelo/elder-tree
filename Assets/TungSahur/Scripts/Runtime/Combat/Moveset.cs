using System;
using UnityEngine;

namespace TungSahur.Combat
{
    public enum ImpactWeight { Light, Heavy, Slam }

    /// <summary>
    /// One attack. All timings are normalized (0..1) over the animation clip so tuning survives
    /// playback-speed changes. Phase speeds give the club its weight: slow anticipation, a fast
    /// snap through the active frames, then a heavy follow-through.
    /// </summary>
    [Serializable]
    public class AttackData
    {
        public string id = "Light1";
        public string state = "Light1";
        [Tooltip("Normalized time the animation starts from (e.g. a jump attack skips the windup).")]
        public float startOffset;
        [Tooltip("Filled by the builder from the animation clip.")]
        public float clipLength = 1f;

        [Header("Damage")]
        public float damage = 30f;
        public float poiseDamage = 12f;
        public float staminaCost = 16f;
        [Tooltip("Mana spent to start the attack (specials).")]
        [UnityEngine.Serialization.FormerlySerializedAs("fpCost")] public float manaCost;
        public ImpactWeight weight = ImpactWeight.Light;

        [Header("Timing (normalized)")]
        public float hitStart = 0.3f;
        public float hitEnd = 0.45f;
        [Tooltip("From here a buffered follow-up attack is executed.")]
        public float comboOpen = 0.55f;
        [Tooltip("From here a buffered dodge/jump cancels the recovery.")]
        public float cancelOpen = 0.6f;
        [Tooltip("Return to locomotion; movement input also exits from here.")]
        public float exitTime = 0.88f;
        [Tooltip("The character can steer until this point (ER-style early tracking).")]
        public float trackUntil = 0.25f;
        public float trackTurnSpeed = 540f;

        [Header("Weight / phase speeds")]
        public float windupSpeed = 0.9f;
        public float activeSpeed = 1.3f;
        public float recoverySpeed = 1.0f;

        [Header("Charge (heavy attacks)")]
        public bool chargeable;
        [Tooltip("Normalized time where the swing holds while the button is held.")]
        public float chargeHold = 0.3f;
        public float chargeMaxTime = 0.9f;
        public float chargeDamageMultiplier = 1.6f;

        [Header("Lunge (root motion substitute)")]
        public float lungeDistance = 0.6f;
        public float lungeStart = 0.15f;
        public float lungeEnd = 0.42f;

        [Header("Feel")]
        public float hitStop = 0.07f;
        public float shake = 0.35f;
        public float rumbleLow = 0.25f, rumbleHigh = 0.45f;
        [Tooltip("Ground-slam shockwave radius (0 = none). Applied at hitStart+.")]
        public float slamRadius;
        public float slamDamageScale = 0.6f;

        [Header("Chain")]
        public string next = "";
    }

    [CreateAssetMenu(menuName = "Tung Sahur/Moveset")]
    public class Moveset : ScriptableObject
    {
        public AttackData[] lightCombo = Array.Empty<AttackData>();
        public AttackData[] heavyCombo = Array.Empty<AttackData>();
        public AttackData runningAttack;
        public AttackData jumpAttack;
        public AttackData dodgeAttack;
        public AttackData skill;

        public AttackData Find(string id)
        {
            foreach (var a in lightCombo) if (a.id == id) return a;
            foreach (var a in heavyCombo) if (a.id == id) return a;
            if (runningAttack?.id == id) return runningAttack;
            if (jumpAttack?.id == id) return jumpAttack;
            if (dodgeAttack?.id == id) return dodgeAttack;
            if (skill?.id == id) return skill;
            return null;
        }
    }
}
