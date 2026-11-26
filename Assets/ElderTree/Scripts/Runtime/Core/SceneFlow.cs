using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ElderTree
{
    /// <summary>
    /// Scene routing: MainMenu (title, level select, options) -> BossSelect (Megaman-style hub) or straight into a stage
    /// -> back to BossSelect after a win. Every stage you enter is remembered for the menu's Continue.
    /// Bosses without an arena of their own yet fight in the training yard (Arena), summoned on arrival
    /// through <see cref="PendingBoss"/>.
    /// </summary>
    public static class SceneFlow
    {
        public const string MainMenu = "MainMenu";
        public const string BossSelect = "BossSelect";
        public const string Training = "Arena";
        public const string Swamp = "Swamp_Crocodile";
        public const string Beach = "Beach_Shark";
        public const string Forest = "Forest_Druid";
        public const string Cave = "Cave_Fairy";

        /// <summary>Boss to summon when the next arena loads (read and cleared by BossArena).</summary>
        public static string PendingBoss;

        const string LastStageKey = "ElderTree.LastStage";

        public static void Load(string scene, string boss = null)
        {
            PendingBoss = boss;
            if (scene != MainMenu && scene != BossSelect)
            {
                PlayerPrefs.SetString(LastStageKey, scene + "|" + boss);
                PlayerPrefs.Save();
            }
            Time.timeScale = 1f;
            SceneManager.LoadScene(scene);
        }

        /// <summary>The last stage entered (for Continue), or false if none yet.</summary>
        public static bool LastStage(out string scene, out string boss)
        {
            var parts = PlayerPrefs.GetString(LastStageKey, "").Split('|');
            scene = parts[0];
            boss = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
            return !string.IsNullOrEmpty(scene) && Application.CanStreamedLevelBeLoaded(scene);
        }

        public static void ForgetLastStage()
        {
            PlayerPrefs.DeleteKey(LastStageKey);
            PlayerPrefs.Save();
        }

        public static IEnumerator FadeAndLoad(UI.HUD hud, string scene, string boss = null)
        {
            if (hud != null) yield return hud.FadeScreen(1f, 0.8f);
            Load(scene, boss);
        }
    }

    /// <summary>Which bosses have been felled (PlayerPrefs). A boss also counts as felled if its mask is owned.</summary>
    public static class Progress
    {
        const string Key = "ElderTree.Bosses.Felled";
        const string MaskKey = Masks.MaskSystem.OwnedKey;
        const string IntroKey = "ElderTree.Bosses.IntroSeen";

        static HashSet<string> Read(string key)
        {
            var s = new HashSet<string>();
            foreach (var id in PlayerPrefs.GetString(key, "").Split(','))
                if (!string.IsNullOrWhiteSpace(id)) s.Add(id.Trim());
            return s;
        }

        /// <summary>Stable id of a boss: "crocodile", "shark", "druid", "fairy".</summary>
        public static string IdOf(Enemies.BossBase b) => b == null ? "" : b.GetType().Name.Replace("Boss", "").ToLowerInvariant();

        public static bool IsFelled(string bossId, string maskId = null) =>
            Read(Key).Contains(bossId) || (!string.IsNullOrEmpty(maskId) && Read(MaskKey).Contains(maskId));

        public static void MarkFelled(string bossId)
        {
            if (string.IsNullOrEmpty(bossId)) return;
            var s = Read(Key);
            if (!s.Add(bossId)) return;
            PlayerPrefs.SetString(Key, string.Join(",", s));
            PlayerPrefs.Save();
        }

        public static bool OwnsMask(string maskId) => !string.IsNullOrEmpty(maskId) && Read(MaskKey).Contains(maskId);

        /// <summary>Will meeting this boss now play its intro cinematic (the Options choice, then the seen flag)?</summary>
        public static bool IntroPlays(string bossId) => Settings.IntroCinematics switch
        {
            Settings.IntroMode.Never => false,
            Settings.IntroMode.EveryVisit => true,
            _ => !IntroSeen(bossId),
        };

        /// <summary>Has this boss's first-meeting cinematic been seen (it plays once per save)?</summary>
        public static bool IntroSeen(string bossId) => Read(IntroKey).Contains(bossId);

        public static void MarkIntroSeen(string bossId)
        {
            if (string.IsNullOrEmpty(bossId)) return;
            var s = Read(IntroKey);
            if (!s.Add(bossId)) return;
            PlayerPrefs.SetString(IntroKey, string.Join(",", s));
            PlayerPrefs.Save();
        }

        /// <summary>Debug: every boss plays its intro cinematic again on the next meeting.</summary>
        public static void ForgetIntros()
        {
            PlayerPrefs.DeleteKey(IntroKey);
            PlayerPrefs.Save();
        }

        /// <summary>Every boss back to unfelled (and unmet): portraits return to silhouettes, intros play again.
        /// Owned masks also count as felled, so they go too.</summary>
        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.DeleteKey(IntroKey);
            PlayerPrefs.DeleteKey(MaskKey);
            PlayerPrefs.Save();
        }

        /// <summary>Forget every mask: the next pickup plays its transformation cinematic again. Felled bosses stay felled.</summary>
        public static void ForgetMasks()
        {
            // keep the bosses those masks stood for felled
            var felled = Read(Key);
            foreach (var m in Read(MaskKey)) felled.Add(m == "alligator" ? "crocodile" : m);
            PlayerPrefs.SetString(Key, string.Join(",", felled));
            PlayerPrefs.DeleteKey(MaskKey);
            PlayerPrefs.Save();
        }
    }
}
