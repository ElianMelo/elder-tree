using TungSahur.Audio;
using TungSahur.InputHandling;
using UnityEngine;

namespace TungSahur
{
    /// <summary>Player options, persisted in PlayerPrefs and pushed to the live systems on Apply().</summary>
    public static class Settings
    {
        public static float CameraSensitivity = 1f;
        public static bool InvertY;
        public static float MasterVolume = 0.9f;
        public static float ShakeScale = 1f;
        public static bool Rumble = true;

        public static void Load()
        {
            CameraSensitivity = PlayerPrefs.GetFloat("tung.sens", 1f);
            InvertY = PlayerPrefs.GetInt("tung.invert", 0) == 1;
            MasterVolume = PlayerPrefs.GetFloat("tung.vol", 0.9f);
            ShakeScale = PlayerPrefs.GetFloat("tung.shake", 1f);
            Rumble = PlayerPrefs.GetInt("tung.rumble", 1) == 1;
            Apply();
        }

        public static void Apply()
        {
            PlayerPrefs.SetFloat("tung.sens", CameraSensitivity);
            PlayerPrefs.SetInt("tung.invert", InvertY ? 1 : 0);
            PlayerPrefs.SetFloat("tung.vol", MasterVolume);
            PlayerPrefs.SetFloat("tung.shake", ShakeScale);
            PlayerPrefs.SetInt("tung.rumble", Rumble ? 1 : 0);
            var input = PlayerInputReader.Instance;
            if (input != null)
            {
                input.mouseSensitivity = CameraSensitivity;
                input.stickYawSpeed = 200f * CameraSensitivity;
                input.stickPitchSpeed = 120f * CameraSensitivity;
                input.invertY = InvertY;
            }
            AudioListener.volume = MasterVolume;
            if (AudioManager.Instance != null) AudioManager.Instance.masterVolume = MasterVolume;
        }
    }
}
