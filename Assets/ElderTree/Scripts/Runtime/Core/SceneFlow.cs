using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ElderTree
{
    /// <summary>
    /// Scene routing for the Megaman-style boss select: BossSelect -> a boss's arena -> back to BossSelect.
    /// Bosses without an arena of their own yet fight in the training yard (Arena), summoned on arrival
    /// through <see cref="PendingBoss"/>.
    /// </summary>
    public static class SceneFlow
    {
        public const string BossSelect = "BossSelect";
        public const string Training = "Arena";
        public const string Swamp = "Swamp_Crocodile";
        public const string Beach = "Beach_Shark";
        public const string Forest = "Forest_Druid";

        /// <summary>Boss to summon when the next arena loads (read and cleared by BossArena).</summary>
        public static string PendingBoss;

        public static void Load(string scene, string boss = null)
        {
            PendingBoss = boss;
            Time.timeScale = 1f;
            SceneManager.LoadScene(scene);
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
        const string MaskKey = "ElderTree.Masks.Owned";

        static HashSet<string> Read(string key)
        {
            var s = new HashSet<string>();
            foreach (var id in PlayerPrefs.GetString(key, "").Split(','))
                if (!string.IsNullOrWhiteSpace(id)) s.Add(id.Trim());
            return s;
        }

        /// <summary>Stable id of a boss: "crocodile", "shark", "druid".</summary>
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

        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
