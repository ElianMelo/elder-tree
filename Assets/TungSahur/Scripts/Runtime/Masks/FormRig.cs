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
        [Header("Ranged")]
        public BowRig bowRig;
        public CannonRig cannonRig;
        [Header("Face (mask cinematic)")]
        [Tooltip("Where a mask sits on this body's face (child of the head bone).")]
        public Transform faceSocket;

        public Transform Head => animator ? animator.GetBoneTransform(HumanBodyBones.Head) : transform;

        void Reset() => animator = GetComponent<Animator>();
    }
}
