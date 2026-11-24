using ElderTree.Enemies;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree.World
{
    /// <summary>
    /// A shore crab: idles, then scuttles sideways to a new spot near home, stopping now and then to wave its
    /// claws. Anything big coming close (the player, a boss) sends it skittering away fast. It walks on whatever
    /// the ground ray hits (sand, the dock) and never steps into the sea. Legs and claws are moved here; the model
    /// (Tools/Blender/build_beach.py) has them as children "Leg0".."Leg5", "ClawL", "ClawR" with the pivot at the
    /// joint, and an empty "Front".
    /// </summary>
    public class Crab : MonoBehaviour
    {
        public float walkSpeed = 0.8f;
        public float fleeSpeed = 3.4f;
        [Tooltip("How far from its home spot it wanders (m).")]
        public float roam = 4.5f;
        public float fleeDistance = 3.2f;
        [Tooltip("It never walks where the ground is below this height (the sea).")]
        public float minGroundY = -0.85f;
        public float turnSpeed = 260f;

        static readonly RaycastHit[] hits = new RaycastHit[4];
        static PlayerController player;
        static BossBase[] bosses;
        static float lastBossScan = -99f;

        Transform[] legs = new Transform[6];
        Quaternion[] legRest = new Quaternion[6];
        float[] legSide = new float[6];
        Transform clawL, clawR;
        Quaternion clawLRest, clawRRest;
        Vector3 frontLocal = Vector3.forward;     // model front in the root's space
        Vector3 home, target;
        float idle, gait, speed, wave, waveT, fleeT;
        bool moving;

        void Start()
        {
            home = transform.position;
            target = home;
            idle = Random.Range(0.2f, 3f);
            foreach (var t in GetComponentsInChildren<Transform>())
            {
                if (t.name.StartsWith("Leg") && t.name.Length == 4 && int.TryParse(t.name.Substring(3), out int i) && i < 6)
                {
                    legs[i] = t;
                    legRest[i] = t.localRotation;
                    legSide[i] = Mathf.Sign(transform.InverseTransformPoint(t.position).x + 1e-4f);
                }
                else if (t.name == "ClawL") { clawL = t; clawLRest = t.localRotation; }
                else if (t.name == "ClawR") { clawR = t; clawRRest = t.localRotation; }
                else if (t.name == "Front") frontLocal = Vector3.ProjectOnPlane(transform.InverseTransformPoint(t.position), Vector3.up).normalized;
            }
            if (frontLocal.sqrMagnitude < 0.5f) frontLocal = Vector3.forward;
            gait = Random.value * 10f;
            Ground(transform.position, out var y);
            transform.position = transform.position.WithY(y);
        }

        bool Ground(Vector3 p, out float y)
        {
            y = p.y;
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 1.5f, Vector3.down, hits, 4f, 1, QueryTriggerInteraction.Ignore);
            float best = float.MinValue;
            for (int i = 0; i < n; i++) if (hits[i].point.y > best) best = hits[i].point.y;
            if (best == float.MinValue) return false;
            y = best;
            return best > minGroundY;
        }

        Vector3 Threat(out float dist)
        {
            dist = float.MaxValue;
            Vector3 from = Vector3.zero;
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (player != null)
            {
                float d = Vector3.Distance(player.transform.position, transform.position);
                if (d < dist) { dist = d; from = player.transform.position; }
            }
            if (Time.time - lastBossScan > 2f) { bosses = FindObjectsByType<BossBase>(FindObjectsSortMode.None); lastBossScan = Time.time; }
            if (bosses != null)
                foreach (var b in bosses)
                {
                    if (b == null || !b.isActiveAndEnabled) continue;
                    float d = Vector3.Distance(b.transform.position, transform.position) - 2.5f;
                    if (d < dist) { dist = d; from = b.transform.position; }
                }
            return from;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector3 pos = transform.position;
            var threat = Threat(out float td);
            if (td < fleeDistance)
            {
                Vector3 away = Vector3.ProjectOnPlane(pos - threat, Vector3.up).normalized;
                if (away.sqrMagnitude < 0.01f) away = transform.right;
                away = Quaternion.Euler(0, Random.Range(-35f, 35f), 0) * away;
                target = pos + away * Random.Range(2.5f, 4f);
                fleeT = 1.2f;
                moving = true;
            }
            else if (!moving && (idle -= dt) <= 0f)
            {
                for (int k = 0; k < 6; k++)
                {
                    Vector2 c = Random.insideUnitCircle * roam;
                    var spot = home + new Vector3(c.x, 0, c.y);
                    if (Ground(spot, out _)) { target = spot; moving = true; break; }
                }
                idle = Random.Range(1.5f, 5f);
            }
            if (fleeT > 0f) fleeT -= dt;

            float want = 0f;
            if (moving)
            {
                Vector3 to = Vector3.ProjectOnPlane(target - pos, Vector3.up);
                if (to.magnitude < 0.15f) { moving = false; waveT = Random.value < 0.35f ? 1.2f : 0f; }
                else
                {
                    // crabs walk sideways: turn whichever side is nearer to face the way
                    Vector3 side = transform.rotation * Vector3.Cross(Vector3.up, frontLocal);
                    Vector3 dir = to.normalized;
                    float s = Vector3.Dot(side, dir) >= 0f ? 1f : -1f;
                    Quaternion look = Quaternion.LookRotation(Vector3.Cross(dir * s, Vector3.up), Vector3.up);   // front such that side = dir * s
                    var modelFix = Quaternion.FromToRotation(frontLocal, Vector3.forward);
                    if (Vector3.Dot(frontLocal, Vector3.forward) < -0.9f) modelFix = Quaternion.Euler(0, 180f, 0);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, look * modelFix, turnSpeed * dt);
                    float aligned = Mathf.Abs(Vector3.Dot(transform.rotation * Vector3.Cross(Vector3.up, frontLocal), dir));
                    want = (fleeT > 0f ? fleeSpeed : walkSpeed) * Mathf.Clamp01((aligned - 0.5f) * 2f);
                }
            }
            speed = Mathf.MoveTowards(speed, want, 12f * dt);
            if (speed > 0.01f)
            {
                Vector3 step = Vector3.ProjectOnPlane(target - pos, Vector3.up).normalized * speed * dt;
                if (Ground(pos + step, out float y)) pos = (pos + step).WithY(Mathf.Lerp(pos.y, y, 0.5f));
                else { moving = false; speed = 0f; target = pos; }
                transform.position = pos;
            }

            // legs: alternating tripod gait, faster the faster it goes
            gait += dt * (4f + speed * 16f) * (speed > 0.05f ? 1f : 0.15f);
            float amp = Mathf.Clamp01(speed / walkSpeed) * 22f + 2f;
            Vector3 frontP = frontLocal;
            for (int i = 0; i < 6; i++)
            {
                if (legs[i] == null) continue;
                float ph = gait + ((i % 2 == 0) ^ (i >= 3) ? 0f : Mathf.PI);
                float lift = Mathf.Max(0f, Mathf.Sin(ph)) * amp * legSide[i];
                float swing = Mathf.Cos(ph) * amp * 0.7f;
                legs[i].localRotation = Quaternion.AngleAxis(lift, frontP) * Quaternion.AngleAxis(swing, Vector3.up) * legRest[i];
            }
            // claws: a slow idle sway, raised and waved after it stops, now and then
            if (waveT > 0f) waveT -= dt;
            wave = Mathf.MoveTowards(wave, waveT > 0f ? 1f : 0f, dt * 3f);
            float t = Time.time * 2.3f + gait * 0.1f;
            Vector3 sideP = Vector3.Cross(Vector3.up, frontLocal);
            if (clawL) clawL.localRotation = Quaternion.AngleAxis(-wave * 38f - Mathf.Sin(t * 3f) * 10f * wave - Mathf.Sin(t) * 3f, sideP) * clawLRest;
            if (clawR) clawR.localRotation = Quaternion.AngleAxis(-wave * 38f - Mathf.Sin(t * 3f + 1.4f) * 10f * wave - Mathf.Sin(t + 2f) * 3f, sideP) * clawRRest;
        }
    }
}
