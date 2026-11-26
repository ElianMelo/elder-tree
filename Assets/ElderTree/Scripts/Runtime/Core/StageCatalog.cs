using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ElderTree
{
    /// <summary>
    /// Every stage the level select offers, in order: where it is, who rules it, what it looks like. One asset in
    /// Resources (built by Elder Tree/9l) so the main menu and the in-game level select (F2, pause menu, after a
    /// victory) read the same list without each scene wiring it.
    /// </summary>
    [CreateAssetMenu(menuName = "Elder Tree/Stage Catalog")]
    public class StageCatalog : ScriptableObject
    {
        [Serializable]
        public class Stage
        {
            public string region;             // "Drowned Garrison"
            public string boss;               // "Crocodile"; empty = no boss (training grounds)
            public string bossId;             // progress id ("crocodile")
            public string maskId;             // owning this mask also counts as felled
            public string maskName;           // "Alligator Mask"
            public string epithet;
            [TextArea(2, 5)] public string description;
            public string scene;
            public string bossArg;            // SceneFlow.PendingBoss for shared arenas
            public Texture2D vista;           // wide in-game render behind the list
            public Texture2D portrait;        // in-game bust, shown once felled
            public Texture2D silhouette;      // shown until then

            public bool HasBoss => !string.IsNullOrEmpty(bossId);
            public bool Felled => HasBoss && Progress.IsFelled(bossId, maskId);
        }

        public List<Stage> stages = new();
        [Tooltip("Styles of the level select view.")]
        public StyleSheet style;

        public const string ResourceName = "StageCatalog";
        static StageCatalog cached;

        public static StageCatalog Instance => cached != null ? cached : cached = Resources.Load<StageCatalog>(ResourceName);

        public Stage Find(string scene) => stages.Find(s => s.scene == scene);
    }
}
