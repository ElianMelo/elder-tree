using UnityEngine;

namespace ElderTree.FX
{
    /// <summary>
    /// Sea wind on a pivot: a palm crown leaning and nodding with slow gusts, or (aroundUp) a pennant swinging about
    /// its pole and flapping. Gusts are Perlin noise shared by position, so neighbours move together, not in lockstep.
    /// </summary>
    public class PalmSway : MonoBehaviour
    {
        [Tooltip("Degrees of lean in a gust.")]
        public float amplitude = 3f;
        [Tooltip("Degrees of fast flutter on top.")]
        public float flutter = 0.8f;
        public float gustSpeed = 0.22f;
        public float flutterSpeed = 2.6f;
        public Vector3 windDirection = new(0.25f, 0f, 1f);
        [Tooltip("Swing about the vertical (flags) instead of leaning with the wind (crowns).")]
        public bool aroundUp;

        Quaternion rest;
        float seed;

        void Awake()
        {
            rest = transform.localRotation;
            seed = transform.position.x * 0.071f + transform.position.z * 0.053f;
        }

        void Update()
        {
            float t = Time.time;
            float gust = Mathf.PerlinNoise(seed, t * gustSpeed);
            float a = amplitude * (gust * 1.6f - 0.3f) + flutter * Mathf.Sin(t * flutterSpeed + seed * 7f) * (0.4f + gust);
            Vector3 axisW = aroundUp ? Vector3.up : Vector3.Cross(Vector3.up, windDirection.normalized);
            Vector3 axis = transform.parent ? transform.parent.InverseTransformDirection(axisW) : axisW;
            transform.localRotation = Quaternion.AngleAxis(a, axis) * rest;
        }
    }
}
