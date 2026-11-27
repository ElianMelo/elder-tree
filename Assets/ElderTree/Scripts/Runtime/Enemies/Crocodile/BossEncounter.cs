using System.Collections;
using System.Collections.Generic;
using ElderTree.Audio;
using ElderTree.Equipment;
using ElderTree.FX;
using ElderTree.Masks;
using ElderTree.Player;
using ElderTree.UI;
using UnityEngine;

namespace ElderTree.Enemies
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
        /// <summary>Raised once the victory sequence is over (banner shown, rewards dropped).</summary>
        public event System.Action FightWon;

        // co-op: what the host's encounter does, so the network layer can tell the clients
        /// <summary>Host: a boss entered the fight (<c>afterIntro</c>: from its cinematic).</summary>
        public event System.Action<BossBase, bool> Spawned;
        /// <summary>Host: the fight was called off (no fanfare; <c>keepWreck</c> leaves felled bosses where they lie).</summary>
        public event System.Action<bool> FightEnded;
        /// <summary>Host: every tree fell. The wipe sequence starts everywhere.</summary>
        public event System.Action Wiped;
        /// <summary>The wipe is over here: everyone is back at the start and the boss is gone.</summary>
        public event System.Action WipeFinished;
        bool wiping;

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
                if (Coop.IsClient && Coop.InStage) b.BeginMirror();     // co-op: the host runs the bosses
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
            if (Coop.IsClient && Coop.InStage) return;                 // co-op: only the host calls bosses
            if (index < 0 || index >= bosses.Count || bosses[index] == null) return;
            var b = bosses[index];
            foreach (var o in bosses) if (o != null && o != b && IsSpawned(o)) o.ResetBoss();
            Spawn(b);
        }

        /// <summary>Find a boss in the roster by (part of) its name, e.g. "Shark".</summary>
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

        /// <summary>Bring a boss into the fight (restarts it if it was already there). <paramref name="afterIntro"/>: it was just
        /// staged by its first-meeting cinematic, so it starts from there instead of making its usual entrance.</summary>
        public void Spawn(BossBase b, bool afterIntro = false)
        {
            if (b == null) return;
            if (Coop.IsClient && Coop.InStage) return;                 // co-op: clients follow the host (MirrorSpawn)
            if (Coop.InStage ? !Coop.AnyAlive : player != null && player.IsDead) return;
            Present(b);
            b.ApplyPartyScaling();                                     // more health and less rest per extra player
            if (afterIntro) b.BeginFightAfterIntro(); else b.BeginFight();
            Spawned?.Invoke(b, afterIntro);
        }

        /// <summary>Co-op client: the boss entered the fight on the host. Everything but the boss itself (the host poses it).</summary>
        public void MirrorSpawn(BossBase b) { if (b != null) Present(b); }

        /// <summary>Co-op client: the host called the fight off.</summary>
        public void MirrorEndFight(bool keepWreck) => EndFightLocal(keepWreck);

        /// <summary>The fight starts here: music, bar, the dummies leave, special charges reset.</summary>
        void Present(BossBase b)
        {
            if (ending != null && !wiping) { StopCoroutine(ending); ending = null; }
            if (!Active)
            {
                primaryFelled = false;
                felled.Clear();
                // special charges start empty each fight, and only so many specials are allowed per boss
                if (player != null) player.stats.BeginBossFight();
            }
            Active = true;
            AudioManager.SetMusic(MusicMood.Boss);
            SetDummies(false);
            hud?.ShowBossBar(b);
        }

        /// <summary>Index of a boss in the roster (the same on every machine: the scene defines it).</summary>
        public int IndexOf(BossBase b) => bosses.IndexOf(b);
        public BossBase At(int i) => i >= 0 && i < bosses.Count ? bosses[i] : null;
        public IReadOnlyList<BossBase> Roster => bosses;

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
            if (Coop.IsClient && Coop.InStage) return;                 // co-op: only the host calls the fight off
            EndFightLocal(keepWreck);
            FightEnded?.Invoke(keepWreck);
        }

        void EndFightLocal(bool keepWreck)
        {
            if (ending != null) { StopCoroutine(ending); ending = null; }
            Active = false;
            if (player != null) player.stats.EndBossFight();
            AudioManager.SetMusic(MusicMood.Explore);
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
                using (Coop.LocalOnly()) FXManager.Spawn(FXType.Dust, d.transform.position, Quaternion.identity, 1.1f);
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
            Progress.MarkFelled(Progress.IdOf(b));
            if (b == boss) primaryFelled = true;
            if (!felled.Contains(b)) felled.Add(b);
            // others still in the air: keep fighting, move the bar to the next one
            var next = NextLivingBoss();
            if (next != null) { if (hud != null && hud.BossBarTarget == b) hud.ShowBossBar(next); return; }
            // the fight music falls away under the victory sting; the arena's music returns once things settle
            AudioManager.SetMusic(MusicMood.None);
            ending = StartCoroutine(Victory());
        }

        IEnumerator Victory()
        {
            yield return new WaitForSeconds(2.6f);
            AudioManager.Play2D(Sfx.GreatEnemyFelled, 1f);
            hud?.ShowBanner("GREAT ENEMY FELLED");
            yield return new WaitForSeconds(2.2f);
            // co-op: a tree that fell before the end stands back up for the spoils
            if (Coop.InStage && player != null && player.IsDead)
            {
                if (hud != null) yield return hud.FadeScreen(1f, 0.6f);
                Respawn();
                if (hud != null) yield return hud.FadeScreen(0f, 0.8f);
            }
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
            if (player != null) player.stats.EndBossFight();
            AudioManager.SetMusic(MusicMood.Explore);
            FightWon?.Invoke();
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
            if (Coop.InStage) { StartCoroutine(CoopDown()); return; }
            ending = StartCoroutine(Defeat());
        }

        // ================================================================== co-op: down, spectate, wipe
        /// <summary>Your tree fell but the others fight on: YOU DIED, then the camera follows a living ally until the
        /// fight ends (victory revives you; a wipe sends everyone back).</summary>
        IEnumerator CoopDown()
        {
            yield return new WaitForSeconds(1.6f);
            if (player == null || !player.IsDead || wiping) yield break;
            AudioManager.Play2D(Sfx.YouDied, 1f);
            hud?.ShowDeath();
            yield return new WaitForSeconds(3.4f);
            while (player != null && player.IsDead && Active && !wiping)
            {
                Spectate(NextAlly());
                yield return new WaitForSeconds(0.5f);
            }
        }

        PlayerController NextAlly()
        {
            PlayerController best = null;
            float bd = float.MaxValue;
            foreach (var pc in PlayerController.All)
            {
                if (pc == null || pc == player || pc.IsDead) continue;
                float d = (pc.transform.position - player.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = pc; }
            }
            return best;
        }

        static void Spectate(PlayerController pc)
        {
            var cd = CameraSystem.CameraDirector.Instance;
            if (cd == null) return;
            var t = pc != null ? pc.transform : PlayerController.Instance != null ? PlayerController.Instance.transform : null;
            if (t != null && cd.player != t) cd.player = t;
        }

        void Update()
        {
            // co-op host: the last tree standing fell
            if (Coop.InStage && Coop.IsHost && Active && !wiping && ending == null && !Coop.AnyAlive)
            {
                Wiped?.Invoke();
                RunWipe();
            }
        }

        /// <summary>Co-op: the whole party fell. Everyone fades out, the boss leaves, and everyone stands up at the start.</summary>
        public void RunWipe()
        {
            if (wiping) return;
            if (ending != null) StopCoroutine(ending);
            ending = StartCoroutine(Wipe());
        }

        IEnumerator Wipe()
        {
            wiping = true;
            yield return new WaitForSeconds(2.4f);
            if (hud != null) yield return hud.FadeScreen(1f, 0.9f);
            Active = false;
            if (player != null) player.stats.EndBossFight();
            if (Coop.IsHost)
            {
                ResetAllBosses();
                foreach (var l in LivingBomb.Active.ToArray()) l.Despawn();
            }
            hud?.HideBossBar();
            SetDummies(true);
            Respawn();
            yield return new WaitForSeconds(0.4f);
            if (hud != null) yield return hud.FadeScreen(0f, 1.1f);
            ending = null;
            wiping = false;
            WipeFinished?.Invoke();
        }

        IEnumerator Defeat()
        {
            yield return new WaitForSeconds(1.6f);
            AudioManager.Play2D(Sfx.YouDied, 1f);
            hud?.ShowDeath();
            yield return new WaitForSeconds(4.2f);
            if (hud != null) yield return hud.FadeScreen(1f, 0.9f);
            Active = false;
            if (player != null) player.stats.EndBossFight();
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
            p += SpawnOffset(r);
            player.Revive(p, r);
            Spectate(player);
            CameraSystem.CameraDirector.Instance?.Recenter();
        }

        /// <summary>Co-op: each player stands at their own spot beside the grace (slot 0 at the grace itself).</summary>
        public static Vector3 SpawnOffset(Quaternion facing)
        {
            var me = Coop.InStage ? Coop.Local : null;
            if (me == null || me.slot == 0) return Vector3.zero;
            Vector3 local = me.slot switch
            {
                1 => new Vector3(1.8f, 0f, 0f),
                2 => new Vector3(-1.8f, 0f, 0f),
                _ => new Vector3(0f, 0f, -1.8f),
            };
            return facing * local;
        }
    }
}
