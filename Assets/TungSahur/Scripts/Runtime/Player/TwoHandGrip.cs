using UnityEngine;

namespace TungSahur.Player
{
    /// <summary>
    /// Keeps the off hand on a two-handed weapon. The weapon rides in the right fist (pack prop-bone
    /// grip); after the Animator has posed the body, the left fist is slid onto the nearest point of the
    /// shaft and the left arm is re-solved with an analytic two-bone IK. Using the nearest point keeps
    /// each clip's own hold: the pack's 2H clips stack the hands at the butt, the Polearm / Mining clips
    /// slide the left hand up the handle. The hand keeps its animated wrap, twisted so the grip axis runs
    /// along the shaft. <see cref="Weight"/> is driven by the PlayerController (off for rolls, hits, the
    /// cannon, throws).
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class TwoHandGrip : MonoBehaviour
    {
        public Animator animator;
        [Tooltip("The two-handed weapon (shaft along its local +Y, origin in the right fist).")]
        public Transform weapon;
        [Tooltip("Where the left hand may hold the shaft (weapon-local Y range, metres).")]
        public Vector2 shaftRange = new Vector2(-0.24f, 0.42f);
        [Tooltip("Preferred hold when the clip's left hand is far from the shaft (weapon-local Y).")]
        public float restGrip = -0.13f;
        [Tooltip("Left fist centre in the LeftHand bone's space (calibrated by the builder).")]
        public Vector3 leftFistLocal = new Vector3(0f, 0.08f, 0.02f);
        [Tooltip("Axis through the closed left fist, in the LeftHand bone's space (calibrated by the builder).")]
        public Vector3 leftGripAxisLocal = Vector3.right;

        [Tooltip("Share of the gap the right hand closes by drawing the weapon toward the left hand (the alligator's wide chest puts the shaft out of the left arm's reach).")]
        [Range(0f, 1f)] public float rightHandShare = 0.5f;
        [Tooltip("Most the right hand may move the weapon per pass (metres), so the swing arc stays the clip's.")]
        public float maxRightPull = 0.18f;

        /// <summary>0..1, blended by the controller.</summary>
        public float Weight { get; set; }
        /// <summary>Metres between the left fist and the shaft before the last correction (debug / tests).</summary>
        public float LastGap { get; private set; }

        Transform upper, lower, hand, rUpper, rLower, rHand;

        void Awake() => Cache();

        void Cache()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (animator == null || !animator.isHuman) return;
            upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            lower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            rHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }

        void LateUpdate()
        {
            if (Weight <= 0.001f || weapon == null || !weapon.gameObject.activeInHierarchy) return;
            if (hand == null) { Cache(); if (hand == null) return; }
            Apply(Weight);
        }

        /// <summary>One correction pass on the current pose (also used by the editor previews).</summary>
        public void Apply(float w)
        {
            if (hand == null) Cache();
            if (hand == null || weapon == null) return;
            LastGap = -1f;
            // pass 1: the right hand brings the weapon part of the way; pass 2: whatever the left arm
            // could not reach is taken up by the right hand again
            for (int pass = 0; pass < 2; pass++)
            {
                Vector3 off = OffShaft(out float gap);
                if (LastGap < 0f) LastGap = gap;
                if (gap < 0.01f && pass > 0) break;
                if (rHand != null)
                {
                    float share = pass == 0 ? rightHandShare : 1f;
                    Quaternion rRot = rHand.rotation;
                    Solve(rUpper, rLower, rHand, rHand.position + Vector3.ClampMagnitude(off * share, maxRightPull) * w);
                    rHand.rotation = rRot;
                }
                SolveLeft(w);
            }
        }

        /// <summary>Vector from the shaft to the left fist, perpendicular to the shaft (clamped to the handle).</summary>
        Vector3 OffShaft(out float gap)
        {
            Vector3 axis = weapon.up;
            Vector3 fist = hand.TransformPoint(leftFistLocal);
            float s = Mathf.Clamp(Vector3.Dot(fist - weapon.position, axis), shaftRange.x, shaftRange.y);
            Vector3 off = fist - (weapon.position + axis * s);
            gap = off.magnitude;
            return off;
        }

        void SolveLeft(float w)
        {
            Vector3 axis = weapon.up;
            Vector3 origin = weapon.position;
            Vector3 fist = hand.TransformPoint(leftFistLocal);

            // nearest point on the handle; a hand that wandered far (one-handed moments) goes to the rest hold
            float s = Vector3.Dot(fist - origin, axis);
            Vector3 onShaft = origin + axis * Mathf.Clamp(s, shaftRange.x, shaftRange.y);
            float far = Mathf.InverseLerp(0.45f, 0.8f, Vector3.Distance(fist, onShaft));
            float sGrip = Mathf.Lerp(Mathf.Clamp(s, shaftRange.x, shaftRange.y), restGrip, far);
            Vector3 targetFist = origin + axis * sGrip;

            // twist the animated wrap so the fist's grip axis lies along the shaft (either way round)
            Vector3 g = hand.rotation * leftGripAxisLocal;
            Quaternion targetRot = Quaternion.FromToRotation(g, Vector3.Dot(g, axis) >= 0f ? axis : -axis) * hand.rotation;
            Vector3 targetWrist = targetFist - targetRot * leftFistLocal;

            Solve(upper, lower, hand, Vector3.Lerp(hand.position, targetWrist, w));
            hand.rotation = Quaternion.Slerp(hand.rotation, targetRot, w);
        }

        /// <summary>Analytic two-bone IK (shoulder fixed), keeping the animated elbow bend plane.</summary>
        void Solve(Transform upper, Transform lower, Transform hand, Vector3 target)
        {
            Vector3 a = upper.position, b = lower.position, c = hand.position;
            float lab = (b - a).magnitude, lcb = (c - b).magnitude;
            if (lab < 1e-4f || lcb < 1e-4f) return;
            float lat = Mathf.Clamp((target - a).magnitude, Mathf.Abs(lab - lcb) + 1e-3f, lab + lcb - 1e-3f);

            float acab0 = Angle(c - a, b - a);
            float babc0 = Angle(a - b, c - b);
            float acat0 = Angle(c - a, target - a);
            float acab1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
            float babc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));

            Vector3 axis0 = Vector3.Cross(c - a, b - a);
            if (axis0.sqrMagnitude < 1e-8f) axis0 = Vector3.Cross(c - a, transform.up);
            if (axis0.sqrMagnitude < 1e-8f) axis0 = transform.right;
            axis0.Normalize();
            Vector3 axis1 = Vector3.Cross(c - a, target - a);
            axis1 = axis1.sqrMagnitude < 1e-8f ? axis0 : axis1.normalized;

            Quaternion handRot = hand.rotation;
            upper.rotation = Quaternion.AngleAxis((acab1 - acab0) * Mathf.Rad2Deg, axis0) * upper.rotation;
            lower.rotation = Quaternion.AngleAxis((babc1 - babc0) * Mathf.Rad2Deg, axis0) * lower.rotation;
            upper.rotation = Quaternion.AngleAxis(acat0 * Mathf.Rad2Deg, axis1) * upper.rotation;
            hand.rotation = handRot;
        }

        static float Angle(Vector3 x, Vector3 y)
        {
            float d = Vector3.Dot(x.normalized, y.normalized);
            return Mathf.Acos(Mathf.Clamp(d, -1f, 1f));
        }
    }
}
