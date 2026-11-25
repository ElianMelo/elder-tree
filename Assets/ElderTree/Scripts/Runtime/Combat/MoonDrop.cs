using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.FX;
using UnityEngine;

namespace ElderTree.Combat
{
    /// <summary>
    /// Moonfall (the fairy form's special): a huge pale moon drops out of the night sky onto the target. A ring of light
    /// opens on the ground and brightens as the moon's own light grows over it; the moon comes in on a slant from beyond
    /// the target, accelerating, turning slowly, shedding motes. On impact: a wide blast (everything in the radius is hit
    /// once), a flash, a shockwave ring, dust and shards; the moon sinks into the crater and crumbles away.
    /// </summary>
    public class MoonDrop : MonoBehaviour
    {
        public Transform moon;
        public Renderer moonRenderer;
        public Transform marker;
        public Renderer markerRenderer;
        public Light glow;
        public ParticleSystem trail;
        public ParticleSystem shards;
        public ParticleSystem shockwave;

        public System.Action Landed;
        public bool Done { get; private set; }

        Vector3 from, to;
        float fallTime, radius, damage, poise, moonRadius, t, landedAt = -1f, glowBase;
        GameObject source;
        MaterialPropertyBlock mpb;
        Color markerColor = Color.white;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        Vector3 spinAxis;
        float rumbleT;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            if (markerRenderer && markerRenderer.sharedMaterial) markerColor = markerRenderer.sharedMaterial.GetColor(BaseColorId);
            if (glow) glowBase = glow.intensity;
        }

        /// <summary>Drop a moon of <paramref name="size"/> radius from <paramref name="start"/> onto <paramref name="ground"/>.</summary>
        public void Drop(Vector3 start, Vector3 ground, float size, float time, float blastRadius, float blastDamage, float blastPoise, GameObject owner)
        {
            from = start;
            to = ground;
            moonRadius = size;
            fallTime = Mathf.Max(0.3f, time);
            radius = blastRadius;
            damage = blastDamage;
            poise = blastPoise;
            source = owner;
            t = 0f;
            landedAt = -1f;
            spinAxis = Random.onUnitSphere;
            transform.position = ground;
            transform.rotation = Quaternion.identity;
            if (moon)
            {
                moon.position = from;
                moon.localScale = Vector3.one * size * 2f;
            }
            if (marker) marker.position = ground + Vector3.up * 0.05f;
            if (trail) trail.Play(true);
            AudioManager.Play(Sfx.DeepRumble, ground, 0.8f, 1.2f);
            AudioManager.Play(Sfx.OrbCharge, ground, 0.7f, 0.6f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            t += dt;
            if (landedAt < 0f)
            {
                float k = Mathf.Clamp01(t / fallTime);
                float e = k * k * (1.4f - 0.4f * k);                     // accelerates all the way down
                Vector3 p = Vector3.Lerp(from, to + Vector3.up * moonRadius * 0.35f, e);
                if (moon)
                {
                    moon.position = p;
                    moon.Rotate(spinAxis, 25f * dt, Space.World);
                }
                // the ring on the ground fills and brightens as the moon's light comes down over it
                if (marker)
                {
                    float d = radius * 2f * Mathf.Lerp(1.25f, 1f, k);
                    marker.localScale = new Vector3(d, d, 1f);
                    Tint(markerRenderer, markerColor, Mathf.Lerp(0.2f, 1f, k) * (k > 0.75f ? 0.85f + 0.15f * Mathf.Sin(t * 45f) : 1f));
                }
                if (glow)
                {
                    glow.transform.position = p;
                    glow.intensity = glowBase * Mathf.Lerp(0.4f, 1.4f, k);
                    glow.range = Mathf.Lerp(18f, 28f, k);
                }
                if ((rumbleT -= dt) <= 0f)
                {
                    rumbleT = 0.12f;
                    CameraDirector.Instance?.Shake(0.04f + 0.14f * k, Vector3.down);
                }
                if (k >= 1f) Impact();
                return;
            }

            // after the blast: the moon settles into its crater and crumbles away, the light dies, the ring fades
            float f = Mathf.Clamp01((t - landedAt) / 1.6f);
            if (moon)
            {
                moon.position = to + Vector3.up * moonRadius * Mathf.Lerp(0.35f, -0.4f, Ease.InCubic(f));
                moon.localScale = Vector3.one * moonRadius * 2f * Mathf.Lerp(1f, 0.05f, Ease.InCubic(f));
            }
            if (glow) glow.intensity = Mathf.Lerp(glowBase * 4f, 0f, Ease.OutCubic(Mathf.Clamp01((t - landedAt) / 0.9f)));
            Tint(markerRenderer, markerColor, 1f - f);
            if (f >= 1f && (shards == null || !shards.IsAlive(true)))
            {
                Done = true;
                Destroy(gameObject);
            }
        }

        void Impact()
        {
            landedAt = t;
            Vector3 c = to;
            if (trail) trail.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Explosions.Detonate(new Explosions.Blast
            {
                center = c + Vector3.up * 0.8f, radius = radius, damage = damage, poise = poise, fxScale = 2f,
                weight = ImpactWeight.Slam, silent = true, source = source
            });
            if (shards) { shards.transform.position = c + Vector3.up * 0.5f; shards.Play(true); }
            if (shockwave) { shockwave.transform.position = c + Vector3.up * 0.15f; shockwave.Play(true); }
            FXManager.Spawn(FXType.SlamRing, c + Vector3.up * 0.05f, Quaternion.identity, radius * 0.75f);
            FXManager.Spawn(FXType.Dust, c, Quaternion.identity, 4f);
            FXManager.Spawn(FXType.SlamDebris, c, Quaternion.identity, 2.4f);
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * 0.75f;
                FXManager.Spawn(FXType.Dust, Explosions.Ground(p), Quaternion.identity, 1.8f);
            }
            AudioManager.Play(Sfx.Slam, c, 1f, 0.55f);
            AudioManager.Play(Sfx.RockBreak, c, 1f, 0.7f);
            AudioManager.Play(Sfx.DeepRumble, c, 1f, 0.8f);
            AudioManager.Play(Sfx.OrbImpact, c, 1f, 0.55f);
            CameraDirector.Instance?.Shake(0.6f, Vector3.down);
            CameraDirector.Instance?.FovKick(-6f);
            TimeDirector.HitStop(0.12f);
            TimeDirector.SlowMo(0.6f, 0.4f);
            Landed?.Invoke();
        }

        void Tint(Renderer r, Color c, float k)
        {
            if (!r) return;
            r.GetPropertyBlock(mpb);
            c.a *= Mathf.Clamp01(k);
            mpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(mpb);
        }
    }
}
