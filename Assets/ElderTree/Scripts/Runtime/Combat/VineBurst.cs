using ElderTree.Audio;
using ElderTree.Equipment;
using ElderTree.FX;
using UnityEngine;

namespace ElderTree.Combat
{
    /// <summary>
    /// Where a Vine Seeker lands: vines burst out of the spot, arch outward and writhe, then coil in and squeeze
    /// (one more hit to everything in reach), then wither and sink. Stuck to what was hit, so vines that grew out of
    /// a boss's shin ride along with it. Each tendril is a circular arc whose curl tightens when it squeezes.
    /// </summary>
    public class VineBurst : MonoBehaviour
    {
        public LineRenderer template;
        public int tendrils = 7;
        public int points = 12;
        public float length = 1.6f;
        public float width = 0.3f;
        [Tooltip("Seconds the vines take to burst out.")]
        public float growTime = 0.22f;

        LineRenderer[] lines;
        Vector3[] baseLocal, dirLocal, curlLocal, wobLocal;
        float[] lenK, phase, baseW;
        Vector3[] buf;
        SpellData data;
        Transform owner;
        IDamageable direct;
        float t, power, damageMul, life;
        bool squeezed;

        public void Begin(Vector3 point, Vector3 normal, Transform stickTo, SpellData d, float dmgMul, float charge, IDamageable directTarget, Transform shooter)
        {
            data = d;
            owner = shooter;
            direct = directTarget;
            power = charge;
            damageMul = dmgMul;
            life = d.burstLife * Mathf.Lerp(1f, 1.25f, power);
            if (normal.sqrMagnitude < 1e-4f) normal = Vector3.up;
            normal.Normalize();
            transform.SetPositionAndRotation(point, Quaternion.LookRotation(normal));
            if (stickTo != null) transform.SetParent(stickTo, true);

            int n = Mathf.RoundToInt(tendrils * Mathf.Lerp(1f, 1.5f, power));
            lines = new LineRenderer[n];
            baseLocal = new Vector3[n]; dirLocal = new Vector3[n]; curlLocal = new Vector3[n]; wobLocal = new Vector3[n];
            lenK = new float[n]; phase = new float[n]; baseW = new float[n];
            buf = new Vector3[points];
            float size = Mathf.Lerp(1f, 1.4f, power);
            for (int i = 0; i < n; i++)
            {
                var lr = i == 0 ? template : Instantiate(template, transform);
                lr.useWorldSpace = false;
                lr.positionCount = 0;
                baseW[i] = width * size * Random.Range(0.75f, 1.1f);
                lr.widthMultiplier = baseW[i];
                lines[i] = lr;
                // local frame: +Z is the surface normal
                float a = (i + Random.value * 0.6f) / n * Mathf.PI * 2f;
                Vector3 radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                baseLocal[i] = radial * Random.Range(0.02f, 0.18f) * size;
                // arch out of the surface, leaning away from the centre
                dirLocal[i] = (Vector3.forward * Random.Range(0.75f, 1.1f) + radial * Random.Range(0.35f, 0.8f)).normalized;
                // curl back over the centre (toward the middle), a little sideways
                Vector3 inward = -radial + Vector3.Cross(Vector3.forward, radial) * Random.Range(-0.4f, 0.4f);
                curlLocal[i] = (inward - dirLocal[i] * Vector3.Dot(inward, dirLocal[i])).normalized;
                wobLocal[i] = Vector3.Cross(dirLocal[i], curlLocal[i]).normalized;
                lenK[i] = length * size * Random.Range(0.65f, 1.1f);
                phase[i] = Random.value * 10f;
            }
            FXManager.Spawn(FXType.LeafFall, point + normal * 0.15f, Quaternion.LookRotation(normal), size);
            AudioManager.Play(Sfx.VineRustle, point, 0.8f, Random.Range(0.9f, 1.05f));
            Draw();
        }

        void Update()
        {
            if (lines == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            t += dt;
            if (!squeezed && t >= data.burstDelay) Squeeze();
            Draw();
            if (t >= life + 0.45f) Destroy(gameObject);
        }

        void Draw()
        {
            float grow = Ease.OutCubic(Mathf.Clamp01(t / growTime));
            // curl: arching while they grow, coiling tight on the squeeze, slackening as they wither
            float sq = Mathf.Clamp01((t - data.burstDelay) / 0.12f);
            float slack = Mathf.Clamp01((t - life) / 0.45f);
            float kappa = Mathf.Lerp(1.6f, 4.6f, Ease.OutCubic(sq)) * (1f - slack * 0.7f);
            float widthK = 1f - slack;
            for (int i = 0; i < lines.Length; i++)
            {
                float L = lenK[i] * grow * (1f - slack * 0.35f);
                float k = kappa / Mathf.Max(0.3f, lenK[i]);
                for (int j = 0; j < points; j++)
                {
                    float s = L * j / (points - 1);
                    // circular arc: forward along dir, bending toward curl
                    Vector3 p = baseLocal[i] + dirLocal[i] * (Mathf.Sin(k * s) / k) + curlLocal[i] * ((1f - Mathf.Cos(k * s)) / k);
                    p += wobLocal[i] * (0.04f * Mathf.Sin(t * 9f + phase[i] + j * 0.8f) * (j / (float)points));
                    p -= Vector3.forward * slack * 0.5f * (j / (float)points);       // withering: droop and sink
                    buf[j] = p;
                }
                lines[i].positionCount = points;
                lines[i].SetPositions(buf);
                lines[i].widthMultiplier = baseW[i] * widthK;
            }
        }

        /// <summary>The vines coil in: one more hit to everything in reach (the struck target included).</summary>
        void Squeeze()
        {
            squeezed = true;
            float k = Mathf.Lerp(1f, 1.5f, power);
            Explosions.Detonate(new Explosions.Blast
            {
                center = transform.position, radius = data.burstRadius * k, damage = data.burstDamage * damageMul,
                poise = data.burstPoise * k, fxScale = 0.25f, weight = ImpactWeight.Light, silent = true,
                source = owner != null ? owner.gameObject : null
            });
            FXManager.Spawn(FXType.LeafFall, transform.position, transform.rotation, 0.9f * k);
            AudioManager.Play(Sfx.RootSnap, transform.position, 0.55f, Random.Range(1.05f, 1.25f));
        }
    }
}
