using UnityEngine;

namespace ElderTree.Enemies
{
    /// <summary>
    /// Where the Druid hides in the Verdant Cathedral before its first-meeting cinematic (DruidBoss.Intro.cs): a spot in
    /// the forest past the standing stones, beside the great kapok, with two trees it shoves aside and a third it tears in
    /// two on its way into the clearing. Built by the forest builder (Elder Tree/9h, or 7c2 to patch the scene).
    /// The cinematic plays once per save; on later visits the lair starts as the Druid left it (trees leaning, a splintered
    /// stump, the two halves lying in the undergrowth).
    /// </summary>
    public class DruidLair : MonoBehaviour
    {
        [Tooltip("Where the Druid waits, facing the clearing (+Z).")]
        public Transform hide;
        [Tooltip("Where it stops in the clearing to roar.")]
        public Transform entry;
        [Tooltip("The trees it shoves aside: 0 on its left, 1 on its right (pivots at the foot of the trunk).")]
        public Transform[] pushTrees = new Transform[2];
        [Tooltip("The tree it tears in two: the stump stays, the middle goes to its left hand, the crown to its right.")]
        public Transform ripStump, ripMid, ripTop;
        [Tooltip("Heights of the two breaks above the stump's foot (m): off the stump, then the middle from the crown.")]
        public Vector2 ripBreaks = new Vector2(2f, 5f);
        [Tooltip("Where the two halves come down: 0 the middle (thrown left), 1 the crown (thrown right).")]
        public Transform[] landing = new Transform[2];
        [Tooltip("Leaves shaken loose from the canopy over its hiding place (a box emitter, off until the cinematic).")]
        public ParticleSystem leafRain;
        [Tooltip("How far the shoved trees lean (degrees).")]
        public float pushAngle = 30f;
        [Tooltip("How far they spring back once let go (degrees).")]
        public float settleAngle = 24f;

        Quaternion[] treeRest;
        Vector3 midRestPos, topRestPos;
        Quaternion midRestRot, topRestRot;
        bool captured;

        public static DruidLair Find() => FindAnyObjectByType<DruidLair>();

        void Awake() => Capture();

        void Start()
        {
            // no cinematic this visit (met before, or intros off in Options): the path it tore stays torn
            if (!Progress.IntroPlays("druid")) Aftermath();
        }

        void Capture()
        {
            if (captured) return;
            captured = true;
            treeRest = new Quaternion[pushTrees.Length];
            for (int i = 0; i < pushTrees.Length; i++) if (pushTrees[i]) treeRest[i] = pushTrees[i].rotation;
            if (ripMid) { midRestPos = ripMid.position; midRestRot = ripMid.rotation; }
            if (ripTop) { topRestPos = ripTop.position; topRestRot = ripTop.rotation; }
        }

        /// <summary>The way out into the clearing (the lair's +Z).</summary>
        public Vector3 Out => hide ? Flat(hide.forward).normalized : Flat(transform.forward).normalized;
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>Leans tree <paramref name="i"/> away from the path by <paramref name="deg"/> (0 = upright), about its foot.</summary>
        public void Lean(int i, float deg, float shiver = 0f)
        {
            Capture();
            var t = i < pushTrees.Length ? pushTrees[i] : null;
            if (!t) return;
            // away from the path (its side), and a little toward the clearing as it's shoved past
            Vector3 side = Flat(t.position - (hide ? hide.position : transform.position));
            side -= Vector3.Project(side, Out);
            if (side.sqrMagnitude < 1e-4f) side = i == 0 ? -Vector3.Cross(Vector3.up, Out) : Vector3.Cross(Vector3.up, Out);
            side.Normalize();
            Vector3 lean = (side + Out * 0.3f).normalized;
            Vector3 axis = Vector3.Cross(Vector3.up, lean);
            float wob = shiver > 0f ? Mathf.Sin(Time.time * 31f + i * 2f) * shiver : 0f;
            t.rotation = Quaternion.AngleAxis(deg + wob, axis) * treeRest[i];
        }

        /// <summary>A point on tree <paramref name="i"/>'s trunk, <paramref name="h"/> m up its axis (follows the lean).</summary>
        public Vector3 TrunkPoint(int i, float h)
        {
            var t = i < pushTrees.Length ? pushTrees[i] : null;
            return t ? t.position + t.up * h : transform.position + Vector3.up * h;
        }

        /// <summary>Leaves coming down over the hiding place: <paramref name="perSecond"/> of them (0 stops it; those falling finish).</summary>
        public void Rain(float perSecond)
        {
            if (!leafRain) return;
            var em = leafRain.emission;
            em.rateOverTime = perSecond;
            if (perSecond > 0f && !leafRain.isPlaying) leafRain.Play(true);
        }

        /// <summary>The torn tree whole again, standing on its stump.</summary>
        public void RestoreRipTree()
        {
            Capture();
            if (ripMid) ripMid.SetPositionAndRotation(midRestPos, midRestRot);
            if (ripTop) ripTop.SetPositionAndRotation(topRestPos, topRestRot);
        }

        /// <summary>Where a torn half comes to rest, lying in the undergrowth.</summary>
        public void LandingPose(int i, out Vector3 pos, out Quaternion rot)
        {
            var l = i < landing.Length ? landing[i] : null;
            pos = l ? l.position : transform.position;
            rot = l ? l.rotation : Quaternion.identity;
        }

        /// <summary>As the Druid left it: both trees leaning, the halves lying where they fell.</summary>
        public void Aftermath()
        {
            Capture();
            for (int i = 0; i < pushTrees.Length; i++) Lean(i, settleAngle);
            if (ripMid) { LandingPose(0, out var p, out var r); ripMid.SetPositionAndRotation(p, r); }
            if (ripTop) { LandingPose(1, out var p, out var r); ripTop.SetPositionAndRotation(p, r); }
        }

        /// <summary>Back the way the forest was before the Druid came out (the cinematic starts from here).</summary>
        public void Intact()
        {
            Capture();
            for (int i = 0; i < pushTrees.Length; i++) Lean(i, 0f);
            RestoreRipTree();
        }
    }
}
