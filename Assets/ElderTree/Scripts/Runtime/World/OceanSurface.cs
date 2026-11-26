using UnityEngine;

namespace ElderTree.World
{
    /// <summary>
    /// The sea's surface height on the CPU: the same four Gerstner swells <c>Ocean.shader</c> rolls in from the south, read
    /// from the stage's ocean material, so whatever rides the water (a fin, a wake) rises and falls with the swell the
    /// player sees. The horizontal pull of the crests (a third of a metre) is left out.
    /// </summary>
    public static class OceanSurface
    {
        static Renderer ocean;
        static float amp, len, speed;
        static readonly int AmpId = Shader.PropertyToID("_WaveAmp"), LenId = Shader.PropertyToID("_WaveLength"),
                            SpeedId = Shader.PropertyToID("_WaveSpeed");

        /// <summary>Is there an ocean on this stage?</summary>
        public static bool Exists => Find();

        /// <summary>The calm sea level (the ocean mesh's height).</summary>
        public static float Level => Find() ? ocean.transform.position.y : 0f;

        static bool Find()
        {
            if (ocean != null) return true;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m == null || m.shader == null || m.shader.name != "ElderTree/Ocean") continue;
                ocean = r;
                amp = m.HasProperty(AmpId) ? m.GetFloat(AmpId) : 0.32f;
                len = m.HasProperty(LenId) ? m.GetFloat(LenId) : 16f;
                speed = m.HasProperty(SpeedId) ? m.GetFloat(SpeedId) : 1f;
                return true;
            }
            return false;
        }

        /// <summary>How open the water is at <paramref name="p"/> (0 at the shore, 1 in open water): the mesh's vertex colour,
        /// rebuilt from the depth to the seabed. The swell shrinks to a fifth in the shallows.</summary>
        public static float Openness(Vector3 p)
        {
            float level = Level;
            // the lowest surface under p: through the dock to the seabed
            var hits = Physics.RaycastAll(new Vector3(p.x, level + 20f, p.z), Vector3.down, 80f, 1, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) return 1f;
            float bed = float.MaxValue;
            foreach (var h in hits) bed = Mathf.Min(bed, h.point.y);
            float d = level - bed;
            float t = Mathf.Clamp01((d - 0.2f) / 2.8f);
            return t * t * (3f - 2f * t);
        }

        /// <summary>The surface height at (x, z) right now.</summary>
        public static float Height(float x, float z, float open = 1f)
        {
            if (!Find()) return 0f;
            float a = amp * Mathf.Lerp(0.18f, 1f, open);
            float t = Time.timeSinceLevelLoad * speed;          // the shader's _Time.y
            float y = 0f;
            y += Swell(0.12f, 1f, len, a * 0.55f, x, z, t);
            y += Swell(-0.45f, 0.9f, len * 0.61f, a * 0.25f, x, z, t);
            y += Swell(0.55f, 0.85f, len * 0.37f, a * 0.13f, x, z, t);
            y += Swell(-0.1f, 1f, len * 0.21f, a * 0.07f, x, z, t);
            return ocean.transform.position.y + y;
        }

        public static float Height(Vector3 p, float open = 1f) => Height(p.x, p.z, open);

        static float Swell(float dx, float dz, float l, float a, float x, float z, float t)
        {
            float k = 2f * Mathf.PI / l;
            float c = Mathf.Sqrt(9.8f / k);
            float n = Mathf.Sqrt(dx * dx + dz * dz);
            return a * Mathf.Sin(k * ((dx * x + dz * z) / n - c * t));
        }
    }
}
