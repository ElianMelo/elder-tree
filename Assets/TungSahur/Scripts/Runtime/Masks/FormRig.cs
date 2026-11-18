using TungSahur.Combat;
using TungSahur.FX;
using TungSahur.Player;
using UnityEngine;

namespace TungSahur.Masks
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
        [Header("Ranged")]
        public BowRig bowRig;
        [Tooltip("Shouldered ranged weapon: the cannon, or the crossbow (same rig: stowed on the back, raised to the shoulder).")]
        public CannonRig cannonRig;
        [Header("Face (mask cinematic)")]
        [Tooltip("Where a mask sits on this body's face (child of the head bone).")]
        public Transform faceSocket;

        public Transform Head => animator ? animator.GetBoneTransform(HumanBodyBones.Head) : transform;

        void Reset() => animator = GetComponent<Animator>();
    }
}
