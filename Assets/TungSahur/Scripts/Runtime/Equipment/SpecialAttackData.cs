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
    }
}
