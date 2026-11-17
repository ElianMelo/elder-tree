using UnityEngine;

namespace TungSahur.FX
{
    /// <summary>Point light that flares when its pooled effect is (re)enabled, then dies out.</summary>
    [RequireComponent(typeof(Light))]
    public class FlashLight : MonoBehaviour
    {
        public float peakIntensity = 40f;
        public float duration = 0.45f;
        Light l;
        float t;

        void Awake() => l = GetComponent<Light>();

        void OnEnable()
        {
            t = 0f;
            if (l) l.intensity = peakIntensity * transform.lossyScale.x;
        }

        void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            l.intensity = peakIntensity * transform.lossyScale.x * (1f - k) * (1f - k);
            l.enabled = k < 1f;
        }
    }
}
