using System.Collections;
using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.InputHandling;
using ElderTree.Player;
using ElderTree.UI;
using Unity.Cinemachine;
using UnityEngine;

namespace ElderTree.Enemies
{
    /// <summary>
    /// A boss's first-meeting cinematic. <see cref="BossArena"/> runs it once per save (<see cref="Progress.IntroSeen"/>),
    /// the first time you reach the boss's stage; retries after a death and later visits go straight to the fight.
    /// While it plays, the camera is taken from Cinemachine and the boss's <see cref="BossBase.IntroCinematic"/> moves it
    /// with <see cref="Shot"/>. The player stands still and can't be hurt, and the HUD letterboxes. It can be skipped after a
    /// moment (A / Enter or B / Esc): the screen dips to black and the boss jumps to where the cinematic would have left it.
    /// </summary>
    public class BossCinematic : MonoBehaviour
    {
        [Tooltip("Seconds before a press can skip.")]
        public float skipAfter = 1.2f;

        public bool Playing { get; private set; }
        public bool Skipped { get; private set; }
        public Camera Cam { get; private set; }
        public PlayerController Player { get; private set; }
        public HUD Hud { get; private set; }

        CinemachineBrain brain;
        float fovWas;

        /// <summary>Would meeting this boss now play its cinematic?</summary>
        public static bool ShouldPlay(BossBase b) => b != null && b.HasIntroCinematic && !Progress.IntroSeen(Progress.IdOf(b));

        public IEnumerator Run(BossBase boss, PlayerController player, HUD hud)
        {
            Playing = true;
            Skipped = false;
            Player = player;
            Hud = hud;
            var input = PlayerInputReader.Instance;
            input?.SetGameplayEnabled(false);
            player.EnterCinematic();
            hud?.SetCinematic(true);
            Cam = Camera.main;
            brain = Cam != null ? Cam.GetComponent<CinemachineBrain>() : null;
            if (brain) brain.enabled = false;
            fovWas = Cam != null ? Cam.fieldOfView : 60f;
            // seen as soon as it starts: quitting halfway doesn't make you sit through it again
            Progress.MarkIntroSeen(Progress.IdOf(boss));

            bool done = false;
            StartCoroutine(Play(boss.IntroCinematic(this), () => done = true));
            float t = 0f;
            while (!done)
            {
                t += Time.deltaTime;
                if (t > skipAfter && input != null && !(hud != null && hud.MenuOpen) && (input.CancelPressed || input.SubmitPressed))
                {
                    Skipped = true;
                    break;
                }
                yield return null;
            }

            if (Skipped)
            {
                AudioManager.Play2D(Sfx.UIBack, 0.5f);
                if (hud != null) yield return hud.FadeScreen(1f, 0.35f);
                StopAllCoroutines();               // the boss's choreography (and anything it nested) runs on this component
                boss.SkipIntroCinematic();
            }

            if (Cam != null) Cam.fieldOfView = fovWas;
            if (brain) brain.enabled = true;
            CameraDirector.Instance?.Recenter();
            hud?.SetCinematic(false);
            player.ExitCinematic();
            input?.ClearAll();
            input?.SetGameplayEnabled(!(hud != null && hud.MenuOpen));
            if (Skipped && hud != null) hud.StartCoroutine(hud.FadeScreen(0f, 0.6f));
            Playing = false;
        }

        static IEnumerator Play(IEnumerator routine, System.Action onDone)
        {
            yield return routine;
            onDone();
        }

        // ------------------------------------------------------------------ helpers for the choreography
        /// <summary>Put the camera at <paramref name="pos"/> looking at <paramref name="lookAt"/>.</summary>
        public void Shot(Vector3 pos, Vector3 lookAt, float fov, float roll = 0f)
        {
            if (Cam == null) return;
            Vector3 d = lookAt - pos;
            if (d.sqrMagnitude < 1e-4f) d = Vector3.forward;
            Cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(d) * Quaternion.Euler(0f, 0f, roll));
            Cam.fieldOfView = fov;
        }

        /// <summary>Turn the player to watch <paramref name="target"/> (they stand still through the cinematic).</summary>
        public void PlayerWatch(Vector3 target)
        {
            if (Player == null) return;
            Vector3 d = target - Player.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 1e-4f) Player.transform.rotation = Quaternion.LookRotation(d);
        }

        void OnDisable()
        {
            if (!Playing) return;
            if (brain) brain.enabled = true;
            if (Cam != null) Cam.fieldOfView = fovWas;
            Playing = false;
        }
    }
}
