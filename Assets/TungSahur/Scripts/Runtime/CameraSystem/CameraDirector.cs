using TungSahur.InputHandling;
using Unity.Cinemachine;
using UnityEngine;

namespace TungSahur.CameraSystem
{
    /// <summary>
    /// Drives a single Cinemachine third-person camera (OrbitalFollow + RotationComposer +
    /// Deoccluder). Free look reads the input; lock-on steers the orbit to sit behind the player
    /// facing the target and slides the aim point toward the target (Elden Ring framing).
    /// Using one camera avoids blend pops when locking/unlocking.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class CameraDirector : MonoBehaviour
    {
        public CinemachineCamera vcam;
        public CinemachineOrbitalFollow orbit;
        public CinemachineImpulseSource impulse;
        public Transform player;
        public Transform followPivot;   // smoothed position the orbit follows
        public Transform aimPoint;      // what the composer looks at
        public LockOnSystem lockOn;

        [Header("Framing")]
        public float pivotHeight = 1.5f;
        public float pivotSharpnessXZ = 30f;
        public float pivotSharpnessY = 9f;
        public float minPitch = -30f, maxPitch = 62f;
        public float baseFov = 52f;
        public float sprintFovAdd = 5f;

        [Header("Lock-on")]
        public float lockYawSmooth = 0.13f;
        public float lockPitchSmooth = 0.2f;
        public float lockAimBlend = 0.45f;
        public float lockPitchBase = 14f;
        [Tooltip("Over-the-shoulder offset so the hero never blocks the target.")]
        public float lockYawOffset = 11f;

        [Header("Bow aim")]
        [Tooltip("Orbit radius while aiming (zoomed in over the shoulder).")]
        public float aimRadius = 3.0f;
        public float aimFov = 42f;
        [Tooltip("FOV at full draw: the view tightens as the string comes back.")]
        public float aimFullDrawFov = 34f;
        [Tooltip("Shoulder offset while aiming: x = right, y = up (metres, camera-relative).")]
        public Vector2 aimShoulder = new Vector2(1.05f, 0.42f);
        [Tooltip("Look sensitivity multiplier while aiming (finer control).")]
        public float aimLookScale = 0.55f;
        public float aimInTime = 0.16f, aimOutTime = 0.22f;
        public float aimMinPitch = -55f, aimMaxPitch = 70f;

        public static CameraDirector Instance { get; private set; }
        public float Yaw => orbit != null ? orbit.HorizontalAxis.Value : 0f;
        public bool SprintFov { get; set; }
        /// <summary>Set by the player while the bow is raised.</summary>
        public bool Aiming { get; set; }
        /// <summary>0..1 draw amount; tightens the aim zoom.</summary>
        public float AimZoom { get; set; }
        /// <summary>Look-speed multiplier (aim friction on targets with a pad).</summary>
        public float AimFriction { get; set; } = 1f;
        /// <summary>Smoothed 0..1 blend between the exploration and aim framings.</summary>
        public float AimWeight { get; private set; }

        float yawVel, pitchVel, recenterUntil, recenterYaw;
        float fovKick, fovKickVel, currentFov;
        Vector3 aimVel, pivotBase;
        float baseRadius, aimLinear, aimSnapUntil;
        [Tooltip("Bow aim while locked on: how far (degrees) look input can lead the target.")]
        public float maxAimLead = 14f;
        Vector2 aimLead;
        /// <summary>Bow aim while locked on: the hand-steered lead offset (degrees).</summary>
        public Vector2 AimLead => aimLead;
        bool wasAiming;

        void Awake()
        {
            Instance = this;
            currentFov = baseFov;
            if (orbit)
            {
                baseRadius = orbit.Radius;
                // the director clamps per mode; the orbit itself must allow the steeper aim pitch
                orbit.VerticalAxis.Range = new Vector2(Mathf.Min(minPitch, aimMinPitch), Mathf.Max(maxPitch, aimMaxPitch));
            }
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (player) pivotBase = player.position + Vector3.up * pivotHeight;
            if (followPivot && player) followPivot.position = player.position + Vector3.up * pivotHeight;
            if (aimPoint && player) aimPoint.position = player.position + Vector3.up * pivotHeight;
            if (aimPoint) aimSmoothed = aimPoint.position;
            if (orbit && player) orbit.HorizontalAxis.Value = player.eulerAngles.y;
        }

        public void Recenter()
        {
            recenterYaw = player.eulerAngles.y;
            recenterUntil = Time.unscaledTime + 0.35f;
        }

        /// <summary>Screen shake through Cinemachine Impulse.</summary>
        public void Shake(float strength, Vector3? dir = null)
        {
            strength *= Settings.ShakeScale;
            if (impulse == null || strength <= 0f) return;
            var d = dir ?? Random.onUnitSphere;
            d = (d.normalized + Vector3.down * 0.6f).normalized;
            impulse.GenerateImpulseWithVelocity(d * strength);
        }

        public void FovKick(float amount) => fovKick += amount;

        // ------------------------------------------------------------------ cinematic focus
        // A short takeover (Sky Bombardment): pull back, rise and turn to frame a point, then give the
        // stick back. Look input is ignored while the focus holds; on release the steering fades out
        // and the free/lock camera carries on from wherever the shot ended (no snap back).
        [Header("Focus takeover")]
        public float focusInTime = 0.45f;
        public float focusOutTime = 0.7f;
        [Tooltip("Safety: a focus never holds longer than this (seconds).")]
        public float focusMaxTime = 6f;

        bool focusHeld;
        float focusLinear, focusRelease = -1f, focusStartedAt;
        Vector3 focusPoint;
        float focusZoom, focusPitch, focusAimBlend;
        Vector3 aimSmoothed;

        /// <summary>0..1 eased weight of the focus takeover.</summary>
        public float FocusWeight { get; private set; }
        public bool FocusActive => focusHeld;

        /// <summary>Take the camera: frame <paramref name="point"/> from further back and a little higher.</summary>
        public void FocusOn(Vector3 point, float zoomOut, float pitch, float aimBlend)
        {
            if (!focusHeld) focusStartedAt = Time.time;
            focusHeld = true;
            focusRelease = -1f;
            focusPoint = point;
            focusZoom = zoomOut;
            focusPitch = pitch;
            focusAimBlend = aimBlend;
        }

        /// <summary>Hand the camera back after <paramref name="delay"/> game seconds (the first scheduled release wins).</summary>
        public void ReleaseFocus(float delay = 0f)
        {
            if (!focusHeld || focusRelease >= 0f) return;
            focusRelease = Time.time + Mathf.Max(0f, delay);
        }

        void UpdateFocus(float sdt)
        {
            if (focusHeld && ((focusRelease >= 0f && Time.time >= focusRelease) || Time.time - focusStartedAt > focusMaxTime))
            {
                focusHeld = false;
                focusRelease = -1f;
            }
            focusLinear = Mathf.MoveTowards(focusLinear, focusHeld ? 1f : 0f, sdt / Mathf.Max(0.01f, focusHeld ? focusInTime : focusOutTime));
            FocusWeight = Ease.InOutCubic(focusLinear);
        }

        void Update()
        {
            if (player == null || orbit == null) return;
            float dt = Time.unscaledDeltaTime;
            var input = PlayerInputReader.Instance;
            bool paused = TimeDirector.Instance != null && TimeDirector.Instance.Paused;

            // ---- aim blend (eased both ways so the zoom reads as a deliberate "raise")
            float sdt = Time.deltaTime;
            aimLinear = Mathf.MoveTowards(aimLinear, Aiming ? 1f : 0f, sdt / Mathf.Max(0.01f, Aiming ? aimInTime : aimOutTime));
            AimWeight = Aiming ? Ease.OutCubic(aimLinear) : Ease.InOutCubic(aimLinear);
            float aw = AimWeight;
            UpdateFocus(sdt);

            // ---- pivot follow (smoothed, softer vertically so jumps don't jerk the frame)
            Vector3 target = player.position + Vector3.up * pivotHeight;
            Vector3 p = pivotBase;
            p.x = Mathf.Lerp(p.x, target.x, Ease.Damp(pivotSharpnessXZ, sdt));
            p.z = Mathf.Lerp(p.z, target.z, Ease.Damp(pivotSharpnessXZ, sdt));
            p.y = Mathf.Lerp(p.y, target.y, Ease.Damp(Mathf.Lerp(pivotSharpnessY, pivotSharpnessXZ, aw), sdt));
            pivotBase = p;

            var locked = lockOn != null ? lockOn.Current : null;
            float yaw = orbit.HorizontalAxis.Value;
            float pitch = orbit.VerticalAxis.Value;

            // over-the-shoulder offset, applied after smoothing so it turns rigidly with the view
            Vector3 camRight = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            followPivot.position = pivotBase + (camRight * aimShoulder.x + Vector3.up * aimShoulder.y) * aw;

            // raising the bow while locked on swings the view onto the target, then hands control back
            if (Aiming && !wasAiming && locked != null) { aimSnapUntil = Time.unscaledTime + 0.3f; aimLead = Vector2.zero; }
            wasAiming = Aiming;

            if (Aiming || aw > 0.5f)
            {
                if (Aiming && locked != null && Camera.main != null)
                {
                    // closed loop on the real view ray: whatever the composer framing, the screen-centre
                    // crosshair ends up on the lock point. After the initial snap the aim stays stuck to a
                    // locked target (moving and flying enemies), and look input becomes a lead offset.
                    bool snapping = Time.unscaledTime < aimSnapUntil;
                    if (!snapping && !paused && input != null && input.GameplayEnabled)
                    {
                        var d = input.LookDelta(dt) * aimLookScale;
                        aimLead = Vector2.ClampMagnitude(aimLead + new Vector2(d.x, -d.y), maxAimLead);
                    }
                    var ct = Camera.main.transform;
                    Vector3 to = locked.LockPosition - ct.position;
                    Vector3 f = ct.forward;
                    float yawErr = Mathf.DeltaAngle(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg) + aimLead.x;
                    float pitchErr = Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f)) * Mathf.Rad2Deg + aimLead.y - Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
                    float k = Ease.Damp(snapping ? 22f : 14f, dt);
                    yaw += yawErr * k;
                    pitch -= pitchErr * k;
                }
                else if (!paused && input != null && input.GameplayEnabled)
                {
                    var d = input.LookDelta(dt) * Mathf.Lerp(1f, aimLookScale, aw) * AimFriction;
                    yaw += d.x;
                    pitch -= d.y;
                }
                pitch = Mathf.Clamp(pitch, aimMinPitch, aimMaxPitch);
                aimSmoothed = followPivot.position;
                aimVel = Vector3.zero;
            }
            else if (locked != null)
            {
                Vector3 to = locked.LockPosition - followPivot.position;
                Vector3 flat = new Vector3(to.x, 0, to.z);
                float dist = Mathf.Max(0.5f, flat.magnitude);
                // shoulder offset grows as the target gets close (that's when the hero blocks it)
                float shoulder = lockYawOffset * Mathf.Lerp(1.6f, 0.6f, Mathf.InverseLerp(1.2f, 7f, dist));
                float desiredYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg + shoulder;
                // closer targets → camera rises a little to keep both in frame
                float desiredPitch = Mathf.Clamp(lockPitchBase + Mathf.Lerp(16f, 0f, Mathf.InverseLerp(1.2f, 8f, dist))
                                                 - Mathf.Atan2(to.y, dist) * Mathf.Rad2Deg * 0.6f, -5f, 40f);
                yaw = Mathf.SmoothDampAngle(yaw, desiredYaw, ref yawVel, lockYawSmooth, Mathf.Infinity, sdt);
                pitch = Mathf.SmoothDamp(pitch, desiredPitch, ref pitchVel, lockPitchSmooth, Mathf.Infinity, sdt);
                Vector3 aim = Vector3.Lerp(followPivot.position, locked.LockPosition, lockAimBlend);
                aimSmoothed = Vector3.SmoothDamp(aimSmoothed, aim, ref aimVel, 0.08f, Mathf.Infinity, sdt);
            }
            else
            {
                if (!paused && input != null && input.GameplayEnabled && !focusHeld)
                {
                    var d = input.LookDelta(dt);
                    yaw += d.x;
                    pitch = Mathf.Clamp(pitch - d.y, minPitch, maxPitch);
                }
                if (Time.unscaledTime < recenterUntil)
                {
                    yaw = Mathf.SmoothDampAngle(yaw, recenterYaw, ref yawVel, 0.09f, Mathf.Infinity, dt);
                    pitch = Mathf.SmoothDamp(pitch, 12f, ref pitchVel, 0.12f, Mathf.Infinity, dt);
                }
                aimSmoothed = Vector3.SmoothDamp(aimSmoothed, followPivot.position, ref aimVel, 0.06f, Mathf.Infinity, sdt);
            }

            // ---- focus takeover: turn behind the player toward the point, rise, pull back, aim between
            float fw = aw > 0.5f ? 0f : FocusWeight;
            Vector3 aimFinal = aimSmoothed;
            if (fw > 0f)
            {
                Vector3 toF = focusPoint - player.position; toF.y = 0f;
                float steer = Ease.Damp(focusHeld ? 6f : 3f, dt) * fw;
                if (toF.sqrMagnitude > 0.25f)
                    yaw = Mathf.LerpAngle(yaw, Mathf.Atan2(toF.x, toF.z) * Mathf.Rad2Deg + lockYawOffset * 0.5f, steer);
                pitch = Mathf.Lerp(pitch, focusPitch, steer);
                Vector3 focusAim = Vector3.Lerp(followPivot.position, focusPoint + Vector3.up * 0.8f, focusAimBlend);
                aimFinal = Vector3.Lerp(aimSmoothed, focusAim, fw);
            }
            aimPoint.position = aimFinal;

            orbit.HorizontalAxis.Value = Mathf.Repeat(yaw + 180f, 360f) - 180f;
            orbit.VerticalAxis.Value = Mathf.Clamp(pitch, Mathf.Lerp(minPitch, aimMinPitch, aw), Mathf.Lerp(maxPitch, aimMaxPitch, aw));
            if (baseRadius > 0f) orbit.Radius = Mathf.Lerp(baseRadius, aimRadius, aw) + focusZoom * fw;

            // ---- FOV (sprint widening, aim zoom tightening with the draw, punch kicks that spring back)
            fovKick = Mathf.SmoothDamp(fovKick, 0f, ref fovKickVel, 0.12f, Mathf.Infinity, dt);
            float exploreFov = baseFov + (SprintFov ? sprintFovAdd : 0f);
            float drawFov = Mathf.Lerp(aimFov, aimFullDrawFov, Ease.InOutCubic(Mathf.Clamp01(AimZoom)));
            float targetFov = Mathf.Lerp(exploreFov, drawFov, aw);
            currentFov = Mathf.Lerp(currentFov, targetFov, Ease.Damp(Aiming ? 9f : 4f, dt));
            var lens = vcam.Lens;
            lens.FieldOfView = currentFov + fovKick;
            vcam.Lens = lens;
        }
    }
}
