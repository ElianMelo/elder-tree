using UnityEngine;

namespace ElderTree.Equipment
{
    /// <summary>Melee weapon tuning layered on top of a form's <see cref="Combat.Moveset"/> (timings live
    /// there; this is what the weapon itself adds: damage scale, an explosion on contact, obstacle breaking).</summary>
    [CreateAssetMenu(menuName = "Elder Tree/Equipment/Melee Weapon", fileName = "Melee_")]
    public class MeleeWeaponData : ScriptableObject
    {
        public string displayName = "Elder Club";
        public Texture2D icon;

        [Header("Damage")]
        public float damageMultiplier = 1f;
        public float poiseMultiplier = 1f;
        public float hitStopMultiplier = 1f;
        public float shakeMultiplier = 1f;

        [Header("Speed")]
        [Tooltip("Scales every swing's playback (wind-up, active, recovery) for this weapon: 0.7 = 30% slower. " +
                 "Hit / combo / cancel windows are normalized, so they stretch with it.")]
        [Range(0.3f, 2f)] public float attackSpeedMultiplier = 1f;

        [Header("Explosion on hit")]
        [Tooltip("Every swing that connects (enemy, obstacle or the ground under a slam) detonates once.")]
        public bool explodeOnHit;
        public float explosionRadius = 2.2f;
        public float explosionDamage = 40f;
        public float explosionPoise = 18f;
        [Tooltip("Charged / heavy swings scale the blast by this much.")]
        public float heavyExplosionScale = 1.35f;
        [Tooltip("The swing also detonates when the weapon head strikes the ground (overheads, slams).")]
        public bool explodeOnGround = true;
        public float explosionFxScale = 0.75f;

        [Header("Slashing (blades)")]
        [Tooltip("A cutting edge: draws a crescent slash with every swing and cuts through vines and other cuttables.")]
        public bool slashing;
        [Tooltip("Cuttables need at least their requiredCut (0 = blunt, cuts nothing).")]
        public int cutPower;
        [Tooltip("Width of the cut across a vine curtain from the blade's contact point (metres).")]
        public float cutWidth = 2.2f;
        public Color slashColor = new Color(0.78f, 0.93f, 1f, 0.9f);
        public Color slashEdgeColor = new Color(0.25f, 0.62f, 0.78f, 0.6f);
        [Tooltip("Crescent width (from the blade's inner third to past the tip).")]
        public float slashArcWidth = 1f;

        [Header("Spirit gauge (long sword)")]
        [Tooltip("Hits fill the gauge; heavy (spirit) attacks spend it for extra damage, and a spirited roundslash raises the gauge level.")]
        public bool spiritGauge;
        public float spiritPerHit = 14f;
        public float spiritPerHeavyHit = 6f;
        [Tooltip("Gauge each spirit (heavy) attack spends. Without enough, it still swings, just without the bonus.")]
        public float spiritAttackCost = 22f;
        public float spiritDamageBonus = 1.25f;
        [Tooltip("Damage multiplier per gauge level (white, yellow, red).")]
        public float[] spiritLevelDamage = { 1.05f, 1.1f, 1.2f };
        [Tooltip("Seconds a gauge level lasts before dropping one.")]
        public float spiritLevelDuration = 45f;
        [Tooltip("Gauge drains this much per second after a few seconds without hitting.")]
        public float spiritDrainPerSec = 3f;
        public Color[] spiritLevelColors = { new Color(0.92f, 0.95f, 1f, 0.95f), new Color(1f, 0.82f, 0.32f, 0.95f), new Color(1f, 0.28f, 0.2f, 0.95f) };

        [Header("Obstacles")]
        [Tooltip("Breakable rocks and cracked walls need at least their requiredPower to break.")]
        public int breakPower;
    }
}
