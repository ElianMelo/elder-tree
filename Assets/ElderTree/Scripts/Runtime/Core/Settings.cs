using ElderTree.FX;
using ElderTree.InputHandling;
using UnityEngine;

namespace ElderTree
{
    /// <summary>
    /// Player options, persisted in PlayerPrefs and pushed to the live systems on Apply().
    /// Volumes are the player's multipliers on top of the tuned mix in AudioManager (the layer
    /// volumes there stay the sound designer's), so 100% means "as mixed".
    /// Resolution and window mode are not stored here: Unity's player keeps them itself.
    /// Control remaps live in <see cref="ControlBindings"/>.
    /// </summary>
    public static class Settings
    {
        // controls
        public static float CameraSensitivity = 1f;
        public static bool InvertY;
        public static bool Rumble = true;
        // audio
        public static float MasterVolume = 0.9f;
        public static float SfxVolume = 1f;
        public static float MusicVolume = 1f;
        public static float AmbienceVolume = 1f;
        public static float FootstepVolume = 1f;
        // graphics
        public static bool PsxEffect = true;
        public static bool Pixelation = true;
        public static float ShakeScale = 1f;

        static bool loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            loaded = false;
            Load();
        }

        /// <summary>Reads the saved options (once per run) and applies them to whatever is alive now.</summary>
        public static void Load()
        {
            if (!loaded)
            {
                loaded = true;
                CameraSensitivity = PlayerPrefs.GetFloat("eldertree.sens", 1f);
                InvertY = PlayerPrefs.GetInt("eldertree.invert", 0) == 1;
                Rumble = PlayerPrefs.GetInt("eldertree.rumble", 1) == 1;
                MasterVolume = PlayerPrefs.GetFloat("eldertree.vol", 0.9f);
                SfxVolume = PlayerPrefs.GetFloat("eldertree.sfx", 1f);
                MusicVolume = PlayerPrefs.GetFloat("eldertree.music", 1f);
                AmbienceVolume = PlayerPrefs.GetFloat("eldertree.amb", 1f);
                FootstepVolume = PlayerPrefs.GetFloat("eldertree.steps", 1f);
                PsxEffect = PlayerPrefs.GetInt("eldertree.psx", 1) == 1;
                Pixelation = PlayerPrefs.GetInt("eldertree.pixel", 1) == 1;
                ShakeScale = PlayerPrefs.GetFloat("eldertree.shake", 1f);
            }
            Push();
        }

        /// <summary>Saves and pushes every option.</summary>
        public static void Apply()
        {
            PlayerPrefs.SetFloat("eldertree.sens", CameraSensitivity);
            PlayerPrefs.SetInt("eldertree.invert", InvertY ? 1 : 0);
            PlayerPrefs.SetInt("eldertree.rumble", Rumble ? 1 : 0);
            PlayerPrefs.SetFloat("eldertree.vol", MasterVolume);
            PlayerPrefs.SetFloat("eldertree.sfx", SfxVolume);
            PlayerPrefs.SetFloat("eldertree.music", MusicVolume);
            PlayerPrefs.SetFloat("eldertree.amb", AmbienceVolume);
            PlayerPrefs.SetFloat("eldertree.steps", FootstepVolume);
            PlayerPrefs.SetInt("eldertree.psx", PsxEffect ? 1 : 0);
            PlayerPrefs.SetInt("eldertree.pixel", Pixelation ? 1 : 0);
            PlayerPrefs.SetFloat("eldertree.shake", ShakeScale);
            PlayerPrefs.Save();
            Push();
        }

        static void Push()
        {
            var input = PlayerInputReader.Instance;
            if (input != null)
            {
                input.mouseSensitivity = CameraSensitivity;
                input.stickYawSpeed = 200f * CameraSensitivity;
                input.stickPitchSpeed = 120f * CameraSensitivity;
                input.invertY = InvertY;
            }
            // layer multipliers are read live by AudioManager; master is the listener
            AudioListener.volume = MasterVolume;
            PSXGlobals.Enabled = PsxEffect;
            PsxPipeline.SetPixelation(Pixelation);
        }

        // ------------------------------------------------------------------ display
        public static readonly FullScreenMode[] DisplayModes = { FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen, FullScreenMode.Windowed };

        public static string DisplayModeName(FullScreenMode m) => m switch
        {
            FullScreenMode.FullScreenWindow => "Borderless",
            FullScreenMode.ExclusiveFullScreen => "Fullscreen",
            FullScreenMode.MaximizedWindow => "Maximized",
            _ => "Windowed",
        };

        /// <summary>Distinct screen sizes the display offers, smallest first, always including the current one.</summary>
        public static System.Collections.Generic.List<Vector2Int> Resolutions()
        {
            var list = new System.Collections.Generic.List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (!list.Contains(v)) list.Add(v);
            }
            var cur = new Vector2Int(Screen.width, Screen.height);
            if (!list.Contains(cur)) list.Add(cur);
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return list;
        }

        public static void SetDisplay(Vector2Int size, FullScreenMode mode) => Screen.SetResolution(size.x, size.y, mode);
    }
}
