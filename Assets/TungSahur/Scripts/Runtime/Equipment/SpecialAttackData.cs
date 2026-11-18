using UnityEngine;

namespace TungSahur.Equipment
{
    /// <summary>
    /// "Sky Bombardment": the wearer leaps straight up, hangs at the top, hurls the weapon down at the
    /// ground in front (or at the lock target) and it detonates in a huge blast, then flies back to the hand.
    /// Triggered by light + heavy pressed together, paid with mana.
    /// </summary>
    [CreateAssetMenu(menuName = "Tung Sahur/Equipment/Special Attack (Sky Bombardment)", fileName = "Special_")]
    public class SpecialAttackData : ScriptableObject
    {
        public string displayName = "Sky Bombardment";

        [Header("Cost")]
        public float manaCost = 45f;
        public float staminaCost = 25f;

        [Header("Rise")]
        public float crouchTime = 0.22f;
        public float riseHeight = 5.5f;
        public float riseTime = 0.5f;
        public float hangTime = 0.35f;

        [Header("Throw")]
        [Tooltip("Where the weapon lands: this far ahead without a lock target (capped at maxThrowDistance with one).")]
        public float throwDistance = 6f;
        public float maxThrowDistance = 12f;
        public float throwWindup = 0.28f;
        public float weaponFlightTime = 0.32f;
        public float weaponSpin = 1400f;

        [Header("Blast")]
        public float radius = 6.5f;
        public float damage = 260f;
        public float poiseDamage = 120f;
        public int breakPower = 2;
        public float fxScale = 2.4f;
        public float shake = 1.6f;

        [Header("Recovery")]
        public float fallGravityScale = 2.2f;
        public float landRecovery = 0.55f;
        public float weaponReturnTime = 0.45f;

        [Header("Camera (takes over from the hang until after the blast)")]
        public bool cameraFocus = true;
        [Tooltip("Extra orbit distance (metres): the view pulls back to show the whole blast.")]
        public float cameraZoomOut = 2.6f;
        [Tooltip("Orbit pitch (degrees) while framing the blast: a little higher to see the ring of pops.")]
        public float cameraPitch = 24f;
        [Tooltip("0 = look at the player, 1 = look at the impact point.")]
        [Range(0f, 1f)] public float cameraAimBlend = 0.6f;
        [Tooltip("Seconds the camera stays on the blast after it goes off before control returns.")]
        public float cameraHoldAfterBlast = 0.9f;
    }
}
