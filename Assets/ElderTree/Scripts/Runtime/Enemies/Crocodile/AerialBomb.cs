using ElderTree.Audio;
using UnityEngine;

namespace ElderTree.Enemies
{
    /// <summary>
    /// A falling bomb. There is no glowing ground marker any more: the only warning is the bomb's own
    /// shadow on the ground, a soft dark blob right under the bomb that starts wide and faint when it
    /// leaves the plane and tightens and darkens as it comes down (plus the whistle). Read the shadows,
    /// not a UI. The bomb lands on its planned spot exactly at the planned time. Pooled by the boss.
    /// </summary>
    public class AerialBomb : MonoBehaviour
    {
        public Transform model;
        [Tooltip("Flat blob under the bomb (alpha-blended dark disc, no lighting).")]
        public Transform shadow;
        public Renderer shadowRenderer;
        [Tooltip("Shadow diameter as a multiple of the blast radius: high up / at impact.")]
        public Vector2 shadowSize = new Vector2(1.7f, 1.05f);
        [Tooltip("Shadow opacity high up / at impact.")]
        public Vector2 shadowAlpha = new Vector2(0.3f, 0.95f);
        public Color shadowColor = new Color(0.02f, 0.01f, 0.03f, 1f);

        public bool InUse { get; private set; }
        public static readonly System.Collections.Generic.List<AerialBomb> Live = new();
        public Vector3 Target => target;

        Vector3 target, from, fromVel;
        float radius, damage, impactAt, releasedAt = -1f;
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
            if (shadow) shadow.gameObject.SetActive(false);
        }

        /// <summary>Reserve the strike; the bomb must be released before <paramref name="impactTime"/>.</summary>
        public void Plan(Vector3 groundTarget, float blastRadius, float blastDamage, float impactTime, GameObject owner)
        {
            InUse = true;
            if (!Live.Contains(this)) Live.Add(this);
            released = false;
            target = groundTarget;
            radius = blastRadius;
            damage = blastDamage;
            impactAt = impactTime;
            source = owner;
            model.gameObject.SetActive(false);
            if (shadow) shadow.gameObject.SetActive(false);
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
            if (shadow) shadow.gameObject.SetActive(true);
            AudioManager.Play(Sfx.BombWhistle, from, 0.7f, Random.Range(0.94f, 1.08f));
        }

        void Update()
        {
            if (!InUse) return;
            float now = Time.time;
            if (!released && now > impactAt - 0.35f) Release(target + Vector3.up * 9f, Vector3.zero);   // safety net
            if (!released) return;

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
            UpdateShadow(p, s);
            if (now >= impactAt) Explode();
        }

        void UpdateShadow(Vector3 bombPos, float s)
        {
            if (!shadow) return;
            // straight down under the bomb, on the ground plane of the target
            shadow.SetPositionAndRotation(new Vector3(bombPos.x, target.y + 0.04f, bombPos.z), Quaternion.identity);
            float k = s * s;                        // height falls with s^2
            float d = radius * 2f * Mathf.Lerp(shadowSize.x, shadowSize.y, k);
            shadow.localScale = new Vector3(d, 1f, d);
            if (shadowRenderer)
            {
                var c = shadowColor;
                c.a = Mathf.Lerp(shadowAlpha.x, shadowAlpha.y, k);
                mpb.SetColor(BaseColorId, c);
                shadowRenderer.SetPropertyBlock(mpb);
            }
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
