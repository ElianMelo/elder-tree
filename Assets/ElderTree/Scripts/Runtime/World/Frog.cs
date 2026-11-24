using ElderTree.Enemies;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree.World
{
    /// <summary>
    /// A forest frog: sits puffing its throat, turns, and hops off to a new spot near home in a short arc, back legs
    /// kicking out on take-off and folding as it lands. Anything big coming close (the player, a boss) sends it
    /// away in a chain of longer hops. It hops on whatever the ground ray hits. The model
    /// (Tools/Blender/build_forest.py Frog_*) has "LegL"/"LegR" (pivot at the hip), "Throat" and an empty "Front".
    /// </summary>
    public class Frog : MonoBehaviour
    {
        [Tooltip("How far from its home spot it wanders (m).")]
        public float roam = 4f;
        public float hopDistance = 1.0f;
        public float hopHeight = 0.35f;
        public float hopTime = 0.38f;
        public float fleeDistance = 3.2f;
        public float turnSpeed = 420f;

        static readonly RaycastHit[] hits = new RaycastHit[4];
        static PlayerController player;
        static BossBase[] bosses;
        static float lastBossScan = -99f;

        Transform legL, legR, throat;
        Quaternion legLRest, legRRest;
        Vector3 throatRest = Vector3.one;
        Vector3 frontLocal = Vector3.forward, sideLocal = Vector3.right;
        float kickL = 1f, kickR = 1f;
        Quaternion modelFix = Quaternion.identity;
        Vector3 home, from, to, hopDir;
        float idle, hopT = -1f, yaw, croak, croakT, pitch, hopLen, hopH, hopDur;
        int fleeHops;

        void Start()
        {
            home = transform.position;
            foreach (var t in GetComponentsInChildren<Transform>())
            {
                if (t.name == "LegL") { legL = t; legLRest = t.localRotation; }
                else if (t.name == "LegR") { legR = t; legRRest = t.localRotation; }
                else if (t.name == "Throat") { throat = t; throatRest = t.localScale; }
                else if (t.name == "Front") frontLocal = Vector3.ProjectOnPlane(transform.InverseTransformPoint(t.position), Vector3.up).normalized;
            }
            if (frontLocal.sqrMagnitude < 0.5f) frontLocal = Vector3.forward;
            sideLocal = Vector3.Cross(Vector3.up, frontLocal).normalized;
            modelFix = Vector3.Dot(frontLocal, Vector3.forward) < -0.9f ? Quaternion.Euler(0, 180f, 0) : Quaternion.FromToRotation(frontLocal, Vector3.forward);
            kickL = KickSign(legL);
            kickR = KickSign(legR);
            yaw = transform.eulerAngles.y;
            idle = Random.Range(0.5f, 4f);
            croakT = Random.Range(1f, 6f);
            if (Ground(transform.position, out float y)) transform.position = transform.position.WithY(y);
        }

        /// <summary>Which way round the side axis swings this leg's foot backward (the kick).</summary>
        float KickSign(Transform leg)
        {
            if (leg == null) return 1f;
            var mf = leg.GetComponent<MeshFilter>();
            Vector3 c = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.center : Vector3.zero;
            Vector3 pivot = leg.localPosition;
            Vector3 p = transform.InverseTransformPoint(leg.TransformPoint(c)) - pivot;
            Vector3 q = Quaternion.AngleAxis(25f, sideLocal) * p;
            return Vector3.Dot(q - p, -frontLocal) >= 0f ? 1f : -1f;
        }

        bool Ground(Vector3 p, out float y)
        {
            y = p.y;
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 1.5f, Vector3.down, hits, 4f, 1, QueryTriggerInteraction.Ignore);
            float best = float.MinValue;
            for (int i = 0; i < n; i++) if (hits[i].point.y > best) best = hits[i].point.y;
            if (best == float.MinValue) return false;
            y = best;
            return true;
        }

        Vector3 Threat(out float dist)
        {
            dist = float.MaxValue;
            Vector3 src = Vector3.zero;
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (player != null)
            {
                float d = Vector3.Distance(player.transform.position, transform.position);
                if (d < dist) { dist = d; src = player.transform.position; }
            }
            if (Time.time - lastBossScan > 2f) { bosses = FindObjectsByType<BossBase>(FindObjectsSortMode.None); lastBossScan = Time.time; }
            if (bosses != null)
                foreach (var b in bosses)
                {
                    if (b == null || !b.isActiveAndEnabled) continue;
                    float d = Vector3.Distance(b.transform.position, transform.position) - 3f;
                    if (d < dist) { dist = d; src = b.transform.position; }
                }
            return src;
        }

        void StartHop(Vector3 dir, float dist, float height, float dur)
        {
            dir = Vector3.ProjectOnPlane(dir, Vector3.up).normalized;
            if (dir.sqrMagnitude < 0.01f) return;
            Vector3 target = transform.position + dir * dist;
            if (!Ground(target, out float y)) return;
            if (Mathf.Abs(y - transform.position.y) > 0.8f) return;      // no leaping up walls
            from = transform.position;
            to = target.WithY(y);
            hopDir = dir;
            hopT = 0f; hopLen = dist; hopH = height; hopDur = dur;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (hopT >= 0f)
            {
                hopT += dt / hopDur;
                float t = Mathf.Clamp01(hopT);
                transform.position = Vector3.Lerp(from, to, t) + Vector3.up * (4f * hopH * t * (1f - t));
                pitch = Mathf.Lerp(28f, -18f, t);                                   // nose up off the ground, down to land
                yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(hopDir.x, hopDir.z) * Mathf.Rad2Deg, turnSpeed * dt);
                float kick = Mathf.Sin(Mathf.PI * Mathf.Min(1f, t * 1.7f));          // legs shoot back, then fold in for the landing
                Legs(kick * 75f);
                if (hopT >= 1f)
                {
                    hopT = -1f;
                    pitch = 0f;
                    Legs(0f);
                    idle = fleeHops > 0 ? 0.06f : Random.Range(1.2f, 5f);
                }
            }
            else
            {
                pitch = Mathf.MoveTowards(pitch, 0f, 120f * dt);
                var threat = Threat(out float td);
                if (td < fleeDistance && fleeHops == 0)
                {
                    fleeHops = Random.Range(2, 4);
                    idle = 0f;
                }
                if ((idle -= dt) <= 0f)
                {
                    Vector3 dir;
                    if (fleeHops > 0)
                    {
                        fleeHops--;
                        dir = Quaternion.Euler(0, Random.Range(-40f, 40f), 0) * Vector3.ProjectOnPlane(transform.position - threat, Vector3.up);
                        if (dir.sqrMagnitude < 0.01f) dir = Random.insideUnitSphere;
                        StartHop(dir, hopDistance * Random.Range(1.6f, 2.2f), hopHeight * 1.4f, hopTime * 1.2f);
                    }
                    else
                    {
                        // wander, drifting back toward home when it strays
                        Vector2 c = Random.insideUnitCircle;
                        dir = new Vector3(c.x, 0, c.y);
                        Vector3 back = home - transform.position; back.y = 0;
                        if (back.magnitude > roam) dir = back.normalized + dir * 0.3f;
                        StartHop(dir, hopDistance * Random.Range(0.7f, 1.2f), hopHeight, hopTime);
                    }
                    if (hopT < 0f) idle = Random.Range(0.4f, 1.2f);
                }
                // the throat: now and then a run of quick puffs
                if ((croakT -= dt) <= 0f) { croak = Random.Range(1.2f, 2.4f); croakT = Random.Range(4f, 11f); }
            }
            if (croak > 0f) croak -= dt;
            if (throat != null)
            {
                float puff = croak > 0f ? Mathf.Max(0f, Mathf.Sin(Time.time * 16f)) * 0.9f : 0.06f * Mathf.Sin(Time.time * 3f + home.x);
                throat.localScale = throatRest * (1f + puff);
            }
            transform.rotation = Quaternion.Euler(0, yaw, 0) * Quaternion.AngleAxis(-pitch, Vector3.right) * modelFix;
        }

        void Legs(float angle)
        {
            if (legL) legL.localRotation = Quaternion.AngleAxis(angle * kickL, sideLocal) * legLRest;
            if (legR) legR.localRotation = Quaternion.AngleAxis(angle * kickR, sideLocal) * legRRest;
        }
    }
}
