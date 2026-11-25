using UnityEngine;

namespace ElderTree.Equipment
{
    /// <summary>
    /// The fairy's crescent chakram: raise it (LT / RMB), throw it (RT / LMB). It spins out along the aim like a
    /// boomerang, curving a little, turns at the end of its reach (or off a wall) and comes back to the hand. It cuts
    /// everything it passes, once on the way out and once on the way back. One chakram: it has to be caught before the
    /// next throw. Costs mana per throw.
    /// </summary>
    [CreateAssetMenu(menuName = "Elder Tree/Equipment/Chakram", fileName = "Chakram_")]
    public class ChakramData : ScriptableObject
    {
        public string displayName = "Moon Chakram";
        public Texture2D icon;

        [Header("Cost / handling")]
        public float manaPerShot = 10f;
        public float raiseTime = 0.3f;
        public float aimMoveSpeed = 2.4f;
        [Tooltip("Seconds after the catch before the next throw can start.")]
        public float catchRecovery = 0.15f;

        [Header("Flight")]
        [Tooltip("How far it flies out before it turns back (metres). The crosshair point is used when it is closer.")]
        public float range = 17f;
        public float outSpeed = 24f;
        [Tooltip("Top speed on the way back (it accelerates into it).")]
        public float returnSpeed = 26f;
        [Tooltip("Sideways bow of the outward path at its middle (metres; + = to the thrower's left).")]
        public float curve = 1.6f;
        [Tooltip("Spin about its own axis, degrees per second.")]
        public float spin = 1500f;
        [Tooltip("Hit radius around the blade (metres).")]
        public float radius = 0.42f;
        [Tooltip("It is caught when it comes this close to the hand.")]
        public float catchRadius = 0.7f;
        [Tooltip("Safety net: it snaps back to the hand after this long.")]
        public float maxFlightTime = 3.5f;
        [Range(0f, 1.2f)] public float leadFactor = 0.6f;
        public float openSkyDistance = 30f;

        [Header("Damage (per pass: out and back)")]
        public float damage = 46f;
        public float poiseDamage = 14f;
        [Tooltip("It is a blade: cuts vines and other cuttables.")]
        public int cutPower = 1;
        public float cutWidth = 1.2f;

        [Header("Feel")]
        public Color colour = new Color(1f, 0.55f, 0.72f, 1f);
        public float shake = 0.12f;
    }
}
