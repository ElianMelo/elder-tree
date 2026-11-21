using UnityEngine;

namespace ElderTree.FX
{
    /// <summary>
    /// Leaf cover that never stops moving (the Druid Mask form). Every clump sways on its own about a random axis,
    /// harder while the body moves, and much harder when <see cref="Agitate"/> is called (casting, hits, rolls).
    /// As a clump tilts it catches the light and falls back into shadow: its tint drifts between a lighter and a
    /// darker green with the sway, so the cover shimmers between the two tones.
    /// </summary>
    public class LeafSway : MonoBehaviour
    {
        public Transform[] leaves = System.Array.Empty<Transform>();
        [Tooltip("Degrees of calm sway, and of the fast flutter on top.")]
        public float amplitude = 7f;
        public float flutter = 2.5f;
        [Tooltip("How far the tint swings either way (0.25 = 25% lighter / darker).")]
        [Range(0f, 0.6f)] public float tintRange = 0.24f;
        [Tooltip("Body speed (m/s) at which the leaves are fully stirred by moving.")]
        public float stirSpeed = 5f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        Quaternion[] rest;
        Vector3[] axis;
        float[] phase, freq, tintPhase;
        Renderer[] rends;
        MaterialPropertyBlock mpb;
        Vector3 prevPos;
        float agitation, kick;

        void Awake()
        {
            int n = leaves.Length;
            rest = new Quaternion[n]; axis = new Vector3[n]; phase = new float[n]; freq = new float[n]; tintPhase = new float[n];
            rends = new Renderer[n];
            var rnd = new System.Random(5150);
            for (int i = 0; i < n; i++)
            {
                if (!leaves[i]) continue;
                rest[i] = leaves[i].localRotation;
                axis[i] = new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f).normalized;
                phase[i] = (float)rnd.NextDouble() * 6.28f;
                freq[i] = 1.5f + (float)rnd.NextDouble() * 1.4f;
                tintPhase[i] = (float)rnd.NextDouble() * 6.28f;
                rends[i] = leaves[i].GetComponent<Renderer>();
            }
            mpb = new MaterialPropertyBlock();
        }

        void OnEnable() => prevPos = transform.position;

        /// <summary>Stir the leaves (0..1+): casting, being hit, a roll.</summary>
        public void Agitate(float amount) => kick = Mathf.Max(kick, amount);

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || rest == null) return;
            float speed = (transform.position - prevPos).magnitude / dt;
            prevPos = transform.position;
            float moving = Mathf.Clamp01(speed / Mathf.Max(0.1f, stirSpeed)) * 0.6f;
            kick = Mathf.MoveTowards(kick, 0f, dt * 1.4f);
            agitation = Mathf.Lerp(agitation, Mathf.Max(moving, kick), 1f - Mathf.Exp(-6f * dt));

            float t = Time.time;
            float amp = amplitude * (1f + agitation * 1.8f);
            float flut = flutter * (0.35f + agitation * 1.6f);
            for (int i = 0; i < leaves.Length; i++)
            {
                var l = leaves[i];
                if (!l) continue;
                float w = t * freq[i] * (1f + agitation * 1.4f) + phase[i];
                float s = Mathf.Sin(w);
                float a = amp * s + flut * Mathf.Sin(w * 3.7f + phase[i]);
                l.localRotation = rest[i] * Quaternion.AngleAxis(a, axis[i]);
                var r = rends[i];
                if (r == null) continue;
                // tilting into the light brightens it, swinging back darkens it; a slow drift keeps neighbours apart
                float b = 1f + tintRange * (0.65f * s + 0.35f * Mathf.Sin(t * 0.6f + tintPhase[i]));
                r.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColor, new Color(b, b, b * 0.97f, 1f));
                r.SetPropertyBlock(mpb);
            }
        }
    }
}
