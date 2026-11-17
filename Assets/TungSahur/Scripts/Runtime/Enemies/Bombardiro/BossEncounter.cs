using System.Collections;
using System.Collections.Generic;
using TungSahur.Audio;
using TungSahur.Equipment;
using TungSahur.FX;
using TungSahur.Masks;
using TungSahur.Player;
using TungSahur.UI;
using UnityEngine;

namespace TungSahur.Enemies
{
    /// <summary>
    /// Runs the Bombardiro fight: summoned from the pause menu, it clears the training dummies off the
    /// field, shows the boss bar, and handles both endings. On death: YOU DIED, fade, back at the
    /// spawn, boss gone, dummies back. On victory: GREAT ENEMY FELLED, and the wreck smoulders until
    /// the next summon or reset. Several bosses can be configured in <see cref="bosses"/>; the F1
    /// debug menu spawns and despawns any of them, and the fight lasts until every spawned one is
    /// down or gone.
    /// </summary>
    public class BossEncounter : MonoBehaviour
    {
        [Tooltip("Primary boss: the one the pause menu summons.")]
        public BombardiroBoss boss;
        [Tooltip("Every boss the F1 debug menu can spawn / despawn. The primary is added automatically.")]
        public List<BombardiroBoss> bosses = new();
        public PlayerController player;
        public HUD hud;
        public Transform spawnPoint;
        [Header("Reward")]
        [Tooltip("Dropped where the primary boss falls, unless the player already owns it.")]
        public MaskData rewardMask;
        public MaskPickup pickupPrefab;

        public static BossEncounter Instance { get; private set; }
        public bool Active { get; private set; }

        readonly List<TrainingDummy> dummies = new();
        Coroutine ending;

        void Awake()
        {
            Instance = this;
            bosses.RemoveAll(b => b == null);
            if (boss != null && !bosses.Contains(boss)) bosses.Insert(0, boss);
            if (boss == null && bosses.Count > 0) boss = bosses[0];
        }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            dummies.AddRange(FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None));
            foreach (var b in bosses)
            {
                b.Felled += OnBossFelled;
                b.ResetBoss();
            }
            if (player != null) player.Died += OnPlayerDied;
            if (hud != null)
            {
                hud.SummonBossRequested += StartFight;
                hud.ResetTrainingRequested += () => EndFight(false);
            }
        }

        public void StartFight() => Spawn(boss);

        /// <summary>A boss is spawned while it is in the world: fighting, or felled and smouldering.</summary>
        public static bool IsSpawned(BombardiroBoss b) => b != null && b.gameObject.activeSelf;

        /// <summary>Bring a boss into the fight (restarts it if it was already there).</summary>
        public void Spawn(BombardiroBoss b)
        {
            if (b == null) return;
            if (player != null && player.IsDead) return;
            if (ending != null) { StopCoroutine(ending); ending = null; }
            Active = true;
            SetDummies(false);
            b.BeginFight();
            hud?.ShowBossBar(b);
        }

        /// <summary>Send one boss away. The fight ends once no boss is left standing.</summary>
        public void Despawn(BombardiroBoss b)
        {
            if (b == null) return;
            if (IsSpawned(b)) FXManager.Spawn(FXType.Dust, b.transform.position, Quaternion.identity, 2f);
            b.ResetBoss();
            var next = NextLivingBoss();
            if (next == null) { EndFight(false); return; }
            if (hud != null && hud.BossBarTarget == b) hud.ShowBossBar(next);
        }

        public void DespawnAll() => EndFight(false);

        BombardiroBoss NextLivingBoss()
        {
            foreach (var o in bosses) if (o != null && o.IsAlive) return o;
            return null;
        }

        /// <summary>Stop the fight and put the training yard back (no fanfare).</summary>
        public void EndFight(bool keepWreck)
        {
            if (ending != null) { StopCoroutine(ending); ending = null; }
            Active = false;
            if (!keepWreck) ResetAllBosses();
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

        void ResetAllBosses()
        {
            foreach (var b in bosses) if (b != null) b.ResetBoss();
        }

        void OnBossFelled(BombardiroBoss b)
        {
            if (!Active) return;
            // others still in the air: keep fighting, move the bar to the next one
            var next = NextLivingBoss();
            if (next != null) { if (hud != null && hud.BossBarTarget == b) hud.ShowBossBar(next); return; }
            ending = StartCoroutine(Victory());
        }

        IEnumerator Victory()
        {
            yield return new WaitForSeconds(2.6f);
            AudioManager.Play2D(Sfx.GreatEnemyFelled, 1f);
            hud?.ShowBanner("GREAT ENEMY FELLED");
            yield return new WaitForSeconds(2.2f);
            // the mask rises out of the wreck (only the first time: you can't win it twice)
            var ms = MaskSystem.Instance;
            if (rewardMask != null && pickupPrefab != null && (ms == null || !ms.Owns(rewardMask)))
                MaskPickup.Spawn(pickupPrefab, rewardMask, RewardPoint(boss != null ? boss.transform.position : Vector3.zero));
            yield return new WaitForSeconds(0.8f);
            hud?.HideBossBar();
            Active = false;
            yield return new WaitForSeconds(6f);
            SetDummies(true);
            ending = null;
        }

        /// <summary>A clear spot beside the wreck, on the arena side, where the player can walk.</summary>
        Vector3 RewardPoint(Vector3 wreck)
        {
            Vector3 c = boss != null ? boss.arenaCenter : Vector3.zero;
            Vector3 to = c - wreck; to.y = 0f;
            Vector3 p = wreck + (to.sqrMagnitude > 1f ? to.normalized : Vector3.forward) * 6.5f;
            return Blast.Floor(p);
        }

        /// <summary>Debug (F1): drop the reward mask a few steps in front of the player.</summary>
        public void SpawnMaskForTest()
        {
            if (rewardMask == null || pickupPrefab == null || player == null) return;
            Vector3 p = player.transform.position + player.transform.forward * 3f;
            MaskPickup.Spawn(pickupPrefab, rewardMask, Blast.Floor(p));
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
            ResetAllBosses();
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
