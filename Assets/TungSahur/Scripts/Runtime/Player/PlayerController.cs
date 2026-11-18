using TungSahur.Audio;
using TungSahur.CameraSystem;
using TungSahur.Combat;
using TungSahur.FX;
using TungSahur.InputHandling;
using UnityEngine;

namespace TungSahur.Player
{
    public enum PlayerState { Locomotion, Air, Landing, Attack, Dodge, Hit, Dead, Aim, Special, Transform, Cinematic }

    /// <summary>
    /// Souls-like player brain. Locomotion (free + lock-on strafing), sprint, jump, roll/backstep with
    /// i-frames, a queued attack system with light/heavy chains, charged heavies, running/jump/roll
    /// attacks and a slam skill. Animation playback speed is driven per attack phase to sell the
    /// club's weight; motion comes from code (in-place clips) so every lunge is tunable.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public partial class PlayerController : MonoBehaviour
    {
        [Header("References")]
        public Animator animator;
        public Transform visualRoot;       // parent of the model; lean/roll happen here
        public PlayerMotor motor;
        public PlayerStats stats;
        public Moveset moveset;
        public WeaponHitbox weapon;
        public LockOnSystem lockOn;
        public Transform clubTip;
        public Renderer clubRenderer;

        [Header("Locomotion")]
        public float walkSpeed = 1.75f;
        public float runSpeed = 4.4f;
        public float sprintSpeed = 6.6f;
        public float groundSharpness = 11f;
        public float stopSharpness = 14f;
        public float airSharpness = 1.6f;
        public float turnSpeed = 900f;
        public float lockTurnSpeed = 600f;
        public float sprintStaminaPerSec = 14f;

        [Header("Jump")]
        public float jumpHeight = 1.1f;
        public float jumpStamina = 10f;
        public float coyoteTime = 0.12f;
        public float hardLandingSpeed = 9f;

        [Header("Dodge")]
        public float rollDistance = 3.7f;
        [Tooltip("Roll state length (seconds). The Roll01 clip is played from rollClipRange.x to .y over this time.")]
        public float rollDuration = 0.8f;
        [Tooltip("Elden Ring medium roll: 13 i-frames @30fps ≈ 0.43s.")]
        public Vector2 rollIFrames = new Vector2(0.03f, 0.46f);
        [Tooltip("Normalized part of the Roll01 clip the roll plays: tuck-in to back on the feet.")]
        public Vector2 rollClipRange = new Vector2(0.02f, 0.8f);
        public float backstepDistance = 2.1f;
        public float backstepDuration = 0.46f;
        public Vector2 backstepIFrames = new Vector2(0.02f, 0.26f);
        public float dodgeStamina = 16f;
        [Tooltip("Roll clip length (Roll01), filled by the builder.")]
        public float dodgeClipLength = 1.3f;
        [Tooltip("Backstep clip length (Dodge01), filled by the builder.")]
        public float backstepClipLength = 1.07f;

        [Header("Feel")]
        public float maxLean = 11f;
        public float leanSharpness = 8f;

        // animator hashes
        static readonly int HMoveX = Animator.StringToHash("MoveX");
        static readonly int HMoveZ = Animator.StringToHash("MoveZ");
        static readonly int HLocoSpeed = Animator.StringToHash("LocoSpeed");
        static readonly int HActionSpeed = Animator.StringToHash("ActionSpeed");
        static readonly int HGrounded = Animator.StringToHash("Grounded");

        public PlayerState State { get; private set; }
        public bool Invulnerable { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsGuarding { get; private set; }
        public AttackData CurrentAttack => attack;
        public float ChargeNormalized => attack != null && attack.chargeable ? Mathf.Clamp01(chargeTime / attack.chargeMaxTime) : 0f;
        public static PlayerController Instance { get; private set; }

        PlayerInputReader input;
        Camera cam;
        float stateTime;
        Vector3 moveInputWorld;
        float moveInputMag;
        bool sprintBlocked;
        float lastGroundedTime;
        float guardWeight;
        float leanRoll, leanPitch;
        float prevYaw;
        Vector3 lastPlanarVel;

        void Awake()
        {
            Instance = this;
            input = GetComponent<PlayerInputReader>();
            if (!motor) motor = GetComponent<PlayerMotor>();
            if (!stats) stats = GetComponent<PlayerStats>();
            if (weapon) weapon.OnHit = OnWeaponHit;
            prevYaw = transform.eulerAngles.y;
            InitForms();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start() => cam = Camera.main;

        // ------------------------------------------------------------------ main loop
        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return; // paused
            if (cam == null) cam = Camera.main;
            ReadMovementInput();

            if (input.GameplayEnabled && !IsDead && State == PlayerState.Aim)
            {
                // no lock-on while a ranged weapon is up; pressing it cancels the lock that would come back
                if (input.ConsumeLockOn()) aimSuspendedLock = null;
                input.ConsumeSwitchTarget();
            }
            else if (input.GameplayEnabled && !IsDead)
            {
                if (input.ConsumeLockOn())
                {
                    if (!lockOn.Toggle() && lockOn.Current == null) CameraDirector.Instance?.Recenter();
                }
                if (lockOn.Current != null)
                {
                    int sw = input.ConsumeSwitchTarget();
                    if (sw != 0) lockOn.Switch(sw);
                }
                else input.ConsumeSwitchTarget();
            }

            stateTime += dt;
            UpdateLockVelocity(dt);
            switch (State)
            {
                case PlayerState.Locomotion: UpdateLocomotion(dt); break;
                case PlayerState.Air: UpdateAir(dt); break;
                case PlayerState.Landing: UpdateLanding(dt); break;
                case PlayerState.Attack: UpdateAttack(dt); break;
                case PlayerState.Dodge: UpdateDodge(dt); break;
                case PlayerState.Hit: UpdateHitReaction(dt); break;
                case PlayerState.Dead: UpdateDead(dt); break;
                case PlayerState.Aim: UpdateAim(dt); break;
                case PlayerState.Special: UpdateSpecial(dt); break;
                case PlayerState.Transform: UpdateTransform(dt); break;
                case PlayerState.Cinematic: UpdateCinematic(dt); break;
            }
            if (State != PlayerState.Special) UpdateThrownHammer(dt);

            motor.Tick(dt);
            if (motor.Grounded) lastGroundedTime = Time.time;
            UpdateGuardLayer(dt);
            UpdateTwoHand(dt);
            UpdateSpirit(dt);
            UpdateBowAnimator(dt);
            UpdateLean(dt);
            stats.Guarding = IsGuarding;
            CameraDirector cd = CameraDirector.Instance;
            if (cd != null)
            {
                cd.SprintFov = IsSprinting;
                cd.Aiming = State == PlayerState.Aim;
                cd.AimZoom = State == PlayerState.Aim ? BowDraw : 0f;
                if (State != PlayerState.Aim) cd.AimFriction = 1f;
            }
            animator.SetBool(HGrounded, motor.Grounded);
        }

        void LateUpdate()
        {
            // bones are posed now: turn the blade for this cut, then sweep the weapon
            UpdateBladeRoll(Time.deltaTime);
            if (weapon && !HammerThrown) weapon.Tick();
            if (weapon) slashPrevTip = weapon.TipWorld;
            UpdateVisualRoll();
        }

        // ------------------------------------------------------------------ lock-on while aiming
        Targetable aimSuspendedLock;

        void SuspendLockForAim()
        {
            aimSuspendedLock = null;
            if (lockOn == null || lockOn.Current == null) return;
            aimSuspendedLock = lockOn.Current;
            lockOn.ForceRelease();
        }

        void ResumeLockAfterAim(bool resume)
        {
            var t = aimSuspendedLock;
            aimSuspendedLock = null;
            if (!resume || lockOn == null || lockOn.Current != null || !lockOn.CanHold(t)) return;
            lockOn.ForceLock(t);
        }

        void ReadMovementInput()
        {
            Vector2 m = input.GameplayEnabled ? input.Move : Vector2.zero;
            moveInputMag = m.magnitude;
            Vector3 fwd = cam ? cam.transform.forward : Vector3.forward;
            fwd.y = 0;
            if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            moveInputWorld = (fwd * m.y + right * m.x);
            if (moveInputWorld.sqrMagnitude > 1f) moveInputWorld.Normalize();
        }

        void SetState(PlayerState s)
        {
            // a special cut short (hit, death) gives the camera back; after a blast its own release stands
            if (State == PlayerState.Special && s != PlayerState.Special) CameraDirector.Instance?.ReleaseFocus(0.35f);
            if (State == PlayerState.Special && s != PlayerState.Special && spinMode) AbortSpin();
            // raising a ranged weapon drops the lock (free crosshair aim); lowering it picks the target back up
            if (s == PlayerState.Aim && State != PlayerState.Aim) SuspendLockForAim();
            else if (State == PlayerState.Aim && s != PlayerState.Aim) ResumeLockAfterAim(s != PlayerState.Dead);
            State = s;
            stateTime = 0f;
        }

        // ------------------------------------------------------------------ locomotion
        float DesiredSpeed()
        {
            if (moveInputMag < 0.05f) return 0f;
            float speed = moveInputMag <= 0.55f
                ? walkSpeed * Mathf.Clamp01(moveInputMag / 0.55f + 0.25f)
                : Mathf.Lerp(walkSpeed, runSpeed, (moveInputMag - 0.55f) / 0.45f);
            if (input.WalkModifier || IsGuarding) speed = Mathf.Min(speed, walkSpeed);
            return speed;
        }

        void UpdateLocomotion(float dt)
        {
            // leave the ground (walked off a ledge)
            if (!motor.Grounded && Time.time - lastGroundedTime > coyoteTime)
            {
                EnterAir(false);
                return;
            }

            // ---- action requests (priority: dodge > attack > jump)
            if (TryStartActionFromNeutral()) return;

            // ---- sprint
            bool wantsSprint = input.SprintHeld && moveInputMag > 0.3f && !IsGuarding;
            if (stats.Stamina <= 0f) sprintBlocked = true;
            if (sprintBlocked && (stats.Stamina > 25f || !input.SprintHeld)) sprintBlocked = false;
            IsSprinting = wantsSprint && !sprintBlocked;
            if (IsSprinting) stats.DrainStamina(sprintStaminaPerSec, dt);

            float speed = IsSprinting ? sprintSpeed : DesiredSpeed();
            Vector3 dir = moveInputWorld.sqrMagnitude > 1e-4f ? moveInputWorld.normalized : Vector3.zero;
            bool strafing = lockOn.Current != null && !IsSprinting;

            if (strafing)
            {
                FaceTowards(lockOn.Current.transform.position, lockTurnSpeed, dt);
            }
            else if (dir != Vector3.zero)
            {
                // sharp reversals slow down briefly instead of moonwalking
                float angle = Vector3.Angle(transform.forward, dir);
                float turn = turnSpeed * (angle > 120f ? 1.6f : 1f);
                RotateTowards(dir, turn, dt);
                if (angle > 100f && motor.PlanarVelocity.magnitude > runSpeed * 0.8f) speed *= 0.6f;
            }

            Vector3 desired = dir * speed;
            motor.SetDesiredPlanarVelocity(desired, speed > 0.01f ? groundSharpness : stopSharpness, dt);
            DriveLocomotionAnimator(dt);
        }

        bool TryStartActionFromNeutral()
        {
            if (!input.GameplayEnabled) return false;
            if (input.HasBufferedDodge && TryDodge()) return true;
            if (input.AimHeld && motor.Grounded && TryEnterAim()) return true;
            if (input.HasBufferedSkill)
            {
                input.ConsumeSkill();
                if (TryStartSpecial()) return true;
            }
            if (input.HasBufferedLight)
            {
                input.ConsumeLight();
                var a = IsSprinting || (motor.PlanarVelocity.magnitude > runSpeed * 1.05f) ? moveset.runningAttack : moveset.lightCombo[0];
                if (stateTime < 0.35f && lastDodgeEndedAt > 0 && Time.time - lastDodgeEndedAt < 0.35f) a = moveset.dodgeAttack;
                if (StartAttack(a)) return true;
            }
            if (input.HasBufferedHeavy)
            {
                input.ConsumeHeavy();
                if (StartAttack(moveset.heavyCombo[0])) return true;
            }
            if (input.HasBufferedJump && motor.Grounded)
            {
                input.ConsumeJump();
                if (stats.TrySpendStamina(jumpStamina))
                {
                    motor.Jump(jumpHeight);
                    // a running jump keeps its momentum (and a touch extra)
                    motor.SetPlanarVelocityImmediate(motor.PlanarVelocity * 1.05f);
                    EnterAir(true);
                    return true;
                }
            }
            return false;
        }

        void DriveLocomotionAnimator(float dt)
        {
            Vector3 v = motor.PlanarVelocity;
            float speed = v.magnitude;
            Vector3 local = transform.InverseTransformDirection(v);
            local.y = 0;
            float ring = SpeedToRing(speed);
            Vector2 dir2 = speed > 0.05f ? new Vector2(local.x, local.z).normalized : Vector2.zero;
            Vector2 param = dir2 * ring;
            animator.SetFloat(HMoveX, param.x, 0.08f, dt);
            animator.SetFloat(HMoveZ, param.y, 0.08f, dt);

            // match playback to ground speed so feet don't slide
            float nominal = ring <= 1f ? 1.8f * Mathf.Max(ring, 0.01f) : ring <= 2f ? Mathf.Lerp(1.8f, 3.6f, ring - 1f) : Mathf.Lerp(3.6f, 5.4f, ring - 2f);
            float playback = speed > 0.1f ? Mathf.Clamp(speed / Mathf.Max(0.1f, nominal), 0.8f, 1.35f) : 1f;
            animator.SetFloat(HLocoSpeed, playback, 0.1f, dt);
        }

        float SpeedToRing(float s)
        {
            if (s <= walkSpeed) return s / walkSpeed;
            if (s <= runSpeed) return 1f + (s - walkSpeed) / (runSpeed - walkSpeed);
            return 2f + Mathf.Clamp01((s - runSpeed) / (sprintSpeed - runSpeed));
        }

        void RotateTowards(Vector3 dir, float degPerSec, float dt)
        {
            dir.y = 0;
            if (dir.sqrMagnitude < 1e-5f) return;
            var target = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, degPerSec * dt);
        }

        void FaceTowards(Vector3 worldPos, float degPerSec, float dt) => RotateTowards(worldPos - transform.position, degPerSec, dt);

        void FaceInstant(Vector3 dir)
        {
            dir.y = 0;
            if (dir.sqrMagnitude > 1e-5f) transform.rotation = Quaternion.LookRotation(dir);
        }

        // ------------------------------------------------------------------ air
        float jumpAnimSwitchAt;

        void EnterAir(bool jumped)
        {
            SetState(PlayerState.Air);
            IsSprinting = false;
            if (jumped)
            {
                animator.CrossFadeInFixedTime("JumpStart", 0.08f);
                AudioManager.Play(Sfx.Jump, transform.position, 0.8f);
                FXManager.Spawn(FXType.DustSmall, transform.position, Quaternion.identity);
                jumpAnimSwitchAt = 0.3f;
            }
            else
            {
                jumpAnimSwitchAt = 0f;
                animator.CrossFadeInFixedTime("Fall", 0.25f);
            }
            fallAnimPlaying = !jumped;
        }

        bool fallAnimPlaying;

        void UpdateAir(float dt)
        {
            if (!fallAnimPlaying && (stateTime > jumpAnimSwitchAt && motor.VerticalVelocity < 1.5f))
            {
                animator.CrossFadeInFixedTime("Fall", 0.3f);
                fallAnimPlaying = true;
            }
            // light air steering
            Vector3 desired = moveInputWorld * Mathf.Max(motor.PlanarVelocity.magnitude, runSpeed * 0.6f);
            if (moveInputWorld.sqrMagnitude > 0.01f)
                motor.SetDesiredPlanarVelocity(desired, airSharpness, dt);
            if (moveInputWorld.sqrMagnitude > 0.01f && lockOn.Current == null) RotateTowards(moveInputWorld, 360f, dt);

            // jump attack
            if (input.GameplayEnabled && (input.HasBufferedLight || input.HasBufferedHeavy) && stateTime > 0.08f)
            {
                input.ConsumeLight(); input.ConsumeHeavy();
                if (StartAttack(moveset.jumpAttack)) return;
            }

            if (motor.LandedThisFrame || (motor.Grounded && stateTime > 0.1f)) Land();
        }

        void Land()
        {
            float impact = motor.LandingSpeed;
            AudioManager.Play(Sfx.Land, transform.position, Mathf.Clamp01(impact / 10f) * 0.8f + 0.2f);
            FXManager.Spawn(FXType.LandDust, transform.position, Quaternion.identity, Mathf.Clamp(impact / 8f, 0.5f, 1.4f));
            squash = Mathf.Clamp01(impact / 12f) * 0.18f;
            if (impact > hardLandingSpeed * 0.65f)
            {
                SetState(PlayerState.Landing);
                animator.CrossFadeInFixedTime("Land", 0.05f, 0, 0.15f);
                CameraDirector.Instance?.Shake(Mathf.Clamp01(impact / 15f) * 0.6f);
            }
            else
            {
                SetState(PlayerState.Locomotion);
                animator.CrossFadeInFixedTime("Locomotion", 0.15f);
            }
        }

        void UpdateLanding(float dt)
        {
            motor.SetDesiredPlanarVelocity(Vector3.zero, 18f, dt);
            if (stateTime > 0.22f && (input.HasBufferedDodge || input.HasBufferedLight || input.HasBufferedHeavy))
            {
                SetState(PlayerState.Locomotion);
                animator.CrossFadeInFixedTime("Locomotion", 0.1f);
                TryStartActionFromNeutral();
                return;
            }
            if (stateTime > 0.38f || (stateTime > 0.2f && moveInputMag > 0.2f))
            {
                SetState(PlayerState.Locomotion);
                animator.CrossFadeInFixedTime("Locomotion", 0.2f);
            }
        }

        // ------------------------------------------------------------------ guard (upper-body layer)
        void UpdateGuardLayer(float dt)
        {
            bool canGuard = State == PlayerState.Locomotion && input.GameplayEnabled && input.GuardHeld;
            if (canGuard && !IsGuarding) { AudioManager.Play(Sfx.Guard, transform.position, 0.5f); animator.CrossFadeInFixedTime("GuardLoop", 0.12f, 1); }
            if (!canGuard && IsGuarding) animator.CrossFadeInFixedTime("Empty", 0.15f, 1);
            IsGuarding = canGuard;
            guardWeight = Mathf.MoveTowards(guardWeight, IsGuarding ? 1f : 0f, dt * 6f);
            animator.SetLayerWeight(1, guardWeight);
        }

        void ReturnToLocomotion(float fade)
        {
            SetState(PlayerState.Locomotion);
            animator.CrossFadeInFixedTime("Locomotion", fade);
            animator.SetFloat(HActionSpeed, 1f);
        }

        // ------------------------------------------------------------------ procedural lean + squash
        float squash;

        void UpdateLean(float dt)
        {
            float yaw = transform.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(prevYaw, yaw) / Mathf.Max(dt, 1e-4f);
            prevYaw = yaw;
            Vector3 v = motor.PlanarVelocity;
            Vector3 accel = (v - lastPlanarVel) / Mathf.Max(dt, 1e-4f);
            lastPlanarVel = v;
            float speedK = Mathf.Clamp01(v.magnitude / runSpeed);
            bool grounded = State == PlayerState.Locomotion;
            float targetRoll = grounded ? Mathf.Clamp(-yawRate * 0.022f * speedK, -maxLean, maxLean) : 0f;
            float fwdAccel = Vector3.Dot(accel, transform.forward);
            float targetPitch = grounded ? Mathf.Clamp(fwdAccel * 0.35f, -5f, 7f) + (IsSprinting ? 1.5f : 0f) : 0f;
            leanRoll = Mathf.Lerp(leanRoll, targetRoll, Ease.Damp(leanSharpness, dt));
            leanPitch = Mathf.Lerp(leanPitch, targetPitch, Ease.Damp(leanSharpness * 0.7f, dt));
            squash = Mathf.Lerp(squash, 0f, Ease.Damp(10f, dt));
        }

        void UpdateVisualRoll()
        {
            // lean around the feet (the dodge roll is the pack's Roll01 clip, not a procedural spin)
            visualRoot.localRotation = Quaternion.Euler(leanPitch, 0, leanRoll + SpinVisualLean);
            visualRoot.localPosition = SpinVisualLift;
            float s = squash;
            visualRoot.localScale = new Vector3(1f + s * 0.6f, 1f - s, 1f + s * 0.6f);
        }
    }
}
