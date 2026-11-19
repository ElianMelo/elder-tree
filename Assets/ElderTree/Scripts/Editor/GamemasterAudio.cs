using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ElderTree.EditorTools
{
    /// <summary>
    /// Code-side map of the Gamemaster Audio "Pro Sound Collection" (Assets/Gamemaster Audio - Pro Sound Collection).
    /// All game audio comes from this package; nothing is generated or copied. Paths are relative to <see cref="Root"/>
    /// and have no extension, e.g. <c>"Punches/kick_hard_impact_04"</c>.
    ///
    /// Naming in the package: <c>&lt;family&gt;_&lt;NN&gt;.wav</c>, where NN is a variation number (mostly zero-padded,
    /// a few families aren't: <c>chimes_magic_bell_ding_1</c>). Loops have <c>loop</c> in the name. Everything is
    /// 48 kHz 16-bit (stereo except ~950 mono clips), music is 44.1 kHz. See Docs/AUDIO.md for the browsable catalog
    /// and Docs/audio_package_families.csv for every family with variation count and length.
    /// </summary>
    public static class GamemasterAudio
    {
        public const string Root = "Assets/Gamemaster Audio - Pro Sound Collection";

        /// <summary>Top-level category folders (and the subfolders worth naming). Counts are .wav files.</summary>
        public static class Folder
        {
            public const string Alarms = "Alarms_Beeps_Siren";                     // 36  beeps, sirens, timers
            public const string AnimalVoices = "Animal_Impersonations";            // 1085 human-performed animal calls. Sounded wrong in game: don't use
            public const string NatureAmbience = "Animals_Nature_Ambiences";       // 187 loops: night/wind/rain/swamp/cave/river, birds, thunder
            public const string Backgrounds = "Backgrounds";                       // 34  room tones, machinery, crowd loops
            public const string Cinematic = "Cinematic Sounds";                    // 17  booms, bass drops, rumbles, drones
            public const string Collectibles = "Collectibles_Items_Powerup";       // 148 pickups, jingles (positive / negative), game over
            public const string Comedy = "Comedy_Cartoon";                         // 162 boings, slide whistles, bites, squeaks, viking horns. Cartoonish: not for this game
            public const string Doors = "Doors";                                   // 132 wood/metal/stone doors, creaks, locks, rock drag
            public const string Electricity = "Electricity_Hums";                  // 36  hums, static
            public const string Explosions = "Explosion_Fire_Gas";                 // 86  explosions (large/small/far), fire loops, fuses, gas
            public const string Foley = "Foley";                                   // 198 cloth, grabs, throws, bone snaps, gear rattle
            public const string Footsteps = "Footsteps";                           // 581 walk/run/land/slide per surface
            public const string Weapons = "Guns_Weapons";
            public const string BowArrow = "Guns_Weapons/Bow_Arrow";               // 51  draw, shoot, nock
            public const string Bullets = "Guns_Weapons/Bullets";                  // 263 flybys + impacts per surface (usable for any projectile)
            public const string Guns = "Guns_Weapons/Guns";                        // 300 firearms, cannon / tank shots
            public const string GunsSilenced = "Guns_Weapons/Guns_Silenced";       // 98
            public const string Blades = "Guns_Weapons/Knife_Sword_Pick";          // 44  sword hits, unsheathe, knife, pickaxe
            public const string Taser = "Guns_Weapons/Taser";                      // 4
            public const string FunWeapons = "Guns_Weapons/Θ Fun Weapons Θ";       // 7
            public const string Impacts = "Impacts_Smashable";                     // 39  rock slams, breaks, debris, glass
            public const string Magic = "Magic_Spells";                            // 395 charges, blasts, fire/ice/water/nature/dark spells, chimes
            public const string Metal = "Metal";                                   // 179 impacts, creaks, scrapes, rattles
            public const string Misc = "Miscellaneous";
            public const string Bells = "Miscellaneous/Bell";                      // 21
            public const string Gravel = "Miscellaneous/Gravel Dirt Stones";       // 14
            public const string DeepThud = "Miscellaneous/Object Deep Thud";       // 10
            public const string Wood = "Miscellaneous/Wood";                       // 52  wood block knocks, branch break / rustle
            public const string Punches = "Punches";                               // 68  punches, kicks, blocks, bat hits
            public const string SnowIce = "Snow_Ice";                              // 51
            public const string Mechanisms = "Switches_Buttons_Gears_Levers";      // 107 switches, ratchets, cranks, gears
            public const string UI = "User_Interface_Menu";                        // 152 clicks, scroll, confirm, cancel, error, popups. Bright app-style: not for this game (menus use Doors latches / Switches clicks)
            public const string Vehicles = "Vehicles_Engines_Motors";              // 49  engine loops, skids, horns, trains
            public const string Voice = "Voice";
            public const string TrollVoice = "Voice/TrollMonster";                 // 194 big-creature voice set. Human voices: don't use
            public const string GoblinVoice = "Voice/GoblinFairy";                 // 214 small-creature voice set
            public const string MonsterVoice = "Voice/Monster";                    // 4
            public const string GhostVoice = "Voice/Ghost";                        // 12
            public const string Water = "Water";                                   // 73
            public const string Whooshes = "Whooshes";                             // 81  swishes, deep whooshes, weapon swing / spin
            public const string Zombie = "Zombie";                                 // 95
            public const string Music = "Ω_Bonus_Music_16bit44kOnly";              // 33  music tracks and stings
        }

        /// <summary>Asset path for a package-relative clip path (no extension).</summary>
        public static string PathOf(string rel) => $"{Root}/{rel}.wav";

        /// <summary>Load one clip. Logs an error (and returns null) if the path is wrong.</summary>
        public static AudioClip Load(string rel)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(PathOf(rel));
            if (clip == null) Debug.LogError($"[GamemasterAudio] Missing clip: {PathOf(rel)}");
            return clip;
        }

        /// <summary>Load several clips, dropping missing ones.</summary>
        public static AudioClip[] Load(params string[] rels) => rels.Select(Load).Where(c => c != null).ToArray();

        /// <summary>Every variation of a family, e.g. <c>Family(Folder.Punches, "kick_hard_impact")</c> → kick_hard_impact_01..08.</summary>
        public static AudioClip[] Family(string folder, string family)
        {
            string dir = $"{Root}/{folder}";
            if (!Directory.Exists(dir)) { Debug.LogError($"[GamemasterAudio] Missing folder: {dir}"); return Array.Empty<AudioClip>(); }
            var rx = new Regex("^" + Regex.Escape(family) + @"(_[0-9]+[a-z]?)?\.wav$", RegexOptions.IgnoreCase);
            return Directory.GetFiles(dir, "*.wav")
                .Select(Path.GetFileName).Where(f => rx.IsMatch(f)).OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => AssetDatabase.LoadAssetAtPath<AudioClip>($"{dir}/{f}")).Where(c => c != null).ToArray();
        }

        /// <summary>How a clip is used, which decides its import settings.</summary>
        public enum Use { OneShot, Loop3D, Ambience }

        /// <summary>
        /// Apply game import settings to the clips we actually use (the rest of the package keeps its defaults):
        /// one-shots and positional loops are forced to mono (the AudioManager pans them in 3D), short clips decompress
        /// on load, long loops stream. Only re-imports when something changed.
        /// </summary>
        public static void Configure(IEnumerable<AudioClip> clips, Use use)
        {
            foreach (var clip in clips)
            {
                if (clip == null) continue;
                string path = AssetDatabase.GetAssetPath(clip);
                if (AssetImporter.GetAtPath(path) is not AudioImporter imp) continue;
                bool mono = use != Use.Ambience;
                var s = imp.defaultSampleSettings;
                var load = use switch
                {
                    Use.Ambience => AudioClipLoadType.Streaming,
                    Use.Loop3D => AudioClipLoadType.CompressedInMemory,
                    _ => clip.length > 3f ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad,
                };
                // "Normalize" (only exposed via serialization) peak-normalizes the mono downmix, which would undo the
                // loudness matching in SoundPicks; keep clips at their mastered level.
                var so = new SerializedObject(imp);
                var normalize = so.FindProperty("m_Normalize");
                bool normalized = normalize != null && normalize.boolValue;
                bool dirty = normalized || imp.forceToMono != mono || s.loadType != load || s.compressionFormat != AudioCompressionFormat.Vorbis
                             || Mathf.Abs(s.quality - 0.7f) > 0.001f || !s.preloadAudioData;
                if (!dirty) continue;
                if (normalized) { normalize.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
                imp.forceToMono = mono;
                s.loadType = load;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.7f;
                s.preloadAudioData = true;
                imp.defaultSampleSettings = s;
                imp.SaveAndReimport();
            }
        }
    }
}
