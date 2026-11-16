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

        public static CameraDirector Instance { get; private set; }
        public float Yaw => orbit != null ? orbit.HorizontalAxis.Value : 0f;
        public bool SprintFov { get; set; }

        float yawVel, pitchVel, recenterUntil, recenterYaw;
        float fovKick, fovKickVel, currentFov;
        Vector3 aimVel;

        void Awake()
        {
            Instance = this;
            currentFov = baseFov;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (followPivot && player) followPivot.position = player.position + Vector3.up * pivotHeight;
            if (aimPoint && player) aimPoint.position = player.position + Vector3.up * pivotHeight;
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

        void Update()
        {
            if (player == null || orbit == null) return;
            float dt = Time.unscaledDeltaTime;
            var input = PlayerInputReader.Instance;
            bool paused = TimeDirector.Instance != null && TimeDirector.Instance.Paused;

            // ---- pivot follow (smoothed, softer vertically so jumps don't jerk the frame)
            Vector3 target = player.position + Vector3.up * pivotHeight;
            Vector3 p = followPivot.position;
            float sdt = Time.deltaTime;
            p.x = Mathf.Lerp(p.x, target.x, Ease.Damp(pivotSharpnessXZ, sdt));
            p.z = Mathf.Lerp(p.z, target.z, Ease.Damp(pivotSharpnessXZ, sdt));
            p.y = Mathf.Lerp(p.y, target.y, Ease.Damp(pivotSharpnessY, sdt));
            followPivot.position = p;

            var locked = lockOn != null ? lockOn.Current : null;
            float yaw = orbit.HorizontalAxis.Value;
            float pitch = orbit.VerticalAxis.Value;

            if (locked != null)
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
                aimPoint.position = Vector3.SmoothDamp(aimPoint.position, aim, ref aimVel, 0.08f, Mathf.Infinity, sdt);
            }
            else
            {
                if (!paused && input != null && input.GameplayEnabled)
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
                aimPoint.position = Vector3.SmoothDamp(aimPoint.position, followPivot.position, ref aimVel, 0.06f, Mathf.Infinity, sdt);
            }

            orbit.HorizontalAxis.Value = Mathf.Repeat(yaw + 180f, 360f) - 180f;
            orbit.VerticalAxis.Value = Mathf.Clamp(pitch, minPitch, maxPitch);

            // ---- FOV (sprint widening + punch kicks that spring back)
            fovKick = Mathf.SmoothDamp(fovKick, 0f, ref fovKickVel, 0.12f, Mathf.Infinity, dt);
            float targetFov = baseFov + (SprintFov ? sprintFovAdd : 0f);
            currentFov = Mathf.Lerp(currentFov, targetFov, Ease.Damp(4f, dt));
            var lens = vcam.Lens;
            lens.FieldOfView = currentFov + fovKick;
            vcam.Lens = lens;
        }
    }
}
