using UnityEngine;

namespace ElderTree.Equipment
{
    /// <summary>Bow tuning (the Elder Tree's ranged weapon). Arrows cost mana instead of ammo.</summary>
    [CreateAssetMenu(menuName = "Elder Tree/Equipment/Bow", fileName = "Bow_")]
    public class BowData : ScriptableObject
    {
        public string displayName = "Elder Bow";
        public Texture2D icon;

        [Header("Cost")]
        public float manaPerShot = 7f;

        [Header("Draw")]
        public float raiseTime = 0.2f;
        public float drawTime = 0.62f;
        [Range(0f, 1f)] public float minDrawToFire = 0.3f;
        public float releaseTime = 0.12f;
        public float renockTime = 0.42f;
        public float aimMoveSpeed = 2.2f;

        [Header("Flight")]
        public float arrowSpeedMin = 34f, arrowSpeedMax = 72f;
        public float arrowGravity = 9.81f;
        [Tooltip("Where the crosshair ray hits nothing (open sky), arrows aim at this distance along it.")]
        public float openSkyDistance = 60f;
        [Tooltip("While locked on, arrows lead a moving target by its velocity (0 = aim at where it is).")]
        [Range(0f, 1.2f)] public float leadFactor = 1f;

        [Header("Damage")]
        public float damageMin = 14f, damageMax = 40f;
        [Tooltip("Bonus multiplier for an arrow loosed at full draw.")]
        public float fullDrawBonus = 1.25f;
        public float poiseMin = 3f, poiseMax = 15f;
    }
}
