using UnityEngine;

namespace ElderTree
{
    /// <summary>
    /// The only writer of Time.timeScale. Pause, hit-stop and slow-motion requests are merged here so
    /// effects never fight each other.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class TimeDirector : MonoBehaviour
    {
        public static TimeDirector Instance { get; private set; }

        public bool Paused { get; private set; }
        float hitStopUntil;
        float hitStopScale = 0.03f;
        float slowMoUntil;
        float slowMoScale = 1f;
        float baseFixedDelta;

        void Awake()
        {
            Instance = this;
            baseFixedDelta = Time.fixedDeltaTime;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Time.timeScale = 1f;
        }

        public void SetPaused(bool paused)
        {
            // co-op: the others are still fighting. Menus open over a running world.
            Paused = paused && !Coop.InStage;
            Apply();
        }

        /// <summary>Freeze-frame on impact. Longer requests win; uses unscaled time.</summary>
        public static void HitStop(float seconds, float scale = 0.03f)
        {
            if (Instance == null || seconds <= 0f) return;
            float until = Time.unscaledTime + seconds;
            if (until > Instance.hitStopUntil)
            {
                Instance.hitStopUntil = until;
                Instance.hitStopScale = scale;
            }
        }

        public static void SlowMo(float seconds, float scale)
        {
            if (Instance == null || Coop.InStage) return;    // co-op: one shared world clock, no slow motion
            Instance.slowMoUntil = Time.unscaledTime + seconds;
            Instance.slowMoScale = scale;
        }

        public static bool InHitStop => Instance != null && Time.unscaledTime < Instance.hitStopUntil;

        void Update() => Apply();

        void Apply()
        {
            float scale = 1f;
            if (Paused) scale = 0f;
            else if (Time.unscaledTime < hitStopUntil) scale = hitStopScale;
            else if (Time.unscaledTime < slowMoUntil)
            {
                // ease back to 1 over the final 30% of the slow-mo
                float remain = slowMoUntil - Time.unscaledTime;
                scale = Mathf.Lerp(1f, slowMoScale, Mathf.Clamp01(remain / 0.35f));
            }
            Time.timeScale = scale;
            Time.fixedDeltaTime = baseFixedDelta * Mathf.Max(0.05f, scale);
        }
    }
}
