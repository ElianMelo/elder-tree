using UnityEngine;

namespace ElderTree.FX
{
    /// <summary>A slow, breathing swell on a light (glowing mushrooms): each one on its own phase and rate.</summary>
    [RequireComponent(typeof(Light))]
    public class GlowPulse : MonoBehaviour
    {
        [Tooltip("Fraction of the intensity that comes and goes.")]
        public float depth = 0.25f;
        [Tooltip("Breaths per second.")]
        public float rate = 0.18f;

        Light lightSource;
        float baseIntensity, seed;

        void Awake()
        {
            lightSource = GetComponent<Light>();
            baseIntensity = lightSource.intensity;
            seed = transform.position.x * 0.37f + transform.position.z * 0.21f;
        }

        void Update()
        {
            float t = Time.time * rate * Mathf.PI * 2f + seed;
            float s = 0.5f + 0.5f * Mathf.Sin(t) * (0.75f + 0.25f * Mathf.Sin(t * 0.37f + seed));
            lightSource.intensity = baseIntensity * (1f - depth + depth * s);
        }
    }
}
