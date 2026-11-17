using TungSahur.Audio;
using TungSahur.Player;
using UnityEngine;

namespace TungSahur.FX
{
    /// <summary>Plants footsteps when a foot bone dips near the ground: dust puff + step sound.</summary>
    public class FootstepFX : MonoBehaviour
    {
        public Animator animator;
        public PlayerMotor motor;
        public PlayerController controller;
        public float downHeight = 0.105f;
        public float upHeight = 0.15f;

        Transform lf, rf;
        bool lDown = true, rDown = true;

        void Start() => SetAnimator(animator);

        /// <summary>Follow another body (mask transformations swap the model).</summary>
        public void SetAnimator(Animator a)
        {
            animator = a;
            if (a == null) return;
            lf = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            rf = a.GetBoneTransform(HumanBodyBones.RightFoot);
        }

        void LateUpdate()
        {
            if (lf == null || !motor.Grounded) return;
            float speed = motor.PlanarVelocity.magnitude;
            bool moving = speed > 0.6f && controller.State == PlayerState.Locomotion;
            Check(lf, ref lDown, moving, speed);
            Check(rf, ref rDown, moving, speed);
        }

        void Check(Transform foot, ref bool down, bool moving, float speed)
        {
            float h = foot.position.y - transform.position.y;
            if (!down && h < downHeight)
            {
                down = true;
                if (!moving) return;
                float k = Mathf.Clamp01(speed / 6.5f);
                AudioManager.Play(Sfx.Footstep, foot.position, 0.25f + 0.5f * k, 0.9f + 0.2f * k);
                if (k > 0.45f) FXManager.Spawn(FXType.DustSmall, foot.position - Vector3.up * 0.05f, Quaternion.identity, 0.4f + 0.5f * k);
            }
            else if (down && h > upHeight) down = false;
        }
    }
}
