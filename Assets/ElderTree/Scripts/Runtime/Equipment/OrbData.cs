using UnityEngine;

namespace ElderTree.Equipment
{
    /// <summary>
    /// The druid's ranged attack: raise both hands (LT / RMB) and a big ball of green energy gathers between them;
    /// RT / LMB throws it. Slower than an arrow or a bolt, both to cycle and in flight, and it does not steer, but it
    /// is big, it lights everything it passes, and it bursts on impact (direct hit + a splash). Costs mana per ball.
    /// </summary>
    [CreateAssetMenu(menuName = "Elder Tree/Equipment/Druid Orb", fileName = "Orb_")]
    public class OrbData : ScriptableObject
    {
        public string displayName = "Verdant Orb";
        public Texture2D icon;

        [Header("Cost / rate")]
        public float manaPerShot = 18f;
        [Tooltip("Seconds for the next ball to gather after a throw (the bow cycles in ~0.6 s, the crossbow 1.45 s).")]
        public float fireInterval = 1.25f;
        public float raiseTime = 0.4f;
        public float aimMoveSpeed = 1.6f;

        [Header("Flight")]
        [Tooltip("Slow on purpose: arrows fly 34-72 m/s, bolts 88.")]
        public float speed = 17f;
        public float gravity = 0f;
        public float range = 70f;
        [Tooltip("Size of the ball (core radius, metres). Also its hit radius.")]
        public float radius = 0.32f;
        [Tooltip("Where the crosshair ray hits nothing, the ball flies at a point this far along it.")]
        public float openSkyDistance = 55f;
        [Range(0f, 1.2f)] public float leadFactor = 0.8f;

        [Header("Damage")]
        public float directDamage = 96f;
        public float directPoise = 34f;
        public float burstRadius = 2.6f;
        public float burstDamage = 44f;
        public float burstPoise = 18f;

        [Header("Feel")]
        public float recoil = 0.8f;
        public float shake = 0.2f;
        public float fovKick = 2f;
        public Color colour = new Color(0.5f, 1f, 0.32f, 1f);
    }
}
