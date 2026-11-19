using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.Combat;
using ElderTree.Equipment;
using ElderTree.InputHandling;
using UnityEngine;

namespace ElderTree.Player
{
    public enum BowPhase { None, Raise, Draw, Release, Renock }

    /// <summary>
    /// Bow: hold LT / RMB to raise and draw, RT / LMB (or RB) to loose. Each arrow costs mana
    /// (<see cref="BowData.manaPerShot"/>); all tuning lives in the form's <see cref="BowData"/>.
    /// Body motion is the Human Mega Animations Pack archer set: Load (scrubbed by the draw), Hold
    /// (blended Down/Level/Up by aim pitch) and Release, with the pack's walk clips on a legs-only
    /// layer for strafing.
    ///
    /// Precision: the arrow is solved onto the point under the crosshair (open sky = a point far along
    /// the view ray, never a 45° lob), and while locked on it flies at where the target will be when
    /// it arrives (velocity lead, iterated on the flight time) unless the player steers the lead offset.
    /// A shot that enters a hurtbox always lands, even if it misses the visible-mesh ellipsoids.
    /// </summary>
    public partial class PlayerController
    {
        [Header("Bow")]
        public BowRig bowRig;
        public ArrowProjectile arrowPrefab;
        [Tooltip("Fallback bow tuning when the current form has none.")]
        public BowData bowData;
        public float aimTurnSpeed = 1200f;
        [Tooltip("Root yaw relative to the aim. The pack's archer clips point the arrow ~6 deg left of the root.")]
        public float stanceYaw = 6f;
        [Tooltip("Hips yaw inside the pack's BowShot clips (measured): the legs layer steps in this frame.")]
        public float archerHipsYaw = 85f;
        [Tooltip("Normalized time of BowShot01 - Load at which the string reaches full draw.")]
        public float loadFullDrawTime = 0.5f;
        public float aimRange = 160f;
        public LayerMask aimMask = (1 << 0) | (1 << 9) | (1 << 10);
        [Tooltip("Pad only: look slows down while the reticle is on a target.")]
        [Range(0.1f, 1f)] public float padAimFriction = 0.5f;

        BowData Bow => (CurrentForm != null && CurrentForm.bow != null) ? CurrentForm.bow : bowData;

        public bool BowRaised => State == PlayerState.Aim;
        public BowPhase CurrentBowPhase => State == PlayerState.Aim && !cannonMode ? bowPhase : BowPhase.None;
        public float BowPhaseProgress { get; private set; }
        /// <summary>Gameplay draw amount 0..1 (eased). With the cannon: readiness of the next shot.</summary>
        public float BowDraw { get; private set; }
        public bool ArrowNocked { get; private set; }
        public bool FullDraw => BowDraw >= 0.999f;
        /// <summary>World point under the crosshair (what a shot will hit).</summary>
        public Vector3 AimPoint { get; private set; }
        public bool AimOnTarget { get; private set; }
        public int ArrowsLoosed { get; private set; }
        /// <summary>Mana one shot of the raised ranged weapon costs.</summary>
        public float RangedManaCost => CurrentForm != null && CurrentForm.rangedKind == RangedKind.Cannon && CurrentForm.cannon != null
            ? CurrentForm.cannon.manaPerShot
            : CurrentForm != null && CurrentForm.rangedKind == RangedKind.Crossbow && CurrentForm.crossbow != null
                ? CurrentForm.crossbow.manaPerShot : (Bow != null ? Bow.manaPerShot : 0f);
        public event System.Action<ArrowProjectile> ArrowFired;
        public event System.Action FullDrawReached;

        static readonly int HAimPitch = Animator.StringToHash("AimPitch");
        static readonly int HBowDrawTime = Animator.StringToHash("BowDrawTime");
        static readonly int HBowMoveX = Animator.StringToHash("BowMoveX");
        static readonly int HBowMoveZ = Animator.StringToHash("BowMoveZ");
        const int BowLegsLayer = 2;

        BowPhase bowPhase;
        float drawLinear;
        float bowLegsWeight;
        bool holdPlaying;
        bool fireQueued;
        static readonly RaycastHit[] aimHits = new RaycastHit[16];

        // lock-on target velocity (for leading moving / flying targets)
        Targetable lockTrack;
        Vector3 lockPrevPos, lockVel;

        void UpdateLockVelocity(float dt)
        {
            var t = lockOn != null ? lockOn.Current : null;
            if (t == null) { lockTrack = null; lockVel = Vector3.zero; return; }
            Vector3 p = t.LockPosition;
            if (t != lockTrack) { lockTrack = t; lockPrevPos = p; lockVel = Vector3.zero; return; }
            Vector3 v = (p - lockPrevPos) / Mathf.Max(dt, 1e-4f);
            lockPrevPos = p;
            if (v.sqrMagnitude > 80f * 80f) v = Vector3.zero;   // teleport / respawn
            lockVel = Vector3.Lerp(lockVel, v, Ease.Damp(10f, dt));
        }

        bool TryEnterAim()
        {
            if (CurrentForm != null && (CurrentForm.rangedKind == RangedKind.Cannon || CurrentForm.rangedKind == RangedKind.Crossbow)) return TryEnterCannonAim();
            if (!input.GameplayEnabled || !input.AimHeld || arrowPrefab == null || bowRig == null || Bow == null) return false;
            EndSwing();
            SetState(PlayerState.Aim);
            cannonMode = false;
            IsSprinting = false;
            bowPhase = BowPhase.Raise;
            BowPhaseProgress = 0f;
            drawLinear = 0f;
            BowDraw = 0f;
            ArrowNocked = true;
            fireQueued = false;
            input.ClearFire();
            input.ClearAttackBuffers();
            animator.SetFloat(HBowDrawTime, 0f);
            animator.SetFloat(HActionSpeed, 1f);
            animator.CrossFadeInFixedTime("BowDraw", 0.18f);
            holdPlaying = false;
            AudioManager.Play(Sfx.BowNock, transform.position + Vector3.up * 1.2f, 0.6f, 0.9f);
            UpdateAimPoint();
            return true;
        }

        void ExitAim()
        {
            BowDraw = 0f;
            drawLinear = 0f;
            fireQueued = false;
            holdPlaying = false;
            bool wasCannon = cannonMode;
            cannonMode = false;
            if (State == PlayerState.Aim) ReturnToLocomotion(wasCannon ? 0.3f : 0.25f);
        }

        /// <summary>Archer animation: draw scrub, pitch blend and the legs layer (runs every frame).</summary>
        void UpdateBowAnimator(float dt)
        {
            bool aiming = State == PlayerState.Aim;
            bool moving = motor.PlanarVelocity.sqrMagnitude > 0.04f;
            // the cannon stance keeps its own legs while standing; the walk layer only steps when moving
            float target = aiming ? (cannonMode ? (moving ? 1f : 0f) : 1f) : 0f;
            bowLegsWeight = Mathf.MoveTowards(bowLegsWeight, target, dt / (target > bowLegsWeight ? 0.15f : 0.2f));
            animator.SetLayerWeight(BowLegsLayer, bowLegsWeight);
            if (!aiming) return;

            Vector3 from = transform.position + Vector3.up * 1.38f;
            Vector3 d = AimPoint - from;
            float pitch = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            animator.SetFloat(HAimPitch, Mathf.Clamp(pitch, -50f, 40f), 0.06f, dt);

            if (!cannonMode)
            {
                // draw: the Load clip reaches full draw at loadFullDrawTime; scrub it with the gameplay draw
                float drawT = bowPhase == BowPhase.Draw ? BowDraw * loadFullDrawTime : 0f;
                animator.SetFloat(HBowDrawTime, drawT);
                if (bowPhase == BowPhase.Draw && FullDraw && !holdPlaying)
                {
                    animator.CrossFadeInFixedTime("BowHold", 0.12f);
                    holdPlaying = true;
                }
            }

            // legs: move direction in the stance's hips frame, walk ring 0..1
            Vector3 v = motor.PlanarVelocity;
            float speed = v.magnitude;
            Vector3 local = Quaternion.Euler(0f, cannonMode ? 0f : -archerHipsYaw, 0f) * transform.InverseTransformDirection(v);
            Vector2 dir2 = speed > 0.05f ? new Vector2(local.x, local.z).normalized * Mathf.Clamp01(SpeedToRing(speed)) : Vector2.zero;
            animator.SetFloat(HBowMoveX, dir2.x, 0.1f, dt);
            animator.SetFloat(HBowMoveZ, dir2.y, 0.1f, dt);
        }

        /// <summary>Shared by bow and cannon: leave aim on air / dodge / release, face the aim, strafe.</summary>
        bool AimCommon(float dt, float moveSpeed, float yawOffset)
        {
            UpdateAimPoint();
            if (!motor.Grounded && Time.time - lastGroundedTime > coyoteTime)
            {
                ExitAim();
                EnterAir(false);
                return false;
            }
            // a roll always wins: lowering the weapon is part of the dodge
            if (input.HasBufferedDodge)
            {
                ExitAim();
                TryDodge();
                return false;
            }
            if (!input.AimHeld || !input.GameplayEnabled)
            {
                ExitAim();
                return false;
            }
            Vector3 aimFlat = AimPoint - transform.position;
            aimFlat.y = 0f;
            if (cam != null)
            {
                Vector3 cf = cam.transform.forward; cf.y = 0f;
                if (cf.sqrMagnitude > 1e-4f) aimFlat = cf;
            }
            if (aimFlat.sqrMagnitude > 1e-4f)
                RotateTowards(Quaternion.AngleAxis(yawOffset, Vector3.up) * aimFlat.normalized, aimTurnSpeed, dt);
            Vector3 dir = moveInputWorld.sqrMagnitude > 1e-4f ? moveInputWorld.normalized : Vector3.zero;
            float speed = moveSpeed * Mathf.Clamp01(moveInputMag);
            motor.SetDesiredPlanarVelocity(dir * speed, speed > 0.01f ? groundSharpness : stopSharpness, dt);
            DriveLocomotionAnimator(dt);

            // stale melee / jump presses don't fire later
            input.ConsumeJump();
            input.ClearAttackBuffers();
            if (input.ConsumeFire()) fireQueued = true;

            // pad aim friction over targets (mouse aim is never slowed)
            var cd = CameraDirector.Instance;
            if (cd != null) cd.AimFriction = AimOnTarget && input.Device == InputDeviceKind.Gamepad ? padAimFriction : 1f;
            return true;
        }

        void UpdateAim(float dt)
        {
            if (cannonMode) { UpdateCannonAim(dt); return; }
            var b = Bow;
            if (!AimCommon(dt, b.aimMoveSpeed, stanceYaw)) return;

            BowPhaseProgress += dt / Mathf.Max(0.01f, PhaseDuration(bowPhase));
            switch (bowPhase)
            {
                case BowPhase.Raise:
                    if (BowPhaseProgress >= 1f) EnterBowPhase(BowPhase.Draw);
                    break;
                case BowPhase.Draw:
                {
                    bool wasFull = FullDraw;
                    drawLinear = Mathf.MoveTowards(drawLinear, 1f, dt / Mathf.Max(0.05f, b.drawTime));
                    // heavy start, quick middle, settling into the anchor
                    BowDraw = Ease.InOutCubic(drawLinear);
                    if (!wasFull && FullDraw)
                    {
                        AudioManager.Play2D(Sfx.FullDraw, 0.55f);
                        input.Rumble(0.04f, 0.16f, 0.06f);
                        FullDrawReached?.Invoke();
                    }
                    if (fireQueued && BowDraw >= b.minDrawToFire) Loose();
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
        }

        float PhaseDuration(BowPhase p) => p switch
        {
            BowPhase.Raise => Bow.raiseTime,
            BowPhase.Release => Bow.releaseTime,
            BowPhase.Renock => Bow.renockTime,
            _ => 1f
        };

        void EnterBowPhase(BowPhase p)
        {
            bowPhase = p;
            BowPhaseProgress = 0f;
            // after the release snap, settle back into the Load pose while the hand fetches an arrow
            if (p == BowPhase.Renock) animator.CrossFadeInFixedTime("BowDraw", Bow.renockTime * 0.7f);
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
            float openSky = UsingCrossbow ? Crossbow.openSkyDistance : Bow != null ? Bow.openSkyDistance : 60f;
            if (cam == null) { AimPoint = transform.position + transform.forward * openSky; AimOnTarget = false; return; }
            Vector3 origin = cam.transform.position;
            Vector3 fwd = cam.transform.forward;
            // start the ray level with the character so nothing between camera and hero is hit
            float skip = Mathf.Max(0f, Vector3.Dot(transform.position + Vector3.up * 1.2f - origin, fwd));
            origin += fwd * skip;
            int n = Physics.RaycastNonAlloc(origin, fwd, aimHits, aimRange, aimMask, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            Collider bestCol = null;
            // nothing under the crosshair: aim at a point a fixed way out along the view ray
            Vector3 point = origin + fwd * openSky;
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

        /// <summary>
        /// Where a projectile leaving <paramref name="from"/> at <paramref name="speed"/> should be aimed. Locked on
        /// (and not steering the lead offset by hand): the lock point advanced by the target's velocity over the
        /// flight time. Otherwise the crosshair point.
        /// </summary>
        Vector3 ProjectileTarget(Vector3 from, float speed, float leadFactor)
        {
            var locked = lockOn != null ? lockOn.Current : null;
            var cd = CameraDirector.Instance;
            bool manualLead = cd != null && cd.AimLead.magnitude > 2.5f;
            if (locked == null || manualLead) return AimPoint;
            Vector3 p = locked.LockPosition;
            for (int i = 0; i < 3; i++)
            {
                float t = Vector3.Distance(from, p) / Mathf.Max(1f, speed);
                p = locked.LockPosition + lockVel * (t * leadFactor);
            }
            return p;
        }

        void Loose()
        {
            fireQueued = false;
            var b = Bow;
            if (!stats.TrySpendMana(b.manaPerShot))
            {
                // dry: the string slips back, nothing flies
                AudioManager.Play2D(Sfx.ManaEmpty, 0.8f);
                input.Rumble(0.02f, 0.1f, 0.05f);
                return;
            }
            float power = Mathf.Clamp01(Mathf.InverseLerp(b.minDrawToFire, 1f, BowDraw));
            bool full = FullDraw;
            Vector3 start = bowRig.NockWorld;
            float speed = Mathf.Lerp(b.arrowSpeedMin, b.arrowSpeedMax, power * power * 0.4f + power * 0.6f);
            Vector3 tipStart = start + (bowRig.ArrowRestWorld - start).normalized * arrowPrefab.length;
            Vector3 target = ProjectileTarget(tipStart, speed, b.leadFactor);
            Vector3 vel = SolveLaunch(tipStart, target, speed, b.arrowGravity);
            // launch from the nock along the solved direction (the tip then sits exactly on the solved line)
            start = tipStart - vel.normalized * arrowPrefab.length;

            var arrow = Instantiate(arrowPrefab);
            float dmg = Mathf.Lerp(b.damageMin, b.damageMax, power) * (full ? b.fullDrawBonus : 1f);
            arrow.Launch(start, vel, b.arrowGravity, dmg, Mathf.Lerp(b.poiseMin, b.poiseMax, power), power, full, transform);
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
            animator.SetFloat(HActionSpeed, 1f);
            animator.CrossFadeInFixedTime("BowRelease", 0.04f);
            holdPlaying = false;
        }

        /// <summary>
        /// Low-arc launch velocity that lands on <paramref name="target"/>. Out of reach (a weak draw at a far
        /// point): fly straight at it and let gravity drop it short, which reads as "too far" instead of the old
        /// 45-degree lob that sent arrows high over the crosshair.
        /// </summary>
        static Vector3 SolveLaunch(Vector3 from, Vector3 target, float speed, float g)
        {
            Vector3 d = target - from;
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            float x = flat.magnitude;
            if (x < 0.05f || g <= 0f) return d.normalized * speed;
            float y = d.y;
            float v2 = speed * speed;
            float disc = v2 * v2 - g * (g * x * x + 2f * y * v2);
            if (disc < 0f) return d.normalized * speed;
            float angle = Mathf.Atan((v2 - Mathf.Sqrt(disc)) / (g * x));
            Vector3 h = flat / x;
            return (h * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)) * speed;
        }
    }
}
