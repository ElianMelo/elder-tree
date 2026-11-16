using TungSahur.Audio;
using TungSahur.FX;
using UnityEngine;

namespace TungSahur.Player
{
    public partial class PlayerController
    {
        Vector3 dodgeDir;
        bool dodgeIsRoll;
        float dodgeDuration, dodgeDistance, dodgePrev;
        float lastDodgeEndedAt = -99f;
        bool dodgeQueuedAttack, dodgeQueuedDodge;

        bool TryDodge()
        {
            if (!input.ConsumeDodge()) return false;
            return TryDodgeDirect();
        }

        /// <summary>Roll toward the stick, or backstep with no input (Elden Ring rules).</summary>
        bool TryDodgeDirect()
        {
            if (!stats.TrySpendStamina(dodgeIsRollFromInput() ? dodgeStamina : dodgeStamina * 0.7f)) return false;
            EndSwing();
            IsSprinting = false;
            dodgeIsRoll = dodgeIsRollFromInput();
            dodgeDir = dodgeIsRoll ? moveInputWorld.normalized : -transform.forward;
            dodgeDuration = dodgeIsRoll ? rollDuration : backstepDuration;
            dodgeDistance = dodgeIsRoll ? rollDistance : backstepDistance;
            dodgePrev = 0f;
            dodgeQueuedAttack = dodgeQueuedDodge = false;
            if (dodgeIsRoll) FaceInstant(dodgeDir);
            SetState(PlayerState.Dodge);
            motor.SetPlanarVelocityImmediate(Vector3.zero);

            float animSpeed = dodgeClipLength * (dodgeIsRoll ? 0.75f : 0.62f) / dodgeDuration;
            animator.CrossFadeInFixedTime(dodgeIsRoll ? "Dodge" : "Backstep", 0.05f, 0, dodgeIsRoll ? 0.08f : 0.1f);
            animator.SetFloat(HActionSpeed, animSpeed);
            AudioManager.Play(Sfx.Dodge, transform.position, dodgeIsRoll ? 0.9f : 0.7f);
            FXManager.Spawn(FXType.DustSmall, transform.position - dodgeDir * 0.2f, Quaternion.LookRotation(-dodgeDir));
            rollAxisLocal = Vector3.right;
            return true;
        }

        bool dodgeIsRollFromInput() => moveInputMag > 0.2f;

        void UpdateDodge(float dt)
        {
            float t = Mathf.Clamp01(stateTime / dodgeDuration);
            float c = dodgeIsRoll ? 1f - Mathf.Pow(1f - t, 2.2f) : 1f - Mathf.Pow(1f - t, 3f);
            motor.AddDisplacement(dodgeDir * (c - dodgePrev) * dodgeDistance);
            dodgePrev = c;
            motor.SetDesiredPlanarVelocity(Vector3.zero, 20f, dt);

            var iframes = dodgeIsRoll ? rollIFrames : backstepIFrames;
            Invulnerable = stateTime >= iframes.x && stateTime <= iframes.y;

            if (dodgeIsRoll)
            {
                // a log rolls: full forward flip around the body centre, eased in/out
                float r = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.82f, t));
                rollAngle = r * 360f;
                if (t > 0.8f && squash < 0.05f && t < 0.86f) squash = 0.12f;
            }

            if (input.GameplayEnabled)
            {
                if (input.ConsumeLight() || input.ConsumeHeavy()) dodgeQueuedAttack = true;
                if (t > 0.5f && input.HasBufferedDodge) dodgeQueuedDodge = true;
            }

            if (dodgeQueuedAttack && t >= 0.78f)
            {
                FinishDodge();
                StartAttack(moveset.dodgeAttack);
                return;
            }
            if (dodgeQueuedDodge && t >= 0.86f)
            {
                FinishDodge();
                if (TryDodge()) return;
                ReturnToLocomotion(0.15f);
                return;
            }
            if (t >= 1f)
            {
                FinishDodge();
                if (!motor.Grounded) { EnterAir(false); return; }
                if (moveInputMag > 0.2f) motor.SetPlanarVelocityImmediate(moveInputWorld.normalized * runSpeed * 0.5f);
                ReturnToLocomotion(0.18f);
            }
        }

        void FinishDodge()
        {
            Invulnerable = false;
            rollAngle = 0f;
            lastDodgeEndedAt = Time.time;
        }
    }
}
