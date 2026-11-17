using System.Collections.Generic;
using TungSahur.Audio;
using TungSahur.FX;
using UnityEngine;

namespace TungSahur.Combat
{
    /// <summary>
    /// A loosed arrow: ballistic flight stepped with raycasts (no tunnelling at any speed), damages
    /// whatever owns the hurtbox it meets, then sticks in with a short quiver. Stuck arrows ride
    /// along with what they hit (a wobbling dummy carries them) and are recycled oldest-first.
    /// The model's origin is the nock and it points along +Z.
    /// </summary>
    public class ArrowProjectile : MonoBehaviour
    {
        public float length = 0.80f;
        public LayerMask hitMask = (1 << 0) | (1 << 9) | (1 << 10);
        public TrailRenderer trail;
        public float maxFlightTime = 6f;
        public float stuckLifetime = 25f;
        public const int MaxStuck = 40;

        /// <summary>Raised when an arrow lands on something damageable (HUD hit marker, rumble).</summary>
        public static event System.Action<ArrowProjectile, IDamageable, DamageInfo> HitTarget;

        public bool FullDraw { get; private set; }
        public float Power { get; private set; }

        static readonly List<ArrowProjectile> stuck = new();
        static readonly RaycastHit[] hits = new RaycastHit[16];

        Vector3 velocity;
        float gravity, damage, poise, age;
        bool flying;
        Transform owner;
        // stuck wobble
        float wobbleT = -1f;
        Quaternion stuckLocalRot;
        Vector3 wobbleAxis;

        public void Launch(Vector3 nockPos, Vector3 vel, float gravityAccel, float dmg, float poiseDmg, float power, bool full, Transform shooter)
        {
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(nockPos, Quaternion.LookRotation(vel));
            velocity = vel;
            gravity = gravityAccel;
            damage = dmg;
            poise = poiseDmg;
            Power = power;
            FullDraw = full;
            owner = shooter;
            age = 0f;
            flying = true;
            if (trail)
            {
                trail.Clear();
                trail.emitting = true;
                trail.widthMultiplier = full ? 1.25f : 1f;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (flying) Fly(dt);
            else Stuck(dt);
        }

        void Fly(float dt)
        {
            age += dt;
            if (age > maxFlightTime) { Destroy(gameObject); return; }
            velocity += Vector3.down * gravity * dt;
            Vector3 tip = transform.position + transform.forward * length;
            Vector3 step = velocity * dt;
            float dist = step.magnitude;
            if (dist > 1e-5f && FindHit(tip, step / dist, dist, out var hit))
            {
                Impact(hit);
                return;
            }
            transform.position += step;
            if (velocity.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(velocity);
        }

        struct Hit
        {
            public Vector3 point, normal;
            public float distance;
            public Transform part;          // what the arrow sticks into
            public IDamageable target;
        }

        static readonly List<Renderer> renderers = new();

        bool FindHit(Vector3 from, Vector3 dir, float dist, out Hit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(from, dir, hits, dist, hitMask, QueryTriggerInteraction.Collide);
            float bestD = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (owner != null && h.collider.transform.IsChildOf(owner)) continue;
                var hb = h.collider.GetComponent<Hurtbox>();
                // triggers only count when they are hurtboxes (ignore pickup / audio volumes)
                if (h.collider.isTrigger && hb == null) continue;
                var target = hb != null ? hb.Owner : h.collider.GetComponentInParent<IDamageable>();
                if (target != null)
                {
                    // hurtboxes are generous capsules: only count it where the shaft meets the visible body,
                    // so arrows stick in the straw instead of hovering beside it (and clean misses stay misses)
                    Vector3 entry = from + dir * Mathf.Max(0f, h.distance - 0.02f);
                    if (!VisualHit(target, entry, dir, 1.6f, out var vp, out var vn, out var part)) continue;
                    float d = Vector3.Distance(from, vp);
                    if (d < bestD) { bestD = d; best = new Hit { point = vp, normal = vn, distance = d, part = part, target = target }; }
                    continue;
                }
                if (h.distance < bestD)
                {
                    bestD = h.distance;
                    best = new Hit { point = h.point, normal = h.normal, distance = h.distance, part = h.collider.transform };
                }
            }
            return bestD < float.MaxValue;
        }

        /// <summary>Ray vs the target's mesh parts, each approximated by the ellipsoid inscribed in its bounds.</summary>
        static bool VisualHit(IDamageable target, Vector3 from, Vector3 dir, float maxDist, out Vector3 point, out Vector3 normal, out Transform part)
        {
            point = normal = default;
            part = null;
            float best = maxDist;
            target.Root.GetComponentsInChildren(renderers);
            foreach (var r in renderers)
            {
                if (!r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                if (r.GetComponentInParent<ArrowProjectile>() != null) continue;
                var t = r.transform;
                Bounds lb = r.localBounds;
                Vector3 e = lb.extents;
                if (e.x < 1e-3f || e.y < 1e-3f || e.z < 1e-3f) continue;
                Vector3 o = t.InverseTransformPoint(from) - lb.center;
                Vector3 d = t.InverseTransformVector(dir);   // keeps world-distance parameterisation
                o = new Vector3(o.x / e.x, o.y / e.y, o.z / e.z);
                d = new Vector3(d.x / e.x, d.y / e.y, d.z / e.z);
                float a = Vector3.Dot(d, d), b = 2f * Vector3.Dot(o, d), c = Vector3.Dot(o, o) - 1f;
                float disc = b * b - 4f * a * c;
                if (disc < 0f || a < 1e-8f) continue;
                float sq = Mathf.Sqrt(disc);
                float s = (-b - sq) / (2f * a);
                if (s < 0f) s = (-b + sq) / (2f * a) < 0f ? -1f : 0f;   // starting inside: hit right here
                if (s < 0f || s >= best) continue;
                best = s;
                point = from + dir * s;
                Vector3 lp = o + d * s;   // unit-sphere space
                normal = t.TransformDirection(new Vector3(lp.x / e.x, lp.y / e.y, lp.z / e.z)).normalized;
                part = t;
            }
            return part != null;
        }

        void Impact(Hit hit)
        {
            flying = false;
            if (trail) trail.emitting = false;
            Vector3 dir = velocity.normalized;
            var target = hit.target;
            bool landed = false;

            if (target != null && target.IsAlive)
            {
                Vector3 push = dir; push.y = 0f;
                var info = new DamageInfo
                {
                    damage = Mathf.Round(damage * Random.Range(0.96f, 1.04f)),
                    poiseDamage = poise,
                    point = hit.point,
                    direction = push.sqrMagnitude > 1e-4f ? push.normalized : dir,
                    weight = FullDraw ? ImpactWeight.Heavy : ImpactWeight.Light,
                    charged = FullDraw,
                    source = owner != null ? owner.gameObject : null
                };
                landed = target.ReceiveHit(info);
                if (landed) HitTarget?.Invoke(this, target, info);
                AudioManager.Play(Sfx.ArrowHit, hit.point, 0.85f, 1.1f);
            }
            else
            {
                AudioManager.Play(Sfx.ArrowHit, hit.point, 0.9f, Random.Range(0.92f, 1.02f));
                FXManager.Spawn(FXType.DustSmall, hit.point, Quaternion.LookRotation(hit.normal), 0.45f);
                FXManager.Spawn(FXType.WoodChips, hit.point, Quaternion.LookRotation(Vector3.Reflect(dir, hit.normal) + hit.normal), 0.45f);
            }

            // bury the head: deeper in soft targets, shallower at a grazing angle
            float graze = Mathf.Clamp01(Vector3.Dot(-dir, hit.normal));
            float pen = (target != null ? 0.16f : 0.13f) * Mathf.Lerp(0.55f, 1f, graze) * Mathf.Lerp(0.75f, 1.15f, Power);
            transform.rotation = Quaternion.LookRotation(dir);
            transform.position = hit.point + dir * pen - dir * length;
            // ride along with what we hit (a wobbling dummy carries its arrows); odd scales on props: stay put
            var parent = hit.part;
            if (parent != null && (target != null || IsUniformish(parent.lossyScale))) transform.SetParent(parent, true);
            stuckLocalRot = transform.localRotation;
            wobbleAxis = Vector3.Cross(Vector3.forward, Random.onUnitSphere).normalized;
            if (wobbleAxis.sqrMagnitude < 0.1f) wobbleAxis = Vector3.right;
            wobbleT = 0f;
            age = 0f;
            Register();
        }

        static bool IsUniformish(Vector3 s) =>
            Mathf.Abs(s.x - s.y) < 0.02f * Mathf.Abs(s.x) + 0.02f && Mathf.Abs(s.x - s.z) < 0.02f * Mathf.Abs(s.x) + 0.02f;

        void Stuck(float dt)
        {
            age += dt;
            if (wobbleT >= 0f)
            {
                wobbleT += dt;
                float a = Mathf.Sin(wobbleT * 62f) * Mathf.Exp(-wobbleT * 9f) * 7f * Mathf.Lerp(0.6f, 1.2f, Power);
                transform.localRotation = stuckLocalRot * Quaternion.AngleAxis(a, wobbleAxis);
                if (wobbleT > 0.6f) { transform.localRotation = stuckLocalRot; wobbleT = -1f; }
            }
            if (age > stuckLifetime)
            {
                // sink away instead of popping
                transform.position -= transform.forward * dt * 0.5f;
                if (age > stuckLifetime + 1.2f) Destroy(gameObject);
            }
        }

        void Register()
        {
            stuck.Add(this);
            while (stuck.Count > MaxStuck)
            {
                var old = stuck[0];
                stuck.RemoveAt(0);
                if (old != null) Destroy(old.gameObject);
            }
        }

        void OnDestroy() => stuck.Remove(this);
    }
}
