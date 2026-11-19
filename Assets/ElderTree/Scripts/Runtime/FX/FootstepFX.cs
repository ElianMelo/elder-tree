using ElderTree.Audio;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree.FX
{
    /// <summary>
    /// Plants footsteps when a foot bone dips to the ground: dust puff + step sound.
    /// The planted height is learned per rig (ankle bones sit at different heights on the Elder Tree and the mask
    /// forms), so a step fires when the foot comes within <see cref="plantMargin"/> of its lowest recent height and
    /// re-arms once it has lifted <see cref="liftMargin"/> above it.
    /// </summary>
    public class FootstepFX : MonoBehaviour
    {
        public Animator animator;
        public PlayerMotor motor;
        public PlayerController controller;
        [Tooltip("A step lands when the foot is within this height of its planted height (m).")]
        public float plantMargin = 0.03f;
        [Tooltip("The foot must lift this far above its planted height before the next step can land (m).")]
        public float liftMargin = 0.07f;
        [Tooltip("How fast the learned planted height drifts back up (m/s), so a new rig or slope re-learns it.")]
        public float plantRelax = 0.05f;
        [Range(0, 1)] public float volume = 1f;

        Transform lf, rf;
        bool lDown = true, rDown = true;
        float lPlant, rPlant;

        void Start() => SetAnimator(animator);

        /// <summary>Follow another body (mask transformations swap the model).</summary>
        public void SetAnimator(Animator a)
        {
            animator = a;
            lf = rf = null;
            if (a == null || !a.isActiveAndEnabled || !a.isHuman) return;
            lf = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            rf = a.GetBoneTransform(HumanBodyBones.RightFoot);
            lPlant = lf != null ? Height(lf) : 0f;
            rPlant = rf != null ? Height(rf) : 0f;
            lDown = rDown = true;
        }

        float Height(Transform foot) => foot.position.y - transform.position.y;

        void LateUpdate()
        {
            // follow whichever body the controller is driving: a mask swap replaces the animator, and a rig that was
            // hidden when its bones were looked up has none
            var a = controller != null && controller.animator != null ? controller.animator : animator;
            if (a != animator || lf == null || rf == null || !lf.gameObject.activeInHierarchy) SetAnimator(a);
            if (lf == null || rf == null || !motor.Grounded) return;
            float speed = motor.PlanarVelocity.magnitude;
            bool moving = speed > 0.6f && controller.State == PlayerState.Locomotion;
            Check(lf, ref lDown, ref lPlant, moving, speed);
            Check(rf, ref rDown, ref rPlant, moving, speed);
        }

        void Check(Transform foot, ref bool down, ref float plant, bool moving, float speed)
        {
            float h = Height(foot);
            plant = Mathf.Min(plant + plantRelax * Time.deltaTime, h);
            if (!down && h < plant + plantMargin)
            {
                down = true;
                if (!moving) return;
                float k = Mathf.Clamp01(speed / 6.5f);
                AudioManager.Play(Sfx.Footstep, foot.position, volume * (0.4f + 0.6f * k), 0.9f + 0.2f * k);
                if (k > 0.45f) FXManager.Spawn(FXType.DustSmall, foot.position - Vector3.up * 0.05f, Quaternion.identity, 0.4f + 0.5f * k);
            }
            else if (down && h > plant + liftMargin) down = false;
        }
    }
}
