using UnityEngine;

namespace TungSahur
{
    /// <summary>
    /// Art bible — the single colour source for materials, lights, particles and UI.
    ///
    /// Style:  "Moody storybook night: soft PBR wood and stone, chunky readable silhouettes,
    ///          warm lantern pools cutting through cool moonlit blue."
    /// Mood:   Pre-dawn sahur. A cold moon key from high behind, warm lanterns at the
    ///          Pos Ronda, mist swallowing the distance; gold accents mark anything important.
    /// </summary>
    public static class Palette
    {
        // World
        public static readonly Color Ground      = FromHex(0x4A3F35); // packed earth
        public static readonly Color Stone       = FromHex(0x5B5E66); // moonlit stone
        public static readonly Color Timber      = FromHex(0x6B4A2E); // dark timber props
        public static readonly Color Shadow      = FromHex(0x1A2233); // night indigo (never black)
        public static readonly Color Fog         = FromHex(0x1F2A3D);
        public static readonly Color SkyTop      = FromHex(0x0B1220);
        public static readonly Color SkyHorizon  = FromHex(0x2B3A55);
        public static readonly Color Moon        = FromHex(0xA9C1FF);
        public static readonly Color Lantern     = FromHex(0xFFB547); // accent
        public static readonly Color Ember       = FromHex(0xFF7A2E);

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
