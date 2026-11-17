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

        /// <summary>Configures a texture baked outside Unity (Tools/Blender/build_tung_sahur.py --bake).
        /// Returns null when the file is missing so callers can fall back to procedural textures.</summary>
        public static Texture2D ImportBaked(string name, bool isNormal, int maxSize = 4096)
        {
            string path = $"{TexDir}/{name}.png";
            if (!File.Exists(path)) return null;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !isNormal;
            imp.anisoLevel = 8;
            imp.mipmapEnabled = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.maxTextureSize = maxSize;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>PSX import: point filtering, no mipmaps, no compression (keeps the 15-bit dither crisp).</summary>
        public static Texture2D ImportPixel(string name, bool alpha = false, int maxSize = 512)
        {
            string path = $"{TexDir}/{name}.png";
            if (!File.Exists(path)) return null;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;
            imp.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            imp.alphaIsTransparency = alpha;
            imp.filterMode = FilterMode.Point;
            imp.anisoLevel = 0;
            imp.mipmapEnabled = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.maxTextureSize = maxSize;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
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

        // ------------------------------------------------------------------ PSX world textures
        // Docs/ART_DIRECTION.md: small, point filtered, 15-bit colour with a 4x4 ordered dither,
        // shading painted in (no normal maps), dark desaturated values, every texel carries detail.
        static readonly float[] Bayer4 = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        public static Color Psx(Color c, int x, int y, float strength = 1f)
        {
            float b = (Bayer4[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f - 0.5f;
            const float L = 31f;
            c.r = Mathf.Clamp01(Mathf.Floor(c.r * L + 0.5f + b * strength) / L);
            c.g = Mathf.Clamp01(Mathf.Floor(c.g * L + 0.5f + b * strength) / L);
            c.b = Mathf.Clamp01(Mathf.Floor(c.b * L + 0.5f + b * strength) / L);
            return c;
        }

        /// <summary>Saves a tileable PSX albedo: dithered 15-bit, point filtered, uncompressed.
        /// Mips stay on (nearest-mip) for big tiling surfaces so the ground does not boil.</summary>
        public static Texture2D SavePsx(string name, int size, Func<float, float, int, int, Color> pixel, bool mips = true,
            bool alpha = false)
        {
            Save(name, size, size, (x, y) =>
            {
                var c = pixel(x / (float)size, y / (float)size, x, y);
                var q = Psx(c, x, y);
                q.a = alpha ? (c.a > 0.5f ? 1f : 0f) : 1f;
                return q;
            }, aniso: 0);
            string path = $"{TexDir}/{name}.png";
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.filterMode = FilterMode.Point;
            imp.anisoLevel = 0;
            imp.mipmapEnabled = mips;
            imp.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            imp.alphaIsTransparency = alpha;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Color Mix(Color a, Color b, float t) => Color.Lerp(a, b, Mathf.Clamp01(t));
        static float Grit(int x, int y, int seed) => Hash(x, y, seed) - 0.5f;

        /// <summary>Club: gnarled dark wood along v, leather-wrapped grip for v below 0.3 (u around, v along).</summary>
        public static void BuildClub(out Texture2D albedo, out Texture2D normal)
        {
            normal = null;
            albedo = SavePsx("T_Club_Albedo", 128, (u, v, x, y) =>
            {
                if (v < 0.30f)
                {
                    float wrap = Mathf.Abs(Mathf.Sin((u * 5f + v * 30f) * Mathf.PI));
                    var lc = Mix(Hex("#1A1310"), Hex("#3B2B20"), Mathf.SmoothStep(0.25f, 0.95f, wrap));
                    lc *= 0.85f + 0.3f * Fbm(u, v, 8, 8, 3, 12) + 0.12f * Grit(x, y, 3);
                    lc = Mix(lc, Hex("#0C0908"), Mathf.SmoothStep(0.12f, 0.0f, wrap));
                    return lc;
                }
                float warp = Fbm(u, v, 4, 2, 3, 17) - 0.5f;
                float ridge = Mathf.Abs(Mathf.Sin((u * 7f + warp * 2.5f) * Mathf.PI));
                float fib = Fbm(u, v, 32, 4, 3, 18);
                var c = Mix(Hex("#241B15"), Hex("#4E3E31"), 0.3f + 0.6f * fib + 0.2f * Grit(x, y, 4));
                c *= 1f - 0.6f * Mathf.SmoothStep(0.3f, 0f, ridge);
                float knot = Mathf.SmoothStep(0.78f, 0.88f, Fbm(u, v, 3, 3, 2, 19));
                c = Mix(c, Hex("#120D0A"), knot * Mathf.Abs(Mathf.Sin(knot * 12f)));
                // dried blood soaked into the striking end
                float blood = Mathf.SmoothStep(0.62f, 0.72f, Fbm(u, v, 6, 6, 3, 20)) * Mathf.SmoothStep(0.6f, 0.95f, v);
                return Mix(c, Hex("#3A120D"), blood * 0.75f);
            }, mips: false);
        }

        /// <summary>Mud, sunken cobbles, moss and dead grass. Tileable 256.</summary>
        public static void BuildGround(out Texture2D albedo, out Texture2D normal)
        {
            normal = null;
            albedo = SavePsx("T_Ground_Albedo", 256, (u, v, x, y) =>
            {
                Worley(u, v, 7, 3, out float f1, out float f2, out int id);
                float edge = f2 - f1;
                float n = Fbm(u, v, 8, 8, 5, 21);
                float stone = Mathf.SmoothStep(0.05f, 0.14f, edge) * (Hash(id, 1, 5) > 0.45f ? 1 : 0);
                var mud = Mix(Hex("#2B241E"), Hex("#4F4236"), n * 1.25f + 0.25f * Grit(x, y, 1));
                float puddle = Mathf.SmoothStep(0.70f, 0.76f, Fbm(u, v, 4, 4, 3, 24));
                mud = Mix(mud, Hex("#1A1A24"), puddle * 0.75f);
                var cob = Mix(Hex("#3A3B43"), Hex("#666874"), 0.35f + 0.55f * Fbm(u, v, 24, 24, 3, 33) + 0.2f * Grit(x, y, 2));
                cob *= 0.8f + 0.35f * Hash(id, 9, 2);
                cob *= 0.65f + 0.35f * Mathf.SmoothStep(0.05f, 0.3f, edge);            // painted bevel shading
                var c = Mix(mud, cob, stone);
                float moss = Mathf.SmoothStep(0.55f, 0.72f, Fbm(u, v, 6, 6, 4, 26));
                c = Mix(c, Mix(Hex("#1F2817"), Hex("#3A4626"), Fbm(u, v, 64, 64, 2, 27)), moss * (1 - stone * 0.5f) * 0.8f);
                float grass = Mathf.Pow(Hash(x, y, 28), 14f) * (1 - stone);
                return Mix(c, Hex("#4A4630"), grass * 1.6f);
            });
        }

        /// <summary>Rotting grey planks with dark gaps, nail rust and moss.</summary>
        public static void BuildPlanks(out Texture2D albedo, out Texture2D normal)
        {
            normal = null;
            const int planks = 5;
            albedo = SavePsx("T_Planks_Albedo", 256, (u, v, x, y) =>
            {
                float pu = u * planks;
                int pi = Mathf.FloorToInt(pu);
                float fu = pu - pi;
                float vv = Mathf.Repeat(v + Hash(pi, 0, 4), 1f);
                float g = Fbm(u, vv, 40, 4, 4, 40 + pi);
                float grain = Mathf.Pow(Mathf.Sin((u * 160f + g * 9f) * Mathf.PI) * 0.5f + 0.5f, 3);
                var c = Mix(Hex("#2C251F"), Hex("#5A4F45"), 0.25f + 0.45f * g + 0.25f * Hash(pi, 3, 9) - 0.35f * grain + 0.18f * Grit(x, y, 5));
                float seam = Mathf.SmoothStep(0.0f, 0.06f, fu) * Mathf.SmoothStep(1.0f, 0.94f, fu);
                float endSeam = Mathf.SmoothStep(0.0f, 0.015f, vv) * Mathf.SmoothStep(1.0f, 0.985f, vv);
                c *= 0.7f + 0.3f * Mathf.SmoothStep(0.0f, 0.2f, fu);                     // painted edge shading
                float rot = Mathf.SmoothStep(0.62f, 0.75f, Fbm(u, v, 5, 5, 3, 44));
                c = Mix(c, Hex("#1A1612"), rot * 0.7f);
                c = Mix(c, Hex("#2F3A22"), Mathf.SmoothStep(0.66f, 0.8f, Fbm(u, v, 7, 7, 3, 45)) * 0.7f);
                float nail = (Mathf.Abs(fu - 0.5f) < 0.06f && Mathf.Abs(vv - 0.08f) < 0.012f) ? 1f : 0f;
                c = Mix(c, Hex("#4A2614"), nail);
                return Mix(Hex("#0B0908"), c, seam * endSeam);
            });
        }

        /// <summary>Cold blue-grey blocks, black mortar, moss and lichen.</summary>
        public static void BuildStone(out Texture2D albedo, out Texture2D normal)
        {
            normal = null;
            albedo = SavePsx("T_Stone_Albedo", 256, (u, v, x, y) =>
            {
                const int rows = 4; const float cols = 3;
                float rv = v * rows; int ri = Mathf.FloorToInt(rv); float fv = rv - ri;
                float cu = u * cols + (ri % 2) * 0.5f; int ci = Mathf.FloorToInt(cu); float fu = cu - ci;
                int wci = Wrap(ci, (int)cols);
                float n = Fbm(u, v, 16, 16, 5, 61);
                float bevel = Mathf.SmoothStep(0, 0.07f, fu) * Mathf.SmoothStep(1, 0.93f, fu) *
                              Mathf.SmoothStep(0, 0.09f, fv) * Mathf.SmoothStep(1, 0.91f, fv);
                var c = Mix(Hex("#2A2C33"), Hex("#555865"), 0.3f + 0.5f * n + 0.25f * Hash(wci, ri, 12) + 0.2f * Grit(x, y, 6));
                c *= 0.72f + 0.28f * Mathf.SmoothStep(0.0f, 0.5f, fv);                  // top-lit block faces
                float crack = Mathf.SmoothStep(0.012f, 0.0f, Mathf.Abs(Fbm(u, v, 8, 8, 3, 63) - 0.5f));
                c = Mix(c, Hex("#15161A"), crack * 0.8f);
                float moss = Mathf.SmoothStep(0.52f, 0.7f, Fbm(u, v, 6, 6, 4, 70) + (1 - fv) * 0.15f);
                c = Mix(c, Mix(Hex("#1E2816"), Hex("#3D4A27"), Fbm(u, v, 48, 48, 2, 71)), moss * 0.85f);
                float lichen = Mathf.SmoothStep(0.70f, 0.76f, Fbm(u, v, 20, 20, 3, 72));
                c = Mix(c, Hex("#6B7163"), lichen * 0.6f);
                return Mix(Hex("#0E0E11"), c, bevel);
            });
        }

        /// <summary>Filthy sack-cloth with dried blood (training dummy, sacks).</summary>
        public static void BuildBurlap(out Texture2D albedo, out Texture2D normal)
        {
            normal = null;
            albedo = SavePsx("T_Burlap_Albedo", 128, (u, v, x, y) =>
            {
                bool warp = ((x >> 1) + (y >> 1)) % 2 == 0;
                var c = Mix(Hex("#3F3426"), Hex("#6A5A40"), (warp ? 0.65f : 0.35f) + 0.35f * (Fbm(u, v, 8, 8, 3, 5) - 0.5f) + 0.25f * Grit(x, y, 7));
                c = Mix(c, Hex("#221B14"), Mathf.SmoothStep(0.6f, 0.75f, Fbm(u, v, 4, 4, 3, 8)) * 0.7f);
                return Mix(c, Hex("#3C130E"), Mathf.SmoothStep(0.7f, 0.78f, Fbm(u, v, 5, 5, 3, 9)) * 0.85f);
            });
        }

        /// <summary>Dead-tree bark: deep vertical fissures (v runs up the trunk).</summary>
        public static Texture2D BuildBark()
        {
            return SavePsx("T_Bark_Albedo", 128, (u, v, x, y) =>
            {
                float warp = Fbm(u, v, 3, 2, 3, 81) - 0.5f;
                float ridge = Mathf.Abs(Mathf.Sin((u * 6f + warp * 2.2f) * Mathf.PI));
                var c = Mix(Hex("#1E1813"), Hex("#4A3D31"), 0.3f + 0.5f * Fbm(u, v, 24, 3, 3, 82) + 0.25f * Grit(x, y, 8));
                c *= 1f - 0.7f * Mathf.SmoothStep(0.35f, 0f, ridge);
                c = Mix(c, Hex("#646B5C"), Mathf.SmoothStep(0.72f, 0.78f, Fbm(u, v, 12, 12, 3, 83)) * 0.55f);
                return Mix(c, Hex("#26311C"), Mathf.SmoothStep(0.58f, 0.75f, Fbm(u, v, 5, 5, 3, 84)) * 0.75f);
            });
        }

        /// <summary>Pine needles for the dark conifers: near-black teal with sparse cold highlights.</summary>
        public static Texture2D BuildFoliage()
        {
            return SavePsx("T_Foliage_Albedo", 128, (u, v, x, y) =>
            {
                float needles = Mathf.Abs(Mathf.Sin((u * 26f + v * 13f + Fbm(u, v, 8, 8, 2, 91) * 3f) * Mathf.PI));
                var c = Mix(Hex("#0D1612"), Hex("#2A3D33"), needles * 0.6f + 0.35f * Fbm(u, v, 8, 8, 3, 92) + 0.3f * Grit(x, y, 9));
                return Mix(c, Hex("#3E4B3A"), Mathf.Pow(Hash(x, y, 93), 10f));
            });
        }

        /// <summary>Dark oiled wood for the bow and arrows (grain along v).</summary>
        public static Texture2D BuildDarkWood()
        {
            return SavePsx("T_DarkWood_Albedo", 128, (u, v, x, y) =>
            {
                float g = Fbm(u, v, 16, 2, 3, 101);
                float grain = Mathf.Pow(Mathf.Sin((u * 40f + g * 6f) * Mathf.PI) * 0.5f + 0.5f, 3);
                return Mix(Hex("#1C1410"), Hex("#4A3526"), 0.35f + 0.45f * g - 0.3f * grain + 0.18f * Grit(x, y, 10));
            });
        }

        /// <summary>Worn, scuffed leather with stitching (quiver, straps).</summary>
        public static Texture2D BuildLeather()
        {
            return SavePsx("T_Leather_Albedo", 128, (u, v, x, y) =>
            {
                var c = Mix(Hex("#1E1511"), Hex("#4A3426"), 0.4f + 0.45f * Fbm(u, v, 12, 12, 4, 111) + 0.2f * Grit(x, y, 11));
                c = Mix(c, Hex("#6A5643"), Mathf.SmoothStep(0.68f, 0.78f, Fbm(u, v, 6, 6, 3, 112)) * 0.5f);   // scuffs
                bool stitch = (x % 128 < 3 || x % 128 > 124) && (y % 6) < 3;
                return stitch ? Hex("#7A6A52") : c;
            });
        }

        /// <summary>Black iron with rust blooms.</summary>
        public static Texture2D BuildIron()
        {
            return SavePsx("T_Iron_Albedo", 64, (u, v, x, y) =>
            {
                var c = Mix(Hex("#141416"), Hex("#3A3A40"), 0.4f + 0.4f * Fbm(u, v, 8, 8, 3, 121) + 0.25f * Grit(x, y, 12));
                return Mix(c, Mix(Hex("#3A1A0C"), Hex("#6B3418"), Fbm(u, v, 16, 16, 2, 122)),
                    Mathf.SmoothStep(0.55f, 0.7f, Fbm(u, v, 5, 5, 3, 123)) * 0.8f);
            });
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
