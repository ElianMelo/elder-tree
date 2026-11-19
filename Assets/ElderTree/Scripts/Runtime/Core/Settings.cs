using ElderTree.Audio;
using ElderTree.InputHandling;
using UnityEngine;

namespace ElderTree
{
    /// <summary>Player options, persisted in PlayerPrefs and pushed to the live systems on Apply().</summary>
    public static class Settings
    {
        public static float CameraSensitivity = 1f;
        public static bool InvertY;
        public static float MasterVolume = 0.9f;
        public static float MusicVolume = 1f;
        public static float EffectsVolume = 1f;
        public static float ShakeScale = 1f;
        public static bool Rumble = true;

        public static void Load()
        {
            CameraSensitivity = PlayerPrefs.GetFloat("eldertree.sens", 1f);
            InvertY = PlayerPrefs.GetInt("eldertree.invert", 0) == 1;
            MasterVolume = PlayerPrefs.GetFloat("eldertree.vol", 0.9f);
            MusicVolume = PlayerPrefs.GetFloat("eldertree.music", 1f);
            EffectsVolume = PlayerPrefs.GetFloat("eldertree.sfx", 1f);
            ShakeScale = PlayerPrefs.GetFloat("eldertree.shake", 1f);
            Rumble = PlayerPrefs.GetInt("eldertree.rumble", 1) == 1;
            Apply();
        }

        public static void Apply()
        {
            PlayerPrefs.SetFloat("eldertree.sens", CameraSensitivity);
            PlayerPrefs.SetInt("eldertree.invert", InvertY ? 1 : 0);
            PlayerPrefs.SetFloat("eldertree.vol", MasterVolume);
            PlayerPrefs.SetFloat("eldertree.music", MusicVolume);
            PlayerPrefs.SetFloat("eldertree.sfx", EffectsVolume);
            PlayerPrefs.SetFloat("eldertree.shake", ShakeScale);
            PlayerPrefs.SetInt("eldertree.rumble", Rumble ? 1 : 0);
            var input = PlayerInputReader.Instance;
            if (input != null)
            {
                input.mouseSensitivity = CameraSensitivity;
                input.stickYawSpeed = 200f * CameraSensitivity;
                input.stickPitchSpeed = 120f * CameraSensitivity;
                input.invertY = InvertY;
            }
            AudioListener.volume = MasterVolume;
            var am = AudioManager.Instance;
            if (am != null)
            {
                am.masterVolume = MasterVolume;
                am.musicVolume = MusicVolume;
                am.sfxVolume = am.footstepVolume = EffectsVolume;
            }
        }
    }
}
