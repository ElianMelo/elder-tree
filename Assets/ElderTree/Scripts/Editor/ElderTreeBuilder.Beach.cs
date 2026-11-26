using System.Collections.Generic;
using System.Linq;
using ElderTree.FX;
using ElderTree.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static ElderTree.EditorTools.TextureForge;

namespace ElderTree.EditorTools
{
    /// <summary>
    /// The Shark's arena: "Bleached Atoll". A grey-blue day on a crescent island: the fight is on a wide wooden
    /// dock standing over a turquoise lagoon, whose north edge runs onto a sandbar. Coconut palms lean over the
    /// beach, a leviathan's bones lie in the dunes with gulls circling them, crabs scuttle over the sand and the
    /// planks, a wreck lies heeled over on the reef, and the open sea rolls in from the south. Daylight, but the
    /// grade keeps it dark fantasy: crushed blue shadows, a heavy cloud deck, cold sun, sea haze.
    /// Models: Tools/Blender/build_beach.py -> Art/Models/Beach. Menu: Elder Tree/9e-9f.
    /// </summary>
    public static partial class ElderTreeBuilder
    {
        public const string BeachScenePath = Root + "/Scenes/Beach_Shark.unity";
        public const string BeachDir = Root + "/Art/Models/Beach";
        const float SeaLevel = -1.0f;
        const float DeckNorth = 8f;          // the deck's north edge (z); the sandbar starts here
        const float BeachWall = 19.6f;       // invisible ring that keeps the fight in

        static readonly string[] BeachSlotMats =
        {
            "M_PalmBark", "M_BeachFlora", "M_Coconut", "M_Bone", "M_DockPlanks", "M_DockPile", "M_SeaRock", "M_Driftwood",
            "M_Sail", "M_CrabShell", "M_BirdDark", "M_BirdPale", "M_Iron", "M_Rope", "M_DarkWood"
        };

        static GameObject BeachProp(string n) => AssetDatabase.LoadAssetAtPath<GameObject>($"{BeachDir}/{n}.fbx");

        // ================================================================== art
        [MenuItem("Elder Tree/9e. Build Beach Art (textures, materials, imports)")]
        public static string BuildBeachArt()
        {
            var bark = SavePsx("T_PalmBark", 128, (u, v, x, y) =>
            {
                // leaf-scar rings across the trunk (v runs up it), fibres crossing between them
                float ring = (v * 4f + (Fbm(u, v, 6, 2, 2, 401) - 0.5f) * 0.3f) % 1f;
                var c = Mx(Hex("#3A332A"), Hex("#73695A"), 0.3f + 0.45f * Fbm(u, v, 8, 16, 3, 402) + 0.2f * Gr(x, y, 41));
                float fib = Mathf.Abs(Mathf.Sin((u * 26f + v * 18f) * Mathf.PI)) * Mathf.Abs(Mathf.Sin((u * 26f - v * 18f) * Mathf.PI));
                c *= 1f - 0.18f * Sst(0.75f, 1f, fib);
                c = Mx(c, Hex("#211C17"), Sst(0.14f, 0.02f, ring) * 0.85f);
                c = Mx(c, Hex("#8E8676"), Sst(0.16f, 0.22f, ring) * Sst(0.3f, 0.16f, ring) * 0.5f);   // lit lip above the scar
                return Mx(c, Hex("#4E5244"), Sst(0.62f, 0.78f, Fbm(u, v, 4, 4, 3, 403)) * 0.4f);    // grey lichen
            });
            var flora = SavePsx("T_BeachFlora", 256, BeachFlora, mips: true, alpha: true);
            // nearest mips that keep the cutout's coverage: distant fronds stay solid shapes instead of breaking into dots
            var fimp = (TextureImporter)AssetImporter.GetAtPath(TexDir + "/T_BeachFlora.png");
            fimp.mipMapsPreserveCoverage = true; fimp.alphaTestReferenceValue = 0.5f; fimp.SaveAndReimport();
            var coco = SavePsx("T_Coconut", 64, (u, v, x, y) =>
            {
                float fib = Mathf.Abs(Mathf.Sin((u * 22f + Fbm(u, v, 4, 4, 2, 411) * 3f) * Mathf.PI));
                var c = Mx(Hex("#2E2015"), Hex("#6E4E2E"), 0.3f + 0.4f * Fbm(u, v, 6, 6, 3, 412) + 0.25f * Gr(x, y, 42));
                c *= 1f - 0.3f * Sst(0.3f, 0f, fib);
                return Mx(c, Hex("#7A6A4E"), Sst(0.7f, 0.85f, Fbm(u, v, 8, 8, 2, 413)) * 0.4f);     // dry, grey fibre
            });
            var bone = SavePsx("T_Bone", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#9C957F"), Hex("#D6D0BD"), 0.4f + 0.35f * Fbm(u, v, 8, 8, 4, 421) + 0.2f * Gr(x, y, 43));
                Worley(u, v, 7, 422, out float f1, out float f2, out _);
                c *= 1f - 0.45f * Sst(0.035f, 0f, f2 - f1);                                       // cracks
                c = Mx(c, Hex("#7E7356"), Sst(0.58f, 0.78f, Fbm(u, v, 4, 4, 3, 423)) * 0.55f);   // old stains
                if (Hash(x, y, 44) > 0.985f) c = Hex("#5E6A52");                                    // lichen specks
                return c;
            });
            var planks = SavePsx("T_DockPlanks", 128, (u, v, x, y) =>
            {
                // a 2 m tile of five 40 cm boards (grain along u), each its own tone, butt joints staggered,
                // nail pairs over the joists, dark seams between the boards
                int board = Mathf.FloorToInt(v * 5f);
                float bv = v * 5f - board;
                float shift = Hash(board, 3, 430);
                float g = Fbm((u + shift) % 1f, v, 2, 16, 3, 431);
                var c = Mx(Hex("#4A3426"), Hex("#9A7458"), 0.25f + 0.45f * g + 0.3f * (Hash(board, 5, 436) - 0.4f) + 0.14f * Gr(x, y, 45));
                float line = ((v * 13f + Fbm(u, v, 3, 3, 2, 432) * 1.6f) % 1f);
                c *= 1f - 0.28f * Sst(0.09f, 0f, line);
                Worley((u + shift) % 1f * 0.5f, v, 4, 433, out float k1, out _, out _);
                c = Mx(c, Hex("#2A241E"), Sst(0.05f, 0.015f, k1) * 0.8f);                             // knots
                c = Mx(c, Hex("#9C9284"), Sst(0.74f, 0.88f, Fbm(u, v, 10, 3, 2, 434)) * 0.35f);      // salt silvering
                c = Mx(c, Hex("#34402E"), Sst(0.66f, 0.8f, Fbm(u, v, 5, 5, 3, 435)) * 0.3f);         // damp green
                float joint = (u + shift * 0.7f) % 1f;
                if (bv < 0.06f || bv > 0.96f) c *= 0.42f;                                                // seams between boards
                else if (joint < 0.012f) c *= 0.5f;                                                      // butt joint
                else if ((Mathf.Abs(joint - 0.25f) < 0.012f || Mathf.Abs(joint - 0.75f) < 0.012f) && (Mathf.Abs(bv - 0.25f) < 0.07f || Mathf.Abs(bv - 0.75f) < 0.07f))
                    c = Hex("#1C1814");                                                                  // nail heads
                return c;
            });
            var pile = SavePsx("T_DockPile", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#16130F"), Hex("#3A3128"), 0.3f + 0.5f * Fbm(u, v, 16, 2, 3, 441) + 0.2f * Gr(x, y, 46));
                c = Mx(c, Hex("#233222"), Sst(0.55f, 0.7f, Fbm(u, v, 4, 4, 3, 442)) * 0.7f);       // weed slime
                if (Hash(x, y, 47) > 0.975f && Fbm(u, v, 6, 6, 2, 443) > 0.45f) c = Mx(Hex("#7E7C70"), Hex("#A6A396"), Hash(x, y, 48));   // barnacles
                return c;
            });
            var rock = SavePsx("T_SeaRock", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#1B1D1F"), Hex("#46494B"), 0.3f + 0.45f * Fbm(u, v, 10, 10, 4, 451) + 0.22f * Gr(x, y, 49));
                Worley(u, v, 9, 452, out float f1, out _, out _);
                c *= 1f - 0.45f * Sst(0.07f, 0f, f1);                                               // pits
                c = Mx(c, Hex("#8C908A"), Sst(0.64f, 0.74f, Fbm(u, v, 6, 6, 3, 453)) * 0.6f);      // salt crust
                return Mx(c, Hex("#1F2C1C"), Sst(0.6f, 0.75f, Fbm(u, v, 3, 3, 3, 454)) * 0.6f);    // weed
            });
            var drift = SavePsx("T_Driftwood", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#5E5A52"), Hex("#A29C8E"), 0.35f + 0.45f * Fbm(u, v, 12, 2, 3, 461) + 0.2f * Gr(x, y, 50));
                float crack = ((u * 9f + Fbm(u, v, 3, 3, 2, 462) * 2f) % 1f);
                return c * (1f - 0.4f * Sst(0.07f, 0f, crack));
            });
            var sail = SavePsx("T_Sail", 64, (u, v, x, y) =>
            {
                float weave = (Mathf.Sin(x * 1.7f) * Mathf.Sin(y * 1.7f)) * 0.5f + 0.5f;
                var c = Mx(Hex("#101214"), Hex("#2C2F33"), 0.3f + 0.4f * Fbm(u, v, 6, 6, 3, 471) + 0.2f * weave);
                return Mx(c, Hex("#4A4C4C"), Sst(0.66f, 0.82f, Fbm(u, v, 4, 4, 3, 472)) * 0.5f);   // salt bleach
            });
            var shell = SavePsx("T_CrabShell", 64, (u, v, x, y) =>
            {
                var c = Mx(Hex("#341009"), Hex("#702614"), 0.3f + 0.5f * Fbm(u, v, 6, 6, 3, 481) + 0.2f * Gr(x, y, 51));
                if (Hash(x, y, 52) > 0.96f) c = Hex("#A66E4A");
                return Mx(c, Hex("#1E0A06"), Sst(0.6f, 0.75f, Fbm(u, v, 5, 5, 2, 482)) * 0.6f);
            });
            var sand = SavePsx("T_Sand", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#7A6A50"), Hex("#AE9C78"), 0.35f + 0.35f * Fbm(u, v, 8, 8, 4, 491) + 0.3f * Gr(x, y, 53));
                float rip = Mathf.Sin((u * 9f + v * 3f + Fbm(u, v, 3, 3, 2, 492) * 2.5f) * Mathf.PI * 2f);
                c *= 1f - 0.06f * rip;
                if (Hash(x, y, 54) > 0.988f) c = Hex("#C2B494");     // shell grit
                if (Hash(x, y, 55) < 0.012f) c = Hex("#36332B");     // dark grit
                return c;
            });
            var wet = SavePsx("T_WetSand", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#4A4030"), Hex("#6E6048"), 0.35f + 0.4f * Fbm(u, v, 8, 8, 4, 501) + 0.25f * Gr(x, y, 56));
                float rip = Mathf.Sin((u * 12f + Fbm(u, v, 4, 4, 2, 502) * 3f) * Mathf.PI * 2f);
                c *= 1f - 0.1f * rip;
                return Mx(c, Hex("#2A3226"), Sst(0.68f, 0.8f, Fbm(u, v, 5, 5, 3, 503)) * 0.5f);   // weed wrack
            });
            var bed = SavePsx("T_Seabed", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#4A4E46"), Hex("#72766A"), 0.35f + 0.4f * Fbm(u, v, 6, 6, 4, 511) + 0.25f * Gr(x, y, 57));
                return Mx(c, Hex("#1E2A22"), Sst(0.58f, 0.72f, Fbm(u, v, 4, 4, 3, 512)) * 0.7f);   // weed beds
            });

            Lit("M_PalmBark", Color.white, bark);
            Cutout(Lit("M_BeachFlora", Color.white, flora, null, 0f, 1f, null, false));
            Lit("M_Coconut", Color.white, coco);
            Lit("M_Bone", Color.white, bone);
            Lit("M_DockPlanks", Color.white, planks);
            Lit("M_DockPile", Color.white, pile);
            Lit("M_SeaRock", Color.white, rock);
            Lit("M_Driftwood", Color.white, drift);
            var sm = Lit("M_Sail", Color.white, sail);
            sm.SetFloat("_Cull", 0f);
            Lit("M_CrabShell", Color.white, shell);
            Lit("M_BirdDark", Hex("#25282D"));
            Lit("M_BirdPale", Hex("#8E949A"));
            Lit("M_Sand", Color.white, sand);
            Lit("M_WetSand", Color.white, wet);
            Lit("M_Seabed", Color.white, bed);

            var ocean = AssetOf("M_Ocean", "ElderTree/Ocean");
            ocean.SetColor("_DeepColor", new Color(0.03f, 0.1f, 0.22f));
            ocean.SetColor("_ShallowColor", new Color(0.16f, 0.44f, 0.48f));
            ocean.SetColor("_SkyColor", new Color(0.5f, 0.6f, 0.7f));
            ocean.SetColor("_CrestColor", new Color(0.12f, 0.36f, 0.4f));
            ocean.SetColor("_FoamColor", new Color(0.74f, 0.8f, 0.84f));
            ocean.SetFloat("_DepthFade", 5f);
            ocean.SetFloat("_ShallowAlpha", 0.6f);
            ocean.SetFloat("_WaveAmp", 0.34f);
            ocean.SetFloat("_WaveLength", 15f);
            ocean.SetFloat("_FoamWidth", 0.5f);

            var sky = AssetOf("M_BeachSky", "ElderTree/NightSky");
            sky.SetColor("_ZenithColor", new Color(0.13f, 0.26f, 0.46f));
            sky.SetColor("_HorizonColor", new Color(0.56f, 0.64f, 0.72f));
            sky.SetColor("_GroundColor", new Color(0.5f, 0.58f, 0.66f));
            sky.SetFloat("_HorizonSharpness", 2.2f);
            sky.SetFloat("_StarBrightness", 0f);
            sky.SetColor("_MoonColor", new Color(1f, 0.97f, 0.88f));
            sky.SetFloat("_MoonSize", 0.028f);
            sky.SetFloat("_HaloStrength", 1.5f);
            sky.SetColor("_CloudColor", new Color(0.44f, 0.5f, 0.57f));
            sky.SetFloat("_CloudStrength", 0.7f);
            // a heavy deck of grey cumulus drifting in off the sea, bright where the sun catches the edges
            sky.SetFloat("_CloudLayer", 1f);
            sky.SetFloat("_CloudCover", 0.62f);
            sky.SetFloat("_CloudScale", 1.05f);
            sky.SetFloat("_CloudSpeed", 0.01f);
            sky.SetColor("_CloudLitColor", new Color(0.9f, 0.92f, 0.94f));

            Unlit("M_FX_Spray", new Color(0.9f, 0.93f, 0.94f, 1f), AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/T_FX_SoftDot.png"));
            AssetDatabase.SaveAssets();

            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { BeachDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                imp.animationType = ModelImporterAnimationType.None;
                imp.importAnimation = false;
                imp.importCameras = false; imp.importLights = false;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                imp.addCollider = false;
                imp.isReadable = true;
                foreach (var m in BeachSlotMats)
                {
                    var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{m}.mat");
                    if (mat) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m), mat);
                }
                imp.SaveAndReimport();
                n++;
            }
            BuildSplashFX();
            EnsureFogFeature();
            AssetDatabase.SaveAssets();
            return $"Beach art built ({n} models)";
        }

        /// <summary>2x2 cutout atlas: green palm frond (TL), dead frond (TR), dune grass (BL), fishing net (BR).</summary>
        static Color BeachFlora(float u, float v, int x, int y)
        {
            float lu = (u % 0.5f) * 2f, lv = (v % 0.5f) * 2f;
            bool top = v >= 0.5f, right = u >= 0.5f;
            var clear = new Color(0, 0, 0, 0);
            if (top)
            {
                // a frond: the rachis up the middle (v = base -> tip), leaflets slanting toward the tip, ragged ends
                bool dead = right;
                float dx = Mathf.Abs(lu - 0.5f) * 2f;
                float env = Mathf.Sin(Mathf.Clamp01(lv) * Mathf.PI * 0.9f + 0.1f) * (dead ? 0.8f : 1f);
                if (dx > env * 0.97f) return clear;
                bool rachis = dx < 0.06f;
                float ph = lv * (dead ? 11f : 12f) - dx * (dead ? -3f : 4f) + (Fbm(lu, lv, 3, 3, 2, dead ? 521 : 522) - 0.5f) * 0.5f;
                bool leaf = ph - Mathf.Floor(ph) < (dead ? 0.66f : 0.8f);
                if (!leaf && !rachis) return clear;
                if (!rachis && Hash(x / 2, y / 2, dead ? 523 : 524) < (dead ? 0.12f : 0.02f) + 0.3f * Sst(0.8f, 1f, dx / Mathf.Max(env, 0.01f))) return clear;
                Color c;
                if (rachis) c = dead ? Hex("#6A5A3E") : Hex("#5E6444");
                else if (dead) c = Mx(Hex("#2C241A"), Hex("#6E5C40"), 0.3f + 0.4f * lv + 0.25f * Gr(x, y, 58));
                else
                {
                    c = Mx(Hex("#22402A"), Hex("#5E8A4C"), 0.25f + 0.45f * (1f - dx) + 0.2f * lv + 0.2f * Gr(x, y, 59));
                    c = Mx(c, Hex("#5A4A2A"), Sst(0.78f, 1f, dx / Mathf.Max(env, 0.01f)) * Sst(0.45f, 0.65f, Fbm(lu, lv, 5, 5, 2, 525)) * 0.8f);   // brown, salt-burnt tips
                }
                c.a = 1; return c;
            }
            if (!right)
            {
                // dune grass: thin blades rising from the bottom, straw tips, leaning with the wind
                float col = lu * 14f;
                int ci = Mathf.FloorToInt(col);
                float h = 0.4f + 0.6f * Hash(ci, 7, 531);
                float lean = (0.15f + Hash(ci, 9, 532) * 0.35f) * lv;
                float fx = col - ci - lean * 3f;
                float w = 0.3f * (1f - lv / h);
                if (lv > h || Mathf.Abs(fx - 0.5f) > w) return clear;
                var c = Mx(Hex("#26301C"), Hex("#6A7048"), 0.3f + 0.6f * lv / h + 0.2f * Gr(x, y, 60));
                c = Mx(c, Hex("#9A9068"), Sst(0.65f, 1f, lv / h) * 0.8f);
                c.a = 1; return c;
            }
            {
                // fishing net: a diamond mesh of tarred twine with knots, torn in places
                float a = (lu + lv) * 7f, b = (lu - lv) * 7f;
                float fa = Mathf.Abs(a - Mathf.Round(a)), fb = Mathf.Abs(b - Mathf.Round(b));
                bool line = fa < 0.07f || fb < 0.07f;
                if (!line || Fbm(lu, lv, 4, 4, 3, 541) > 0.68f) return clear;
                var c = Mx(Hex("#211E18"), Hex("#57503E"), 0.35f + 0.4f * Hash(x, y, 61));
                if (fa < 0.1f && fb < 0.1f) c *= 0.6f;
                c.a = 1; return c;
            }
        }

        static void BuildSplashFX()
        {
            // the sea bursting up through the planks: a spray of droplets falling back, and a puff of mist
            var ps = NewPS("FX_Splash", M("M_FX_Spray"), out var pr);
            var m = ps.main;
            m.duration = 1f;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.95f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 7.5f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.2f);     // big enough to survive the 0.4 render scale
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.92f, 0.95f, 0.96f, 1f), new Color(0.7f, 0.78f, 0.8f, 1f));
            m.gravityModifier = 1.6f;
            m.maxParticles = 120;
            Burst(ps, 54);
            // droplets streak along their flight instead of floating as round dots
            pr.renderMode = ParticleSystemRenderMode.Stretch;
            pr.velocityScale = 0.05f;
            pr.lengthScale = 1.3f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 24f; sh.radius = 0.35f; sh.rotation = new Vector3(-90f, 0, 0);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, new Color(0.8f, 0.9f, 0.95f), 1f, 0f);
            SizeOverLife(ps, 1f, 0.55f);

            var mist = NewPS("Mist", M("M_FX_Spray"), out _);
            mist.transform.SetParent(ps.transform, false);
            var mm = mist.main;
            mm.duration = 1f;
            mm.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
            mm.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            mm.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
            mm.startColor = new Color(0.86f, 0.9f, 0.92f, 0.5f);
            mm.gravityModifier = 0.1f;
            mm.maxParticles = 12;
            Burst(mist, 9);
            var ms = mist.shape; ms.shapeType = ParticleSystemShapeType.Hemisphere; ms.radius = 0.4f; ms.rotation = new Vector3(-90f, 0, 0);
            var mc = mist.colorOverLifetime; mc.enabled = true; mc.color = Grad(Color.white, Color.white, 0.6f, 0f);
            SizeOverLife(mist, 0.6f, 1.8f);
            SavePS(ps, "FX_Splash");
        }

        // ================================================================== island + lagoon
        static float DistSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        static float SMax(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (a - b) / k);
            return Mathf.Lerp(b, a, h) + k * h * (1f - h);
        }

        /// <summary>Signed distance into the island (m): a round main island to the north, two arms curling south
        /// around the lagoon, so the open sea only shows to the south.</summary>
        static float IslandD(float x, float z)
        {
            var p = new Vector2(x, z);
            float main = 38f - Vector2.Distance(p, new Vector2(0f, 44f));
            float west = 8.5f - DistSeg(p, new Vector2(-30f, 32f), new Vector2(-60f, -26f));
            float east = 7.5f - DistSeg(p, new Vector2(32f, 30f), new Vector2(64f, -20f));
            float d = SMax(main, SMax(west, east, 5f), 7f);
            return d + (Mathf.PerlinNoise(x * 0.045f + 3.1f, z * 0.045f + 8.7f) - 0.5f) * 7f;
        }

        static float BeachHeight(float x, float z)
        {
            float d = IslandD(x, z);
            float n2 = Mathf.PerlinNoise(x * 0.11f + 5.3f, z * 0.11f + 9.1f);
            float n3 = Mathf.PerlinNoise(x * 0.35f + 1.9f, z * 0.35f + 2.7f);
            float h;
            if (d >= 0f)
            {
                h = SeaLevel - 0.05f + Sst(0f, 6f, d) * 1.15f;                      // the beach, up from the waterline
                float dunes = Sst(8f, 20f, d);
                h += dunes * (1.8f + 3.2f * Mathf.PerlinNoise(x * 0.06f + 1.7f, z * 0.06f + 4.2f) + (n2 - 0.5f) * 1.2f);
            }
            else
            {
                float s = -d;
                h = SeaLevel - 0.05f - Sst(0f, 9f, s) * 1.7f - Sst(14f, 60f, s) * 6.5f + (n2 - 0.5f) * 0.5f;   // lagoon, then the deep
            }
            // the arena: north of the deck a broad sandbar comes up to deck height
            float r = Mathf.Sqrt(x * x + z * z);
            if (z > DeckNorth - 1.8f)
            {
                float k = Sst(27f, 19f, r) * Sst(DeckNorth - 1.8f, DeckNorth - 0.8f, z);     // already level under the deck's last metre
                float plateau = -0.06f + 0.05f * (n3 - 0.5f) + Sst(15f, 26f, r) * 0.55f;
                h = Mathf.Lerp(h, Mathf.Max(h, plateau), k);
            }
            // never let the shore graze the water plane (it z-fights)
            float w = h - SeaLevel;
            if (Mathf.Abs(w) < 0.06f) h = SeaLevel + (w < 0f ? -0.06f : 0.06f);
            return h;
        }

        static GameObject BuildBeachGround(Transform parent, out MeshCollider collider)
        {
            const float Ext = 140f, Cell = 1.6f;
            int n = Mathf.CeilToInt(Ext * 2 / Cell) + 1;
            var verts = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = -Ext + i * Cell, z = -Ext + j * Cell;
                verts[j * n + i] = new Vector3(x, BeachHeight(x, z), z);
                uvs[j * n + i] = new Vector2(x, z) / 4f;
            }
            var subs = new[] { new List<int>(), new List<int>(), new List<int>() };   // seabed, wet sand, dry sand
            void Tri(int a, int b, int c)
            {
                float y = (verts[a].y + verts[b].y + verts[c].y) / 3f;
                var l = y < SeaLevel - 0.1f ? subs[0] : y < SeaLevel + 0.55f ? subs[1] : subs[2];
                l.Add(a); l.Add(b); l.Add(c);
            }
            for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                if (((i + j) & 1) == 0) { Tri(a, c, d); Tri(a, d, b); }
                else { Tri(a, c, b); Tri(b, c, d); }
            }
            var mesh = new Mesh { name = "BeachGround", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts; mesh.uv = uvs;
            mesh.subMeshCount = 3;
            for (int s = 0; s < 3; s++) mesh.SetTriangles(subs[s], s);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            string path = BeachDir + "/BeachGround.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) { old.Clear(); EditorUtility.CopySerialized(mesh, old); mesh = old; }
            else AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject("BeachGround");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { M("M_Seabed"), M("M_WetSand"), M("M_Sand") };
            collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            go.AddComponent<GroundSurface>().kind = SurfaceKind.Sand;
            go.isStatic = true;
            return go;
        }

        /// <summary>A polar grid out to the horizon, fine near the arena. Vertex colour R = open water (0 at the
        /// shore, 1 where the seabed is 3 m down): the shader calms the swell and draws surf bands from it.</summary>
        static GameObject BuildOcean(Transform parent)
        {
            const int Seg = 180;
            var radii = new List<float> { 0f };
            float r = 0f, dr = 1.1f;
            while (r < 900f)
            {
                r += dr;
                radii.Add(r);
                if (r > 45f) dr *= 1.07f;
            }
            var verts = new List<Vector3> { Vector3.zero };
            var cols = new List<Color> { Open(0, 0) };
            Color Open(float x, float z)
            {
                if (Mathf.Abs(x) > 138f || Mathf.Abs(z) > 138f) return Color.white;
                float depth = SeaLevel - BeachHeight(x, z);
                float o = Sst(0.2f, 3f, depth);
                return new Color(o, o, o, 1f);
            }
            for (int k = 1; k < radii.Count; k++)
                for (int s = 0; s < Seg; s++)
                {
                    float a = (s + (k & 1) * 0.5f) / Seg * Mathf.PI * 2f;
                    var p = new Vector3(Mathf.Cos(a) * radii[k], 0f, Mathf.Sin(a) * radii[k]);
                    verts.Add(p);
                    cols.Add(Open(p.x, p.z));
                }
            var tris = new List<int>();
            for (int s = 0; s < Seg; s++) { tris.Add(0); tris.Add(1 + (s + 1) % Seg); tris.Add(1 + s); }
            for (int k = 1; k < radii.Count - 1; k++)
            {
                int r0 = 1 + (k - 1) * Seg, r1 = 1 + k * Seg;
                for (int s = 0; s < Seg; s++)
                {
                    int a = r0 + s, b = r0 + (s + 1) % Seg, c = r1 + s, d = r1 + (s + 1) % Seg;
                    tris.Add(a); tris.Add(b); tris.Add(d);
                    tris.Add(a); tris.Add(d); tris.Add(c);
                }
            }
            var mesh = new Mesh { name = "BeachOcean", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1800f, 4f, 1800f));
            string path = BeachDir + "/BeachOcean.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) { old.Clear(); EditorUtility.CopySerialized(mesh, old); mesh = old; }
            else AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject("Ocean");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0f, SeaLevel, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = M("M_Ocean");
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        // ================================================================== placement
        static GameObject BP(string prop, Vector3 pos, float yaw, Transform parent, float scale = 1f, Col col = Col.Mesh, bool ground = true, string name = null, bool isStatic = true)
        {
            var prefab = BeachProp(prop) ?? AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Art/Models/Props/{prop}.fbx");
            if (prefab == null) { Debug.LogWarning("[Beach] missing prop " + prop); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            if (ground) pos.y = BeachHeight(pos.x, pos.z);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = Vector3.one * scale;
            if (name != null) go.name = name;
            switch (col)
            {
                case Col.Mesh:
                    foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                        if (mf.GetComponent<Collider>() == null) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                    break;
                case Col.Box:
                {
                    var b = go.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
                    var bc = go.AddComponent<BoxCollider>();
                    bc.center = go.transform.InverseTransformPoint(b.center);
                    var sz = go.transform.InverseTransformVector(b.size);
                    bc.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z));
                    break;
                }
                case Col.Trunk:
                {
                    var cap = go.AddComponent<CapsuleCollider>();
                    cap.radius = 0.3f; cap.height = 6f; cap.center = new Vector3(0, 3f, 0);
                    break;
                }
            }
            if (isStatic) foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            return go;
        }

        /// <summary>Unity yaw that turns local +X toward the world direction (dx, dz). (Blender X imports as Unity -X, Blender +Y as Unity -Z.)</summary>
        static float YawX(float dx, float dz) => Mathf.Atan2(-dz, dx) * Mathf.Rad2Deg;

        // ================================================================== scene
        [MenuItem("Elder Tree/9f. Build Beach Arena (Shark)")]
        public static string BuildBeachScene()
        {
            if (cues == null) BuildAudio();
            if (BeachProp("Palm_A") == null) return "Beach models missing: run Tools/Blender/build_beach.py first";
            Random.InitState(2207);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupPsxPipeline();

            // ---------------- sky, ambient, Unity fog: an overcast blue day, cold and heavy
            var sky = M("M_BeachSky");
            var sunRot = Quaternion.Euler(38f, 18f, 0);                // the sun hangs in the south-south-west over the open sea
            Vector3 toSun = -(sunRot * Vector3.forward);
            sky.SetVector("_MoonDir", new Vector4(toSun.x, toSun.y, toSun.z, 0));
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.65f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.44f, 0.5f, 0.57f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.21f, 0.19f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.52f, 0.6f, 0.68f);
            RenderSettings.fogDensity = 0.0062f;
            RenderSettings.reflectionIntensity = 0.2f;

            var sysRoot = new GameObject("--- Systems ---").transform;
            var envRoot = new GameObject("--- Environment ---").transform;
            var dockRoot = new GameObject("--- Dock ---").transform;
            var beachRoot = new GameObject("--- Beach ---").transform;
            var lifeRoot = new GameObject("--- Wildlife ---").transform;
            var lightRoot = new GameObject("--- Lighting ---").transform;
            var actorRoot = new GameObject("--- Actors ---").transform;

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(lightRoot);
            sunGo.transform.rotation = sunRot;
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.98f, 0.96f, 0.9f);
            sun.intensity = 2.1f;
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 0.8f;
            sunGo.AddComponent<UniversalAdditionalLightData>();
            RenderSettings.sun = sun;

            var ground = BuildBeachGround(envRoot, out var groundCol);
            BuildOcean(envRoot);
            float H(float x, float z) => BeachHeight(x, z);

            // ---------------- the dock: 4 m modules filling the arena south of the sandbar, plus piers out to sea
            var cells = new HashSet<Vector2Int>();
            for (int i = -6; i <= 5; i++)
            for (int j = -6; j <= 1; j++)
            {
                float cx = i * 4f + 2f, cz = j * 4f + 2f;
                float qx = Mathf.Clamp(0f, cx - 2f, cx + 2f), qz = Mathf.Clamp(0f, cz - 2f, cz + 2f);
                if (new Vector2(qx, qz).magnitude < BeachWall + 0.3f) cells.Add(new Vector2Int(i, j));
            }
            var piers = new[] { new Vector2Int(-3, -6), new Vector2Int(-3, -7), new Vector2Int(-3, -8), new Vector2Int(1, -6), new Vector2Int(1, -7),
                                new Vector2Int(5, -1), new Vector2Int(6, -1), new Vector2Int(-6, -2) };
            foreach (var p in piers) cells.Add(p);
            Vector3 CellPos(Vector2Int c) => new(c.x * 4f + 2f, 0f, c.y * 4f + 2f);
            // rotten holes broken through the deck: step in and you fall into the sea (DockHoles). Kept off the
            // spawn, the arena centre (the shark measures its floor there) and the rail.
            var holeCells = new Dictionary<Vector2Int, string>
            {
                { new Vector2Int(-3, -2), "DockSection_HoleA" }, { new Vector2Int(1, -1), "DockSection_HoleB" }, { new Vector2Int(1, -4), "DockSection_HoleA" },
                { new Vector2Int(-2, -4), "DockSection_HoleB" }, { new Vector2Int(3, 0), "DockSection_HoleB" }, { new Vector2Int(-3, 1), "DockSection_HoleA" }
            };
            var holes = new List<DockHoles.Hole>();
            int mods = 0;
            foreach (var c in cells.OrderBy(c => c.y).ThenBy(c => c.x))
            {
                var p = CellPos(c);
                float yaw = Random.value < 0.5f ? 0f : 180f;
                if (holeCells.TryGetValue(c, out var holeKind))
                {
                    var hm = BP(holeKind, p, yaw, dockRoot, 1f, Col.None, false, $"Deck_{c.x}_{c.y}_Hole");
                    var k0 = hm.GetComponentsInChildren<Transform>().First(t => t.name.StartsWith("HoleCorner0"));
                    var k1 = hm.GetComponentsInChildren<Transform>().First(t => t.name.StartsWith("HoleCorner1"));
                    Vector3 a = hm.transform.InverseTransformPoint(k0.position), b = hm.transform.InverseTransformPoint(k1.position);
                    float x0 = Mathf.Min(a.x, b.x), x1 = Mathf.Max(a.x, b.x), z0 = Mathf.Min(a.z, b.z), z1 = Mathf.Max(a.z, b.z);
                    // the deck around the opening: four boxes, nothing over the gap
                    void Box(float xa, float xb, float za, float zb)
                    {
                        if (xb - xa < 0.01f || zb - za < 0.01f) return;
                        var bx = hm.AddComponent<BoxCollider>();
                        bx.center = new Vector3((xa + xb) * 0.5f, -0.15f, (za + zb) * 0.5f); bx.size = new Vector3(xb - xa, 0.3f, zb - za);
                    }
                    Box(-2f, 2f, -2f, z0); Box(-2f, 2f, z1, 2f); Box(-2f, x0, z0, z1); Box(x1, 2f, z0, z1);
                    hm.AddComponent<GroundSurface>().kind = SurfaceKind.Wood;
                    Vector3 w0 = k0.position, w1 = k1.position;
                    holes.Add(new DockHoles.Hole { center = new Vector3((w0.x + w1.x) * 0.5f, 0f, (w0.z + w1.z) * 0.5f),
                                                   halfSize = new Vector2(Mathf.Abs(w1.x - w0.x) * 0.5f, Mathf.Abs(w1.z - w0.z) * 0.5f) });
                    mods++;
                    continue;
                }
                var mod = BP(Random.value < 0.3f ? "DockSection_B" : "DockSection", p, yaw, dockRoot, 1f, Col.None, false, $"Deck_{c.x}_{c.y}");
                var bc = mod.AddComponent<BoxCollider>();
                bc.center = new Vector3(0, -0.15f, 0); bc.size = new Vector3(4f, 0.3f, 4f);
                mod.AddComponent<GroundSurface>().kind = SurfaceKind.Wood;
                mods++;
            }
            var holesGo = new GameObject("DockHoles");
            holesGo.transform.SetParent(dockRoot);
            holesGo.transform.position = new Vector3(0f, 0f, -9f);         // fallback respawn: the player spawn
            var dh = holesGo.AddComponent<DockHoles>();
            dh.holes = holes.ToArray();
            dh.waterLevel = SeaLevel;
            bool NearHole(Vector3 q, float pad) => holes.Any(h => Mathf.Abs(q.x - h.center.x) < h.halfSize.x + pad && Mathf.Abs(q.z - h.center.z) < h.halfSize.y + pad);
            // fascia along every open edge, mooring pilings at the outer corners
            var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
            var postSpots = new HashSet<Vector2Int>();
            foreach (var c in cells)
                foreach (var d in dirs)
                {
                    if (cells.Contains(c + d)) continue;
                    var p = CellPos(c) + new Vector3(d.x, 0, d.y) * 2f;
                    if (d.y == 1 && p.z >= DeckNorth - 0.1f && p.magnitude < 24f) continue;      // runs onto the sand there
                    float yaw = YawX(-d.y, d.x);                                                   // local X along the edge
                    var e = BP("DockEdge", p, yaw, dockRoot, 1f, Col.None, false);
                    // face the fascia outward: its outer face is on one side of the local origin, so flip if needed
                    var fwd = e.transform.TransformDirection(Vector3.forward);
                    if (Vector3.Dot(fwd, new Vector3(d.x, 0, d.y)) < 0f) e.transform.Rotate(0, 180f, 0);
                    var side = new Vector2Int(Mathf.Abs(d.y), Mathf.Abs(d.x));
                    var corner0 = c * 2 + new Vector2Int(1, 1) + d - side;     // corners in half-cell units
                    var corner1 = c * 2 + new Vector2Int(1, 1) + d + side;
                    postSpots.Add(corner0); postSpots.Add(corner1);
                }
            int posts = 0;
            foreach (var hc in postSpots)
            {
                var p = new Vector3(hc.x * 2f, 0f, hc.y * 2f);
                if (p.magnitude < BeachWall + 0.8f || p.z > DeckNorth + 0.5f) continue;           // outside the rail only
                if (Random.value < 0.35f) continue;
                BP("DockPost", p + new Vector3(Random.Range(-0.12f, 0.12f), 0, Random.Range(-0.12f, 0.12f)), Random.Range(0f, 360f), dockRoot, 1f, Col.None, false, $"Piling{posts++}");
            }

            // ---------------- the railing around the deck part of the ring (rope rail, reference 2)
            float arcA = Mathf.Asin((DeckNorth - 0.5f) / (BeachWall - 0.2f)) * Mathf.Rad2Deg;   // where the deck meets the sand
            float from = 180f - arcA, to = 360f + arcA;
            int segs = Mathf.RoundToInt((to - from) * Mathf.Deg2Rad * (BeachWall - 0.2f) / 4f);
            var railPts = new List<Vector3>();
            for (int k = 0; k <= segs; k++) railPts.Add(Polar(Mathf.Lerp(from, to, k / (float)segs), BeachWall - 0.2f));
            for (int k = 0; k < segs; k++)
            {
                var a = railPts[k]; var b = railPts[k + 1];
                var rail = BP("DockRail", a, YawX(a.x - b.x, a.z - b.z), dockRoot, 1f, Col.None, false, $"Rail{k}");   // the bay runs along Blender +X = Unity -X
                rail.transform.localScale = new Vector3(Vector3.Distance(a, b) / 4f, 1f, 1f);
            }
            BP("DockPost", railPts[0], 0f, dockRoot, 1f, Col.Box, false, "RailEndW");
            BP("DockPost", railPts[segs], 0f, dockRoot, 1f, Col.Box, false, "RailEndE");
            foreach (int k in new[] { segs / 4, segs / 2, segs * 3 / 4 })
            {
                var p = railPts[k] + railPts[k].normalized * 0.35f;
                var pen = BP("Pennant", p, Random.Range(0f, 360f), dockRoot, 1f, Col.None, false, $"Pennant{k}", false);
                var flag = Find(pen, "Flag");
                if (flag != null) { var sw = flag.gameObject.AddComponent<PalmSway>(); sw.aroundUp = true; sw.amplitude = 14f; sw.flutter = 7f; sw.flutterSpeed = 5.5f; sw.gustSpeed = 0.35f; }
            }

            // ---------------- dock dressing: by the rail, so the middle stays open for the fight
            void Kit(string prop, float ang, float r, float yaw, Col col = Col.Box, float s = 1f)
            {
                var p = Polar(ang, r);
                BP(prop, new Vector3(p.x, 0f, p.z), yaw, dockRoot, s, col, false);
            }
            Kit("Crate", 200f, 17.6f, 15f); Kit("Crate", 204f, 17.9f, 40f, Col.Box, 0.8f); Kit("Barrel", 197f, 18.3f, 0f);
            Kit("CrabPot", 238f, 18.2f, Tangent(238f)); Kit("CrabPot", 241f, 18.3f, Tangent(241f) + 8f);
            Kit("RopeCoil", 252f, 18.4f, 30f, Col.None); Kit("Bollard", 262f, 18.7f, 0f);
            Kit("Barrel", 296f, 18.2f, 0f); Kit("Barrel", 299f, 18.5f, 0f, Col.Box, 0.9f); Kit("Crate", 302f, 17.9f, -20f);
            Kit("CrabPot", 322f, 18.1f, Tangent(322f)); Kit("RopeCoil", 334f, 18.4f, 80f, Col.None); Kit("Bollard", 346f, 18.7f, 0f);
            Kit("Bollard", 214f, 18.7f, 0f); Kit("RopeCoil", 186f, 18.3f, 0f, Col.None);
            // out on the piers: a shack at the end of the east pier, a moored rowboat, pots and nets
            // the doorway (Blender -Y, Unity +Z) faces back down the pier toward the arena
            BP("FishingShack", new Vector3(26.5f, 0f, -2f), -90f, dockRoot, 1f, Col.Mesh, false, "FishingShack");
            var boat = BP("Rowboat", new Vector3(-12.9f, SeaLevel + 0.05f, -25.5f), 4f, dockRoot, 1f, Col.None, false, "Rowboat", false);
            boat.AddComponent<Bobber>();
            var boat2 = BP("Rowboat", new Vector3(29.5f, SeaLevel + 0.05f, -5.4f), 96f, dockRoot, 0.9f, Col.None, false, "Rowboat2", false);
            boat2.AddComponent<Bobber>().period = 5.1f;
            BP("CrabPot", new Vector3(-10.6f, 0f, -29.2f), 20f, dockRoot, 1f, Col.None, false);
            BP("CrabPot", new Vector3(-9.2f, 0f, -29.6f), 70f, dockRoot, 1f, Col.None, false);
            BP("RopeCoil", new Vector3(5.2f, 0f, -26.6f), 0f, dockRoot, 1f, Col.None, false);
            BP("NetRack", new Vector3(22.2f, 0f, -0.6f), 0f, dockRoot, 1f, Col.None, false);

            // ---------------- palms: along the beaches, leaning out over the water; a few framing the sandbar
            var palms = new List<Vector3>();
            int palmCount = 0;
            void Palm(Vector3 p, string kind, float s)
            {
                var go = BP(kind, p, 0f, beachRoot, s, Col.Trunk, true, $"{kind}_{palmCount++}", false);
                var crown = Find(go, "Crown");
                // lean out to sea: try yaws, keep the one whose crown is furthest from the island's heart
                float eps = 1.5f;
                var g = new Vector2(IslandD(p.x + eps, p.z) - IslandD(p.x - eps, p.z), IslandD(p.x, p.z + eps) - IslandD(p.x, p.z - eps));
                var outward = new Vector3(-g.x, 0, -g.y).normalized;
                if (p.magnitude < 26f) outward = Vector3.Lerp(outward, -p.normalized, 0.6f).normalized;   // the framing palms lean over the arena
                float best = float.MinValue, bestYaw = 0f, jit = Random.Range(-35f, 35f);
                for (int q = 0; q < 24; q++)
                {
                    go.transform.rotation = Quaternion.Euler(0, q * 15f, 0);
                    var off = crown != null ? crown.position - go.transform.position : Vector3.zero;
                    float d = Vector3.Dot(new Vector3(off.x, 0, off.z), Quaternion.Euler(0, jit, 0) * outward);
                    if (d > best) { best = d; bestYaw = q * 15f; }
                }
                go.transform.rotation = Quaternion.Euler(0, bestYaw, 0);
                foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = crown == null || !t.IsChildOf(crown);
                if (crown != null)
                {
                    var sw = crown.gameObject.AddComponent<PalmSway>();
                    sw.amplitude = Random.Range(2.2f, 3.8f);
                }
                palms.Add(p);
            }
            foreach (var (x, z, kind) in new[] { (-12.5f, 16.8f, "Palm_B"), (9.5f, 18.6f, "Palm_A"), (17.2f, 12.3f, "Palm_C"), (-18.4f, 10.6f, "Palm_C"), (2.5f, 21.5f, "Palm_D") })
                Palm(new Vector3(x, 0, z), kind, Random.Range(0.95f, 1.15f));
            for (int i = 0; i < 4000 && palmCount < 64; i++)
            {
                var p = new Vector3(Random.Range(-95f, 95f), 0, Random.Range(-60f, 100f));
                if (p.magnitude < BeachWall + 1.5f) continue;
                float h = H(p.x, p.z), d = IslandD(p.x, p.z);
                if (h < SeaLevel + 0.5f || d > 26f && Random.value > 0.25f) continue;              // mostly along the coast
                if (palms.Any(o => (o - p).sqrMagnitude < 5.2f * 5.2f)) continue;
                string kind = new[] { "Palm_A", "Palm_B", "Palm_C", "Palm_D", "Palm_A" }[Random.Range(0, 5)];
                Palm(p, kind, Random.Range(0.85f, 1.3f));
            }

            // ---------------- the leviathan in the dunes, the wreck on the reef
            // on the flat strand of the west arm, skull toward the arena (Blender +Y = Unity -Z)
            var whalePos = new Vector3(-30f, 0f, 33f);
            var whale = BP("WhaleBones", whalePos, 8f, beachRoot, 1f, Col.Mesh, true, "WhaleBones");
            whale.transform.position += Vector3.down * 0.3f;
            var wreckPos = new Vector3(46f, SeaLevel - 1.4f, -64f);
            BP("Shipwreck", wreckPos, 38f, beachRoot, 1.1f, Col.None, false, "Shipwreck");
            foreach (var (x, z, s) in new[] { (38f, -58f, 1.8f), (55f, -70f, 1.3f), (42f, -74f, 2.2f), (60f, -55f, 1.1f), (-48f, -52f, 2.4f), (-58f, -40f, 1.5f),
                                             (-36f, -62f, 1.2f), (70f, -30f, 1.6f), (-70f, -18f, 1.9f), (12f, -88f, 2.6f), (-18f, -95f, 1.7f) })
                BP("ReefSpire", new Vector3(x, H(x, z) - 0.5f, z), Random.Range(0f, 360f), beachRoot, s, Col.None, false);

            // ---------------- rocks on the shoreline and the arms, grass on the dunes, driftwood at the tide line
            int rocks = 0, grass = 0, drift = 0;
            for (int i = 0; i < 6000 && (rocks < 46 || grass < 300 || drift < 14); i++)
            {
                var p = new Vector3(Random.Range(-100f, 100f), 0, Random.Range(-80f, 105f));
                float r = p.magnitude;
                if (r < BeachWall + 0.6f) continue;
                float h = H(p.x, p.z);
                if (rocks < 46 && h > SeaLevel - 1.2f && h < SeaLevel + 0.5f && Random.value < 0.4f)
                {
                    string k = new[] { "Rock_A", "Rock_B", "Rock_C", "Rock_C" }[Random.Range(0, 4)];
                    BP(k, new Vector3(p.x, h - 0.25f, p.z), Random.Range(0f, 360f), beachRoot, Random.Range(0.7f, 1.6f), r < 40f ? Col.Mesh : Col.None, false);
                    rocks++;
                }
                else if (grass < 300 && h > SeaLevel + 1.0f && Random.value < (h > 0.6f ? 0.9f : 0.35f))
                {
                    if (palms.Any(o => (o - p).sqrMagnitude < 1f)) continue;
                    BP(Random.value < 0.45f ? "DuneGrass_B" : "DuneGrass", p, Random.Range(0f, 360f), beachRoot, Random.Range(0.8f, 1.5f), Col.None);
                    grass++;
                }
                else if (drift < 14 && h > SeaLevel + 0.15f && h < SeaLevel + 0.8f && r < 70f)
                {
                    BP(Random.value < 0.5f ? "Driftwood" : "Driftwood_B", p, Random.Range(0f, 360f), beachRoot, Random.Range(0.8f, 1.3f), Col.None);
                    drift++;
                }
            }
            // the sandbar's own edge: rocks, grass and a log closing off the north of the arena
            foreach (var (a, r, k) in new[] { (35f, 20.4f, "Rock_B"), (52f, 21.6f, "Rock_A"), (128f, 20.8f, "Rock_A"), (150f, 21.2f, "Rock_C"), (98f, 22.4f, "Rock_C") })
            {
                var p = Polar(a, r);
                BP(k, new Vector3(p.x, H(p.x, p.z) - 0.3f, p.z), Random.Range(0f, 360f), beachRoot, Random.Range(1f, 1.4f), Col.Mesh, false);
            }
            for (float a = 30f; a < 150f; a += 7f)
            {
                var p = Polar(a + Random.Range(-3f, 3f), Random.Range(19.8f, 22.5f));
                BP(Random.value < 0.5f ? "DuneGrass_B" : "DuneGrass", p, Random.Range(0f, 360f), beachRoot, Random.Range(1f, 1.6f), Col.None);
                grass++;
            }
            BP("Driftwood", Polar(70f, 17.2f), Tangent(70f) + 10f, beachRoot, 1.1f, Col.Box, true, "Driftwood_Arena");
            BP("Driftwood_B", Polar(112f, 16.4f), Tangent(112f) - 25f, beachRoot, 1f, Col.None, true);
            BP("NetRack", new Vector3(-15.5f, 0, 24f), 160f, beachRoot, 1f, Col.None);
            BP("NetRack", new Vector3(13.5f, 0, 26.5f), 200f, beachRoot, 1f, Col.None);

            // ---------------- coconuts: fallen under the palms, a few on the sandbar, a few afloat
            int nuts = 0;
            foreach (var p in palms.OrderBy(p => p.magnitude).Take(26))
            {
                int k = Random.Range(1, 3);
                for (int i = 0; i < k; i++)
                {
                    var q = p + Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * Random.Range(0.8f, 2.6f);
                    if (q.magnitude < BeachWall + 0.4f && q.z < DeckNorth + 0.5f) continue;
                    BP("Coconut", q, Random.Range(0f, 360f), beachRoot, Random.Range(0.9f, 1.15f), Col.None);
                    nuts++;
                }
            }
            foreach (var (a, r) in new[] { (60f, 12.5f), (84f, 14.8f), (118f, 11.6f), (140f, 15.4f), (96f, 10.2f) })
            {
                BP(nuts % 3 == 0 ? "CoconutPile" : "Coconut", Polar(a, r), Random.Range(0f, 360f), beachRoot, 1f, Col.None);
                nuts++;
            }
            BP("CoconutPile", new Vector3(-13.6f, 0, 18.4f), 30f, beachRoot, 1f, Col.None);
            foreach (var p in new[] { new Vector3(-21f, 0, 2f), new Vector3(24f, 0, 7f), new Vector3(-6f, 0, -26f), new Vector3(15f, 0, -24f) })
            {
                var c = BP("Coconut", new Vector3(p.x, SeaLevel - 0.06f, p.z), Random.Range(0f, 360f), envRoot, 1.1f, Col.None, false, "FloatingCoconut", false);
                var bob = c.AddComponent<Bobber>(); bob.heave = 0.07f; bob.roll = 12f; bob.pitch = 10f; bob.period = Random.Range(3.4f, 4.6f);
            }

            // ---------------- wildlife: crabs on the sand and the planks, gulls over the bones and the wreck
            int crabs = 0;
            for (int i = 0; i < 1200 && crabs < 22; i++)
            {
                bool onDeck = crabs < 6;
                Vector3 p = onDeck ? Polar(Random.Range(190f, 350f), Random.Range(9f, 17f)) : new Vector3(Random.Range(-45f, 45f), 0, Random.Range(6f, 40f));
                float y = onDeck ? 0f : H(p.x, p.z);
                if (!onDeck && (y < SeaLevel + 0.12f || y > 1.4f || p.magnitude < 9f)) continue;
                if (onDeck && NearHole(p, 0.8f)) continue;
                var crab = BP("Crab", new Vector3(p.x, y, p.z), Random.Range(0f, 360f), lifeRoot, Random.Range(1.1f, 1.7f), Col.None, false, $"Crab{crabs}", false);
                var cc = crab.AddComponent<Crab>();
                cc.minGroundY = SeaLevel + 0.1f;
                cc.roam = onDeck ? 3.5f : 5.5f;
                crabs++;
            }
            int birds = 0;
            void Flock(Vector3 c, int count, float rMin, float rMax, float hMin, float hMax, float speed)
            {
                for (int i = 0; i < count; i++)
                {
                    var b = BP("SeaBird", c + Vector3.up * 15f, 0f, lifeRoot, Random.Range(0.9f, 1.2f), Col.None, false, $"Gull{birds++}", false);
                    foreach (var r in b.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
                    var sb = b.AddComponent<SeaBird>();
                    sb.center = c; sb.radius = Random.Range(rMin, rMax); sb.height = Random.Range(hMin, hMax);
                    sb.speed = speed * Random.Range(0.85f, 1.15f); sb.direction = Random.value < 0.75f ? 1f : -1f;
                }
            }
            Flock(whale.transform.position, 9, 7f, 16f, 11f, 22f, 6.5f);
            Flock(wreckPos, 5, 8f, 14f, 10f, 18f, 6f);
            Flock(Vector3.zero, 4, 24f, 36f, 24f, 32f, 7.5f);

            // ---------------- keep the fight in: an invisible ring just outside the rail and the sandbar
            var bounds = new GameObject("ArenaBounds").transform;
            bounds.SetParent(envRoot);
            for (int i = 0; i < 40; i++)
            {
                float a = i * 9f;
                var b = new GameObject($"Bound{i}");
                b.transform.SetParent(bounds);
                b.transform.SetPositionAndRotation(Polar(a, BeachWall + 0.3f) + Vector3.up * 3f, Quaternion.Euler(0, Tangent(a), 0));
                var bc = b.AddComponent<BoxCollider>();
                bc.size = new Vector3(3.4f, 10f, 0.6f);
            }

            var volGo = new GameObject("PostProcess");
            volGo.transform.SetParent(lightRoot);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = BuildBeachPostProfile();
            volGo.AddComponent<PSXGlobals>();

            // ---------------- player, camera, HUD, bosses: the Shark is this stage's boss
            var rig = BuildCommon(sysRoot, envRoot, actorRoot, new Vector3(0, 0, -9f), 0f, false, "Shark");
            rig.hud.titleKicker = "Lair of the Shark";
            rig.hud.titleMain = "BLEACHED";
            rig.hud.titleSub = "ATOLL";
            foreach (var b in Object.FindObjectsByType<Enemies.SharkBoss>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { b.arenaCenter = Vector3.zero; EditorUtility.SetDirty(b); }
            var am = Object.FindAnyObjectByType<Audio.AudioManager>(FindObjectsInactive.Include);
            if (am != null) { am.ambienceLoop = SoundPicks.BeachAmbience(); EditorUtility.SetDirty(am); }
            string laneLog = BuildSharkSeaLane();          // the Shark's run in for its first-meeting cinematic

            EditorSceneManager.SaveScene(scene, BeachScenePath);
            RegisterBuildScenes();
            return $"Beach arena built: {mods} deck modules ({holes.Count} holed), {posts} pilings, {segs} rail bays, {palmCount} palms, {rocks} rocks, {grass} grass tufts, {drift} driftwood, {nuts} coconuts, {crabs} crabs, {birds} gulls; {laneLog}";
        }

        static VolumeProfile BuildBeachPostProfile()
        {
            string path = Root + "/Settings/BeachPostProfile.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            T Add<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }
            // a cold, heavy day: blue-crushed shadows, desaturated sand, the sea the one deep colour
            var tone = Add<Tonemapping>(); tone.mode.Override(TonemappingMode.ACES);
            var bloom = Add<Bloom>(); bloom.threshold.Override(1.0f); bloom.intensity.Override(0.55f); bloom.scatter.Override(0.65f);
            bloom.tint.Override(new Color(0.9f, 0.95f, 1f));
            var ca = Add<ColorAdjustments>(); ca.postExposure.Override(0.45f); ca.contrast.Override(16f); ca.saturation.Override(-6f);
            ca.colorFilter.Override(new Color(0.93f, 0.97f, 1.04f));
            var smh = Add<ShadowsMidtonesHighlights>();
            smh.shadows.Override(new Vector4(0.88f, 0.96f, 1.14f, -0.04f));
            smh.midtones.Override(new Vector4(0.97f, 1.0f, 1.04f, 0f));
            smh.highlights.Override(new Vector4(1.03f, 1.0f, 0.97f, 0f));
            var wb = Add<WhiteBalance>(); wb.temperature.Override(-12f); wb.tint.Override(-3f);
            var vig = Add<Vignette>(); vig.intensity.Override(0.3f); vig.smoothness.Override(0.45f); vig.color.Override(new Color(0.01f, 0.025f, 0.06f));
            var grain = Add<FilmGrain>(); grain.type.Override(FilmGrainLookup.Medium2); grain.intensity.Override(0.26f); grain.response.Override(0.7f);
            // sea haze: thin, low over the water, lit by the sun (shafts through the palms)
            var vf = Add<VolumetricFog>();
            vf.enabled.Override(true);
            vf.density.Override(0.007f);
            vf.baseHeight.Override(-1f);
            vf.heightFalloff.Override(0.45f);
            vf.maxDistance.Override(110f);
            vf.steps.Override(20);
            vf.anisotropy.Override(0.55f);
            vf.noiseScale.Override(0.05f);
            vf.noiseStrength.Override(0.5f);
            vf.wind.Override(new Vector3(0.6f, 0.02f, 1.1f));
            vf.albedo.Override(new Color(0.78f, 0.86f, 0.95f));
            vf.ambient.Override(new Color(0.06f, 0.08f, 0.11f));
            vf.mainLightScatter.Override(0.35f);
            vf.localLightScatter.Override(0f);
            vf.groundClear.Override(0f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
