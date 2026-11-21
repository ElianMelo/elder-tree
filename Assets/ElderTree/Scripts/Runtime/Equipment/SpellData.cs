using UnityEngine;

namespace ElderTree.Equipment
{
    /// <summary>
    /// A sorcery cast from a staff, Elden Ring style: the caster slows to a walk while the spell gathers (the cast
    /// time), then releases it and recovers. Light casts at once; heavy can be held to charge a bigger version.
    /// FP (mana) is spent when the spell leaves the staff, so a cast rolled out of during the wind-up costs nothing.
    ///
    /// Vine Seeker: a bundle of writhing vines flies out of the staff and steers onto the target (lock-on target,
    /// else the nearest foe in front of the caster), and where it hits, leaves explode and vines burst out and
    /// squeeze. Replaces the melee weapon for a form that has it (light / heavy cast instead of swinging).
    /// </summary>
    [CreateAssetMenu(menuName = "Elder Tree/Equipment/Spell", fileName = "Spell_")]
    public class SpellData : ScriptableObject
    {
        public string displayName = "Vine Seeker";
        public Texture2D icon;

        [Header("Cost")]
        public float manaCost = 14f;
        [Tooltip("A fully charged heavy cast costs this much (scaled down for a partial charge).")]
        public float chargedManaCost = 26f;
        public float staminaCost = 8f;

        [Header("Casting (light)")]
        [Tooltip("Seconds the spell gathers in the staff before it can leave.")]
        public float castTime = 0.55f;
        [Tooltip("Normalized time of the release clip at which the vines leave the staff.")]
        [Range(0f, 1f)] public float releaseAt = 0.22f;
        [Tooltip("Normalized time of the release clip at which the cast is over (control returns; a buffered cast chains).")]
        [Range(0f, 1f)] public float recoverAt = 0.62f;
        [Tooltip("Walking speed while casting (m/s): the caster slows right down.")]
        public float castMoveSpeed = 1.1f;
        [Tooltip("Turn rate while gathering (deg/s): stays on the lock target / camera aim.")]
        public float castTurnSpeed = 420f;

        [Header("Charging (heavy, hold)")]
        [Tooltip("Minimum gather time for the heavy cast.")]
        public float chargeMinTime = 0.75f;
        [Tooltip("Extra seconds the heavy cast keeps charging while held.")]
        public float chargeMaxTime = 1.1f;
        public float chargeDamageMultiplier = 1.9f;
        [Tooltip("Vines in the bundle: light cast, full charge.")]
        public int strands = 3;
        public int chargedStrands = 5;

        [Header("Flight")]
        public float launchSpeed = 9f;
        public float maxSpeed = 21f;
        [Tooltip("m/s per second while it flies.")]
        public float acceleration = 26f;
        [Tooltip("Seconds of straight flight before it starts to steer.")]
        public float homingDelay = 0.12f;
        [Tooltip("Steering (deg/s) once homing; it ramps up over the flight so close misses curl back in.")]
        public float turnRate = 210f;
        public float maxTurnRate = 420f;
        [Tooltip("Without a lock-on target, it seeks the nearest foe within this cone (deg) and distance.")]
        public float seekCone = 32f;
        public float seekRange = 32f;
        public float lifetime = 3.2f;
        [Tooltip("Upward lift of the launch (deg): the vines leap out of the staff in a low arc.")]
        public float launchPitch = 6f;

        [Header("Damage")]
        public float damage = 72f;
        public float poise = 24f;
        [Tooltip("The vines that burst out where it hits squeeze once more after a moment.")]
        public float burstDamage = 22f;
        public float burstPoise = 10f;
        public float burstRadius = 1.6f;
        [Tooltip("Seconds from the hit to the squeeze.")]
        public float burstDelay = 0.38f;
        [Tooltip("Seconds the burst vines stay before they wither.")]
        public float burstLife = 2.2f;

        [Header("Feel")]
        public float shake = 0.18f;
        public float hitStop = 0.05f;
        public Color glow = new Color(0.55f, 1f, 0.35f, 1f);
    }
}
