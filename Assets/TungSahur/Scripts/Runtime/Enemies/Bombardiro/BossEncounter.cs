using System.Collections;
using System.Collections.Generic;
using TungSahur.Audio;
using TungSahur.FX;
using TungSahur.Player;
using TungSahur.UI;
using UnityEngine;

namespace TungSahur.Enemies
{
    /// <summary>
    /// Runs the Bombardiro fight: summoned from the pause menu, it clears the training dummies off the
    /// field, shows the boss bar, and handles both endings. On death: YOU DIED, fade, back at the
    /// spawn, boss gone, dummies back. On victory: GREAT ENEMY FELLED, and the wreck smoulders until
    /// the next summon or reset.
    /// </summary>
    public class BossEncounter : MonoBehaviour
    {
        public BombardiroBoss boss;
        public PlayerController player;
        public HUD hud;
        public Transform spawnPoint;

        public static BossEncounter Instance { get; private set; }
        public bool Active { get; private set; }

        readonly List<TrainingDummy> dummies = new();
        Coroutine ending;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            dummies.AddRange(FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None));
            if (boss != null)
            {
                boss.Felled += OnBossFelled;
                boss.ResetBoss();
            }
            if (player != null) player.Died += OnPlayerDied;
            if (hud != null)
            {
                hud.SummonBossRequested += StartFight;
                hud.ResetTrainingRequested += () => EndFight(false);
            }
        }

        public void StartFight()
        {
            if (boss == null) return;
            if (ending != null) { StopCoroutine(ending); ending = null; }
            if (player != null && player.IsDead) return;
            Active = true;
            SetDummies(false);
            boss.BeginFight();
            hud?.ShowBossBar(boss);
        }

        /// <summary>Stop the fight and put the training yard back (no fanfare).</summary>
        public void EndFight(bool keepWreck)
        {
            if (ending != null) { StopCoroutine(ending); ending = null; }
            Active = false;
            if (!keepWreck && boss != null) boss.ResetBoss();
            foreach (var l in LivingBomb.Active.ToArray()) l.Despawn();
            hud?.HideBossBar();
            SetDummies(true);
        }

        void SetDummies(bool on)
        {
            foreach (var d in dummies)
            {
                if (d == null || d.gameObject.activeSelf == on) continue;
                FXManager.Spawn(FXType.Dust, d.transform.position, Quaternion.identity, 1.1f);
                d.gameObject.SetActive(on);
                if (on) d.ResetDummy();
            }
        }

        void OnBossFelled(BombardiroBoss b)
        {
            if (!Active) return;
            ending = StartCoroutine(Victory());
        }

        IEnumerator Victory()
        {
            yield return new WaitForSeconds(2.6f);
            AudioManager.Play2D(Sfx.GreatEnemyFelled, 1f);
            hud?.ShowBanner("GREAT ENEMY FELLED");
            yield return new WaitForSeconds(3f);
            hud?.HideBossBar();
            Active = false;
            yield return new WaitForSeconds(6f);
            SetDummies(true);
            ending = null;
        }

        void OnPlayerDied()
        {
            if (!Active) { ending = StartCoroutine(RespawnOnly()); return; }
            ending = StartCoroutine(Defeat());
        }

        IEnumerator Defeat()
        {
            yield return new WaitForSeconds(1.6f);
            AudioManager.Play2D(Sfx.YouDied, 1f);
            hud?.ShowDeath();
            yield return new WaitForSeconds(4.2f);
            if (hud != null) yield return hud.FadeScreen(1f, 0.9f);
            Active = false;
            boss.ResetBoss();
            foreach (var l in LivingBomb.Active.ToArray()) l.Despawn();
            hud?.HideBossBar();
            SetDummies(true);
            Respawn();
            yield return new WaitForSeconds(0.4f);
            if (hud != null) yield return hud.FadeScreen(0f, 1.1f);
            ending = null;
        }

        IEnumerator RespawnOnly()
        {
            yield return new WaitForSeconds(1.6f);
            AudioManager.Play2D(Sfx.YouDied, 1f);
            hud?.ShowDeath();
            yield return new WaitForSeconds(4.2f);
            if (hud != null) yield return hud.FadeScreen(1f, 0.9f);
            Respawn();
            yield return new WaitForSeconds(0.4f);
            if (hud != null) yield return hud.FadeScreen(0f, 1.1f);
            ending = null;
        }

        void Respawn()
        {
            if (player == null) return;
            Vector3 p = spawnPoint != null ? spawnPoint.position : Vector3.zero;
            Quaternion r = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;
            player.Revive(p, r);
            CameraSystem.CameraDirector.Instance?.Recenter();
        }
    }
}
