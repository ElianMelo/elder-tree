using TungSahur.Audio;
using TungSahur.CameraSystem;
using TungSahur.Combat;
using TungSahur.FX;
using UnityEngine;

namespace TungSahur.Player
{
    /// <summary>
    /// Taking hits: roll i-frames dodge everything, a raised guard facing the blow absorbs most of it
    /// for stamina (guard break when stamina runs out), otherwise a flinch scaled by the impact weight
    /// (Slam = knocked off your roots). Zero HP plays the death and raises <see cref="Died"/>.
    /// </summary>
    public partial class PlayerController
    {
        [Header("Taking damage")]
        [Range(0f, 1f)] public float guardAbsorption = 0.72f;
        public float guardStaminaPerPoise = 0.9f;
        public float flinchLight = 0.42f, flinchHeavy = 0.72f, knockdownTime = 1.25f;
        [Tooltip("Knockdown01 - StandUp playback speed and how long until control returns once it starts.")]
        public float standUpSpeed = 1.3f, standUpTime = 0.78f;
        bool knockedDown, standingUp;
        [Tooltip("After a hit lands, further hits are ignored this long (no multi-hit stunlocks from one blow).")]
        public float hurtGrace = 0.4f;

        public bool IsDead => State == PlayerState.Dead;
        /// <summary>Dev / autopilot: hits still react but cost no HP.</summary>
        public static bool DevNoDamage;
        public float LastHurtTime { get; private set; } = -99f;
        public event System.Action<DamageInfo> Hurt;
        public event System.Action Died;

        float hitDuration = 0.55f;
        float hitSharpness = 10f;

        /// <summary>An enemy attack meets the player. Returns false when dodged (i-frames) or already dead.</summary>
        public bool TakeHit(in DamageInfo info)
        {
            if (IsDead || Invulnerable || !isActiveAndEnabled) return false;
            if (Time.time - LastHurtTime < hurtGrace) return false;

            Vector3 dir = info.direction; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = -transform.forward;
            dir.Normalize();
            float dmg = DevNoDamage ? 0f : info.damage;

            // guard: must face the blow
            if (IsGuarding && Vector3.Dot(transform.forward, -dir) > 0.2f)
            {
                dmg *= 1f - guardAbsorption;
                stats.TrySpendStamina(Mathf.Max(8f, info.poiseDamage * guardStaminaPerPoise));
                if (stats.Stamina > 0f)
                {
                    stats.TakeDamage(dmg);
                    LastHurtTime = Time.time;
                    animator.CrossFadeInFixedTime("GuardHit", 0.04f, 1);
                    motor.SetPlanarVelocityImmediate(dir * (info.weight == ImpactWeight.Light ? 2.5f : 5f));
                    AudioManager.Play(Sfx.Guard, transform.position + Vector3.up, 1f, 0.75f);
                    AudioManager.Play(Sfx.Knock, transform.position + Vector3.up, 0.8f, 0.7f);
                    FXManager.Spawn(FXType.HitSpark, transform.position + Vector3.up * 1.2f - dir * 0.4f, Quaternion.LookRotation(-dir), 1f);
                    CameraDirector.Instance?.Shake(0.35f, dir);
                    input.Rumble(0.4f, 0.3f, 0.15f);
                    Hurt?.Invoke(info);
                    if (stats.Health <= 0f) Die(dir);
                    return true;
                }
                // guard broken: fall through to a full heavy flinch
                AudioManager.Play(Sfx.Stagger, transform.position, 0.9f, 0.8f);
            }

            stats.TakeDamage(dmg);
            LastHurtTime = Time.time;
            Hurt?.Invoke(info);

            // feedback: bark chips fly off the treant
            Vector3 p = info.point.sqrMagnitude > 0f ? info.point : transform.position + Vector3.up * 1.1f;
            FXManager.Spawn(FXType.WoodChips, p, Quaternion.LookRotation(dir + Vector3.up * 0.6f), 1.3f);
            FXManager.Spawn(FXType.HitSpark, p, Quaternion.LookRotation(dir), 0.9f);
            AudioManager.Play(Sfx.PlayerHurt, p, 1f, Random.Range(0.92f, 1.04f));
            float k = info.weight == ImpactWeight.Light ? 0.6f : info.weight == ImpactWeight.Heavy ? 1f : 1.5f;
            CameraDirector.Instance?.Shake(0.55f * k, dir);
            CameraDirector.Instance?.FovKick(2.5f * k);
            input.Rumble(0.6f * k, 0.7f * k, 0.25f);
            TimeDirector.HitStop(0.05f * k);

            if (stats.Health <= 0f) { Die(dir); return true; }

            // interrupt whatever we were doing
            EndSwing();
            if (State == PlayerState.Aim) ExitAim();
            IsSprinting = false;
            rollAngle = 0f;
            SetState(PlayerState.Hit);
            FaceInstant(-dir);
            knockedDown = standingUp = false;
            switch (info.weight)
            {
                case ImpactWeight.Light:
                    hitDuration = flinchLight; hitSharpness = 12f;
                    animator.CrossFadeInFixedTime("HitLight", 0.05f);
                    animator.SetFloat(HActionSpeed, 1.3f);
                    motor.SetPlanarVelocityImmediate(dir * 3f);
                    break;
                case ImpactWeight.Heavy:
                    hitDuration = flinchHeavy; hitSharpness = 7f;
                    animator.CrossFadeInFixedTime("HitHeavy", 0.05f);
                    animator.SetFloat(HActionSpeed, 1.1f);
                    motor.SetPlanarVelocityImmediate(dir * 6f);
                    break;
                default: // Slam: launched off your roots (pack Knockdown01: Fall -> Ground -> StandUp)
                    hitDuration = knockdownTime + standUpTime; hitSharpness = 2.2f;
                    knockedDown = true;
                    standingUp = false;
                    animator.CrossFadeInFixedTime("KnockdownFall", 0.04f);
                    animator.SetFloat(HActionSpeed, 1f);
                    motor.SetPlanarVelocityImmediate(dir * 9f);
                    motor.Jump(0.7f);
                    break;
            }
            return true;
        }

        void UpdateHitReaction(float dt)
        {
            motor.SetDesiredPlanarVelocity(Vector3.zero, motor.Grounded ? hitSharpness : 0.6f, dt);
            if (knockedDown && !standingUp && stateTime > knockdownTime && motor.Grounded)
            {
                standingUp = true;
                animator.CrossFadeInFixedTime("KnockdownStandUp", 0.15f);
                animator.SetFloat(HActionSpeed, standUpSpeed);
                hitDuration = stateTime + standUpTime;
            }
            if (stateTime > hitDuration && motor.Grounded && (!knockedDown || standingUp))
            {
                if (!knockedDown && (motor.LandedThisFrame || stateTime < hitDuration + 0.02f)) FXManager.Spawn(FXType.LandDust, transform.position, Quaternion.identity, 0.6f);
                knockedDown = standingUp = false;
                ReturnToLocomotion(0.2f);
            }
        }

        void Die(Vector3 dir)
        {
            EndSwing();
            if (State == PlayerState.Aim) ExitAim();
            IsSprinting = false;
            Invulnerable = false;
            rollAngle = 0f;
            SetState(PlayerState.Dead);
            FaceInstant(-dir);
            animator.CrossFadeInFixedTime("Death", 0.08f);
            animator.SetFloat(HActionSpeed, 0.9f);
            motor.SetPlanarVelocityImmediate(dir * 4f);
            if (lockOn.Current != null) lockOn.Toggle();
            TimeDirector.SlowMo(0.9f, 0.35f);
            Died?.Invoke();
        }

        void UpdateDead(float dt) => motor.SetDesiredPlanarVelocity(Vector3.zero, 4f, dt);

        /// <summary>Stand back up at a grace (respawn after death or an encounter reset).</summary>
        public void Revive(Vector3 position, Quaternion rotation)
        {
            motor.Teleport(position, rotation);
            stats.RestoreAll();
            Invulnerable = false;
            rollAngle = 0f;
            knockedDown = standingUp = false;
            SetState(PlayerState.Locomotion);
            animator.Play("Locomotion", 0, 0f);
            animator.SetFloat(HActionSpeed, 1f);
            input.ClearAll();
        }
    }
}
