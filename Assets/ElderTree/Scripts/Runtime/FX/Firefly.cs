using UnityEngine;

namespace ElderTree.FX
{
    /// <summary>
    /// A firefly big enough to light the haze: drifts lazily around where it was placed, blinks on and off, and its
    /// glow card faces the camera. Drives the Light (and through FogLight, its halo in the volumetric fog) and the
    /// "Glow" child's size. The swarms of small ones are particle systems; these few carry real light.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class Firefly : MonoBehaviour
    {
        public float wander = 1.6f;
        public float speed = 0.22f;
        public Vector2 onTime = new(0.6f, 1.6f);
        public Vector2 offTime = new(0.4f, 2.8f);

        Light glowLight;
        Transform glow;
        Vector3 home, glowScale;
        float baseIntensity, seed, phase, timer, level;
        bool on;

        void Start()
        {
            glowLight = GetComponent<Light>();
            baseIntensity = glowLight.intensity;
            glow = transform.Find("Glow");
            if (glow != null) glowScale = glow.localScale;
            home = transform.position;
            seed = Random.value * 100f;
            on = Random.value < 0.5f;
            timer = Random.Range(0.2f, 2f);
        }

        void Update()
        {
            float t = Time.time * speed + seed;
            transform.position = home + new Vector3((Mathf.PerlinNoise(t, seed) - 0.5f) * 2f * wander,
                                                    (Mathf.PerlinNoise(seed, t * 1.3f) - 0.5f) * wander * 0.8f,
                                                    (Mathf.PerlinNoise(t * 0.9f, seed + 7f) - 0.5f) * 2f * wander);
            if ((timer -= Time.deltaTime) <= 0f)
            {
                on = !on;
                timer = on ? Random.Range(onTime.x, onTime.y) : Random.Range(offTime.x, offTime.y);
            }
            level = Mathf.MoveTowards(level, on ? 1f : 0f, Time.deltaTime * (on ? 5f : 2.5f));
            float flick = 0.85f + 0.15f * Mathf.Sin(Time.time * 23f + seed);
            glowLight.intensity = baseIntensity * level * flick;
            if (glow != null)
            {
                glow.localScale = glowScale * (0.25f + 0.75f * level);
                var cam = Camera.main;
                if (cam != null) glow.rotation = cam.transform.rotation;
            }
        }
    }
}
