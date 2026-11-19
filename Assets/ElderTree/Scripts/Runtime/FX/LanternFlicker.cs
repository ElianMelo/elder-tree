using UnityEngine;

namespace ElderTree.FX
{
    /// <summary>Organic candle flicker for lantern lights (layered noise on intensity + a tiny positional wobble).</summary>
    [RequireComponent(typeof(Light))]
    public class LanternFlicker : MonoBehaviour
    {
        public float amount = 0.18f;
        public float speed = 7f;
        public float wobble = 0.015f;
        public Renderer glowRenderer;

        Light lightSource;
        float baseIntensity;
        Vector3 basePos;
        float seed;
        MaterialPropertyBlock mpb;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        void Awake()
        {
            lightSource = GetComponent<Light>();
            baseIntensity = lightSource.intensity;
            basePos = transform.localPosition;
            seed = Random.value * 100f;
            mpb = new MaterialPropertyBlock();
        }

        void Update()
        {
            float t = Time.time * speed + seed;
            float n = Mathf.PerlinNoise(t, seed) * 0.7f + Mathf.PerlinNoise(t * 2.7f, seed + 3f) * 0.3f;
            float k = 1f + (n - 0.5f) * 2f * amount;
            lightSource.intensity = baseIntensity * k;
            transform.localPosition = basePos + new Vector3(Mathf.PerlinNoise(t * 0.5f, 1) - 0.5f, 0, Mathf.PerlinNoise(1, t * 0.5f) - 0.5f) * wobble;
            if (glowRenderer)
            {
                glowRenderer.GetPropertyBlock(mpb);
                mpb.SetColor(EmissionId, Palette.Lantern * 6f * k);
                glowRenderer.SetPropertyBlock(mpb);
            }
        }
    }
}
