using System.Collections.Generic;
using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.Equipment;
using ElderTree.FX;
using UnityEngine;

namespace ElderTree.Combat
{
    /// <summary>
    /// Vine Seeker (Druid Mask sorcery): a bundle of vines that writhes through the air. The head flies like a homing
    /// sorcery (straight for a moment, then it steers onto its target, turning harder the longer it flies). The vines
    /// leave the staff together, bow apart from each other mid-flight (each on its own side of the path, writhing),
    /// and close in again onto the target like grasping tendrils, so the spell reads even seen from behind. Where it hits,
    /// leaves explode and a <see cref="VineBurst"/> of vines grows out of the spot and squeezes.
    /// Strands are LineRenderers drawn with the leafy vine strip (M_DruidVine, cutout, camera-facing).
    /// </summary>
    public class VineSeeker : MonoBehaviour
    {
        [Tooltip("One strand: cloned per vine in the bundle.")]
        public LineRenderer strandTemplate;
        [Tooltip("Sheds leaves along the flight (world space).")]
        public ParticleSystem leafTrail;
        public Light glowLight;
        public VineBurst burstPrefab;
        public LayerMask hitMask = (1 << 0) | (1 << 9) | (1 << 10);
        public float hitRadius = 0.3f;
        [Tooltip("Rendered points per strand, and their spacing along the path (metres).")]
        public int points = 24;
        public float spacing = 0.17f;
        [Tooltip("Small braid of each vine around its own line (metres).")]
        public float bundleRadius = 0.08f;
        [Tooltip("How far the vines bow apart mid-flight: this share of the flight length, clamped (metres).")]
        public float fanShare = 0.11f;
        public Vector2 fanClamp = new Vector2(0.35f, 1.1f);
        [Tooltip("Without a target, the flight length the fan is shaped for (metres).")]
        public float freeFlight = 14f;
        [Tooltip("Braid twist (radians per metre) and spin (radians per second).")]
        public float twist = 3.2f;
        public float spin = 7f;
        [Tooltip("Strand width (the leafy strip: the stalk is the middle fifth, leaves sprout to the edges).")]
        public float width = 0.34f;

        /// <summary>Raised when the vines land on something that takes damage (HUD hit marker).</summary>
        public static event System.Action<VineSeeker, IDamageable, DamageInfo> HitTarget;

        static readonly RaycastHit[] hits = new RaycastHit[16];
        readonly List<Vector3> path = new();      // head positions, newest last
        LineRenderer[] strands = System.Array.Empty<LineRenderer>();
        Vector3[] buf = System.Array.Empty<Vector3>();
        SpellData data;
        Transform owner;
        Targetable target;
        Vector3 dir;
        float speed, age, power, damageMul, scale = 1f, witherT = -1f, lightBase, travelled;
        float[] fanAng = System.Array.Empty<float>();
        bool flying;

        public float Power => power;

        /// <summary>power: 0 for a light cast, up to 1 for a full charge (bigger, brighter, thicker bundle).</summary>
        public void Launch(Vector3 pos, Vector3 direction, SpellData d, float charge, int strandCount, float dmgMul, Targetable seek, Transform shooter)
        {
            data = d;
            owner = shooter;
            target = seek;
            power = Mathf.Clamp01(charge);
            damageMul = dmgMul;
            scale = Mathf.Lerp(1f, 1.45f, power);
            dir = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            speed = d.launchSpeed;
            age = 0f;
            flying = true;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir));
            path.Clear();
            path.Add(pos - dir * 0.02f);
            path.Add(pos);

            strandCount = Mathf.Max(1, strandCount);
            strands = new LineRenderer[strandCount];
            fanAng = new float[strandCount];
            float a0 = Random.value * Mathf.PI * 2f;
            for (int i = 0; i < strandCount; i++) fanAng[i] = a0 + i * Mathf.PI * 2f / strandCount + Random.Range(-0.3f, 0.3f);
            travelled = 0f;
            buf = new Vector3[points];
            for (int i = 0; i < strandCount; i++)
            {
                var lr = i == 0 ? strandTemplate : Instantiate(strandTemplate, strandTemplate.transform.parent);
                lr.useWorldSpace = true;
                lr.positionCount = 0;
                lr.widthMultiplier = width * scale * (i == 0 ? 1.15f : Random.Range(0.8f, 1f));
                strands[i] = lr;
            }
            if (glowLight)
            {
                lightBase = glowLight.intensity * Mathf.Lerp(1f, 1.6f, power);
                glowLight.range *= Mathf.Lerp(1f, 1.4f, power);
            }
            if (leafTrail) { leafTrail.Clear(); leafTrail.Play(); }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || data == null) return;
            age += dt;
            if (witherT >= 0f) { Wither(dt); return; }
            if (!flying) return;

            speed = Mathf.Min(data.maxSpeed, speed + data.acceleration * dt);
            if (age > data.homingDelay && target != null && target.CanBeTargeted)
            {
                Vector3 want = (target.LockPosition - transform.position).normalized;
                float k = Mathf.Clamp01((age - data.homingDelay) / 0.9f);
                float turn = Mathf.Lerp(data.turnRate, data.maxTurnRate, k) * Mathf.Deg2Rad * dt;
                dir = Vector3.RotateTowards(dir, want, turn, 0f).normalized;
            }
            // a little life in the flight: the head weaves as the vines pull at it
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            side.Normalize();
            Vector3 up = Vector3.Cross(side, dir);
            Vector3 weave = (side * Mathf.Sin(age * 9f) + up * Mathf.Cos(age * 7f)) * 0.9f * (1f - Mathf.Clamp01(age * 1.5f) * 0.6f);

            Vector3 step = (dir * speed + weave) * dt;
            float dist = step.magnitude;
            if (dist > 1e-5f && FindHit(transform.position, step / dist, dist, out var hit, out var t))
            {
                Impact(hit.point, hit.normal, t, hit.collider);
                return;
            }
            transform.position += step;
            travelled += dist;
            transform.rotation = Quaternion.LookRotation(dir);
            path.Add(transform.position);
            float keep = points * spacing + 0.5f;
            // trim the path history to what the strands can use
            float len = 0f;
            for (int i = path.Count - 1; i > 0; i--)
            {
                len += Vector3.Distance(path[i], path[i - 1]);
                if (len > keep) { path.RemoveRange(0, i - 1); break; }
            }
            DrawStrands(1f);
            if (glowLight) glowLight.intensity = lightBase * (0.85f + 0.15f * Mathf.Sin(age * 23f));
            if (age > data.lifetime) BeginWither();
        }

        /// <summary>Sample the path at arc length s back from the head (position + tangent).</summary>
        bool Sample(float s, out Vector3 p, out Vector3 tangent)
        {
            float acc = 0f;
            for (int i = path.Count - 1; i > 0; i--)
            {
                Vector3 a = path[i], b = path[i - 1];
                float seg = Vector3.Distance(a, b);
                if (acc + seg >= s && seg > 1e-5f)
                {
                    float t = (s - acc) / seg;
                    p = Vector3.Lerp(a, b, t);
                    tangent = (a - b) / seg;
                    return true;
                }
                acc += seg;
            }
            p = path.Count > 0 ? path[0] : transform.position;
            tangent = dir;
            return false;
        }

        void DrawStrands(float widthK)
        {
            int n = strands.Length;
            // the fan: each vine bows out to its own side of the path and closes back in onto the target. Progress along
            // the flight is measured against what is left to fly, so the vines meet exactly where the head lands.
            float remaining = target != null && target.CanBeTargeted ? Vector3.Distance(transform.position, target.LockPosition)
                : Mathf.Max(0.5f, freeFlight - travelled);
            float total = Mathf.Max(1f, travelled + (flying ? remaining : 0f));
            float fan = Mathf.Clamp(total * fanShare, fanClamp.x, fanClamp.y) * scale;
            for (int i = 0; i < n; i++)
            {
                var lr = strands[i];
                float phase = i * Mathf.PI * 2f / n + i * 0.37f;
                int count = 0;
                for (int k = 0; k < points; k++)
                {
                    float s = k * spacing * scale;
                    bool ok = Sample(s, out var p, out var tg);
                    if (!ok && k > 1) break;
                    Vector3 sd = Vector3.Cross(tg, Vector3.up);
                    if (sd.sqrMagnitude < 1e-4f) sd = Vector3.right;
                    sd.Normalize();
                    Vector3 upv = Vector3.Cross(sd, tg);
                    float prog = Mathf.Clamp01((travelled - s) / total);
                    float bow = fan * Mathf.Sin(prog * Mathf.PI) * (1f + 0.15f * Mathf.Sin(age * 5f + i * 1.7f));
                    float fa = fanAng[i] + s * 0.35f;
                    // and each vine writhes around its own line
                    float env = Mathf.Sin(Mathf.Clamp01(k / (points * 0.3f)) * Mathf.PI * 0.5f);
                    float r = bundleRadius * scale * env * (1f + 0.35f * Mathf.Sin(age * 6f + k * 0.7f + i * 2.1f));
                    float ang = phase + s * twist - age * spin;
                    buf[count++] = p + (sd * Mathf.Cos(fa) + upv * Mathf.Sin(fa)) * bow + (sd * Mathf.Cos(ang) + upv * Mathf.Sin(ang)) * r;
                }
                lr.positionCount = count;
                lr.SetPositions(buf);
                lr.widthMultiplier = width * scale * widthK * (i == 0 ? 1.15f : 0.9f);
            }
        }

        bool FindHit(Vector3 from, Vector3 d, float dist, out RaycastHit best, out IDamageable t)
        {
            best = default;
            t = null;
            int n = Physics.SphereCastNonAlloc(from, hitRadius * scale, d, hits, dist, hitMask, QueryTriggerInteraction.Collide);
            float bestD = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (owner != null && h.collider.transform.IsChildOf(owner)) continue;
                var hb = h.collider.GetComponent<Hurtbox>();
                if (h.collider.isTrigger && hb == null) continue;
                if (h.distance < bestD)
                {
                    bestD = h.distance;
                    best = h;
                    t = hb != null ? hb.Owner : h.collider.GetComponentInParent<IDamageable>();
                }
            }
            if (bestD < float.MaxValue && best.point == Vector3.zero) best.point = from + d * bestD;
            if (bestD < float.MaxValue && best.normal == Vector3.zero) best.normal = -d;
            return bestD < float.MaxValue;
        }

        void Impact(Vector3 point, Vector3 normal, IDamageable t, Collider col)
        {
            flying = false;
            if (t != null && t.IsAlive)
            {
                Vector3 push = dir; push.y = 0f;
                var info = new DamageInfo
                {
                    damage = Mathf.Round(data.damage * damageMul * Random.Range(0.96f, 1.04f)),
                    poiseDamage = data.poise * Mathf.Lerp(1f, 1.6f, power),
                    point = point,
                    direction = push.sqrMagnitude > 1e-4f ? push.normalized : dir,
                    weight = power > 0.45f ? ImpactWeight.Heavy : ImpactWeight.Light,
                    charged = power > 0.45f,
                    source = owner != null ? owner.gameObject : null
                };
                if (t.ReceiveHit(info)) HitTarget?.Invoke(this, t, info);
            }
            float fx = Mathf.Lerp(1f, 1.6f, power);
            FXManager.Spawn(FXType.LeafExplosion, point + normal * 0.1f, Quaternion.LookRotation(normal), fx);
            AudioManager.Play(Sfx.VineBurst, point, 1f, Random.Range(0.92f, 1.06f) / Mathf.Sqrt(fx));
            CameraDirector.Instance?.Shake(data.shake * fx, dir);
            if (t != null) TimeDirector.HitStop(data.hitStop * fx);
            if (burstPrefab != null)
            {
                var b = Instantiate(burstPrefab);
                // vines grow out of whatever was struck and ride along with it (a moving boss keeps them)
                Transform stick = col != null && t != null ? col.transform : null;
                b.Begin(point, normal, stick, data, damageMul, power, t, owner);
            }
            BeginWither(true);
        }

        void BeginWither(bool hit = false)
        {
            flying = false;
            witherT = 0f;
            if (leafTrail) leafTrail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (!hit) FXManager.Spawn(FXType.LeafFall, transform.position, Quaternion.identity, 0.8f);
        }

        /// <summary>The bundle snaps back into the head and withers away (the trail of leaves finishes falling).</summary>
        void Wither(float dt)
        {
            witherT += dt;
            float k = Mathf.Clamp01(witherT / 0.22f);
            // the tail catches up with the head
            if (path.Count > 2) path.RemoveAt(0);
            DrawStrands(1f - k);
            if (glowLight) glowLight.intensity = lightBase * (1f - k) * (1f + 2f * (1f - k));
            if (k >= 1f)
            {
                foreach (var s in strands) if (s) s.enabled = false;
                if (glowLight) glowLight.enabled = false;
                if (leafTrail == null || !leafTrail.IsAlive(true)) Destroy(gameObject);
            }
        }
    }
}
