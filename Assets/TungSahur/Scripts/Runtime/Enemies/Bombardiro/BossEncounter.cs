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
    /// Runs the boss fights: summoned from the pause menu, it clears the training dummies off the
    /// field, shows the boss bar, and handles both endings. On death: YOU DIED, fade, back at the
    /// spawn, boss gone, dummies back. On victory: GREAT ENEMY FELLED, and the wreck smoulders until
    /// the next summon or reset. Several bosses can be configured in <see cref="bosses"/>; the F1
    /// debug menu spawns and despawns any of them, and the fight lasts until every spawned one is
    /// down or gone.
    /// </summary>
    public class BossEncounter : MonoBehaviour
    {
        [Tooltip("Primary boss: it drops the reward mask.")]
        public BossBase boss;
        [Tooltip("Every boss the pause menu and the F1 debug menu can spawn / despawn. The primary is added automatically.")]
        public List<BossBase> bosses = new();
        public PlayerController player;
        public HUD hud;
        public Transform spawnPoint;
        [Header("Reward")]
        [Tooltip("Dropped where the primary boss falls (when it has no reward of its own), unless the player already owns it. " +
                 "Other bosses drop their own BossBase.rewardMask.")]
        public MaskData rewardMask;
        public MaskPickup pickupPrefab;

        public static BossEncounter Instance { get; private set; }
        public bool Active { get; private set; }

        readonly List<TrainingDummy> dummies = new();
        Coroutine ending;
        bool primaryFelled;
        readonly List<BossBase> felled = new();

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
                hud.SummonBossRequested += Summon;
                hud.ResetTrainingRequested += () => EndFight(false);
            }
        }

        public void StartFight() => Spawn(boss);

        /// <summary>Pause menu: summon boss <paramref name="index"/> of the roster on its own.</summary>
        public void Summon(int index)
        {
            if (index < 0 || index >= bosses.Count || bosses[index] == null) return;
            var b = bosses[index];
            foreach (var o in bosses) if (o != null && o != b && IsSpawned(o)) o.ResetBoss();
            Spawn(b);
        }

        /// <summary>Find a boss in the roster by (part of) its name, e.g. "Tralalero".</summary>
        public BossBase Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return boss;
            foreach (var b in bosses)
                if (b != null && (b.DisplayName.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  b.name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0)) return b;
            return null;
        }

        /// <summary>A boss is spawned while it is in the world: fighting, or felled and smouldering.</summary>
        public static bool IsSpawned(BossBase b) => b != null && b.gameObject.activeSelf;

        /// <summary>Bring a boss into the fight (restarts it if it was already there).</summary>
        public void Spawn(BossBase b)
        {
            if (b == null) return;
            if (player != null && player.IsDead) return;
            if (ending != null) { StopCoroutine(ending); ending = null; }
            if (!Active) { primaryFelled = false; felled.Clear(); }
            Active = true;
            SetDummies(false);
            b.BeginFight();
            hud?.ShowBossBar(b);
        }

        /// <summary>Send one boss away. The fight ends once no boss is left standing.</summary>
        public void Despawn(BossBase b)
        {
            if (b == null) return;
            if (IsSpawned(b)) FXManager.Spawn(FXType.Dust, b.transform.position, Quaternion.identity, 2f);
            b.ResetBoss();
            var next = NextLivingBoss();
            if (next == null) { EndFight(false); return; }
            if (hud != null && hud.BossBarTarget == b) hud.ShowBossBar(next);
        }

        public void DespawnAll() => EndFight(false);

        BossBase NextLivingBoss()
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

        void OnBossFelled(BossBase b)
        {
            if (!Active) return;
            if (b == boss) primaryFelled = true;
            if (!felled.Contains(b)) felled.Add(b);
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
            // each felled boss's mask rises out of its wreck (only the first time: you can't win it twice)
            var ms = MaskSystem.Instance;
            foreach (var b in felled)
            {
                var reward = RewardOf(b);
                if (reward == null || pickupPrefab == null || (ms != null && ms.Owns(reward))) continue;
                MaskPickup.Spawn(pickupPrefab, reward, RewardPoint(b));
                yield return new WaitForSeconds(0.35f);
            }
            felled.Clear();
            yield return new WaitForSeconds(0.8f);
            hud?.HideBossBar();
            Active = false;
            yield return new WaitForSeconds(6f);
            SetDummies(true);
            ending = null;
        }

        /// <summary>The mask a boss drops: its own, or the encounter's reward for the primary boss.</summary>
        public MaskData RewardOf(BossBase b) => b == null ? null : b.rewardMask != null ? b.rewardMask : b == boss ? rewardMask : null;

        /// <summary>A clear spot beside the wreck, on the arena side, where the player can walk.</summary>
        Vector3 RewardPoint(BossBase b)
        {
            Vector3 wreck = b != null ? b.transform.position : Vector3.zero;
            Vector3 c = b != null ? b.ArenaCenter : Vector3.zero;
            Vector3 to = c - wreck; to.y = 0f;
            Vector3 p = wreck + (to.sqrMagnitude > 1f ? to.normalized : Vector3.forward) * 6.5f;
            return Blast.Floor(p);
        }

        /// <summary>Debug (F1): drop the reward mask a few steps in front of the player.</summary>
        public void SpawnMaskForTest() => SpawnMaskForTest(rewardMask);

        /// <summary>Debug (F1): drop a given mask a few steps in front of the player.</summary>
        public void SpawnMaskForTest(MaskData m)
        {
            if (m == null || pickupPrefab == null || player == null) return;
            Vector3 p = player.transform.position + player.transform.forward * 3f;
            MaskPickup.Spawn(pickupPrefab, m, Blast.Floor(p));
        }

        /// <summary>Every mask a boss in the roster can drop (debug menu).</summary>
        public IEnumerable<MaskData> AllRewards()
        {
            var seen = new HashSet<MaskData>();
            if (rewardMask != null && seen.Add(rewardMask)) yield return rewardMask;
            foreach (var b in bosses)
            {
                var r = RewardOf(b);
                if (r != null && seen.Add(r)) yield return r;
            }
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
