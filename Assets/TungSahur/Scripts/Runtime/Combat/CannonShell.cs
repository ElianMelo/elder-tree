using TungSahur.Equipment;
using UnityEngine;

namespace TungSahur.Combat
{
    /// <summary>
    /// A shell from the Crocodilo cannon: a little drop bomb fired nose-first. Ballistic flight stepped
    /// with raycasts; on contact the thing it hits takes the direct damage, then it explodes
    /// (<see cref="Explosions"/>). Self-detonates at the end of its range.
    /// </summary>
    public class CannonShell : MonoBehaviour
    {
        public LayerMask hitMask = (1 << 0) | (1 << 9) | (1 << 10);
        public TrailRenderer trail;
        public Transform model;
        public float spin = 720f;

        public static event System.Action<CannonShell, IDamageable, DamageInfo> HitTarget;

        static readonly RaycastHit[] hits = new RaycastHit[16];
        Vector3 velocity;
        float gravity, travelled, range;
        CannonData data;
        Transform owner;
        bool flying;

        public void Launch(Vector3 pos, Vector3 vel, CannonData d, Transform shooter)
        {
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(vel));
            velocity = vel;
            data = d;
            gravity = d.shellGravity;
            range = d.range;
            owner = shooter;
            travelled = 0f;
            flying = true;
            if (trail) { trail.Clear(); trail.emitting = true; }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (!flying || dt <= 0f) return;
            velocity += Vector3.down * gravity * dt;
            Vector3 step = velocity * dt;
            float dist = step.magnitude;
            if (dist > 1e-5f && FindHit(transform.position, step / dist, dist, out var hit, out var target))
            {
                Impact(hit.point, hit.normal, target, hit.collider);
                return;
            }
            transform.position += step;
            travelled += dist;
            if (velocity.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(velocity);
            if (model) model.Rotate(Vector3.forward, spin * dt, Space.Self);
            if (travelled > range) Impact(transform.position, -velocity.normalized, null, null);
        }

        bool FindHit(Vector3 from, Vector3 dir, float dist, out RaycastHit best, out IDamageable target)
        {
            best = default;
            target = null;
            int n = Physics.SphereCastNonAlloc(from, 0.12f, dir, hits, dist, hitMask, QueryTriggerInteraction.Collide);
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
                    target = hb != null ? hb.Owner : h.collider.GetComponentInParent<IDamageable>();
                }
            }
            if (bestD < float.MaxValue && best.point == Vector3.zero) best.point = from + dir * bestD;
            return bestD < float.MaxValue;
        }

        void Impact(Vector3 point, Vector3 normal, IDamageable target, Collider col)
        {
            flying = false;
            if (trail) { trail.emitting = false; trail.transform.SetParent(null, true); Destroy(trail.gameObject, trail.time + 0.2f); }
            Vector3 dir = velocity.sqrMagnitude > 0.01f ? velocity.normalized : transform.forward;
            if (target != null && target.IsAlive)
            {
                Vector3 push = dir; push.y = 0f;
                var info = new DamageInfo
                {
                    damage = Mathf.Round(data.directDamage * Random.Range(0.96f, 1.04f)),
                    poiseDamage = data.directPoise,
                    point = point,
                    direction = push.sqrMagnitude > 1e-4f ? push.normalized : dir,
                    weight = ImpactWeight.Heavy,
                    charged = true,
                    breakPower = data.breakPower,
                    source = owner != null ? owner.gameObject : null
                };
                if (target.ReceiveHit(info)) HitTarget?.Invoke(this, target, info);
            }
            Explosions.Detonate(new Explosions.Blast
            {
                center = point + normal * 0.15f, radius = data.explosionRadius, damage = data.explosionDamage,
                poise = data.explosionPoise, breakPower = data.breakPower, fxScale = data.explosionFxScale,
                weight = ImpactWeight.Heavy, source = owner != null ? owner.gameObject : null
            }, target);
            Destroy(gameObject);
        }
    }
}
