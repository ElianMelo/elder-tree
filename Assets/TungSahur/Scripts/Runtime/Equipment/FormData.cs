using TungSahur.Combat;
using UnityEngine;

namespace TungSahur.Equipment
{
    public enum RangedKind { Bow, Cannon }

    /// <summary>
    /// A body the player can be: the Elder Tree (base) or a mask form. Holds the loadout (moveset,
    /// melee, ranged, special) and how the body handles. Everything here is safe to tweak in the
    /// inspector: the builder only creates these assets when they are missing.
    /// </summary>
    [CreateAssetMenu(menuName = "Tung Sahur/Equipment/Form", fileName = "Form_")]
    public class FormData : ScriptableObject
    {
        public string displayName = "Elder Tree";
        [Tooltip("Name of the FormRig (model) under the player's Visual that this form shows.")]
        public string rigId = "Elder";

        [Header("Loadout")]
        public Moveset moveset;
        public MeleeWeaponData melee;
        public RangedKind rangedKind = RangedKind.Bow;
        public BowData bow;
        public CannonData cannon;
        [Tooltip("Light + heavy together. Empty = the moveset's skill attack.")]
        public SpecialAttackData special;

        [Header("Body")]
        public float moveSpeedMultiplier = 1f;
        public float rollDistanceMultiplier = 1f;
        [Range(0.2f, 1.5f)] public float damageTakenMultiplier = 1f;
        [Tooltip("Light blows don't make this body flinch (it still takes the damage).")]
        public bool ignoreLightFlinch;
    }
}
