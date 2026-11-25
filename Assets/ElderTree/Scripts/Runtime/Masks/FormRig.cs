using ElderTree.Combat;
using ElderTree.FX;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree.Masks
{
    /// <summary>
    /// One body model under the player's Visual (Elder Tree, Alligator...). Lists the pieces the
    /// PlayerController drives so a mask can swap the whole body in one call.
    /// </summary>
    public class FormRig : MonoBehaviour
    {
        public string rigId = "Elder";
        public Animator animator;
        [Header("Melee weapon")]
        public WeaponHitbox weapon;
        public Transform weaponTip;
        public Renderer weaponRenderer;
        public WeaponTrail trail;
        [Tooltip("Blades: the crescent slash left by each swing (null = blunt weapon).")]
        public SlashArc slash;
        [Tooltip("Two-handed weapons: keeps the off hand on the shaft (null = one-handed).")]
        public TwoHandGrip twoHandGrip;
        [Header("Off hand (dual wield)")]
        [Tooltip("The left-hand weapon of a dual-wield form (null = one weapon).")]
        public WeaponHitbox offWeapon;
        public WeaponTrail offTrail;
        public SlashArc offSlash;
        [Header("Ranged")]
        public BowRig bowRig;
        [Tooltip("Shouldered ranged weapon: the cannon, or the crossbow (same rig: stowed on the back, raised to the shoulder).")]
        public CannonRig cannonRig;
        [Tooltip("Fairy: the chakram model, its grip socket in the right hand and where it rides on the back.")]
        public Transform chakram;
        public Transform chakramHandSocket;
        public Transform chakramStowSocket;
        [Tooltip("Where the right-hand weapon goes while the chakram is in that hand, and where it lives in the hand.")]
        public Transform mainWeaponStowSocket;
        public Transform mainWeaponHandSocket;
        [Tooltip("Fairy: the wings that flutter.")]
        public FairyWings wings;
        [Header("Magic (caster forms)")]
        [Tooltip("Where spells leave the staff (the glowing seed in the crook).")]
        public Transform spellFocus;
        [Tooltip("Motes and leaves spiralling into the focus while a spell gathers.")]
        public ParticleSystem spellGather;
        public Light focusLight;
        [Tooltip("The swaying leaf cover (stirred by casting and hits).")]
        public LeafSway leafSway;
        [Header("Face (mask cinematic)")]
        [Tooltip("Where a mask sits on this body's face (child of the head bone).")]
        public Transform faceSocket;

        public Transform Head => animator ? animator.GetBoneTransform(HumanBodyBones.Head) : transform;

        void Reset() => animator = GetComponent<Animator>();
    }
}
