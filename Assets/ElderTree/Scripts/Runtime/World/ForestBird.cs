using System.Collections.Generic;
using ElderTree.Enemies;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree.World
{
    /// <summary>
    /// A toucan in the branches. It sits on a perch looking about (quick head-turns of the whole bird, a tail bob,
    /// a ruffle of the wings), and when the player or a boss comes close, or just when it feels like it, it drops
    /// off the branch, flaps across in an arc and lands on another free perch. Perches are the "Perch*" empties of
    /// the trees (Tools/Blender/build_forest.py). The model has its wings folded along its sides ("WingL"/"WingR",
    /// pivot at the shoulder) and an empty "Beak"; the wings are swung out and flapped in flight.
    /// </summary>
    public class ForestBird : MonoBehaviour
    {
        public Transform[] perches;
        public int startPerch = -1;
        public float spookDistance = 7.5f;
        public float flySpeed = 8f;
        [Tooltip("Seconds between moves when nothing disturbs it.")]
        public Vector2 restlessness = new(25f, 70f);
        [Tooltip("Body pitch in flight (the perched model sits upright).")]
        public float flightPitch = 48f;

        static readonly HashSet<int> taken = new();
        static PlayerController player;
        static BossBase[] bosses;
        static float lastBossScan = -99f;

        Transform wingL, wingR;
        Quaternion wingLRest, wingRRest, spreadL = Quaternion.identity, spreadR = Quaternion.identity;
        Vector3 frontLocal = Vector3.forward, sideLocal = Vector3.right, flapAxis = Vector3.forward;
        float sideL = -1f, sideR = 1f, pitchSign = 1f;
        Quaternion modelFix = Quaternion.identity;
        int perch = -1;
        bool flying;
        Vector3 a, b, c;
        float t, dur, yaw, lookYaw, lookT, restT, bob, spread, pitch, ruffle;

        void Start()
        {
            foreach (var tr in GetComponentsInChildren<Transform>())
            {
                if (tr.name == "WingL") { wingL = tr; wingLRest = tr.localRotation; }
                else if (tr.name == "WingR") { wingR = tr; wingRRest = tr.localRotation; }
                else if (tr.name == "Beak") frontLocal = transform.InverseTransformPoint(tr.position);
            }
            Vector3 beak = frontLocal;
            frontLocal = Vector3.ProjectOnPlane(frontLocal, Vector3.up).normalized;
            if (frontLocal.sqrMagnitude < 0.5f) frontLocal = Vector3.forward;
            sideLocal = Vector3.Cross(Vector3.up, frontLocal).normalized;
            modelFix = Vector3.Dot(frontLocal, Vector3.forward) < -0.9f ? Quaternion.Euler(0, 180f, 0) : Quaternion.FromToRotation(frontLocal, Vector3.forward);
            // which way round the side axis tips the bill down (level flight)
            pitchSign = (Quaternion.AngleAxis(30f, sideLocal) * beak).y < beak.y ? 1f : -1f;
            var pitchRot = Quaternion.AngleAxis(flightPitch * pitchSign, sideLocal);
            Vector3 flightUp = Quaternion.Inverse(pitchRot) * Vector3.up;
            flapAxis = Quaternion.Inverse(pitchRot) * frontLocal;
            spreadL = Spread(wingL, flightUp, out sideL);
            spreadR = Spread(wingR, flightUp, out sideR);

            yaw = transform.eulerAngles.y;
            if (perches != null && startPerch >= 0 && startPerch < perches.Length && perches[startPerch] != null && taken.Add(startPerch))
            {
                perch = startPerch;
                transform.position = perches[perch].position;
            }
            restT = Random.Range(restlessness.x, restlessness.y);
            lookT = Random.Range(0.5f, 3f);
            lookYaw = yaw;
        }

        void OnDestroy() { if (perch >= 0) taken.Remove(perch); }

        /// <summary>Rotation (in the root's space) that swings a folded wing out sideways, flat, for flight.</summary>
        Quaternion Spread(Transform wing, Vector3 flightUp, out float side)
        {
            side = 1f;
            if (wing == null) return Quaternion.identity;
            var mf = wing.GetComponent<MeshFilter>();
            Vector3 c = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.center : Vector3.zero;
            Vector3 pivot = wing.localPosition;
            Vector3 fold = (transform.InverseTransformPoint(wing.TransformPoint(c)) - pivot).normalized;
            side = Vector3.Dot(pivot, sideLocal) >= 0f ? 1f : -1f;
            Vector3 outward = sideLocal * side;
            if (Mathf.Abs(Vector3.Dot(fold, outward)) > 0.98f) return Quaternion.identity;
            return Quaternion.LookRotation(outward, flightUp) * Quaternion.Inverse(Quaternion.LookRotation(fold, outward));
        }

        float Threat()
        {
            float dist = float.MaxValue;
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (player != null) dist = Vector3.Distance(player.transform.position, transform.position);
            if (Time.time - lastBossScan > 2f) { bosses = FindObjectsByType<BossBase>(FindObjectsSortMode.None); lastBossScan = Time.time; }
            if (bosses != null)
                foreach (var bb in bosses)
                    if (bb != null && bb.isActiveAndEnabled)
                        dist = Mathf.Min(dist, Vector3.Distance(bb.transform.position, transform.position) - 5f);
            return dist;
        }

        bool TakeOff()
        {
            if (perches == null || perches.Length < 2) return false;
            Vector3 threat = player != null ? player.transform.position : transform.position;
            int best = -1; float bestScore = float.MinValue;
            for (int k = 0; k < 12; k++)
            {
                int i = Random.Range(0, perches.Length);
                if (i == perch || perches[i] == null || taken.Contains(i)) continue;
                float d = Vector3.Distance(perches[i].position, transform.position);
                if (d < 6f || d > 45f) continue;
                float score = Vector3.Distance(perches[i].position, threat) - d * 0.3f + Random.value * 4f;
                if (score > bestScore) { bestScore = score; best = i; }
            }
            if (best < 0) return false;
            if (perch >= 0) taken.Remove(perch);
            perch = best;
            taken.Add(perch);
            a = transform.position;
            b = perches[perch].position;
            float dist = Vector3.Distance(a, b);
            c = (a + b) * 0.5f + Vector3.up * (2f + dist * 0.25f) + Vector3.ProjectOnPlane(Random.insideUnitSphere, Vector3.up) * dist * 0.2f;
            dur = dist / flySpeed + 0.4f;
            t = 0f;
            flying = true;
            return true;
        }

        void Update()
        {
            float dt = Time.deltaTime, now = Time.time;
            if (flying)
            {
                t += dt / dur;
                float s = Mathf.Clamp01(t);
                float e = s * s * (3f - 2f * s);
                Vector3 p = Bez(e), ahead = Bez(Mathf.Min(1f, e + 0.03f));
                Vector3 v = ahead - p;
                transform.position = p;
                if (Vector3.ProjectOnPlane(v, Vector3.up).sqrMagnitude > 1e-6f) yaw = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
                spread = Mathf.MoveTowards(spread, s < 0.9f ? 1f : 0f, dt * 4f);
                pitch = Mathf.MoveTowards(pitch, s < 0.88f ? flightPitch : 0f, dt * 160f);
                if (t >= 1f)
                {
                    flying = false;
                    transform.position = b;
                    restT = Random.Range(restlessness.x, restlessness.y);
                    lookYaw = yaw + Random.Range(-90f, 90f);
                }
            }
            else
            {
                spread = Mathf.MoveTowards(spread, 0f, dt * 4f);
                pitch = Mathf.MoveTowards(pitch, 0f, dt * 160f);
                if (perch >= 0 && perches[perch] != null) transform.position = perches[perch].position;
                // sharp little turns of the head (the whole bird, PSX style) and a hop round now and then
                if ((lookT -= dt) <= 0f)
                {
                    lookYaw = Random.value < 0.15f ? yaw + 180f : yaw + Random.Range(-60f, 60f);
                    lookT = Random.Range(0.6f, 3.2f);
                }
                yaw = Mathf.MoveTowardsAngle(yaw, lookYaw, 600f * dt);
                bob = Mathf.Max(0f, bob - dt * 3f);
                if (Random.value < dt * 0.25f) bob = 1f;
                if (Random.value < dt * 0.08f) ruffle = 0.5f;
                if ((restT -= dt) <= 0f || Threat() < spookDistance)
                {
                    if (!TakeOff()) restT = Random.Range(3f, 8f);
                }
            }
            ruffle = Mathf.Max(0f, ruffle - dt);

            float tilt = pitch * pitchSign + Mathf.Sin(bob * Mathf.PI) * 9f * pitchSign;
            transform.rotation = Quaternion.Euler(0, yaw, 0) * modelFix * Quaternion.AngleAxis(tilt, sideLocal);

            // wings: swung out and beating in flight (a glide in the middle of long flights), a shiver at rest
            float beat = flying ? Mathf.Sin(now * 9f * Mathf.PI * 2f * 0.5f) * 50f * (t > 0.35f && t < 0.6f && dur > 3f ? 0.3f : 1f) : 0f;
            float shiver = ruffle > 0f ? Mathf.Sin(now * 60f) * 6f * ruffle : 0f;
            if (wingL) wingL.localRotation = Quaternion.AngleAxis((beat + shiver) * sideL, flapAxis) * Quaternion.Slerp(Quaternion.identity, spreadL, spread) * wingLRest;
            if (wingR) wingR.localRotation = Quaternion.AngleAxis((beat + shiver) * sideR, flapAxis) * Quaternion.Slerp(Quaternion.identity, spreadR, spread) * wingRRest;
        }

        Vector3 Bez(float s) => (1 - s) * (1 - s) * a + 2 * (1 - s) * s * c + s * s * b;
    }
}
