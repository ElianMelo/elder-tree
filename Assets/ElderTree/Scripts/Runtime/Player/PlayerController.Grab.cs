using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.Combat;
using ElderTree.FX;
using UnityEngine;

namespace ElderTree.Player
{
    /// <summary>
    /// Being grabbed by a giant (the Druid): the body belongs to the grabber's hand. No input, no motor
    /// (the CharacterController is off and the boss places us every frame), squeezes cost HP, and the hold
    /// ends with a throw (a Slam hit with a launch) or a drop (the grabber was knocked over). A roll's
    /// i-frames make <see cref="TryGrab"/> fail, so the grab is dodged like any other attack.
    /// </summary>
    public partial class PlayerController
    {
        public bool IsHeld => State == PlayerState.Held;
        float heldAnimT;

        /// <summary>A hand closes on us. False when dodged (i-frames), dead, or busy with something that owns the body.</summary>
        public bool TryGrab()
        {
            if (IsDead || Invulnerable || !isActiveAndEnabled) return false;
            // co-op: a hand can't carry another player's tree across the wire; the grab lands as a crushing blow instead
            if (IsRemote)
            {
                TakeHit(new DamageInfo { damage = 95f, poiseDamage = 60f, point = transform.position + Vector3.up * 1.2f,
                                         direction = -transform.forward, weight = ImpactWeight.Slam });
                return false;
            }
            if (State == PlayerState.Cinematic || State == PlayerState.Transform || State == PlayerState.Held) return false;
            if (State == PlayerState.Aim) ExitAim();
            EndSwing();
            ReturnHammer(true);
            motor.GravityScale = 1f;
            IsSprinting = false;
            knockedDown = standingUp = false;
            SetState(PlayerState.Held);
            motor.SetPlanarVelocityImmediate(Vector3.zero);
            motor.SetVerticalVelocity(0f);
            motor.Controller.enabled = false;
            heldAnimT = 0f;
            animator.CrossFadeInFixedTime("HitHeavy", 0.06f);
            animator.SetFloat(HActionSpeed, 1f);
            AudioManager.Play(Sfx.PlayerHurt, transform.position + Vector3.up, 0.8f, 0.85f);
            CameraDirector.Instance?.Shake(0.35f, Vector3.up);
            input.Rumble(0.5f, 0.6f, 0.2f);
            return true;
        }

        /// <summary>The grabber places the body each frame (after it has posed its hand).</summary>
        public void SetHeldPose(Vector3 position, Quaternion rotation)
        {
            if (IsHeld) transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>The hand crushes: HP only, no flinch (we're already pinned). Can kill.</summary>
        public void HeldSqueeze(float damage, Vector3 point)
        {
            if (!IsHeld) return;
            float dmg = DevNoDamage ? 0f : damage * (CurrentForm != null ? CurrentForm.damageTakenMultiplier : 1f);
            stats.TakeDamage(dmg);
            LastHurtTime = Time.time;
            Vector3 dir = transform.forward;
            Hurt?.Invoke(new DamageInfo { damage = dmg, point = point, direction = -dir, weight = ImpactWeight.Heavy });
            FXManager.Spawn(FXType.WoodChips, point, Quaternion.LookRotation(Vector3.up), 1.4f);
            AudioManager.Play(Sfx.PlayerHurt, point, 1f, Random.Range(0.85f, 0.95f));
            CameraDirector.Instance?.Shake(0.4f, Vector3.right);
            input.Rumble(0.7f, 0.8f, 0.22f);
            TimeDirector.HitStop(0.06f);
            animator.CrossFadeInFixedTime("HitHeavy", 0.04f);
            heldAnimT = 0f;
            if (stats.Health <= 0f) { LeaveHold(); Die(-dir); }
        }

        /// <summary>Thrown: the hit lands as a Slam (knockdown) and the body flies with <paramref name="launch"/>.</summary>
        public void ReleaseHeld(in DamageInfo hit, Vector3 launch)
        {
            if (!IsHeld) return;
            LeaveHold();
            SetState(PlayerState.Locomotion);
            LastHurtTime = -99f;          // the squeezes' grace must not swallow the throw
            if (TakeHit(hit) && !IsDead)
            {
                motor.SetPlanarVelocityImmediate(new Vector3(launch.x, 0f, launch.z));
                motor.SetVerticalVelocity(launch.y);
            }
        }

        /// <summary>Let go without a throw (the grabber was toppled or sent away): we just fall.</summary>
        public void DropHeld()
        {
            if (!IsHeld) return;
            LeaveHold();
            input.ClearAll();
            SetState(PlayerState.Air);
            animator.CrossFadeInFixedTime("Fall", 0.15f);
            animator.SetFloat(HActionSpeed, 1f);
        }

        void LeaveHold()
        {
            // upright again before the controller comes back
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            motor.Controller.enabled = true;
            motor.SetPlanarVelocityImmediate(Vector3.zero);
            motor.SetVerticalVelocity(0f);
        }

        void UpdateHeld(float dt)
        {
            // struggle: replay the heavy flinch now and then so the body keeps writhing in the fist
            heldAnimT += dt;
            if (heldAnimT > 0.9f)
            {
                heldAnimT = 0f;
                animator.CrossFadeInFixedTime("HitLight", 0.12f);
                animator.SetFloat(HActionSpeed, 0.8f);
            }
        }
    }
}
