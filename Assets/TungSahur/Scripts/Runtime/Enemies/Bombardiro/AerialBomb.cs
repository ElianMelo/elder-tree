using TungSahur.Audio;
using UnityEngine;

namespace TungSahur.Enemies
{
    /// <summary>
    /// A falling bomb plus its ground marker. The marker appears when the strike is planned (a dim
    /// rim at the blast radius with a disk that fills as impact approaches, so the timing reads at a
    /// glance); the bomb itself is released later from the plane and lands on the marked spot exactly
    /// at the planned time. Pooled by the boss.
    /// </summary>
    public class AerialBomb : MonoBehaviour
    {
        public Transform model;
        public Transform marker;
        public Transform markerFill;
        public Renderer markerRimRenderer, markerFillRenderer;
        public Color markerColor = new Color(1f, 0.32f, 0.12f, 1f);

        public bool InUse { get; private set; }
        public static readonly System.Collections.Generic.List<AerialBomb> Live = new();
        public Vector3 Target => target;

        Vector3 target, from, fromVel;
        float radius, damage, plannedAt, impactAt, releasedAt = -1f;
        bool released;
        GameObject source;
        MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            Hide();
        }

        void Hide()
        {
            Live.Remove(this);
            InUse = false;
            released = false;
            if (model) model.gameObject.SetActive(false);
            if (marker) marker.gameObject.SetActive(false);
        }

        /// <summary>Show the marker now; the bomb must be released before <paramref name="impactTime"/>.</summary>
        public void Plan(Vector3 groundTarget, float blastRadius, float blastDamage, float impactTime, GameObject owner)
        {
            InUse = true;
            if (!Live.Contains(this)) Live.Add(this);
            released = false;
            target = groundTarget;
            radius = blastRadius;
            damage = blastDamage;
            plannedAt = Time.time;
            impactAt = impactTime;
            source = owner;
            marker.gameObject.SetActive(true);
            marker.SetPositionAndRotation(target + Vector3.up * 0.06f, Quaternion.identity);
            marker.localScale = new Vector3(radius * 2f, 1f, radius * 2f);
            model.gameObject.SetActive(false);
            UpdateMarker(0f);
        }

        public void Release(Vector3 position, Vector3 velocity)
        {
            if (!InUse || released) return;
            released = true;
            releasedAt = Time.time;
            from = position;
            fromVel = velocity;
            model.gameObject.SetActive(true);
            model.position = from;
            AudioManager.Play(Sfx.BombWhistle, from, 0.7f, Random.Range(0.94f, 1.08f));
        }

        void Update()
        {
            if (!InUse) return;
            float now = Time.time;
            float total = Mathf.Max(0.05f, impactAt - plannedAt);
            UpdateMarker(Mathf.Clamp01((now - plannedAt) / total));

            if (!released && now > impactAt - 0.35f) Release(target + Vector3.up * 9f, Vector3.zero);   // safety net
            if (released)
            {
                float T = Mathf.Max(0.05f, impactAt - releasedAt);
                float t = Mathf.Clamp(now - releasedAt, 0f, T);
                // horizontal: start with the plane's drift, arrive on target; vertical: accelerating fall
                Vector3 h0 = new Vector3(from.x, 0f, from.z), h1 = new Vector3(target.x, 0f, target.z);
                Vector3 v0 = new Vector3(fromVel.x, 0f, fromVel.z) * 0.5f;
                float s = t / T;
                // cubic Hermite with zero end tangent horizontally
                float h00 = 2 * s * s * s - 3 * s * s + 1, h10 = s * s * s - 2 * s * s + s, h01 = -2 * s * s * s + 3 * s * s;
                Vector3 hp = h00 * h0 + h10 * T * v0 + h01 * h1;
                float y = Mathf.Lerp(from.y, target.y, s * s);
                Vector3 p = new Vector3(hp.x, y, hp.z);
                Vector3 vel = (p - model.position) / Mathf.Max(Time.deltaTime, 1e-4f);
                model.position = p;
                if (vel.sqrMagnitude > 0.5f) model.rotation = Quaternion.Slerp(model.rotation, Quaternion.LookRotation(vel.normalized), 0.35f);
                if (now >= impactAt) Explode();
            }
        }

        void UpdateMarker(float k)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(8f, 26f, k));
            markerFill.localScale = Vector3.one * Mathf.Lerp(0.08f, 1f, k * k);
            mpb.SetColor(BaseColorId, markerColor * new Color(1, 1, 1, Mathf.Lerp(0.35f, 0.9f, k) * (0.75f + 0.25f * pulse)));
            markerRimRenderer.SetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, markerColor * new Color(1, 1, 1, Mathf.Lerp(0.12f, 0.42f, k)));
            markerFillRenderer.SetPropertyBlock(mpb);
        }

        void Explode()
        {
            Vector3 c = target;
            Hide();
            Blast.Detonate(c, radius, damage, true, source, 1f);
        }

        public void Cancel() => Hide();
    }
}
