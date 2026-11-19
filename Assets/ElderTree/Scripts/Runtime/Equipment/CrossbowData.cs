using UnityEngine;

namespace ElderTree.Equipment
{
    /// <summary>
    /// Crossbow: no draw to manage. Raise it and the loaded bolt fires at once on the trigger, then the
    /// string has to be cranked back (much slower than nocking an arrow). Bolts fly flatter and faster
    /// than arrows and hit far harder. Costs mana per bolt.
    /// </summary>
    [CreateAssetMenu(menuName = "Elder Tree/Equipment/Crossbow", fileName = "Crossbow_")]
    public class CrossbowData : ScriptableObject
    {
        public string displayName = "Shark Crossbow";
        public Texture2D icon;

        [Header("Cost / rate")]
        public float manaPerShot = 12f;
        [Tooltip("Seconds to crank the next bolt in after a shot (the bow cycles in ~0.6 s).")]
        public float reloadTime = 1.45f;
        public float raiseTime = 0.3f;
        public float aimMoveSpeed = 1.9f;

        [Header("Flight")]
        public float boltSpeed = 88f;
        public float boltGravity = 4.5f;
        [Tooltip("Where the crosshair ray hits nothing (open sky), bolts aim at this distance along it.")]
        public float openSkyDistance = 70f;
        [Range(0f, 1.2f)] public float leadFactor = 1f;

        [Header("Damage (every bolt: the bow tops out at 40 x 1.25 at full draw)")]
        public float damage = 92f;
        public float poise = 32f;

        [Header("Feel")]
        [Tooltip("Shove back along the aim when firing (m/s).")]
        public float recoil = 1.2f;
        public float shake = 0.22f;
        public float fovKick = 2.2f;
    }
}
