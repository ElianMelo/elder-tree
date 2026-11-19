using UnityEngine;

namespace ElderTree.Equipment
{
    /// <summary>Shoulder cannon: slower than the bow, but each shell hits harder, flies further and
    /// explodes. Costs mana per shot.</summary>
    [CreateAssetMenu(menuName = "Elder Tree/Equipment/Cannon", fileName = "Cannon_")]
    public class CannonData : ScriptableObject
    {
        public string displayName = "Crocodile Cannon";
        public Texture2D icon;

        [Header("Cost / rate")]
        public float manaPerShot = 22f;
        [Tooltip("Seconds between shots (the bow can loose about every 0.6 s).")]
        public float fireInterval = 1.5f;
        public float raiseTime = 0.35f;
        public float aimMoveSpeed = 1.7f;

        [Header("Shell")]
        public float shellSpeed = 62f;
        public float shellGravity = 4f;
        [Tooltip("Max flight distance before the shell self-detonates (the bow tops out around 60 m).")]
        public float range = 140f;
        public float directDamage = 95f;
        public float directPoise = 30f;
        public float explosionRadius = 3.2f;
        public float explosionDamage = 60f;
        public float explosionPoise = 26f;
        public float explosionFxScale = 1f;
        public int breakPower = 1;

        [Header("Feel")]
        [Tooltip("Shove back along the aim when firing (m/s).")]
        public float recoil = 3.5f;
        public float shake = 0.35f;
        public float fovKick = 3.5f;
    }
}
