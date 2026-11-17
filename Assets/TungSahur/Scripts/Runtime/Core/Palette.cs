using UnityEngine;

namespace TungSahur
{
    /// <summary>
    /// Art bible — the single colour source for materials, lights, particles and UI.
    ///
    /// Style:  "PSX dark fantasy" (Docs/ART_DIRECTION.md): low-poly silhouettes, small dithered
    ///          point-filtered textures with the shading painted in, matte surfaces.
    /// Mood:   Pre-dawn sahur under a deep indigo/violet sky. A cold moon key, dying amber
    ///          lanterns, sickly will-o'-wisps, purple mist swallowing the distance. Surfaces stay
    ///          dark and desaturated; saturation lives only in the sky and in small glows.
    /// </summary>
    public static class Palette
    {
        // World
        public static readonly Color Ground      = FromHex(0x2A241F); // mud
        public static readonly Color Stone       = FromHex(0x3E4049); // cold stone
        public static readonly Color Timber      = FromHex(0x3A2E24); // rotting timber
        public static readonly Color Shadow      = FromHex(0x120E1E); // violet-black (never pure black)
        public static readonly Color Fog         = FromHex(0x2C2450); // purple mist (lighter than surfaces: silhouettes read against it)
        public static readonly Color SkyTop      = FromHex(0x07061A);
        public static readonly Color SkyHorizon  = FromHex(0x3A2A66);
        public static readonly Color Moon        = FromHex(0x9FA8F0);
        public static readonly Color Lantern     = FromHex(0xFFB547); // accent
        public static readonly Color Ember       = FromHex(0xFF7A2E);
        public static readonly Color Wisp        = FromHex(0x8FBF5A); // sickly will-o'-wisp accent

        // Character
        public static readonly Color TungWood    = FromHex(0xC98A4B);
        public static readonly Color TungWoodDark= FromHex(0x8E5A2E);
        public static readonly Color EyeWhite    = FromHex(0xF2E6D0);
        public static readonly Color Brow        = FromHex(0x3A2212);
        public static readonly Color Mouth       = FromHex(0x1C0E08);
        public static readonly Color Burlap      = FromHex(0xB59A6A);

        // UI
        public static readonly Color UIGold      = FromHex(0xD8B46A);
        public static readonly Color UIGoldBright= FromHex(0xF4DFA6);
        public static readonly Color UIText      = FromHex(0xEDE6D6);
        public static readonly Color UITextDim   = FromHex(0x9A927F);
        public static readonly Color UIPanel     = new Color(0.04f, 0.05f, 0.07f, 0.72f);
        public static readonly Color Health      = FromHex(0xB3282D);
        public static readonly Color HealthChip  = FromHex(0xE8C27A);
        public static readonly Color Focus       = FromHex(0x3A6FD8);
        public static readonly Color Stamina     = FromHex(0x3E9E5A);
        public static readonly Color Poise       = FromHex(0xE0A040);
        public static readonly Color DamageText  = FromHex(0xF6E7C1);
        public static readonly Color CritText    = FromHex(0xFFB547);

        public static Color FromHex(int rgb, float a = 1f) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

        public static Color WithAlpha(this Color c, float a) { c.a = a; return c; }
    }
}
