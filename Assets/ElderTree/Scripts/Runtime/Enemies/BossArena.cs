using System.Collections;
using ElderTree.Masks;
using ElderTree.Player;
using ElderTree.UI;
using UnityEngine;

namespace ElderTree.Enemies
{
    /// <summary>
    /// A boss's own stage (Megaman style). Once the title card is dismissed the boss is summoned; dying
    /// respawns you and the boss comes back for another round; felling it (and picking up its mask, if it
    /// drops one) takes you back to the level select. In the training yard it summons whatever boss
    /// the select screen asked for (<see cref="SceneFlow.PendingBoss"/>), and otherwise does nothing.
    /// The first time you meet a boss that has one, its intro cinematic (<see cref="BossCinematic"/>) plays before the fight;
    /// retries after a death and later visits skip it.
    /// </summary>
    public class BossArena : MonoBehaviour
    {
        [Tooltip("Boss this stage belongs to (part of its name). Empty: only SceneFlow.PendingBoss.")]
        public string bossName;
        public BossEncounter encounter;
        public PlayerController player;
        public HUD hud;
        public float startDelay = 2.2f;
        [Tooltip("Pause after the title card before a first-meeting cinematic starts.")]
        public float introDelay = 0.6f;
        public float rematchDelay = 2.5f;
        public bool returnToSelectAfterVictory = true;
        public float returnDelay = 3.5f;

        BossBase target;
        bool won;
        static BossArena current;

        /// <summary>Why nobody may head back to the level select / stage vote yet (null = free to go). Every player claims
        /// their mask first: an unclaimed mask is still out here (it flies to its player after 15 s), its cinematic is
        /// playing, or (co-op) the boss has fallen: the party then only goes back together, once every player's
        /// AfterVictory has sent StageDone.</summary>
        public static string ReturnHeld
        {
            get
            {
                if (MaskPickup.Active.Count > 0) return "CLAIM THE MASK";
                var cine = MaskSystem.Instance != null ? MaskSystem.Instance.cinematic : null;
                if (cine != null && cine.Playing) return "CLAIM THE MASK";
                // co-op: once the boss falls the party goes back together, when everyone has sent StageDone
                if (Coop.InStage && current != null && current.won) return "THE PARTY IS STILL CLAIMING THEIR MASKS";
                return null;
            }
        }

        void Awake() => current = this;
        void OnDestroy() { if (current == this) current = null; }

        IEnumerator Start()
        {
            string want = !string.IsNullOrEmpty(SceneFlow.PendingBoss) ? SceneFlow.PendingBoss : bossName;
            SceneFlow.PendingBoss = null;
            if (string.IsNullOrEmpty(want) || encounter == null) yield break;
            yield return null;                                   // let the encounter register its roster
            target = encounter.Find(want);
            if (target == null) yield break;
            encounter.FightWon += () => { if (!won) { won = true; StartCoroutine(AfterVictory()); } };
            if (Coop.InStage)
            {
                // co-op: the host calls the boss once everyone has arrived; after a wipe, once everyone stands again.
                // Clients follow the host's encounter.
                if (Coop.IsClient) yield break;
                encounter.WipeFinished += () => { if (!won) StartCoroutine(Rematch()); };
                float waited = 0f;
                while (!Coop.StageReady && waited < 20f) { waited += Time.deltaTime; yield return null; }
                yield return new WaitForSeconds(startDelay + 1.2f);
                if (!won) encounter.Spawn(target);
                yield break;
            }
            if (player != null) player.Died += () => { if (!won) StartCoroutine(Rematch()); };
            while (hud != null && hud.TitleShowing) yield return null;
            // the first time you meet this boss its cinematic plays; after that (deaths, later visits) it just arrives
            if (BossCinematic.ShouldPlay(target) && player != null)
            {
                yield return new WaitForSeconds(introDelay);
                var cine = GetComponent<BossCinematic>();
                if (cine == null) cine = gameObject.AddComponent<BossCinematic>();
                yield return cine.Run(target, player, hud);
                if (!won) encounter.Spawn(target, afterIntro: true);
                yield break;
            }
            yield return new WaitForSeconds(startDelay);
            if (!won) encounter.Spawn(target);
        }

        IEnumerator Rematch()
        {
            // BossEncounter.Defeat: YOU DIED, fade, respawn. Wait for the player to be back on their feet.
            yield return new WaitForSeconds(1f);
            while (player != null && player.IsDead) yield return null;
            while (encounter.Active) yield return null;
            // co-op: the whole party back on its feet (or a few seconds, if someone is slow to come back)
            float waited = 0f;
            while (Coop.InStage && !Coop.AllAlive && waited < 6f) { waited += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(rematchDelay);
            if (!won && player != null && !player.IsDead) encounter.Spawn(target);
        }

        static bool Waiting(Equipment.MaskData m)
        {
            foreach (var p in MaskPickup.Active) if (p != null && p.mask == m) return true;
            return false;
        }

        IEnumerator AfterVictory()
        {
            if (!returnToSelectAfterVictory) yield break;
            var reward = encounter.RewardOf(target);
            var ms = MaskSystem.Instance;
            // a fresh mask has to be collected first (its cinematic plays); then a short breath and home. There is no
            // time limit: left alone, the mask flies to its player by itself (MaskPickup.autoCollectAfter)
            if (reward != null && ms != null)
            {
                float wait = 0f;
                while (!ms.Owns(reward))
                {
                    wait += Time.deltaTime;
                    if (wait > 8f && !Waiting(reward)) break;   // none dropped for this player (already owned, or it failed to spawn)
                    yield return null;
                }
            }
            var cine = FindAnyObjectByType<MaskCinematic>();
            yield return null;
            while (cine != null && cine.Playing) yield return null;
            yield return new WaitForSeconds(returnDelay);
            // co-op: tell the host this player is done; the host takes everyone back to the vote together
            if (Coop.InStage)
            {
                Coop.StageDone();
                hud?.ShowBanner("WAITING FOR THE PARTY");
                yield break;
            }
            SceneFlow.OpenLevelSelect = true;
            yield return SceneFlow.FadeAndLoad(hud, SceneFlow.MainMenu);
        }
    }
}
