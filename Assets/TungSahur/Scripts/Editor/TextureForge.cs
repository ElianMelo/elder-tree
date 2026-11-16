using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TungSahur.EditorTools
{
    /// <summary>
    /// Procedural, tileable texture generation (albedo + normal) for the project's art.
    /// Every texture is deterministic so re-running the builder is stable in version control.
    /// </summary>
    public static class TextureForge
    {
        public const string TexDir = "Assets/TungSahur/Art/Textures";

        // ------------------------------------------------------------------ noise
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        static int Wrap(int v, int p) { int r = v % p; return r < 0 ? r + p : r; }

        /// <summary>Periodic value noise. period in lattice cells, coords in cells.</summary>
        public static float VNoise(float x, float y, int px, int py, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
            float a = Hash(Wrap(x0, px), Wrap(y0, py), seed);
            float b = Hash(Wrap(x0 + 1, px), Wrap(y0, py), seed);
            float c = Hash(Wrap(x0, px), Wrap(y0 + 1, py), seed);
            float d = Hash(Wrap(x0 + 1, px), Wrap(y0 + 1, py), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        /// <summary>Tileable fBm on the unit square. u,v in [0,1).</summary>
        public static float Fbm(float u, float v, int baseX, int baseY, int octaves, int seed, float gain = 0.5f)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            int fx = baseX, fy = baseY;
            for (int o = 0; o < octaves; o++)
            {
                sum += VNoise(u * fx, v * fy, fx, fy, seed + o * 31) * amp;
                norm += amp;
                amp *= gain;
                fx *= 2; fy *= 2;
            }
            return sum / norm;
        }

        /// <summary>Tileable Worley (cellular) distance F1 and F2 for stones.</summary>
        public static void Worley(float u, float v, int cells, int seed, out float f1, out float f2, out int id)
        {
            float x = u * cells, y = v * cells;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            f1 = 9; f2 = 9; id = 0;
            for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int gx = cx + i, gy = cy + j;
                int wx = Wrap(gx, cells), wy = Wrap(gy, cells);
                float ox = 0.15f + 0.7f * Hash(wx, wy, seed);
                float oy = 0.15f + 0.7f * Hash(wx, wy, seed + 7);
                float dx = gx + ox - x, dy = gy + oy - y;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d < f1) { f2 = f1; f1 = d; id = wx * 7919 + wy; }
                else if (d < f2) f2 = d;
            }
        }

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        // ------------------------------------------------------------------ io
        public static Texture2D Save(string name, int w, int h, Func<int, int, Color> pixel, bool linear = false,
            bool isNormal = false, int aniso = 8)
        {
            Directory.CreateDirectory(TexDir);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, linear);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = pixel(x, y);
            tex.SetPixels(px);
            tex.Apply();
            string path = $"{TexDir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !linear && !isNormal;
            imp.anisoLevel = aniso;
            imp.mipmapEnabled = true;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Builds a tangent-space normal map from a tileable height function.</summary>
        public static Texture2D SaveNormalFromHeight(string name, int w, int h, Func<int, int, float> height, float strength)
        {
            var hm = new float[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                hm[y * w + x] = height(x, y);
            return Save(name, w, h, (x, y) =>
            {
                float l = hm[y * w + Wrap(x - 1, w)], r = hm[y * w + Wrap(x + 1, w)];
                float d = hm[Wrap(y - 1, h) * w + x], u = hm[Wrap(y + 1, h) * w + x];
                var n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                return new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1);
            }, linear: true, isNormal: true);
        }

        // ------------------------------------------------------------------ textures
        /// <summary>Tung's body wood: vertical grain along V, gentle tone variation, a couple of knots.</summary>
        public static float TungWoodHeight(float u, float v)
        {
            // grain: stretched noise along v (v is along the log), tight in u
            float g = Fbm(u, v, 24, 2, 4, 11, 0.5f);
            float lines = Mathf.Sin((u * 64f + g * 5f) * Mathf.PI) * 0.5f + 0.5f;
            lines = Mathf.Pow(lines, 8f);
            // knots
            float knot = 0;
            Vector2[] knots = { new(0.62f, 0.31f), new(0.18f, 0.74f), new(0.86f, 0.88f) };
            foreach (var k in knots)
            {
                float dx = Mathf.Min(Mathf.Abs(u - k.x), 1 - Mathf.Abs(u - k.x));
                float dy = Mathf.Min(Mathf.Abs(v - k.y), 1 - Mathf.Abs(v - k.y)) * 0.45f;
                float d = Mathf.Sqrt(dx * dx * 900 + dy * dy * 900);
                knot += Mathf.Exp(-d * d * 1.5f) * (0.5f + 0.5f * Mathf.Sin(d * 14f));
            }
            return lines * 0.35f + g * 0.45f + knot * 0.4f;
        }

        public static void BuildTungWood(out Texture2D albedo, out Texture2D normal)
        {
            const int S = 1024;
            Color light = Hex("#D29457"), mid = Hex("#BC7C43"), dark = Hex("#8A552B");
            albedo = Save("T_TungWood_Albedo", S, S, (x, y) =>
            {
                float u = x / (float)S, v = y / (float)S;
                float h = TungWoodHeight(u, v);
                float tone = Fbm(u, v, 4, 2, 3, 5);
                var c = Color.Lerp(light, mid, Mathf.Clamp01(h * 1.2f));
                c = Color.Lerp(c, dark, Mathf.Clamp01((h - 0.5f) * 1.2f));
                c *= 0.9f + tone * 0.22f;
                c.a = 0.32f + (1 - h) * 0.12f; // smoothness in alpha
                return c;
            });
            normal = SaveNormalFromHeight("T_TungWood_Normal", S, S,
                (x, y) => TungWoodHeight(x / (float)S, y / (float)S), 1.1f);
        }

        /// <summary>Eye: planar front projection, centre = pupil.</summary>
        public static Texture2D BuildEye()
        {
            const int S = 512;
            Color sclera = Hex("#F6F1E7"), scleraEdge = Hex("#E2C9B8");
            Color irisOuter = Hex("#1E0F06"), irisInner = Hex("#6A3A16"), pupil = Hex("#050302");
            return Save("T_TungEye", S, S, (x, y) =>
            {
                float u = x / (float)S - 0.5f, v = y / (float)S - 0.5f;
                float r = Mathf.Sqrt(u * u + v * v) * 2f; // 0 centre .. 1 edge
                float ang = Mathf.Atan2(v, u);
                Color c = Color.Lerp(sclera, scleraEdge, Mathf.SmoothStep(0.55f, 1f, r));
                // faint veins
                float vein = Mathf.Pow(Mathf.Abs(Mathf.Sin(ang * 9f + r * 6f)), 40f) * Mathf.SmoothStep(0.6f, 0.95f, r);
                c = Color.Lerp(c, Hex("#C46B5E"), vein * 0.35f);
                const float irisR = 0.50f, pupilR = 0.25f;
                if (r < irisR + 0.02f)
                {
                    float fib = 0.5f + 0.5f * Mathf.Sin(ang * 60f + Fbm((ang + Mathf.PI) / (2 * Mathf.PI), r, 16, 4, 3, 3) * 10f);
                    var ic = Color.Lerp(irisInner, irisOuter, Mathf.SmoothStep(0.3f, irisR, r));
                    ic = Color.Lerp(ic, ic * 1.35f, fib * 0.35f);
                    float limbal = Mathf.SmoothStep(irisR - 0.06f, irisR, r);
                    ic = Color.Lerp(ic, irisOuter * 0.5f, limbal);
                    float edge = Mathf.SmoothStep(irisR + 0.02f, irisR, r);
                    c = Color.Lerp(c, ic, edge);
                }
                c = Color.Lerp(c, pupil, Mathf.SmoothStep(pupilR + 0.015f, pupilR - 0.01f, r));
                // painted catch-light (reads even without a reflection)
                float hx = u + 0.11f, hy = v - 0.12f;
                float hl = Mathf.SmoothStep(0.07f, 0.045f, Mathf.Sqrt(hx * hx + hy * hy));
                c = Color.Lerp(c, Color.white, hl * 0.9f);
                c.a = 0.7f;
                return c;
            });
        }

        public static void BuildClub(out Texture2D albedo, out Texture2D normal)
        {
            const int S = 512;
            Color a = Hex("#7A4424"), b = Hex("#54301A"), grip = Hex("#1E1714"), gripHi = Hex("#3A2E28");
            Func<float, float, float> h = (u, v) =>
            {
                // v covers z*2 in UV (club ~1m => v 0..1.8): the grip band is v < 0.30
                if (v < 0.30f)
                {
                    float wrap = Mathf.Abs(Mathf.Sin((u * 6f + v * 28f) * Mathf.PI));
                    return 0.6f + 0.4f * Mathf.SmoothStep(0.85f, 1f, wrap);
                }
                float g = Fbm(u, v, 32, 4, 4, 17);
                return Mathf.Pow(Mathf.Sin((u * 90f + g * 8f) * Mathf.PI) * 0.5f + 0.5f, 3) * 0.6f + g * 0.4f;
            };
            albedo = Save("T_Club_Albedo", S, S, (x, y) =>
            {
                float u = x / (float)S, v = y / (float)S;
                float hv = h(u, v);
                if (v < 0.30f)
                {
                    var gc = Color.Lerp(grip, gripHi, (hv - 0.6f) * 2.2f);
                    gc.a = 0.15f;
                    return gc;
                }
                var c = Color.Lerp(a, b, hv);
                // battle wear: lighter dents on the barrel
                float wear = Fbm(u, v, 8, 8, 3, 91);
                c = Color.Lerp(c, Hex("#A8704A"), Mathf.SmoothStep(0.62f, 0.75f, wear) * Mathf.SmoothStep(0.8f, 1.2f, v * 1.6f));
                c.a = 0.45f;
                return c;
            });
            normal = SaveNormalFromHeight("T_Club_Normal", S, S, (x, y) => h(x / (float)S, y / (float)S), 2.5f);
        }

        /// <summary>Packed earth + embedded cobbles, tileable 1024.</summary>
        public static void BuildGround(out Texture2D albedo, out Texture2D normal)
        {
            const int S = 1024;
            Color dirt = Hex("#4A3F35"), dirtDark = Hex("#2E2620"), stone = Hex("#6A6560"), stoneDark = Hex("#45423F");
            Func<float, float, (float hgt, Color col, float smooth)> f = (u, v) =>
            {
                Worley(u, v, 9, 3, out float f1, out float f2, out int id);
                float edge = f2 - f1;
                float n = Fbm(u, v, 8, 8, 5, 21);
                float stoneMask = Mathf.SmoothStep(0.06f, 0.16f, edge) * (Hash(id, 1, 5) > 0.35f ? 1 : 0);
                float hgt = n * 0.4f + stoneMask * (0.5f + 0.25f * Fbm(u, v, 32, 32, 3, 77));
                var dc = Color.Lerp(dirtDark, dirt, n * 1.2f);
                float tint = Hash(id, 9, 2);
                var sc = Color.Lerp(stoneDark, stone, 0.4f + 0.6f * Fbm(u, v, 24, 24, 3, 33)) * (0.85f + 0.3f * tint);
                var col = Color.Lerp(dc, sc, stoneMask);
                return (hgt, col, Mathf.Lerp(0.12f, 0.38f, stoneMask));
            };
            albedo = Save("T_Ground_Albedo", S, S, (x, y) =>
            {
                var r = f(x / (float)S, y / (float)S);
                var c = r.col; c.a = r.smooth; return c;
            });
            normal = SaveNormalFromHeight("T_Ground_Normal", S, S, (x, y) => f(x / (float)S, y / (float)S).hgt, 6f);
        }

        /// <summary>Weathered wooden planks (hut, crates, fences).</summary>
        public static void BuildPlanks(out Texture2D albedo, out Texture2D normal)
        {
            const int S = 1024;
            const int planks = 6;
            Color a = Hex("#6B4A2E"), b = Hex("#4A3220"), gap = Hex("#1D140D");
            Func<float, float, (float h, Color c)> f = (u, v) =>
            {
                float pu = u * planks;
                int pi = Mathf.FloorToInt(pu);
                float fu = pu - pi;
                float offset = Hash(pi, 0, 4);
                float vv = Mathf.Repeat(v + offset, 1f);
                float g = Fbm(u, vv, 64, 4, 4, 40 + pi);
                float grain = Mathf.Pow(Mathf.Sin((u * 220f + g * 10f) * Mathf.PI) * 0.5f + 0.5f, 4);
                float seam = Mathf.SmoothStep(0.0f, 0.04f, fu) * Mathf.SmoothStep(1.0f, 0.96f, fu);
                float endSeam = Mathf.SmoothStep(0.0f, 0.01f, vv) * Mathf.SmoothStep(1.0f, 0.99f, vv);
                float s = seam * endSeam;
                var c = Color.Lerp(a, b, grain * 0.7f + g * 0.3f) * (0.85f + 0.3f * Hash(pi, 3, 9));
                c = Color.Lerp(gap, c, s);
                return (s * (0.7f + grain * 0.15f + g * 0.15f), c);
            };
            albedo = Save("T_Planks_Albedo", S, S, (x, y) =>
            {
                var r = f(x / (float)S, y / (float)S); var c = r.c; c.a = 0.22f; return c;
            });
            normal = SaveNormalFromHeight("T_Planks_Normal", S, S, (x, y) => f(x / (float)S, y / (float)S).h, 4f);
        }

        /// <summary>Rough stone blocks for walls / low ruins.</summary>
        public static void BuildStone(out Texture2D albedo, out Texture2D normal)
        {
            const int S = 1024;
            Color a = Hex("#5B5E66"), b = Hex("#3C3F46"), mortar = Hex("#2A2A2C");
            Func<float, float, (float h, Color c)> f = (u, v) =>
            {
                int rows = 4;
                float rv = v * rows;
                int ri = Mathf.FloorToInt(rv);
                float fv = rv - ri;
                float cols = 3;
                float cu = u * cols + (ri % 2) * 0.5f;
                int ci = Mathf.FloorToInt(cu);
                float fu = cu - ci;
                int wci = Wrap(ci, (int)cols);
                float n = Fbm(u, v, 16, 16, 5, 61);
                float bevel = Mathf.SmoothStep(0, 0.06f, fu) * Mathf.SmoothStep(1, 0.94f, fu) *
                              Mathf.SmoothStep(0, 0.08f, fv) * Mathf.SmoothStep(1, 0.92f, fv);
                float tint = Hash(wci, ri, 12);
                var c = Color.Lerp(a, b, n * 0.8f + tint * 0.3f);
                // moss in crevices / bottom
                float moss = Mathf.SmoothStep(0.55f, 0.75f, Fbm(u, v, 6, 6, 4, 70)) * (1 - bevel * 0.6f);
                c = Color.Lerp(c, Hex("#3E4A2E"), moss * 0.55f);
                c = Color.Lerp(mortar, c, bevel);
                return (bevel * (0.75f + n * 0.25f), c);
            };
            albedo = Save("T_Stone_Albedo", S, S, (x, y) =>
            {
                var r = f(x / (float)S, y / (float)S); var c = r.c; c.a = 0.18f; return c;
            });
            normal = SaveNormalFromHeight("T_Stone_Normal", S, S, (x, y) => f(x / (float)S, y / (float)S).h, 5f);
        }

        /// <summary>Burlap / straw for the training dummy.</summary>
        public static void BuildBurlap(out Texture2D albedo, out Texture2D normal)
        {
            const int S = 512;
            Color a = Hex("#B59A6A"), b = Hex("#8C7449");
            Func<float, float, float> h = (u, v) =>
            {
                float wx = Mathf.Sin(u * 2 * Mathf.PI * 96), wy = Mathf.Sin(v * 2 * Mathf.PI * 96);
                float weave = (Mathf.Abs(wx) * (wy > 0 ? 1 : 0.5f) + Mathf.Abs(wy) * (wx > 0 ? 0.5f : 1)) * 0.5f;
                return weave * 0.7f + Fbm(u, v, 16, 16, 3, 5) * 0.3f;
            };
            albedo = Save("T_Burlap_Albedo", S, S, (x, y) =>
            {
                float u = x / (float)S, v = y / (float)S;
                var c = Color.Lerp(b, a, h(u, v)) * (0.85f + 0.3f * Fbm(u, v, 4, 4, 3, 8));
                c.a = 0.1f; return c;
            });
            normal = SaveNormalFromHeight("T_Burlap_Normal", S, S, (x, y) => h(x / (float)S, y / (float)S), 3f);
        }

        /// <summary>Soft radial sprite used by particles (dust, sparks, glow).</summary>
        public static Texture2D BuildSoftDot()
        {
            const int S = 128;
            var t = Save("T_FX_SoftDot", S, S, (x, y) =>
            {
                float u = x / (float)(S - 1) * 2 - 1, v = y / (float)(S - 1) * 2 - 1;
                float r = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.Pow(Mathf.Clamp01(1 - r), 2.2f);
                return new Color(1, 1, 1, a);
            }, aniso: 0);
            SetClamp(t);
            return t;
        }

        /// <summary>Wood chip / splinter sprite.</summary>
        public static Texture2D BuildChip()
        {
            const int S = 64;
            var t = Save("T_FX_Chip", S, S, (x, y) =>
            {
                float u = x / (float)(S - 1) * 2 - 1, v = y / (float)(S - 1) * 2 - 1;
                float d = Mathf.Abs(u) * 0.6f + Mathf.Abs(v) * 1.6f + (u * v) * 0.6f;
                float a = Mathf.SmoothStep(1f, 0.85f, d);
                return new Color(1, 1, 1, a);
            }, aniso: 0);
            SetClamp(t);
            return t;
        }

        /// <summary>Weapon trail gradient (U along length, V across width).</summary>
        public static Texture2D BuildTrail()
        {
            const int W = 256, H = 64;
            var t = Save("T_FX_Trail", W, H, (x, y) =>
            {
                float u = x / (float)(W - 1), v = y / (float)(H - 1);
                float across = Mathf.Pow(Mathf.Sin(v * Mathf.PI), 0.6f) * Mathf.Lerp(0.25f, 1f, v);
                float a = across * Mathf.Pow(1 - u, 1.5f);
                float streak = 0.75f + 0.25f * Mathf.Sin((v * 40f + u * 3f) * Mathf.PI);
                return new Color(1, 1, 1, Mathf.Clamp01(a * streak));
            }, aniso: 0);
            SetClamp(t);
            return t;
        }

        /// <summary>Ground decal ring for heavy impacts.</summary>
        public static Texture2D BuildRing()
        {
            const int S = 256;
            var t = Save("T_FX_Ring", S, S, (x, y) =>
            {
                float u = x / (float)(S - 1) * 2 - 1, v = y / (float)(S - 1) * 2 - 1;
                float r = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.Exp(-Mathf.Pow((r - 0.8f) / 0.09f, 2));
                return new Color(1, 1, 1, a);
            }, aniso: 0);
            SetClamp(t);
            return t;
        }

        static void SetClamp(Texture2D t)
        {
            var path = AssetDatabase.GetAssetPath(t);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.alphaIsTransparency = true;
            imp.SaveAndReimport();
        }
    }
}
