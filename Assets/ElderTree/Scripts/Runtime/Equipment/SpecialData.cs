using UnityEngine;

namespace ElderTree.Equipment
{
    /// <summary>
    /// A form's special (light + heavy pressed together). Each kind has its own asset type
    /// (<see cref="SpecialAttackData"/> = Sky Bombardment, <see cref="SpinSlashData"/> = Shark Tornado);
    /// the shared part is its stamina cost. Every special also spends one special charge
    /// (filled by light / heavy hits, see <see cref="Player.PlayerStats"/>).
    /// </summary>
    public abstract class SpecialData : ScriptableObject
    {
        public string displayName = "Special";

        [Header("Cost")]
        public float staminaCost = 25f;
    }
}
