using ElderTree.Enemies;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree.World
{
    /// <summary>
    /// A cave gnome watching the fight: its head follows the player (or the boss, whichever is nearer) within a
    /// neck's reach, it breathes, and now and then it waddles off to another spot near home and back. Anything big
    /// coming close sends a standing gnome scurrying away, hat bobbing; a sitting one just ducks and shivers.
    /// The model (Tools/Blender/build_cave.py Gnome_*) has a "Head" child (pivot at the neck), an empty "Front"
    /// and, on the lantern / pipe gnomes, a "Lantern" empty that the builder hangs a light on.
    /// </summary>
    public class Gnome : MonoBehaviour
    {
        public bool canWalk = true;
        [Tooltip("How far from its home spot it wanders (m).")]
        public float roam = 2.5f;
        public float walkSpeed = 0.7f;
        public float runSpeed = 2.4f;
        public float fleeDistance = 3.6f;
        public float lookRange = 16f;

        static readonly RaycastHit[] hits = new RaycastHit[4];
        static PlayerController player;
        static BossBase[] bosses;
        static float lastBossScan = -99f;

        Transform head;
        Quaternion headRestRel = Quaternion.identity;
        Vector3 frontLocal = Vector3.forward;
        Quaternion modelFix = Quaternion.identity;
        Vector3 home, target, baseScale;
        float yaw, idle, stepT, cower, seed, headYaw, headPitch;
        bool walking, running;

        void Start()
        {
            home = transform.position;
            baseScale = transform.localScale;
            foreach (var t in GetComponentsInChildren<Transform>())
            {
                if (t.name == "Head") head = t;
                else if (t.name == "Front") frontLocal = Vector3.ProjectOnPlane(transform.InverseTransformPoint(t.position), Vector3.up).normalized;
            }
            if (frontLocal.sqrMagnitude < 0.5f) frontLocal = Vector3.forward;
            modelFix = Vector3.Dot(frontLocal, Vector3.forward) < -0.9f ? Quaternion.Euler(0, 180f, 0) : Quaternion.FromToRotation(frontLocal, Vector3.forward);
            if (head != null) headRestRel = Quaternion.Inverse(transform.rotation) * head.rotation;
            { var fw = transform.rotation * frontLocal; yaw = Mathf.Atan2(fw.x, fw.z) * Mathf.Rad2Deg; }
            idle = Random.Range(2f, 8f);
            seed = Random.value * 100f;
        }

        bool Ground(Vector3 p, out float y)
        {
            y = p.y;
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 1.2f, Vector3.down, hits, 3f, 1, QueryTriggerInteraction.Ignore);
            float best = float.MinValue;
            for (int i = 0; i < n; i++) if (hits[i].point.y > best) best = hits[i].point.y;
            if (best == float.MinValue) return false;
            y = best;
            return true;
        }

        Vector3 Watch(out float dist, out float threatDist, out Vector3 threat)
        {
            dist = threatDist = float.MaxValue;
            Vector3 look = Vector3.zero;
            threat = Vector3.zero;
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (player != null)
            {
                float d = Vector3.Distance(player.transform.position, transform.position);
                dist = threatDist = d; look = player.transform.position + Vector3.up * 1.4f; threat = player.transform.position;
            }
            if (Time.time - lastBossScan > 2f) { bosses = FindObjectsByType<BossBase>(FindObjectsSortMode.None); lastBossScan = Time.time; }
            if (bosses != null)
                foreach (var b in bosses)
                {
                    if (b == null || !b.isActiveAndEnabled) continue;
                    float d = Vector3.Distance(b.transform.position, transform.position);
                    if (d - 3f < threatDist) { threatDist = d - 3f; threat = b.transform.position; }
                    if (d < dist * 0.7f) { dist = d; look = b.transform.position + Vector3.up * 1.6f; }
                }
            return look;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var look = Watch(out float lookDist, out float threatDist, out Vector3 threat);

            if (threatDist < fleeDistance)
            {
                if (canWalk && !running)
                {
                    Vector3 away = Vector3.ProjectOnPlane(transform.position - threat, Vector3.up).normalized;
                    if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere.WithY(0).normalized;
                    Vector3 to = transform.position + Quaternion.Euler(0, Random.Range(-35f, 35f), 0) * away * Random.Range(2.5f, 4f);
                    Vector3 off = to - home; off.y = 0;
                    if (off.magnitude > roam * 2f) to = home + off.normalized * roam * 2f;
                    target = to; walking = running = true;
                }
                if (!canWalk) cower = Mathf.MoveTowards(cower, 1f, dt * 6f);
            }
            else cower = Mathf.MoveTowards(cower, 0f, dt * 1.5f);

            if (walking)
            {
                Vector3 d = target - transform.position; d.y = 0;
                float speed = running ? runSpeed : walkSpeed;
                if (d.magnitude < 0.08f) { walking = running = false; idle = Random.Range(3f, 9f); }
                else
                {
                    Vector3 step = d.normalized * Mathf.Min(d.magnitude, speed * dt);
                    Vector3 p = transform.position + step;
                    if (Ground(p, out float gy) && Mathf.Abs(gy - transform.position.y) < 0.4f) transform.position = p.WithY(gy);
                    else { walking = running = false; idle = Random.Range(1f, 3f); }
                    yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, (running ? 720f : 300f) * dt);
                    stepT += dt * (running ? 16f : 9f);
                }
            }
            else if (canWalk && (idle -= dt) <= 0f)
            {
                Vector2 c = Random.insideUnitCircle * roam;
                target = home + new Vector3(c.x, 0, c.y);
                walking = true;
            }

            // waddle (rock side to side and bob on each step), breathe at rest, duck when cowering
            float roll = walking ? Mathf.Sin(stepT) * (running ? 9f : 6f) : 0f;
            
            float breathe = 1f + 0.018f * Mathf.Sin(Time.time * 2.1f + seed);
            float shiver = cower > 0f ? Mathf.Sin(Time.time * 47f + seed) * 2.5f * cower : 0f;
            transform.rotation = Quaternion.Euler(0, yaw + shiver, 0) * Quaternion.AngleAxis(roll, Vector3.forward) * modelFix;
            transform.localScale = new Vector3(baseScale.x, baseScale.y * breathe * (1f - 0.12f * cower), baseScale.z);

            if (head != null)
            {
                float wantYaw = 0f, wantPitch = 0f;
                if (lookDist < lookRange && !running)
                {
                    Vector3 dir = look - head.position;
                    Vector3 fwd = transform.rotation * frontLocal;
                    Vector3 flat = Vector3.ProjectOnPlane(dir, Vector3.up);
                    wantYaw = Mathf.Clamp(Vector3.SignedAngle(Vector3.ProjectOnPlane(fwd, Vector3.up), flat, Vector3.up), -75f, 75f);
                    wantPitch = Mathf.Clamp(-Mathf.Atan2(dir.y, flat.magnitude) * Mathf.Rad2Deg, -35f, 25f);
                }
                if (cower > 0.5f) { wantPitch = 18f; }
                headYaw = Mathf.MoveTowardsAngle(headYaw, wantYaw, 160f * dt);
                headPitch = Mathf.MoveTowardsAngle(headPitch, wantPitch, 120f * dt);
                Vector3 side = transform.rotation * Vector3.Cross(Vector3.up, frontLocal).normalized;
                head.rotation = Quaternion.AngleAxis(headYaw, Vector3.up) * Quaternion.AngleAxis(headPitch, side) * (transform.rotation * headRestRel);
            }
        }
    }
}
