using ElderTree.Enemies;
using ElderTree.InputHandling;
using ElderTree.Player;
using ElderTree.UI;
using UnityEngine;

namespace ElderTree
{
    /// <summary>Scene glue: cursor, pause, settings, reset/quit, out-of-bounds recovery, frame pacing.</summary>
    public class GameBootstrap : MonoBehaviour
    {
        public PlayerController player;
        public HUD hud;
        public Transform spawnPoint;
        public int targetFrameRate = 120;

        PlayerInputReader input;

        void Awake()
        {
            Application.targetFrameRate = targetFrameRate;
            QualitySettings.vSyncCount = 0;
        }

        void Start()
        {
            input = PlayerInputReader.Instance;
            Settings.Load();
            if (hud != null)
            {
                hud.PauseChanged += OnPause;
                hud.ResetTrainingRequested += ResetTraining;
                hud.QuitRequested += Quit;
                hud.BossSelectRequested += () => StartCoroutine(SceneFlow.FadeAndLoad(hud, SceneFlow.BossSelect));
                hud.MainMenuRequested += () => StartCoroutine(SceneFlow.FadeAndLoad(hud, SceneFlow.MainMenu));
            }
            LockCursor(true);
        }

        void OnPause(bool paused)
        {
            TimeDirector.Instance?.SetPaused(paused);
            input?.SetGameplayEnabled(!paused);
            if (!paused) input?.ClearAll();
            LockCursor(!paused);
        }

        static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void ResetTraining()
        {
            foreach (var d in FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None)) d.ResetDummy();
            player.stats.RestoreAll();
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void Update()
        {
            if (player != null && player.transform.position.y < -25f && spawnPoint != null)
                player.motor.Teleport(spawnPoint.position, spawnPoint.rotation);
            // regain the cursor after alt-tab
            if (Application.isFocused && (hud == null || !hud.MenuOpen) && Cursor.lockState != CursorLockMode.Locked &&
                UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
                LockCursor(true);
        }
    }
}
