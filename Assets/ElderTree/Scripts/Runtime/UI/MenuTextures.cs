using System;
using UnityEngine;

namespace ElderTree.UI
{
    /// <summary>
    /// Soft gradients the menus are drawn with (glow behind a chosen entry, hairline rules fading at both ends, the
    /// dark grade over a vista, a vignette, a band fading to the right). Generated once per run and shared.
    /// </summary>
    public static class MenuTextures
    {
        static readonly Color Gold = new(0.85f, 0.71f, 0.42f);
        static Texture2D glow, line, shade, vignette, side;

        public static Texture2D Glow => glow ? glow : glow = Tex(256, 64, (u, v) =>
        {
            float x = u * 2f - 1f, y = v * 2f - 1f;
            float a = Mathf.Pow(Mathf.Clamp01(1f - x * x), 1.8f) * Mathf.Pow(Mathf.Clamp01(1f - y * y), 1.6f);
            return new Color(Gold.r, Gold.g * 0.92f, Gold.b * 0.8f, a * 0.42f);
        });

        public static Texture2D Line => line ? line : line = Tex(256, 1, (u, _) =>
        {
            float x = u * 2f - 1f;
            return new Color(0.96f, 0.87f, 0.65f, Mathf.Pow(Mathf.Clamp01(1f - x * x), 2.2f));
        });

        /// <summary>Heavy at the bottom (menu and footer), a little at the top.</summary>
        public static Texture2D Shade => shade ? shade : shade = Tex(1, 256, (_, v) =>
        {
            float a = 0.28f + 0.62f * Mathf.Pow(1f - v, 2.2f) + 0.3f * Mathf.Pow(v, 4f);
            return new Color(0.012f, 0.012f, 0.018f, Mathf.Clamp01(a));
        });

        public static Texture2D Vignette => vignette ? vignette : vignette = Tex(128, 128, (u, v) =>
        {
            float x = u * 2f - 1f, y = v * 2f - 1f;
            float d = Mathf.Sqrt(x * x * 0.7f + y * y);
            float a = Mathf.Clamp01((d - 0.5f) / 0.7f);
            return new Color(0, 0, 0, a * a * (3f - 2f * a) * 0.85f);
        });

        /// <summary>Dark on the left, clear on the right: the band a list sits on.</summary>
        public static Texture2D Side => side ? side : side = Tex(256, 1, (u, _) =>
            new Color(0.01f, 0.01f, 0.015f, 0.94f * Mathf.Pow(1f - u, 0.9f)));

        static Texture2D Tex(int w, int h, Func<float, float, Color> f)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
            };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = f(w > 1 ? x / (w - 1f) : 0.5f, h > 1 ? y / (h - 1f) : 0.5f);
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }
    }
}
