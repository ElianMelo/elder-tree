using UnityEngine;

namespace TungSahur.Equipment
{
    /// <summary>
    /// A form's special (light + heavy pressed together). Each kind has its own asset type
    /// (<see cref="SpecialAttackData"/> = Sky Bombardment, <see cref="SpinSlashData"/> = Tralala Tornado);
    /// the shared part is what it costs.
    /// </summary>
    public abstract class SpecialData : ScriptableObject
    {
        public string displayName = "Special";

        [Header("Cost")]
        public float manaCost = 45f;
        public float staminaCost = 25f;
    }
}
