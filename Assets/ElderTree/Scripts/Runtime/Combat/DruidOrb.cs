using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.Equipment;
using ElderTree.FX;
using UnityEngine;

namespace ElderTree.Combat
{
    /// <summary>
    /// The druid's energy ball (<see cref="OrbData"/>). It first gathers between the hands (<see cref="Hold"/>: grows,
    /// pulses, lights the druid's face), then <see cref="Launch"/> sends it flying: slow, big, glowing, lighting the
    /// ground it passes over, two leafy vine rings spinning around the core. On impact it bursts in a green flash:
    /// direct damage to what it hit plus a splash.
    /// </summary>
    public class DruidOrb : MonoBehaviour
    {
        public Transform core;
        public Transform[] rings = System.Array.Empty<Transform>();
        public Renderer glowSprite;
        public Light glow;
        public TrailRenderer trail;
        [Tooltip("Motes and leaves shed while it flies (world space).")]
        public ParticleSystem wisps;
        public LayerMask hitMask = (1 << 0) | (1 << 9) | (1 << 10);

        /// <summary>Raised when the ball lands on something that takes damage (HUD hit marker).</summary>
        public static event System.Action<DruidOrb, IDamageable, DamageInfo> HitTarget;

        static readonly RaycastHit[] hits = new RaycastHit[16];
        OrbData data;
        Transform owner;
        Vector3 velocity;
        float travelled, age, size = 1f, lightBase, rangeBase, flashT = -1f;
        bool flying, holding;
        Vector3[] ringAxis;

        void Awake()
        {
            if (glow) { lightBase = glow.intensity; rangeBase = glow.range; }
            ringAxis = new Vector3[rings.Length];
            for (int i = 0; i < rings.Length; i++) ringAxis[i] = Random.onUnitSphere;
        }

        /// <summary>Gathering in the hands: <paramref name="grow"/> 0..1 sets the size.</summary>
        public void Hold(float grow)
        {
            holding = true;
            // smaller than in flight: it sits right under the over-the-shoulder aim camera
            size = Mathf.Lerp(0.08f, 0.55f, Ease.OutCubic(Mathf.Clamp01(grow)));
            transform.localScale = Vector3.one * size;
            if (trail) trail.emitting = false;
            if (wisps && wisps.isPlaying) wisps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        public void Launch(Vector3 pos, Vector3 vel, OrbData d, Transform shooter)
        {
            data = d;
            owner = shooter;
            holding = false;
            size = 1f;
            transform.localScale = Vector3.one * d.radius / 0.32f;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(vel));
            velocity = vel;
            travelled = 0f;
            flying = true;
            if (trail) { trail.Clear(); trail.emitting = true; }
            if (wisps) { wisps.Clear(); wisps.Play(); }
        }

        /// <summary>Let go of a ball still in the hands (aim lowered, hit, form swap): it fizzles into leaves.</summary>
        public void Dissipate()
        {
            FXManager.Spawn(FXType.LeafFall, transform.position, Quaternion.identity, 0.6f * size);
            Destroy(gameObject);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            age += dt;
            // the rings spin round the core, the core breathes
            for (int i = 0; i < rings.Length; i++)
                if (rings[i]) rings[i].Rotate(ringAxis[i], (flying ? 540f : 260f) * dt * (i % 2 == 0 ? 1f : -1.3f), Space.World);
            if (core) core.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(age * 14f));
            if (glow && flashT < 0f) glow.intensity = lightBase * size * (0.85f + 0.15f * Mathf.Sin(age * 19f));

            if (flashT >= 0f) { Flash(dt); return; }
            if (!flying || holding) return;
            velocity += Vector3.down * data.gravity * dt;
            Vector3 step = velocity * dt;
            float dist = step.magnitude;
            if (dist > 1e-5f && FindHit(transform.position, step / dist, dist, out var hit, out var target))
            {
                Impact(hit.point, hit.normal, target);
                return;
            }
            transform.position += step;
            travelled += dist;
            if (travelled > data.range) Impact(transform.position, -velocity.normalized, null);
        }

        bool FindHit(Vector3 from, Vector3 dir, float dist, out RaycastHit best, out IDamageable target)
        {
            best = default;
            target = null;
            int n = Physics.SphereCastNonAlloc(from, data.radius * 0.9f, dir, hits, dist, hitMask, QueryTriggerInteraction.Collide);
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
            if (bestD < float.MaxValue && best.normal == Vector3.zero) best.normal = -dir;
            return bestD < float.MaxValue;
        }

        void Impact(Vector3 point, Vector3 normal, IDamageable target)
        {
            flying = false;
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
                    source = owner != null ? owner.gameObject : null
                };
                if (target.ReceiveHit(info)) HitTarget?.Invoke(this, target, info);
            }
            Explosions.Detonate(new Explosions.Blast
            {
                center = point + normal * 0.2f, radius = data.burstRadius, damage = data.burstDamage, poise = data.burstPoise,
                fxScale = 0.8f, weight = ImpactWeight.Heavy, silent = true, source = owner != null ? owner.gameObject : null
            }, target);
            FXManager.Spawn(FXType.OrbBurst, point + normal * 0.15f, Quaternion.LookRotation(normal), 1f);
            AudioManager.Play(Sfx.OrbImpact, point, 1f, Random.Range(0.9f, 1.02f));
            CameraDirector.Instance?.Shake(0.3f, dir);
            if (target != null) TimeDirector.HitStop(0.06f);

            // hide the ball; its light flares and dies; trail and motes finish on their own
            transform.position = point + normal * 0.2f;
            if (core) core.gameObject.SetActive(false);
            foreach (var r in rings) if (r) r.gameObject.SetActive(false);
            if (glowSprite) glowSprite.enabled = false;
            if (trail) trail.emitting = false;
            if (wisps) wisps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            flashT = 0f;
        }

        void Flash(float dt)
        {
            flashT += dt;
            float k = Mathf.Clamp01(flashT / 0.5f);
            if (glow)
            {
                glow.intensity = lightBase * 3.2f * (1f - k) * (1f - k);
                glow.range = rangeBase * Mathf.Lerp(1.6f, 1f, k);
            }
            if (k >= 1f && (trail == null || flashT > trail.time + 0.1f) && (wisps == null || !wisps.IsAlive(true)))
                Destroy(gameObject);
        }
    }
}
