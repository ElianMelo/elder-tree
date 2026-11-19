using System.Linq;
using System.Reflection;
using TungSahur.Audio;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace TungSahur.EditorTools
{
    /// <summary>
    /// The game's AudioMixer: Master with one group per <see cref="AudioLayer"/> (Music, Ambience, SFX, UI, Footsteps).
    /// Created once if missing (Unity has no public API for building mixers, so this goes through the editor's own
    /// AudioMixerController by reflection); after that it's a normal asset: balance the layers, add effects or ducking
    /// in Window > Audio > Audio Mixer and rebuilds keep it.
    /// </summary>
    public static class GameMixer
    {
        public const string Path = "Assets/TungSahur/Audio/TungSahur.mixer";
        static readonly string[] Groups = { "Music", "Ambience", "SFX", "UI", "Footsteps" };

        public static AudioMixer Ensure()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(Path);
            if (mixer != null) return mixer;
            try
            {
                var asm = typeof(Editor).Assembly;
                var ctlType = asm.GetType("UnityEditor.Audio.AudioMixerController");
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
                var ctl = ctlType.GetMethod("CreateMixerControllerAtPath", all).Invoke(null, new object[] { Path });
                var master = ctlType.GetProperty("masterGroup", all).GetValue(ctl);
                var create = ctlType.GetMethod("CreateNewGroup", all);
                var add = ctlType.GetMethod("AddChildToParent", all);
                foreach (var name in Groups)
                {
                    var g = create.Invoke(ctl, new object[] { name, false });
                    add.Invoke(ctl, new[] { g, master });
                }
                EditorUtility.SetDirty((Object)ctl);
                AssetDatabase.SaveAssets();
                AddDefaultView();
                mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(Path);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[GameMixer] Could not create {Path} ({e.GetBaseException().Message}); audio plays without mixer routing.");
            }
            return mixer;
        }

        /// <summary>
        /// A mixer made this way has no group view, and the Mixer window only lists groups that are in a view. Write
        /// the default "View" with every group, as Unity's own Create > Audio Mixer does.
        /// </summary>
        static void AddDefaultView()
        {
            string file = System.IO.Path.GetFullPath(Path);
            string text = System.IO.File.ReadAllText(file);
            const string empty = "m_AudioMixerGroupViews: []";
            if (!text.Contains(empty)) return;
            string nl = text.Contains("\r\n") ? "\r\n" : "\n";
            var guids = System.Text.RegularExpressions.Regex.Matches(text, @"m_GroupID: ([0-9a-f]{32})")
                .Select(m => m.Groups[1].Value).Distinct();
            var view = new System.Text.StringBuilder("m_AudioMixerGroupViews:" + nl + "  - guids:" + nl);
            foreach (var g in guids) view.Append("    - ").Append(g).Append(nl);
            view.Append("    name: View");
            System.IO.File.WriteAllText(file, text.Replace(empty, view.ToString()));
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceUpdate);
        }

        public static AudioMixerGroup Group(AudioMixer mixer, string name) =>
            mixer == null ? null : mixer.FindMatchingGroups(name).FirstOrDefault(g => g.name == name);

        /// <summary>Route every AudioManager layer to its mixer group.</summary>
        public static void Assign(AudioManager am)
        {
            var m = Ensure();
            am.musicOutput = Group(m, "Music");
            am.ambienceOutput = Group(m, "Ambience");
            am.sfxOutput = Group(m, "SFX");
            am.uiOutput = Group(m, "UI");
            am.footstepOutput = Group(m, "Footsteps");
        }

        /// <summary>The group a free-standing loop (boss engine, dig) should play through.</summary>
        public static AudioMixerGroup Sfx => Group(Ensure(), "SFX");
    }
}
