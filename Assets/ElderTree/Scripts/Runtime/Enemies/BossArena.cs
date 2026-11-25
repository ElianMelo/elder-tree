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
    /// drops one) takes you back to the boss select screen. In the training yard it summons whatever boss
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

        IEnumerator Start()
        {
            string want = !string.IsNullOrEmpty(SceneFlow.PendingBoss) ? SceneFlow.PendingBoss : bossName;
            SceneFlow.PendingBoss = null;
            if (string.IsNullOrEmpty(want) || encounter == null) yield break;
            yield return null;                                   // let the encounter register its roster
            target = encounter.Find(want);
            if (target == null) yield break;
            encounter.FightWon += () => { if (!won) { won = true; StartCoroutine(AfterVictory()); } };
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
            yield return new WaitForSeconds(rematchDelay);
            if (!won && player != null && !player.IsDead) encounter.Spawn(target);
        }

        IEnumerator AfterVictory()
        {
            if (!returnToSelectAfterVictory) yield break;
            var reward = encounter.RewardOf(target);
            var ms = MaskSystem.Instance;
            // a fresh mask has to be collected first (its cinematic plays); then a short breath and home
            if (reward != null && ms != null)
            {
                float wait = 0f;
                while (!ms.Owns(reward) && wait < 90f) { wait += Time.deltaTime; yield return null; }
            }
            var cine = FindAnyObjectByType<MaskCinematic>();
            yield return null;
            while (cine != null && cine.Playing) yield return null;
            yield return new WaitForSeconds(returnDelay);
            yield return SceneFlow.FadeAndLoad(hud, SceneFlow.BossSelect);
        }
    }
}
