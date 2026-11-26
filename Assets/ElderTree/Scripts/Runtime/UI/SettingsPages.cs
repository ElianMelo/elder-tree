using System;
using System.Globalization;
using ElderTree.Audio;
using ElderTree.InputHandling;
using UnityEngine;
using static ElderTree.UI.OptionList;

namespace ElderTree.UI
{
    /// <summary>
    /// The contents of each options page, shared by the main menu and the pause menu so both always offer the same
    /// choices. Options = how the game plays (cinematics, hints, camera); Graphics / Audio / Controls = settings.
    /// </summary>
    public static class SettingsPages
    {
        public static void Game(OptionList l)
        {
            l.Section("GAME");
            l.AddValue("Boss introductions", () => Settings.IntroModeName(Settings.IntroCinematics),
                d => Settings.IntroCinematics = (Settings.IntroMode)(((int)Settings.IntroCinematics + d + 3) % 3),
                "When a boss's intro cinematic plays: only the first time you meet it, on every visit to its stage, or never.");
            l.AddValue("Control hints", () => OnOff(Settings.ControlHints), _ => Settings.ControlHints = !Settings.ControlHints,
                "Show the control list when a stage begins. It folds away by itself; the Help button brings it back.");
            l.AddValue("Camera shake", () => Pct(Settings.ShakeScale), d => Settings.ShakeScale = Mathf.Clamp(Settings.ShakeScale + d * 0.1f, 0f, 1.5f),
                "How hard impacts, stomps and explosions shake the camera.");

            l.Section("CAMERA");
            l.AddValue("Camera sensitivity", () => Settings.CameraSensitivity.ToString("0.0", CultureInfo.InvariantCulture),
                d => Settings.CameraSensitivity = Mathf.Clamp(Settings.CameraSensitivity + d * 0.1f, 0.2f, 3f),
                "Turning speed of the camera, for the mouse and the right stick.");
            l.AddValue("Invert vertical", () => OnOff(Settings.InvertY), _ => Settings.InvertY = !Settings.InvertY,
                "Push up to look down.");
            l.AddValue("Controller vibration", () => OnOff(Settings.Rumble), _ => Settings.Rumble = !Settings.Rumble,
                "Rumble on hits, guards and heavy landings.");
        }

        public static void Graphics(OptionList l)
        {
            l.Section("PRESENTATION");
            l.AddValue("PSX effect", () => OnOff(Settings.PsxEffect), _ => Settings.PsxEffect = !Settings.PsxEffect,
                "Vertex snapping, affine textures and dithering, as on the original hardware.");
            l.AddValue("Pixelation", () => OnOff(Settings.Pixelation), _ => Settings.Pixelation = !Settings.Pixelation,
                "Render at a low resolution and scale up with crisp pixels.");

            l.Section("DISPLAY");
            var resList = Settings.Resolutions();
            int resIndex = Mathf.Max(0, resList.IndexOf(new Vector2Int(Screen.width, Screen.height)));
            int modeIndex = Array.IndexOf(Settings.DisplayModes, Screen.fullScreenMode);
            if (modeIndex < 0) modeIndex = Settings.DisplayModes.Length - 1;
            // edited locally: Screen.* only updates a frame after a change
            l.AddValue("Resolution", () => $"{resList[resIndex].x} × {resList[resIndex].y}", d =>
            {
                resIndex = Mathf.Clamp(resIndex + d, 0, resList.Count - 1);
                Settings.SetDisplay(resList[resIndex], Settings.DisplayModes[modeIndex]);
            }, "Screen resolution.");
            l.AddValue("Display mode", () => Settings.DisplayModeName(Settings.DisplayModes[modeIndex]), d =>
            {
                modeIndex = (modeIndex + d + Settings.DisplayModes.Length) % Settings.DisplayModes.Length;
                Settings.SetDisplay(resList[resIndex], Settings.DisplayModes[modeIndex]);
            }, "Borderless window, exclusive fullscreen or a window.");
        }

        public static void Audio(OptionList l)
        {
            l.Section("VOLUME");
            l.AddSlider("Master", () => Settings.MasterVolume, v => Settings.MasterVolume = v, null, "Everything at once.");
            l.AddSlider("Sound effects", () => Settings.SfxVolume, v => Settings.SfxVolume = v, Sfx.WhooshLight, "Weapons, impacts, bosses and the menus.");
            l.AddSlider("Music", () => Settings.MusicVolume, v => Settings.MusicVolume = v, null, "Exploration and boss themes.");
            l.AddSlider("Ambience", () => Settings.AmbienceVolume, v => Settings.AmbienceVolume = v, null, "Wind, water and the stage around you.");
            l.AddSlider("Footsteps", () => Settings.FootstepVolume, v => Settings.FootstepVolume = v, Sfx.Footstep, "Your own steps.");
        }

        public static void Controls(OptionList l)
        {
            l.Section("BINDINGS");
            l.AddBindHeader();
            foreach (var e in ControlBindings.Entries) l.AddBind(e);
            l.AddButton("Restore default bindings", () =>
            {
                ControlBindings.ResetAll();
                l.NotifyBindingsChanged();
            });
        }
    }
}
