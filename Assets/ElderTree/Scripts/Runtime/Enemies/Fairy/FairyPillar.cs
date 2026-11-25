using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.Combat;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree.Enemies
{
    /// <summary>
    /// Moonfall: a pale ring of light gathers on the ground (motes rising out of it, brighter and tighter as it
    /// fills), then a column of cold light slams down onto it. Damage to anyone standing in it on the ground or
    /// low in a jump. Pooled by the Fairy.
    /// </summary>
    public class FairyPillar : MonoBehaviour
    {
        public Transform marker;
        public Renderer markerRenderer;
        public Transform beam;
        public Renderer beamRenderer;
        public ParticleSystem rising;
        public ParticleSystem impact;
        public Light glow;
        public float beamHeight = 14f;

        public bool Free => !gameObject.activeSelf;

        float radius, damage, poise = 60f, tell, t;
        bool struck, friendly;
        GameObject source;
        MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        Color markerColor = Color.white, beamColor = Color.white;
        float glowBase = 1f;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            if (markerRenderer && markerRenderer.sharedMaterial) markerColor = markerRenderer.sharedMaterial.GetColor(BaseColorId);
            if (beamRenderer && beamRenderer.sharedMaterial) beamColor = beamRenderer.sharedMaterial.GetColor(BaseColorId);
            if (glow) glowBase = glow.intensity;
            gameObject.SetActive(false);
        }

        /// <summary>Open a ring at <paramref name="ground"/>; the column falls after <paramref name="tell"/> s. A
        /// <paramref name="friendlyToPlayer"/> column (the fairy form's Moonfall) hurts enemies instead of the player.</summary>
        public void Strike(Vector3 ground, float radius, float tell, float damage, GameObject owner, bool friendlyToPlayer = false, float poiseDamage = 60f)
        {
            friendly = friendlyToPlayer;
            poise = poiseDamage;
            gameObject.SetActive(true);
            transform.SetPositionAndRotation(ground + Vector3.up * 0.04f, Quaternion.identity);
            this.radius = radius;
            this.tell = tell;
            this.damage = damage;
            source = owner;
            t = 0f;
            struck = false;
            if (beam) beam.gameObject.SetActive(false);
            if (rising) { rising.transform.localScale = Vector3.one * radius; rising.Play(true); }
            AudioManager.Play(Sfx.SpellGather, ground, 0.35f, Random.Range(1.5f, 1.7f));
        }

        public void Cancel() => gameObject.SetActive(false);

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            t += dt;
            if (!struck)
            {
                // the ring fills: starts wide and faint, tightens to the strike radius and brightens
                float k = Mathf.Clamp01(t / tell);
                float d = radius * 2f * Mathf.Lerp(1.45f, 1f, Ease.OutCubic(k));
                marker.localScale = new Vector3(d, d, 1f);
                SetColor(markerRenderer, markerColor, Mathf.Lerp(0.15f, 1f, k * k) * (k > 0.8f ? 0.8f + 0.2f * Mathf.Sin(t * 50f) : 1f));
                if (glow) glow.intensity = glowBase * 0.5f * k;
                if (t >= tell) Slam();
                return;
            }
            // the column fades and thins
            float f = Mathf.Clamp01((t - tell) / 0.45f);
            if (beam)
            {
                float w = radius * 2f * Mathf.Lerp(0.62f, 0.08f, Ease.OutCubic(f));
                beam.localScale = new Vector3(w, beamHeight * 0.5f, w);
                SetColor(beamRenderer, beamColor, 1f - f);
            }
            SetColor(markerRenderer, markerColor, 1f - f);
            if (glow) glow.intensity = glowBase * 2.5f * (1f - f) * (1f - f);
            if (f >= 1f && (impact == null || !impact.IsAlive(true))) gameObject.SetActive(false);
        }

        void Slam()
        {
            struck = true;
            if (beam)
            {
                beam.gameObject.SetActive(true);
                beam.localPosition = Vector3.up * beamHeight * 0.5f;
            }
            if (rising) rising.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (impact) { impact.transform.localScale = Vector3.one * radius / 1.6f; impact.Play(true); }
            AudioManager.Play(Sfx.OrbImpact, transform.position, 0.8f, Random.Range(1.2f, 1.35f));
            AudioManager.Play(Sfx.Slam, transform.position, 0.45f, 1.4f);
            if (friendly)
            {
                Explosions.Detonate(new Explosions.Blast
                {
                    center = transform.position + Vector3.up * 0.6f, radius = radius + 0.2f, damage = damage, poise = poise,
                    fxScale = 0.6f, weight = ImpactWeight.Heavy, silent = true, source = source
                });
                CameraDirector.Instance?.Shake(0.12f, Vector3.down);
                return;
            }
            var p = PlayerController.Instance;
            if (p != null && !p.IsDead)
            {
                Vector3 d = p.transform.position - transform.position;
                float y = d.y;
                d.y = 0f;
                if (d.magnitude < radius + 0.3f && y < 2.2f)
                    p.TakeHit(new DamageInfo
                    {
                        damage = damage, poiseDamage = poise, point = p.transform.position + Vector3.up,
                        direction = d.sqrMagnitude > 1e-3f ? d.normalized : Vector3.forward, weight = ImpactWeight.Heavy, source = source
                    });
                if (d.magnitude < 12f) CameraDirector.Instance?.Shake(0.18f, Vector3.down);
            }
        }

        void SetColor(Renderer r, Color c, float k)
        {
            if (!r) return;
            r.GetPropertyBlock(mpb);
            c.a *= Mathf.Clamp01(k);
            mpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(mpb);
        }
    }
}
