using UnityEngine;

namespace TungSahur.Equipment
{
    /// <summary>Melee weapon tuning layered on top of a form's <see cref="Combat.Moveset"/> (timings live
    /// there; this is what the weapon itself adds: damage scale, an explosion on contact, obstacle breaking).</summary>
    [CreateAssetMenu(menuName = "Tung Sahur/Equipment/Melee Weapon", fileName = "Melee_")]
    public class MeleeWeaponData : ScriptableObject
    {
        public string displayName = "Sahur Club";
        public Texture2D icon;

        [Header("Damage")]
        public float damageMultiplier = 1f;
        public float poiseMultiplier = 1f;
        public float hitStopMultiplier = 1f;
        public float shakeMultiplier = 1f;

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

        [Header("Obstacles")]
        [Tooltip("Breakable rocks and cracked walls need at least their requiredPower to break.")]
        public int breakPower;
    }
}
