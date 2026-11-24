using UnityEngine;

namespace ElderTree.World
{
    /// <summary>Something afloat (a moored boat, a coconut in the shallows): it heaves and rolls on the swell.</summary>
    public class Bobber : MonoBehaviour
    {
        public float heave = 0.12f;
        public float roll = 4f;
        public float pitch = 2.5f;
        public float period = 4.2f;

        Vector3 restPos;
        Quaternion restRot;
        float seed;

        void Awake()
        {
            restPos = transform.localPosition;
            restRot = transform.localRotation;
            seed = Random.value * 10f;
        }

        void Update()
        {
            float w = (Time.time + seed) * Mathf.PI * 2f / period;
            transform.localPosition = restPos + Vector3.up * Mathf.Sin(w) * heave;
            transform.localRotation = restRot * Quaternion.Euler(Mathf.Sin(w * 0.8f + 1.1f) * pitch, 0f, Mathf.Sin(w + 0.6f) * roll);
        }
    }
}
