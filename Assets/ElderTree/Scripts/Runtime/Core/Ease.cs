using System;
using System.Collections;
using UnityEngine;

namespace ElderTree
{
    /// <summary>Easing curves + a tiny coroutine tween (unscaled by default so UI survives pause/hit-stop).</summary>
    public static class Ease
    {
        public static float OutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
        public static float InCubic(float t) => t * t * t;
        public static float InOutCubic(float t) => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
        public static float OutQuart(float t) => 1f - Mathf.Pow(1f - t, 4f);
        public static float OutExpo(float t) => t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);

        public static float OutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        /// <summary>Standard out-bounce (used for things falling over).</summary>
        public static float OutBounceLike(float t)
        {
            t = Mathf.Clamp01(t);
            const float n1 = 7.5625f, d1 = 2.75f;
            if (t < 1f / d1) return n1 * t * t;
            if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
            if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }

        public static float OutElastic(float t)
        {
            const float c4 = 2f * Mathf.PI / 3f;
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
        }

        public static IEnumerator Tween(float duration, Action<float> apply, Func<float, float> ease, bool unscaled = true)
        {
            for (float t = 0f; t < 1f; t += (unscaled ? Time.unscaledDeltaTime : Time.deltaTime) / Mathf.Max(0.0001f, duration))
            {
                apply(ease(t));
                yield return null;
            }
            apply(1f);
        }

        /// <summary>Frame-rate independent exponential smoothing factor.</summary>
        public static float Damp(float sharpness, float dt) => 1f - Mathf.Exp(-sharpness * dt);
    }
}
