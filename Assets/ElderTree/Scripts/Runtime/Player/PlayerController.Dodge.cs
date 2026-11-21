using ElderTree.Audio;
using ElderTree.FX;
using UnityEngine;

namespace ElderTree.Player
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

            if (dodgeIsRoll)
            {
                // Roll01 (in place) plays its tuck -> roll -> get-up span over the roll duration
                float span = Mathf.Max(0.05f, rollClipRange.y - rollClipRange.x);
                animator.CrossFadeInFixedTime("Dodge", 0.06f, 0, rollClipRange.x * dodgeClipLength);
                animator.SetFloat(HActionSpeed, dodgeClipLength * span / dodgeDuration);
                rollTravelEnd = RollTravel(rollClipRange.y);
                rollTravelStart = RollTravel(rollClipRange.x);
            }
            else
            {
                animator.CrossFadeInFixedTime("Backstep", 0.05f, 0, 0.1f);
                animator.SetFloat(HActionSpeed, backstepClipLength * 0.62f / dodgeDuration);
            }
            AudioManager.Play(Sfx.Dodge, transform.position, dodgeIsRoll ? 0.9f : 0.7f);
            FXManager.Spawn(FXType.DustSmall, transform.position - dodgeDir * 0.2f, Quaternion.LookRotation(-dodgeDir));
            rollDustDone = false;
            return true;
        }

        // Forward travel of HumanM@Roll01 [RM] (RootT.z, sampled every 1/26 of the clip, normalized).
        // The roll moves the body exactly as the animation does: fast push off, carry through the tumble,
        // nothing left once the feet are back under the hips, so it reads as a grounded roll, not a glide.
        static readonly float[] RollTravelTable =
        {
            0f, 0.127f, 0.243f, 0.317f, 0.395f, 0.467f, 0.558f, 0.647f, 0.696f, 0.737f, 0.779f, 0.833f, 0.904f,
            0.946f, 0.984f, 0.995f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f
        };

        float rollTravelStart, rollTravelEnd = 1f;
        bool rollDustDone;

        static float RollTravel(float n)
        {
            float x = Mathf.Clamp01(n) * (RollTravelTable.Length - 1);
            int i = Mathf.Min((int)x, RollTravelTable.Length - 2);
            return Mathf.Lerp(RollTravelTable[i], RollTravelTable[i + 1], x - i);
        }

        bool dodgeIsRollFromInput() => moveInputMag > 0.2f;

        void UpdateDodge(float dt)
        {
            float t = Mathf.Clamp01(stateTime / dodgeDuration);
            float c;
            if (dodgeIsRoll)
            {
                float n = Mathf.Lerp(rollClipRange.x, rollClipRange.y, t);
                c = Mathf.InverseLerp(rollTravelStart, rollTravelEnd, RollTravel(n));
                // shoulder hits the ground: a puff where the back rolls over
                if (!rollDustDone && n >= 0.3f)
                {
                    rollDustDone = true;
                    FXManager.Spawn(FXType.DustSmall, transform.position + dodgeDir * 0.3f, Quaternion.LookRotation(-dodgeDir), 1.2f);
                }
            }
            else c = 1f - Mathf.Pow(1f - t, 3f);
            motor.AddDisplacement(dodgeDir * (c - dodgePrev) * dodgeDistance);
            dodgePrev = c;
            motor.SetDesiredPlanarVelocity(Vector3.zero, 20f, dt);

            var iframes = dodgeIsRoll ? rollIFrames : backstepIFrames;
            Invulnerable = stateTime >= iframes.x && stateTime <= iframes.y;

            if (input.GameplayEnabled)
            {
                if (input.ConsumeLight() || input.ConsumeHeavy()) dodgeQueuedAttack = true;
                if (t > 0.5f && input.HasBufferedDodge) dodgeQueuedDodge = true;
            }

            if (dodgeQueuedAttack && t >= 0.78f)
            {
                FinishDodge();
                if (!IsCaster) StartAttack(moveset.dodgeAttack);
                else if (!TryStartCast(false)) ReturnToLocomotion(0.15f);
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
            lastDodgeEndedAt = Time.time;
        }
    }
}
