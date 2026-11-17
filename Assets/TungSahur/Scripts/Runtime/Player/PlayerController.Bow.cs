using TungSahur.Audio;
using TungSahur.CameraSystem;
using TungSahur.Combat;
using TungSahur.InputHandling;
using UnityEngine;

namespace TungSahur.Player
{
    public enum BowPhase { None, Raise, Draw, Release, Renock }

    /// <summary>
    /// Bow: hold LT / RMB to raise and draw, RT / LMB (or RB) to loose. Infinite arrows.
    /// The body turns side-on (stance) and strafes at walking pace; the arrow is solved
    /// ballistically onto the point under the crosshair so what you aim at is what you hit, while
    /// a partial draw still flies slower and arcs visibly. Firing early (before the minimum draw)
    /// is buffered and looses as soon as the string is far enough back.
    /// </summary>
    public partial class PlayerController
    {
        [Header("Bow")]
        public BowRig bowRig;
        public ArrowProjectile arrowPrefab;
        public float aimMoveSpeed = 2.2f;
        public float aimTurnSpeed = 1200f;
        [Tooltip("Body yaw relative to the aim (side-on archer stance).")]
        public float stanceYaw = 85f;
        public float bowRaiseTime = 0.2f;
        public float drawTime = 0.62f;
        [Range(0f, 1f)] public float minDrawToFire = 0.3f;
        public float releaseTime = 0.12f;
        public float renockTime = 0.42f;
        public float arrowSpeedMin = 22f, arrowSpeedMax = 60f;
        public float arrowGravity = 9.81f;
        public float arrowDamageMin = 14f, arrowDamageMax = 40f;
        [Tooltip("Bonus multiplier for an arrow loosed at full draw.")]
        public float fullDrawBonus = 1.25f;
        public float arrowPoiseMin = 3f, arrowPoiseMax = 15f;
        public float staminaPerShot = 6f;
        public float aimRange = 160f;
        public LayerMask aimMask = (1 << 0) | (1 << 9) | (1 << 10);
        [Tooltip("Pad only: look slows down while the reticle is on a target.")]
        [Range(0.1f, 1f)] public float padAimFriction = 0.5f;

        public bool BowRaised => State == PlayerState.Aim;
        public BowPhase CurrentBowPhase => State == PlayerState.Aim ? bowPhase : BowPhase.None;
        public float BowPhaseProgress { get; private set; }
        /// <summary>Gameplay draw amount 0..1 (eased).</summary>
        public float BowDraw { get; private set; }
        public bool ArrowNocked { get; private set; }
        public bool FullDraw => BowDraw >= 0.999f;
        /// <summary>World point under the crosshair (what an arrow will hit).</summary>
        public Vector3 AimPoint { get; private set; }
        public bool AimOnTarget { get; private set; }
        public int ArrowsLoosed { get; private set; }
        public event System.Action<ArrowProjectile> ArrowFired;
        public event System.Action FullDrawReached;

        BowPhase bowPhase;
        float drawLinear;
        bool fireQueued;
        static readonly RaycastHit[] aimHits = new RaycastHit[16];

        bool TryEnterAim()
        {
            if (!input.GameplayEnabled || !input.AimHeld || arrowPrefab == null || bowRig == null) return false;
            EndSwing();
            SetState(PlayerState.Aim);
            IsSprinting = false;
            bowPhase = BowPhase.Raise;
            BowPhaseProgress = 0f;
            drawLinear = 0f;
            BowDraw = 0f;
            ArrowNocked = true;
            fireQueued = false;
            input.ClearFire();
            input.ClearAttackBuffers();
            animator.CrossFadeInFixedTime("Locomotion", 0.15f);
            animator.SetFloat(HActionSpeed, 1f);
            AudioManager.Play(Sfx.BowNock, transform.position + Vector3.up * 1.2f, 0.6f, 0.9f);
            UpdateAimPoint();
            return true;
        }

        void ExitAim()
        {
            BowDraw = 0f;
            drawLinear = 0f;
            fireQueued = false;
            if (State == PlayerState.Aim) SetState(PlayerState.Locomotion);
        }

        void UpdateAim(float dt)
        {
            UpdateAimPoint();

            if (!motor.Grounded && Time.time - lastGroundedTime > coyoteTime)
            {
                ExitAim();
                EnterAir(false);
                return;
            }
            // a roll always wins: lowering the bow is part of the dodge
            if (input.HasBufferedDodge)
            {
                ExitAim();
                if (TryDodge()) return;
            }
            if (!input.AimHeld || !input.GameplayEnabled)
            {
                ExitAim();
                return;
            }

            // stance: face the aim, turned side-on; strafe at a walk
            Vector3 aimFlat = AimPoint - transform.position;
            aimFlat.y = 0f;
            if (cam != null)
            {
                Vector3 cf = cam.transform.forward; cf.y = 0f;
                if (cf.sqrMagnitude > 1e-4f) aimFlat = cf;
            }
            if (aimFlat.sqrMagnitude > 1e-4f)
                RotateTowards(Quaternion.AngleAxis(stanceYaw, Vector3.up) * aimFlat.normalized, aimTurnSpeed, dt);
            Vector3 dir = moveInputWorld.sqrMagnitude > 1e-4f ? moveInputWorld.normalized : Vector3.zero;
            float speed = aimMoveSpeed * Mathf.Clamp01(moveInputMag);
            motor.SetDesiredPlanarVelocity(dir * speed, speed > 0.01f ? groundSharpness : stopSharpness, dt);
            DriveLocomotionAnimator(dt);

            // stale melee / jump presses don't fire later
            input.ConsumeJump();
            input.ClearAttackBuffers();
            if (input.ConsumeFire()) fireQueued = true;

            BowPhaseProgress += dt / Mathf.Max(0.01f, PhaseDuration(bowPhase));
            switch (bowPhase)
            {
                case BowPhase.Raise:
                    if (BowPhaseProgress >= 1f) EnterBowPhase(BowPhase.Draw);
                    break;
                case BowPhase.Draw:
                {
                    bool wasFull = FullDraw;
                    drawLinear = Mathf.MoveTowards(drawLinear, 1f, dt / Mathf.Max(0.05f, drawTime));
                    // heavy start, quick middle, settling into the anchor
                    BowDraw = Ease.InOutCubic(drawLinear);
                    if (!wasFull && FullDraw)
                    {
                        AudioManager.Play2D(Sfx.FullDraw, 0.55f);
                        input.Rumble(0.04f, 0.16f, 0.06f);
                        FullDrawReached?.Invoke();
                    }
                    if (fireQueued && BowDraw >= minDrawToFire) Loose();
                    break;
                }
                case BowPhase.Release:
                    if (BowPhaseProgress >= 1f) EnterBowPhase(BowPhase.Renock);
                    break;
                case BowPhase.Renock:
                    if (!ArrowNocked && BowPhaseProgress >= 0.45f) ArrowNocked = true;  // arrow in hand
                    if (BowPhaseProgress >= 1f)
                    {
                        AudioManager.Play(Sfx.BowNock, bowRig.NockWorld, 0.7f);
                        EnterBowPhase(BowPhase.Draw);
                    }
                    break;
            }

            // pad aim friction over targets (mouse aim is never slowed)
            var cd = CameraDirector.Instance;
            if (cd != null) cd.AimFriction = AimOnTarget && input.Device == InputDeviceKind.Gamepad ? padAimFriction : 1f;
        }

        float PhaseDuration(BowPhase p) => p switch
        {
            BowPhase.Raise => bowRaiseTime,
            BowPhase.Release => releaseTime,
            BowPhase.Renock => renockTime,
            _ => 1f
        };

        void EnterBowPhase(BowPhase p)
        {
            bowPhase = p;
            BowPhaseProgress = 0f;
            if (p == BowPhase.Draw)
            {
                drawLinear = 0f;
                BowDraw = 0f;
                ArrowNocked = true;
                AudioManager.Play(Sfx.BowDraw, transform.position + Vector3.up * 1.2f, 0.75f);
            }
        }

        void UpdateAimPoint()
        {
            if (cam == null) { AimPoint = transform.position + transform.forward * 20f; AimOnTarget = false; return; }
            Vector3 origin = cam.transform.position;
            Vector3 fwd = cam.transform.forward;
            // start the ray level with the character so nothing between camera and hero is hit
            float skip = Mathf.Max(0f, Vector3.Dot(transform.position + Vector3.up * 1.2f - origin, fwd));
            origin += fwd * skip;
            int n = Physics.RaycastNonAlloc(origin, fwd, aimHits, aimRange, aimMask, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            Collider bestCol = null;
            Vector3 point = origin + fwd * aimRange;
            for (int i = 0; i < n; i++)
            {
                var h = aimHits[i];
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.collider.isTrigger && h.collider.GetComponent<Hurtbox>() == null) continue;
                if (h.distance < best) { best = h.distance; point = h.point; bestCol = h.collider; }
            }
            AimPoint = point;
            AimOnTarget = bestCol != null && (bestCol.GetComponent<Hurtbox>() != null || bestCol.GetComponentInParent<IDamageable>() != null);
        }

        void Loose()
        {
            fireQueued = false;
            if (!stats.TrySpendStamina(staminaPerShot)) return;
            float power = Mathf.Clamp01(Mathf.InverseLerp(minDrawToFire, 1f, BowDraw));
            bool full = FullDraw;
            Vector3 start = bowRig.NockWorld;
            float speed = Mathf.Lerp(arrowSpeedMin, arrowSpeedMax, power * power * 0.4f + power * 0.6f);
            Vector3 vel = SolveLaunch(start + (bowRig.ArrowRestWorld - start).normalized * arrowPrefab.length, AimPoint, speed, arrowGravity);

            var arrow = Instantiate(arrowPrefab);
            float dmg = Mathf.Lerp(arrowDamageMin, arrowDamageMax, power) * (full ? fullDrawBonus : 1f);
            arrow.Launch(start, vel, arrowGravity, dmg, Mathf.Lerp(arrowPoiseMin, arrowPoiseMax, power), power, full, transform);
            ArrowsLoosed++;
            ArrowFired?.Invoke(arrow);

            bowRig.OnRelease(power);
            AudioManager.Play(Sfx.BowRelease, start, Mathf.Lerp(0.6f, 1f, power), Mathf.Lerp(1.08f, 0.94f, power));
            CameraDirector.Instance?.FovKick(Mathf.Lerp(0.6f, 2.2f, power));
            CameraDirector.Instance?.Shake(Mathf.Lerp(0.04f, 0.14f, power), -transform.forward);
            input.Rumble(0.08f + 0.2f * power, 0.25f + 0.35f * power, 0.07f);

            ArrowNocked = false;
            BowDraw = 0f;
            drawLinear = 0f;
            EnterBowPhase(BowPhase.Release);
        }

        /// <summary>Low-arc launch velocity that lands on <paramref name="target"/> (falls back to 45°).</summary>
        static Vector3 SolveLaunch(Vector3 from, Vector3 target, float speed, float g)
        {
            Vector3 d = target - from;
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            float x = flat.magnitude;
            if (x < 0.05f || g <= 0f) return d.normalized * speed;
            float y = d.y;
            float v2 = speed * speed;
            float disc = v2 * v2 - g * (g * x * x + 2f * y * v2);
            float angle = disc >= 0f ? Mathf.Atan((v2 - Mathf.Sqrt(disc)) / (g * x)) : Mathf.PI * 0.25f;
            Vector3 h = flat / x;
            return (h * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)) * speed;
        }
    }
}
