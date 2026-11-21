using System.Collections.Generic;
using ElderTree.Equipment;
using ElderTree.FX;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree.Combat
{
    /// <summary>
    /// The ring a giant druid's stomp sends out (<see cref="GrowthStompData"/>): a circle spreading from the foot, and
    /// wherever its front passes, vines rise out of the dirt, writhe, and sink back. So a rising wall of vines rolls
    /// outward and leaves the ground quiet behind it. Everything the front reaches is struck once (on the ground or
    /// standing on it: it doesn't reach high in the air).
    /// </summary>
    public class VineRing : MonoBehaviour
    {
        public LineRenderer template;
        [Tooltip("Spacing of the vines along the ring, and between rows of the spreading circle (metres).")]
        public float spacing = 1.1f;
        public float rowStep = 0.75f;
        public int points = 10;
        public float width = 0.34f;
        [Tooltip("How high above the ground the wave reaches targets (metres).")]
        public float reachHeight = 3.2f;
        [Tooltip("Seconds a vine takes to shoot up, and to sink back after its life.")]
        public float Rise = 0.11f;
        public float Wither = 0.22f;

        /// <summary>Raised for every target the ring strikes (HUD hit marker).</summary>
        public static event System.Action<VineRing, IDamageable, DamageInfo> HitTarget;

        class Vine
        {
            public LineRenderer lr;
            public Vector3 basePos, dir, curl, wob;
            public float born, height, kappa, phase, w;
        }

        static readonly Collider[] buffer = new Collider[64];
        readonly List<Vine> vines = new();
        readonly List<LineRenderer> spare = new();
        readonly HashSet<IDamageable> struck = new();
        GrowthStompData data;
        Transform owner;
        Vector3 center;
        float radius, t, damageMul, nextRow;
        int row;
        Vector3[] buf;

        public void Begin(Vector3 ground, GrowthStompData d, float dmgMul, Transform shooter)
        {
            data = d;
            owner = shooter;
            center = ground;
            damageMul = dmgMul;
            transform.position = ground;
            radius = 0.3f;
            nextRow = 0.6f;
            buf = new Vector3[points];
            template.gameObject.SetActive(false);
            if (owner != null) struck.Add(owner.GetComponentInParent<IDamageable>());
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || data == null) return;
            t += dt;
            float prev = radius;
            if (radius < data.ringRadius) radius = Mathf.Min(data.ringRadius, radius + data.ringSpeed * dt);
            // a new row of vines bursts up wherever the front has reached
            while (nextRow <= radius && nextRow <= data.ringRadius) { SpawnRow(nextRow); nextRow += rowStep; row++; }
            if (radius > prev) Strike();
            DrawVines();
            if (radius >= data.ringRadius && vines.Count == 0) Destroy(gameObject);
        }

        void SpawnRow(float r)
        {
            int n = Mathf.Max(5, Mathf.RoundToInt(2f * Mathf.PI * r / spacing));
            float a0 = Random.value * Mathf.PI * 2f;
            for (int i = 0; i < n; i++)
            {
                float a = a0 + i * Mathf.PI * 2f / n + Random.Range(-0.15f, 0.15f) / Mathf.Max(1f, r);
                Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 p = Explosions.Ground(center + radial * (r + Random.Range(-0.25f, 0.25f)), 1.5f, 3f);
                var v = new Vine { lr = Take(), basePos = p, born = t, phase = Random.value * 10f };
                // rise up and lean out with the wave, curling back over toward the stomp
                v.dir = (Vector3.up + radial * Random.Range(0.2f, 0.55f)).normalized;
                Vector3 curl = -radial + Vector3.Cross(Vector3.up, radial) * Random.Range(-0.5f, 0.5f);
                v.curl = (curl - v.dir * Vector3.Dot(curl, v.dir)).normalized;
                v.wob = Vector3.Cross(v.dir, v.curl).normalized;
                // the wave is tallest near the foot and lower as it spreads
                float falloff = Mathf.Lerp(1f, 0.65f, r / Mathf.Max(1f, data.ringRadius));
                v.height = Random.Range(data.vineHeight.x, data.vineHeight.y) * falloff;
                v.kappa = Random.Range(0.9f, 2.2f);
                v.w = width * Random.Range(0.8f, 1.15f) * Mathf.Lerp(1.15f, 0.85f, r / data.ringRadius);
                vines.Add(v);
                if (i % 3 == 0) FXManager.Spawn(FXType.DustSmall, p, Quaternion.identity, 1.3f);
                if (i % 5 == 0) FXManager.Spawn(FXType.LeafFall, p + Vector3.up * 0.6f, Quaternion.LookRotation(Vector3.up), 0.8f);
            }
        }

        LineRenderer Take()
        {
            LineRenderer lr;
            if (spare.Count > 0) { lr = spare[spare.Count - 1]; spare.RemoveAt(spare.Count - 1); }
            else lr = Instantiate(template, transform);
            lr.gameObject.SetActive(true);
            lr.useWorldSpace = true;
            return lr;
        }

        void DrawVines()
        {
            float life = data.vineLife;
            for (int i = vines.Count - 1; i >= 0; i--)
            {
                var v = vines[i];
                float age = t - v.born;
                if (age > life + Wither)
                {
                    v.lr.gameObject.SetActive(false);
                    spare.Add(v.lr);
                    vines.RemoveAt(i);
                    continue;
                }
                float grow = Ease.OutBack(Mathf.Clamp01(age / Rise));
                float wither = Mathf.Clamp01((age - life) / Wither);
                float L = v.height * grow * (1f - wither * 0.85f);
                float k = v.kappa * (1f + wither) / Mathf.Max(0.3f, v.height);
                for (int j = 0; j < points; j++)
                {
                    float s = Mathf.Max(0f, L) * j / (points - 1);
                    Vector3 p = v.basePos + v.dir * (Mathf.Sin(k * s) / k) + v.curl * ((1f - Mathf.Cos(k * s)) / k);
                    p += v.wob * (0.07f * Mathf.Sin(t * 10f + v.phase + j * 0.9f) * (j / (float)points));
                    p.y -= wither * 0.6f * (j / (float)points);          // sinking back into the dirt
                    buf[j] = p;
                }
                v.lr.positionCount = points;
                v.lr.SetPositions(buf);
                v.lr.widthMultiplier = v.w * (1f - wither);
            }
        }

        /// <summary>Everything the front has reached (and isn't high in the air) is struck once.</summary>
        void Strike()
        {
            int n = Physics.OverlapSphereNonAlloc(center, radius + 0.4f, buffer, 1 << Explosions.HurtboxLayer, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var col = buffer[i];
                var hb = col.GetComponent<Hurtbox>();
                var target = hb != null ? hb.Owner : col.GetComponentInParent<IDamageable>();
                if (target == null || !target.IsAlive || struck.Contains(target)) continue;
                if (owner != null && target.Root.IsChildOf(owner)) continue;
                Vector3 p = col.ClosestPoint(center + Vector3.up * 0.5f);
                if (p.y - center.y > reachHeight) continue;
                Vector3 flat = p - center; flat.y = 0f;
                if (flat.magnitude > radius + 0.4f) continue;
                struck.Add(target);
                Vector3 dir = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
                var info = new DamageInfo
                {
                    damage = Mathf.Round(data.damage * damageMul * Random.Range(0.96f, 1.04f)),
                    poiseDamage = data.poise * damageMul,
                    point = p,
                    direction = dir,
                    weight = ImpactWeight.Heavy,
                    isSlamWave = true,
                    charged = true,
                    source = owner != null ? owner.gameObject : null
                };
                if (target.ReceiveHit(info))
                {
                    HitTarget?.Invoke(this, target, info);
                    FXManager.Spawn(FXType.LeafExplosion, p, Quaternion.LookRotation(Vector3.up), 1f);
                }
            }
        }
    }
}
