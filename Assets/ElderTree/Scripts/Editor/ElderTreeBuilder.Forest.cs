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
    /// The Druid's arena: "Verdant Cathedral". An Amazonian forest clearing in the early morning: a ring of mossy
    /// standing stones on a floor of leaf litter and snaking roots, walled in by dense understorey (broad-leaf
    /// thickets, ferns, tree ferns, acai palms), under giant kapoks on plank buttresses whose umbrella crowns close
    /// the sky. The great kapok to the north reaches its low limbs out over the clearing. The low sun comes in from
    /// the north-north-east through the canopy, so the haze is cut into god rays; yellow fireflies drift in the
    /// shade, frogs hop through the litter, toucans sit in the branches and black vultures wheel above the gap.
    /// Models: Tools/Blender/build_forest.py -> Art/Models/Forest. Menu: Elder Tree/9g-9h.
    /// </summary>
    public static partial class ElderTreeBuilder
    {
        public const string ForestScenePath = Root + "/Scenes/Forest_Druid.unity";
        public const string ForestDir = Root + "/Art/Models/Forest";
        const float ForestWall = 20.2f;       // invisible ring that keeps the fight in
        static readonly Vector3 MotherKapokPos = new(0f, 0f, 33f);

        static readonly string[] ForestSlotMats =
        {
            "M_KapokBark", "M_JungleBark", "M_JungleLeaves", "M_JungleFronds", "M_ForestMoss", "M_ForestStone", "M_Fungus",
            "M_FrogSkin", "M_FrogDart", "M_FrogBelly", "M_ToucanBeak", "M_ToucanChest", "M_BirdDark", "M_BirdPale", "M_Iron"
        };

        static GameObject ForestProp(string n) => AssetDatabase.LoadAssetAtPath<GameObject>($"{ForestDir}/{n}.fbx");

        // ================================================================== art
        [MenuItem("Elder Tree/9g. Build Forest Art (textures, materials, imports)")]
        public static string BuildForestArt()
        {
            var kapok = SavePsx("T_KapokBark", 128, (u, v, x, y) =>
            {
                // pale grey kapok: fine vertical streaks, faint growth bands, lichen blotches, moss running down
                var c = Mx(Hex("#5E5A50"), Hex("#A29C8A"), 0.3f + 0.4f * Fbm(u, v, 16, 2, 3, 601) + 0.18f * Gr(x, y, 61));
                c *= 1f - 0.12f * Sst(0.55f, 0.8f, Fbm(u, v, 32, 3, 2, 602));
                float band = Mathf.Abs(Mathf.Sin((v * 3f + Fbm(u, v, 4, 4, 2, 603) * 0.6f) * Mathf.PI));
                c *= 1f - 0.12f * Sst(0.08f, 0f, band);
                Worley(u, v, 6, 604, out float f1, out _, out _);
                c = Mx(c, Mx(Hex("#8E9A82"), Hex("#C4C8B4"), Hash(x, y, 62)), Sst(0.16f, 0.08f, f1) * Sst(0.4f, 0.6f, Fbm(u, v, 5, 5, 2, 605)) * 0.75f);
                c = Mx(c, Hex("#2E3C1E"), Sst(0.6f, 0.78f, Fbm(u, v, 6, 2, 3, 606)) * 0.75f);       // moss streaks down the trunk
                if (Hash(x, y, 63) > 0.985f) c = Hex("#D8D6C6");                                     // lichen specks
                return c;
            });
            var bark = SavePsx("T_JungleBark", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#21190F"), Hex("#56432E"), 0.3f + 0.45f * Fbm(u, v, 10, 3, 3, 611) + 0.2f * Gr(x, y, 64));
                float fis = Mathf.Abs(Mathf.Sin((u * 9f + Fbm(u, v, 3, 3, 2, 612) * 2f) * Mathf.PI));
                c *= 1f - 0.45f * Sst(0.18f, 0f, fis);                                               // deep vertical fissures
                c = Mx(c, Hex("#2C3E1A"), Sst(0.55f, 0.72f, Fbm(u, v, 5, 5, 3, 613)) * 0.7f);       // moss
                return Mx(c, Hex("#6A6A58"), Sst(0.74f, 0.86f, Fbm(u, v, 7, 7, 2, 614)) * 0.4f);    // grey lichen
            });
            var leaves = SavePsx("T_JungleLeaves", 256, JungleLeaves, mips: true, alpha: true);
            var fronds = SavePsx("T_JungleFronds", 256, JungleFronds, mips: true, alpha: true);
            foreach (var t in new[] { "T_JungleLeaves", "T_JungleFronds" })
            {
                // nearest mips that keep the cutout's coverage: distant leaves stay solid shapes instead of dots
                var imp = (TextureImporter)AssetImporter.GetAtPath(TexDir + $"/{t}.png");
                imp.mipMapsPreserveCoverage = true; imp.alphaTestReferenceValue = 0.5f; imp.SaveAndReimport();
            }
            var moss = SavePsx("T_ForestMoss", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#1C2A10"), Hex("#4E6A26"), 0.3f + 0.45f * Fbm(u, v, 12, 12, 4, 621) + 0.3f * Gr(x, y, 65));
                if (Hash(x, y, 66) > 0.97f) c = Hex("#7A8E3A");                                       // sporophytes catching light
                return Mx(c, Hex("#3A2E1C"), Sst(0.66f, 0.8f, Fbm(u, v, 5, 5, 3, 622)) * 0.5f);    // litter caught in it
            });
            var stone = SavePsx("T_ForestStone", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#33352F"), Hex("#6C6E64"), 0.3f + 0.45f * Fbm(u, v, 8, 8, 4, 631) + 0.2f * Gr(x, y, 67));
                Worley(u, v, 5, 632, out float f1, out float f2, out _);
                c *= 1f - 0.4f * Sst(0.035f, 0f, f2 - f1);                                            // cracks
                // druid spirals carved into some tiles of the stone
                int cx = Mathf.FloorToInt(u * 2f), cy = Mathf.FloorToInt(v * 2f);
                if (Hash(cx, cy, 633) > 0.35f)
                {
                    float px = u * 2f - cx - 0.5f, py = v * 2f - cy - 0.5f;
                    float r = Mathf.Sqrt(px * px + py * py), th = Mathf.Atan2(py, px) / (Mathf.PI * 2f) + 0.5f;
                    float s = r * 11f - th;
                    float groove = Mathf.Abs(s - Mathf.Round(s));
                    if (r < 0.4f && r > 0.03f && groove < 0.16f) c *= 0.45f;
                }
                c = Mx(c, Mx(Hex("#8C9278"), Hex("#B4B49A"), Hash(x, y, 68)), Sst(0.68f, 0.78f, Fbm(u, v, 6, 6, 3, 634)) * 0.6f);   // lichen
                return Mx(c, Hex("#26361A"), Sst(0.62f, 0.76f, Fbm(u, v, 3, 3, 3, 635)) * 0.55f);   // green damp
            });
            var litter = SavePsx("T_LeafLitter", 128, LeafLitter);
            var floorMoss = SavePsx("T_ForestFloorMoss", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#1E2C12"), Hex("#4C6A28"), 0.35f + 0.4f * Fbm(u, v, 10, 10, 4, 651) + 0.25f * Gr(x, y, 72));
                Worley(u, v, 9, 652, out float f1, out float f2, out int id);
                if (Hash(id, 2, 653) > 0.72f) c = Mx(c, Mx(Hex("#3A2814"), Hex("#5E4422"), Hash(id, 4, 654)), Sst(0.02f, 0.07f, f2 - f1) * 0.9f);   // a few leaves on it
                return c;
            });
            var mud = SavePsx("T_ForestMud", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#0E0B08"), Hex("#2E2418"), 0.35f + 0.45f * Fbm(u, v, 8, 8, 4, 661) + 0.2f * Gr(x, y, 73));
                float root = Mathf.Abs(Mathf.Sin((u * 5f + Fbm(u, v, 4, 4, 3, 662) * 3f) * Mathf.PI));
                c = Mx(c, Hex("#4A3C2A"), Sst(0.06f, 0f, root) * 0.6f);                              // root threads
                return Mx(c, Hex("#1E2A14"), Sst(0.6f, 0.75f, Fbm(u, v, 5, 5, 3, 663)) * 0.5f);
            });
            var frog = SavePsx("T_FrogSkin", 64, (u, v, x, y) =>
            {
                var c = Mx(Hex("#1E3410"), Hex("#4E7024"), 0.35f + 0.4f * Fbm(u, v, 6, 6, 3, 671) + 0.2f * Gr(x, y, 74));
                Worley(u, v, 5, 672, out float f1, out _, out _);
                c = Mx(c, Hex("#141A0A"), Sst(0.18f, 0.1f, f1) * 0.85f);                              // blotches
                if (Hash(x, y, 75) > 0.95f) c *= 1.35f;                                              // warts
                return c;
            });
            var dart = SavePsx("T_FrogDart", 64, (u, v, x, y) =>
            {
                var c = Mx(Hex("#10264A"), Hex("#2A5A86"), 0.35f + 0.4f * Fbm(u, v, 6, 6, 3, 681) + 0.2f * Gr(x, y, 76));
                Worley(u, v, 6, 682, out float f1, out _, out _);
                return Mx(c, Hex("#06080C"), Sst(0.2f, 0.12f, f1));                                 // black spots
            });

            Lit("M_KapokBark", Color.white, kapok);
            Lit("M_JungleBark", Color.white, bark);
            Cutout(Lit("M_JungleLeaves", Color.white, leaves, null, 0f, 1f, null, false));
            Cutout(Lit("M_JungleFronds", Color.white, fronds, null, 0f, 1f, null, false));
            Lit("M_ForestMoss", Color.white, moss);
            Lit("M_ForestStone", Color.white, stone);
            Lit("M_Fungus", Hex("#9A8A68"));
            Lit("M_FrogSkin", Color.white, frog);
            Lit("M_FrogDart", Color.white, dart);
            Lit("M_FrogBelly", Hex("#A89A6A"));
            Lit("M_ToucanBeak", Hex("#B8661A"));
            Lit("M_ToucanChest", Hex("#C8B470"));
            Lit("M_BirdDark", Hex("#25282D"));
            Lit("M_BirdPale", Hex("#8E949A"));
            Lit("M_LeafLitter", Color.white, litter);
            Lit("M_ForestFloorMoss", Color.white, floorMoss);
            Lit("M_ForestMud", Color.white, mud);

            var sky = AssetOf("M_ForestSky", "ElderTree/NightSky");
            sky.SetColor("_ZenithColor", new Color(0.36f, 0.5f, 0.62f));
            sky.SetColor("_HorizonColor", new Color(0.72f, 0.7f, 0.56f));
            sky.SetColor("_GroundColor", new Color(0.32f, 0.36f, 0.26f));
            sky.SetFloat("_HorizonSharpness", 1.6f);
            sky.SetFloat("_StarBrightness", 0f);
            sky.SetColor("_MoonColor", new Color(1f, 0.92f, 0.72f));
            sky.SetFloat("_MoonSize", 0.03f);
            sky.SetFloat("_HaloStrength", 1.3f);
            sky.SetColor("_CloudColor", new Color(0.78f, 0.76f, 0.66f));
            sky.SetFloat("_CloudStrength", 0.35f);
            sky.SetFloat("_CloudLayer", 1f);
            sky.SetFloat("_CloudCover", 0.38f);
            sky.SetFloat("_CloudScale", 1.3f);
            sky.SetFloat("_CloudSpeed", 0.005f);
            sky.SetColor("_CloudLitColor", new Color(1f, 0.93f, 0.78f));

            // morning mist lying in the clearing, gold where the sun reaches it
            var gfog = AssetOf("M_ForestGroundFog", "ElderTree/GroundFog");
            gfog.SetColor("_Color", new Color(0.56f, 0.62f, 0.5f));
            gfog.SetColor("_Ambient", new Color(0.05f, 0.065f, 0.045f));
            gfog.SetFloat("_Density", 0.03f);
            gfog.SetFloat("_MoonScatter", 0.3f);
            gfog.SetFloat("_Height", 1.0f);
            gfog.SetFloat("_SoftDistance", 0.9f);

            // fireflies: an HDR yellow so the bloom picks them up
            var soft = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/T_FX_SoftDot.png");
            Unlit("M_FX_ForestFirefly", new Color(3.2f, 2.5f, 0.7f, 1f), soft, additive: true);
            AssetDatabase.SaveAssets();

            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ForestDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                imp.animationType = ModelImporterAnimationType.None;
                imp.importAnimation = false;
                imp.importCameras = false; imp.importLights = false;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                imp.addCollider = false;
                imp.isReadable = true;
                foreach (var m in ForestSlotMats)
                {
                    var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{m}.mat");
                    if (mat) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m), mat);
                }
                imp.SaveAndReimport();
                n++;
            }
            EnsureFogFeature();
            AssetDatabase.SaveAssets();
            return $"Forest art built ({n} models)";
        }

        /// <summary>Forest floor: fallen leaves (pointed ovals at every angle, piled over each other, each with its own
        /// tone and a midrib) on dark soil. Tileable: the leaves sit on a wrapped jittered grid.</summary>
        static Color LeafLitter(float u, float v, int x, int y)
        {
            const int G = 14;
            float gx = u * G, gy = v * G;
            int cx = Mathf.FloorToInt(gx), cy = Mathf.FloorToInt(gy);
            int best = -1; float bestOrder = -1f, bt = 0f, bs = 0f;
            for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int wx = ((cx + i) % G + G) % G, wy = ((cy + j) % G + G) % G;
                for (int k = 0; k < 3; k++)
                {
                    int id = (wx * 97 + wy) * 3 + k;
                    var c = new Vector2(cx + i + Hash(wx, wy, 800 + k), cy + j + Hash(wx, wy, 810 + k));
                    float ang = Hash(id, 1, 820) * Mathf.PI * 2f;
                    var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    float len = 0.55f + 0.35f * Hash(id, 2, 821), wid = 0.2f + 0.12f * Hash(id, 3, 822);
                    var q = new Vector2(gx, gy) - c;
                    float along = Vector2.Dot(q, d), across = q.x * -d.y + q.y * d.x;
                    if (Mathf.Abs(along) > len) continue;
                    float t = (along / len + 1f) * 0.5f;
                    float w = wid * Mathf.Pow(Mathf.Sin(Mathf.PI * t), 0.8f);
                    if (Mathf.Abs(across) > w) continue;
                    float order = Hash(id, 4, 823);
                    if (order > bestOrder) { bestOrder = order; best = id; bt = t; bs = across / Mathf.Max(w, 1e-4f); }
                }
            }
            if (best < 0)
            {
                var soil = Mx(Hex("#140F0A"), Hex("#2E2418"), 0.4f + 0.4f * Fbm(u, v, 8, 8, 3, 641) + 0.2f * Gr(x, y, 69));
                return soil;
            }
            float kind = Hash(best, 5, 824);
            var leaf = kind < 0.4f ? Mx(Hex("#3E2A18"), Hex("#6E4E2C"), Hash(best, 6, 825))
                     : kind < 0.75f ? Mx(Hex("#2E2418"), Hex("#4E3E28"), Hash(best, 7, 826))
                     : kind < 0.9f ? Mx(Hex("#4E4226"), Hex("#6E6036"), Hash(best, 8, 827))
                     : Mx(Hex("#263420"), Hex("#425430"), Hash(best, 9, 828));
            leaf *= 0.82f + 0.25f * bestOrder + 0.1f * Gr(x, y, 70);                                   // the top of the pile catches more light
            if (Mathf.Abs(bs) < 0.14f) leaf *= 0.72f;                                                 // midrib
            else if (Mathf.Abs(Mathf.Sin((bt * 6f + Mathf.Abs(bs) * 1.5f) * Mathf.PI)) < 0.12f) leaf *= 0.85f;   // veins
            if (Mathf.Abs(bs) > 0.82f) leaf *= 0.75f;                                                 // curled, shadowed edge
            return leaf;
        }

        // ------------------------------------------------------------------ leaf atlases
        struct Leaf { public Vector2 b, d; public float len, wid, tone; }

        static readonly Dictionary<int, Leaf[]> leafSets = new();

        /// <summary>A spray of leaves (deterministic): bases near the cell's centre, pointing outward.</summary>
        static Leaf[] LeafSpray(int seed, int count, float spread, Vector2 lenR, Vector2 widR)
        {
            if (leafSets.TryGetValue(seed, out var s)) return s;
            var rnd = new System.Random(seed);
            float R() => (float)rnd.NextDouble();
            var list = new List<Leaf>();
            for (int i = 0; i < count; i++)
            {
                float a = R() * Mathf.PI * 2f, r = Mathf.Sqrt(R()) * spread;
                var b = new Vector2(0.5f, 0.5f) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                float ang = a + (R() - 0.5f) * 1.4f;
                list.Add(new Leaf { b = b, d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)), len = Mathf.Lerp(lenR.x, lenR.y, R()), wid = Mathf.Lerp(widR.x, widR.y, R()), tone = R() });
            }
            // the inner leaves first, so the outer ones lie over them
            s = list.OrderBy(l => (l.b - new Vector2(0.5f, 0.5f)).sqrMagnitude).ToArray();
            leafSets[seed] = s;
            return s;
        }

        /// <summary>Topmost leaf under p: returns its index or -1, with t (0 base .. 1 tip) and the side offset.</summary>
        static int HitLeaf(Leaf[] set, Vector2 p, out float t, out float side)
        {
            t = 0; side = 0;
            for (int i = set.Length - 1; i >= 0; i--)
            {
                var l = set[i];
                var q = p - l.b;
                float along = Vector2.Dot(q, l.d), across = q.x * -l.d.y + q.y * l.d.x;
                if (along < 0f || along > l.len) continue;
                float tt = along / l.len;
                float w = l.wid * Mathf.Sin(Mathf.PI * Mathf.Pow(tt, 0.7f));
                if (Mathf.Abs(across) > w) continue;
                t = tt; side = across / Mathf.Max(w, 1e-4f);
                return i;
            }
            return -1;
        }

        /// <summary>2x2 cutout atlas: canopy leaves lit (TL) and in shadow (TR), broad understorey leaves (BL), a liana (BR).</summary>
        static Color JungleLeaves(float u, float v, int x, int y)
        {
            float lu = (u % 0.5f) * 2f, lv = (v % 0.5f) * 2f;
            bool top = v >= 0.5f, right = u >= 0.5f;
            var clear = new Color(0, 0, 0, 0);
            var p = new Vector2(lu, lv);
            if (top)
            {
                var set = LeafSpray(right ? 702 : 701, 46, 0.3f, new Vector2(0.14f, 0.24f), new Vector2(0.045f, 0.075f));
                int i = HitLeaf(set, p, out float t, out float side);
                if (i < 0) return clear;
                var l = set[i];
                Color c = right
                    ? Mx(Hex("#0E1E0C"), Hex("#2E4A22"), 0.25f + 0.5f * l.tone + 0.15f * t + 0.15f * Gr(x, y, 81))
                    : Mx(Hex("#22441A"), Hex("#7AA444"), 0.2f + 0.45f * l.tone + 0.25f * t + 0.12f * lv + 0.12f * Gr(x, y, 82));
                c *= 1f - 0.25f * Sst(0.3f, 0f, (p - new Vector2(0.5f, 0.5f)).magnitude) * (1f - t);     // shade in the heart of the spray
                if (Mathf.Abs(side) < 0.14f) c *= 0.75f;                                                // midrib
                if (!right && Hash(i, 3, 703) > 0.86f) c = Mx(c, Hex("#8A8A3A"), 0.45f);                 // a few yellowing leaves
                c.a = 1; return c;
            }
            if (!right)
            {
                // broad understorey leaves fanning up from the bottom centre, torn along the veins, tips browning
                var set = BroadFan();
                int i = HitLeaf(set, p, out float t, out float side);
                if (i < 0) return clear;
                var l = set[i];
                float tear = Hash(i, Mathf.FloorToInt(t * 9f + (side > 0 ? 0 : 50)), 711);
                if (t > 0.25f && tear > 0.8f && Mathf.Abs(side) > 0.25f && Mathf.Abs(Mathf.Sin((t * 9f) * Mathf.PI)) < 0.25f) return clear;
                var c = Mx(Hex("#183214"), Hex("#5C8A36"), 0.25f + 0.4f * l.tone + 0.2f * (1f - Mathf.Abs(side)) + 0.1f * Gr(x, y, 83));
                c *= 0.9f + 0.1f * Mathf.Sin((t * 22f + Mathf.Abs(side) * 4f) * Mathf.PI);                 // side veins
                c = Mx(c, Hex("#6A5A28"), Sst(0.82f, 1f, t) * Sst(0.4f, 0.6f, Fbm(lu, lv, 4, 4, 2, 712)) * 0.8f);
                if (Mathf.Abs(side) < 0.08f) c = Mx(c, Hex("#8AA060"), 0.5f);                            // pale midrib
                c.a = 1; return c;
            }
            {
                // a liana: a wavy woody stem down the cell with heart leaves off alternate sides, a thin aerial root beside it
                float sx = 0.5f + 0.1f * Mathf.Sin(lv * Mathf.PI * 4f + 1f);
                float rx = 0.28f + 0.04f * Mathf.Sin(lv * Mathf.PI * 7f);
                if (Mathf.Abs(lu - sx) < 0.03f) { var s = Mx(Hex("#2A2414"), Hex("#4E4428"), Hash(x, y, 84)); s.a = 1; return s; }
                if (Mathf.Abs(lu - rx) < 0.012f && lv > 0.25f) { var s = Hex("#3A2C1C"); s.a = 1; return s; }
                var set = LianaLeaves();
                int i = HitLeaf(set, p, out float t, out float side);
                if (i < 0) return clear;
                var c = Mx(Hex("#1E3816"), Hex("#5E8A38"), 0.3f + 0.4f * set[i].tone + 0.2f * t + 0.1f * Gr(x, y, 85));
                if (Mathf.Abs(side) < 0.12f) c *= 0.8f;
                c.a = 1; return c;
            }
        }

        static Leaf[] BroadFan()
        {
            if (leafSets.TryGetValue(720, out var s)) return s;
            var rnd = new System.Random(720);
            var list = new List<Leaf>();
            for (int i = 0; i < 7; i++)
            {
                float ang = Mathf.Lerp(-58f, 58f, i / 6f) + ((float)rnd.NextDouble() - 0.5f) * 14f;
                float a = (90f - ang) * Mathf.Deg2Rad;
                list.Add(new Leaf { b = new Vector2(0.5f + ((float)rnd.NextDouble() - 0.5f) * 0.1f, 0.02f), d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)),
                                    len = 0.95f - Mathf.Abs(ang) / 58f * 0.25f, wid = 0.1f + (float)rnd.NextDouble() * 0.05f, tone = (float)rnd.NextDouble() });
            }
            s = list.OrderBy(l => -Mathf.Abs(l.d.x)).ToArray();    // the upright leaves in front
            leafSets[720] = s;
            return s;
        }

        static Leaf[] LianaLeaves()
        {
            if (leafSets.TryGetValue(730, out var s)) return s;
            var rnd = new System.Random(730);
            var list = new List<Leaf>();
            for (int k = 0; k < 9; k++)
            {
                float lv = (k + 0.5f) / 9f;
                float side = k % 2 == 0 ? 1f : -1f;
                float sx = 0.5f + 0.1f * Mathf.Sin(lv * Mathf.PI * 4f + 1f);
                var d = new Vector2(side * 0.75f, -0.66f).normalized;
                list.Add(new Leaf { b = new Vector2(sx, lv), d = d, len = 0.2f + (float)rnd.NextDouble() * 0.06f, wid = 0.075f, tone = (float)rnd.NextDouble() });
            }
            s = list.ToArray();
            leafSets[730] = s;
            return s;
        }

        /// <summary>2x2 cutout atlas: fern frond (TL), acai palm frond (TR), floor tuft (BL), hanging aerial roots (BR).
        /// Fronds run base (v = 0) to tip (v = 1).</summary>
        static Color JungleFronds(float u, float v, int x, int y)
        {
            float lu = (u % 0.5f) * 2f, lv = (v % 0.5f) * 2f;
            bool top = v >= 0.5f, right = u >= 0.5f;
            var clear = new Color(0, 0, 0, 0);
            if (top)
            {
                bool palm = right;
                float dx = Mathf.Abs(lu - 0.5f) * 2f;
                float env = palm ? Mathf.Sin(Mathf.Clamp01(lv) * Mathf.PI * 0.85f + 0.15f) : Mathf.Sin(Mathf.Clamp01(lv) * Mathf.PI * 0.92f + 0.08f) * 0.95f;
                if (dx > env * 0.97f) return clear;
                bool rachis = dx < (palm ? 0.05f : 0.04f);
                Color c;
                if (palm)
                {
                    // narrow leaflets angled to the tip, hanging loose: wide dark gaps between them
                    float ph = lv * 17f - dx * 2.2f + (Fbm(lu, lv, 3, 3, 2, 741) - 0.5f) * 0.6f;
                    bool leaf = ph - Mathf.Floor(ph) < 0.48f;
                    if (!leaf && !rachis) return clear;
                    if (!rachis && Hash(x / 2, y / 2, 742) < 0.04f + 0.35f * Sst(0.75f, 1f, dx / Mathf.Max(env, 0.01f))) return clear;
                    c = rachis ? Hex("#4A5430") : Mx(Hex("#1C3618"), Hex("#5C8238"), 0.25f + 0.45f * (1f - dx) + 0.15f * lv + 0.2f * Gr(x, y, 86));
                }
                else
                {
                    // fern: pinnae with serrated pinnules, the tips curling, a few brown spore spots
                    float ph = lv * 14f - dx * 3f + (Fbm(lu, lv, 3, 3, 2, 751) - 0.5f) * 0.4f;
                    float f = ph - Mathf.Floor(ph);
                    bool leaf = f < 0.78f && (Mathf.Abs(Mathf.Sin((dx * 16f + f * 2f) * Mathf.PI)) > 0.22f || f < 0.3f);
                    if (!leaf && !rachis) return clear;
                    if (!rachis && Hash(x / 2, y / 2, 752) < 0.02f + 0.25f * Sst(0.82f, 1f, dx / Mathf.Max(env, 0.01f))) return clear;
                    c = rachis ? Hex("#3E4A26") : Mx(Hex("#22401A"), Hex("#6E9C44"), 0.25f + 0.4f * (1f - dx) + 0.2f * lv + 0.2f * Gr(x, y, 87));
                    if (!rachis && Hash(x, y, 753) > 0.97f && lv < 0.7f) c = Hex("#5A3A1C");
                }
                c.a = 1; return c;
            }
            if (!right)
            {
                // floor tuft: thin sedge blades and two seedlings with paired leaves
                float col = lu * 12f;
                int ci = Mathf.FloorToInt(col);
                float h = 0.45f + 0.55f * Hash(ci, 7, 761);
                float lean = (Hash(ci, 9, 762) - 0.5f) * 0.6f * lv;
                float fx = col - ci - lean * 3f;
                float w = 0.28f * (1f - lv / h);
                Color c;
                if (lv <= h && Mathf.Abs(fx - 0.5f) < w)
                {
                    c = Mx(Hex("#1A2A12"), Hex("#5A7A34"), 0.3f + 0.6f * lv / h + 0.2f * Gr(x, y, 88));
                    c.a = 1; return c;
                }
                foreach (var (sx, sh) in new[] { (0.3f, 0.55f), (0.72f, 0.4f) })
                {
                    if (Mathf.Abs(lu - sx) < 0.015f && lv < sh) { c = Hex("#3A4A22"); c.a = 1; return c; }
                    for (int k = -1; k <= 1; k += 2)
                    {
                        var q = new Vector2(lu - (sx + k * 0.09f), (lv - sh) * 1.6f);
                        if (q.sqrMagnitude < 0.09f * 0.09f) { c = Mx(Hex("#2E5420"), Hex("#6A9A40"), 0.5f + q.y * 3f); c.a = 1; return c; }
                    }
                }
                return clear;
            }
            {
                // hanging aerial roots and moss strands (top of the cell = where they hang from)
                for (int k = 0; k < 6; k++)
                {
                    float sx = 0.1f + 0.16f * k + 0.035f * Mathf.Sin(lv * 7f + k * 1.7f);
                    float end = 0.05f + 0.45f * Hash(k, 3, 771);
                    float w = 0.012f + 0.012f * lv + (k % 3 == 0 ? 0.008f : 0f);
                    if (lv < end || Mathf.Abs(lu - sx) > w) continue;
                    var c = Mx(Hex("#22180E"), Hex("#5A4630"), 0.3f + 0.4f * Hash(x, y, 89) + 0.2f * lv);
                    if (Fbm(lu, lv, 4, 8, 2, 772 + k) > 0.6f) c = Mx(Hex("#2A3A18"), Hex("#4A5E28"), Hash(x, y, 90));
                    c.a = 1; return c;
                }
                return clear;
            }
        }

        // ================================================================== ground
        static float ForestHeight(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            float n1 = Mathf.PerlinNoise(x * 0.08f + 3.3f, z * 0.08f + 7.1f);
            float n2 = Mathf.PerlinNoise(x * 0.27f + 1.2f, z * 0.27f + 5.9f);
            float flat = (n2 - 0.5f) * 0.12f - 0.05f * Sst(14f, 0f, r);                       // the clearing: almost level, a faint dish
            float wild = (n1 - 0.5f) * 1.8f + (n2 - 0.5f) * 0.5f + Sst(30f, 110f, r) * 5f;     // hummocks, rising away so the forest closes in
            return Mathf.Lerp(flat, wild, Sst(18f, 26f, r));
        }

        static GameObject BuildForestGround(Transform parent, out MeshCollider collider)
        {
            const float Ext = 130f, Cell = 1.5f;
            int n = Mathf.CeilToInt(Ext * 2 / Cell) + 1;
            var verts = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = -Ext + i * Cell, z = -Ext + j * Cell;
                verts[j * n + i] = new Vector3(x, ForestHeight(x, z), z);
                uvs[j * n + i] = new Vector2(x, z) / 4f;
            }
            var subs = new[] { new List<int>(), new List<int>(), new List<int>() };   // litter, moss, mud
            void Tri(int a, int b, int c)
            {
                var m = (verts[a] + verts[b] + verts[c]) / 3f;
                float moss = Mathf.PerlinNoise(m.x * 0.09f + 11.3f, m.z * 0.09f + 2.7f);
                float wet = Mathf.PerlinNoise(m.x * 0.13f + 4.4f, m.z * 0.13f + 19.2f);
                float r = new Vector2(m.x, m.z).magnitude;
                var l = wet > 0.68f && r > 8f ? subs[2] : moss > (r < 18f ? 0.62f : 0.5f) ? subs[1] : subs[0];
                l.Add(a); l.Add(b); l.Add(c);
            }
            for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                if (((i + j) & 1) == 0) { Tri(a, c, d); Tri(a, d, b); }
                else { Tri(a, c, b); Tri(b, c, d); }
            }
            var mesh = new Mesh { name = "ForestGround", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts; mesh.uv = uvs;
            mesh.subMeshCount = 3;
            for (int s = 0; s < 3; s++) mesh.SetTriangles(subs[s], s);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            string path = ForestDir + "/ForestGround.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) { old.Clear(); EditorUtility.CopySerialized(mesh, old); mesh = old; }
            else AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject("ForestGround");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { M("M_LeafLitter"), M("M_ForestFloorMoss"), M("M_ForestMud") };
            collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            go.AddComponent<GroundSurface>().kind = SurfaceKind.Earth;
            go.isStatic = true;
            return go;
        }

        // ================================================================== placement
        static GameObject FP(string prop, Vector3 pos, float yaw, Transform parent, float scale = 1f, Col col = Col.None, bool ground = true,
                             string name = null, bool isStatic = true, float sink = 0f)
        {
            var prefab = ForestProp(prop);
            if (prefab == null) { Debug.LogWarning("[Forest] missing prop " + prop); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            if (ground) pos.y = ForestHeight(pos.x, pos.z) - sink;
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
            }
            if (isStatic) foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            return go;
        }

        static void TrunkCollider(GameObject go, float radius, float height)
        {
            var cap = go.AddComponent<CapsuleCollider>();
            cap.radius = radius; cap.height = height; cap.center = new Vector3(0, height * 0.5f, 0);
        }

        /// <summary>A crown that moves with the wind, high up and slow (shadows and light shafts shift with it).</summary>
        static void SwayCrown(GameObject tree, float amplitude, float flutter, float gust)
        {
            var crown = Find(tree, "Crown");
            if (crown == null) return;
            foreach (var t in crown.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = false;
            var sw = crown.gameObject.AddComponent<PalmSway>();
            sw.amplitude = amplitude; sw.flutter = flutter; sw.gustSpeed = gust; sw.flutterSpeed = 1.3f;
        }

        // ================================================================== scene
        [MenuItem("Elder Tree/9h. Build Forest Arena (Druid)")]
        public static string BuildForestScene()
        {
            if (cues == null) BuildAudio();
            if (ForestProp("Kapok_Mother") == null) return "Forest models missing: run Tools/Blender/build_forest.py first";
            Random.InitState(3111);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupPsxPipeline();

            // ---------------- sky, ambient, Unity fog: a hazy early morning under the canopy
            var sky = M("M_ForestSky");
            var sunRot = Quaternion.Euler(41f, 214f, 0);               // the low sun in the north-east, half behind the great kapok
            Vector3 toSun = -(sunRot * Vector3.forward);
            sky.SetVector("_MoonDir", new Vector4(toSun.x, toSun.y, toSun.z, 0));
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.5f, 0.42f);
            RenderSettings.ambientEquatorColor = new Color(0.29f, 0.34f, 0.24f);
            RenderSettings.ambientGroundColor = new Color(0.14f, 0.13f, 0.09f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.38f, 0.46f, 0.36f);
            RenderSettings.fogDensity = 0.0125f;
            RenderSettings.reflectionIntensity = 0.15f;

            var sysRoot = new GameObject("--- Systems ---").transform;
            var envRoot = new GameObject("--- Environment ---").transform;
            var treeRoot = new GameObject("--- Trees ---").transform;
            var underRoot = new GameObject("--- Understorey ---").transform;
            var stoneRoot = new GameObject("--- Stones ---").transform;
            var lifeRoot = new GameObject("--- Wildlife ---").transform;
            var lightRoot = new GameObject("--- Lighting ---").transform;
            var actorRoot = new GameObject("--- Actors ---").transform;

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(lightRoot);
            sunGo.transform.rotation = sunRot;
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.9f, 0.72f);
            sun.intensity = 2.3f;
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 0.92f;
            sunGo.AddComponent<UniversalAdditionalLightData>();
            RenderSettings.sun = sun;

            BuildForestGround(envRoot, out var groundCol);
            float H(float x, float z) => ForestHeight(x, z);
            var taken = new List<(Vector3 p, float r)>();      // trunks, stones: keep things from growing into each other
            bool Free(Vector3 p, float r) => taken.All(t => (new Vector2(t.p.x - p.x, t.p.z - p.z)).magnitude > t.r + r);
            void Take(Vector3 p, float r) => taken.Add((p, r));

            // ---------------- the great kapok in the north: its low limbs reach out over the clearing (Blender +Y = Unity -Z)
            var mother = FP("Kapok_Mother", MotherKapokPos, 0f, treeRoot, 1f, Col.None, true, "Kapok_Mother", sink: 0.3f);
            TrunkCollider(mother, 3.2f, 40f);
            SwayCrown(mother, 0.35f, 0.12f, 0.07f);
            Take(MotherKapokPos, 9f);
            var altar = FP("Altar", new Vector3(0.4f, 0, 23.6f), 4f, stoneRoot, 1f, Col.Box, true, "Altar", sink: 0.1f);
            Take(altar.transform.position, 3f);

            // ---------------- the druid circle: standing stones just behind the wall
            int stones = 0;
            for (int i = 0; i < 13; i++)
            {
                float a = i * (360f / 13f) + Random.Range(-5f, 5f);
                if (Mathf.Abs(Mathf.DeltaAngle(a, 90f)) < 10f) continue;                 // the gap toward the great kapok and its altar
                var p = Polar(a, ForestWall + 1.1f + Random.Range(-0.3f, 0.4f));
                bool fallen = i == 4 || i == 9;
                string kind = fallen ? "MossRock_B" : new[] { "Menhir_A", "Menhir_B", "Menhir_A", "Menhir_C" }[Random.Range(0, 4)];
                var s = FP(kind, p, Tangent(a) + Random.Range(-15f, 15f), stoneRoot, Random.Range(0.9f, 1.15f), Col.Box, true, $"Stone{stones++}", sink: 0.1f);
                if (!fallen) s.transform.rotation *= Quaternion.Euler(Random.Range(-5f, 5f), 0, Random.Range(-5f, 5f));   // centuries of lean
                Take(p, 1.4f);
            }

            // ---------------- giant kapoks and emergents ringing the clearing and beyond
            int giants = 0, emergents = 0, mids = 0, palms = 0, tfern = 0;
            foreach (var (a, r, kind, s) in new[] { (22f, 38f, "Kapok_A", 1.0f), (148f, 36f, "Kapok_B", 1.05f), (203f, 43f, "Kapok_A", 0.95f), (252f, 35f, "Kapok_B", 1.0f),
                                                    (298f, 41f, "Kapok_A", 1.1f), (338f, 34f, "Kapok_B", 0.9f), (58f, 58f, "Kapok_A", 1.0f), (118f, 62f, "Kapok_B", 1.15f),
                                                    (178f, 66f, "Kapok_A", 1.0f), (228f, 70f, "Kapok_B", 1.1f), (275f, 62f, "Kapok_A", 0.9f), (320f, 70f, "Kapok_A", 1.05f),
                                                    (5f, 72f, "Kapok_B", 1.0f), (80f, 84f, "Kapok_A", 1.2f), (100f, 70f, "Kapok_B", 1.0f) })
            {
                var p = Polar(a + Random.Range(-4f, 4f), r + Random.Range(-2f, 2f));
                var g = FP(kind, p, Random.Range(0f, 360f), treeRoot, s, Col.None, true, $"{kind}_{giants++}", sink: 0.3f);
                TrunkCollider(g, (kind == "Kapok_A" ? 2.4f : 2.0f) * s, 30f);
                SwayCrown(g, 0.4f, 0.12f, 0.08f);
                Take(p, (kind == "Kapok_A" ? 7f : 6f) * s);
            }
            for (int i = 0; i < 4000 && emergents < 30; i++)
            {
                var p = Polar(Random.Range(0f, 360f), Random.Range(25f, 100f));
                if (!Free(p, 3f)) continue;
                string kind = new[] { "Emergent_A", "Emergent_B", "Emergent_C" }[Random.Range(0, 3)];
                float s = Random.Range(0.85f, 1.2f);
                var g = FP(kind, p, Random.Range(0f, 360f), treeRoot, s, Col.None, true, $"{kind}_{emergents++}", sink: 0.2f);
                TrunkCollider(g, 0.9f * s, 20f);
                SwayCrown(g, 0.6f, 0.2f, 0.09f);
                Take(p, 3.5f);
            }
            // understorey trees: dense near the ring (the toucans perch in them), thinning out
            var perchTrees = new List<GameObject> { mother };
            for (int i = 0; i < 6000 && mids < 70; i++)
            {
                float r = mids < 26 ? Random.Range(22.5f, 32f) : Random.Range(26f, 95f);
                var p = Polar(Random.Range(0f, 360f), r);
                if (!Free(p, 1.2f)) continue;
                string kind = new[] { "MidTree_A", "MidTree_B", "MidTree_C" }[Random.Range(0, 3)];
                var g = FP(kind, p, Random.Range(0f, 360f), treeRoot, Random.Range(0.85f, 1.25f), Col.None, true, $"{kind}_{mids++}", sink: 0.1f);
                TrunkCollider(g, 0.3f, 8f);
                if (r < 36f) perchTrees.Add(g);
                Take(p, 2.2f);
            }
            for (int i = 0; i < 4000 && palms < 34; i++)
            {
                var p = Polar(Random.Range(0f, 360f), Random.Range(22f, 90f));
                if (!Free(p, 1f)) continue;
                string kind = Random.value < 0.55f ? "AcaiPalm_A" : "AcaiPalm_B";
                var g = FP(kind, p, Random.Range(0f, 360f), treeRoot, Random.Range(0.85f, 1.25f), Col.None, true, $"{kind}_{palms++}", sink: 0.1f);
                TrunkCollider(g, 0.18f, 10f);
                SwayCrown(g, 1.6f, 0.5f, 0.15f);
                Take(p, 1.4f);
            }
            for (int i = 0; i < 4000 && tfern < 34; i++)
            {
                var p = Polar(Random.Range(0f, 360f), tfern < 16 ? Random.Range(20.8f, 26f) : Random.Range(24f, 60f));
                if (!Free(p, 0.8f)) continue;
                string kind = Random.value < 0.5f ? "TreeFern_A" : "TreeFern_B";
                var g = FP(kind, p, Random.Range(0f, 360f), underRoot, Random.Range(0.85f, 1.3f), Col.None, true, $"{kind}_{tfern++}", sink: 0.05f);
                SwayCrown(g, 1.4f, 0.4f, 0.2f);
                Take(p, 1.2f);
            }

            // ---------------- understorey: a wall of thickets and ferns right behind the stones, thinning out into the forest
            int bushes = 0, ferns = 0, tufts = 0;
            for (int i = 0; i < 20000 && bushes < 440; i++)
            {
                float r = bushes < 160 ? Random.Range(20.6f, 30f) : Random.Range(26f, 85f);
                var p = Polar(Random.Range(0f, 360f), r);
                if (!Free(p, 0.6f)) continue;
                if (Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(p.z, p.x) * Mathf.Rad2Deg, 90f)) < 7f && r < 27f) continue;   // the way to the altar stays open
                string kind = new[] { "Bush_A", "Bush_B", "Bush_C", "Bush_A", "Bush_B" }[Random.Range(0, 5)];
                FP(kind, p, Random.Range(0f, 360f), underRoot, Random.Range(0.85f, 1.35f), Col.None, true, null, true, 0.05f);
                bushes++;
            }
            for (int i = 0; i < 20000 && ferns < 400; i++)
            {
                float r = ferns < 40 ? Random.Range(13f, 20f) : ferns < 200 ? Random.Range(19.5f, 32f) : Random.Range(28f, 80f);
                var p = Polar(Random.Range(0f, 360f), r);
                if (!Free(p, 0.4f)) continue;
                FP(Random.value < 0.5f ? "Fern_A" : "Fern_B", p, Random.Range(0f, 360f), underRoot, r < 20f ? Random.Range(0.6f, 0.9f) : Random.Range(0.9f, 1.4f));
                ferns++;
            }
            for (int i = 0; i < 6000 && tufts < 380; i++)
            {
                float r = tufts < 240 ? Random.Range(2f, 20f) : Random.Range(20f, 60f);
                var p = Polar(Random.Range(0f, 360f), r);
                if (!Free(p, 0.3f)) continue;
                FP(Random.value < 0.5f ? "Tuft_A" : "Tuft_B", p, Random.Range(0f, 360f), underRoot, Random.Range(0.8f, 1.4f));
                tufts++;
            }
            // lianas hanging in curtains between the trees around the ring
            int curtains = 0;
            for (int i = 0; i < 2000 && curtains < 8; i++)
            {
                float a = Random.Range(0f, 360f);
                var p = Polar(a, Random.Range(22.5f, 34f));
                if (!Free(p, 0.5f)) continue;
                FP(Random.value < 0.5f ? "LianaCurtain_A" : "LianaCurtain_B", p, Tangent(a) + Random.Range(-25f, 25f), underRoot, Random.Range(0.9f, 1.3f));
                curtains++;
            }
            // fallen trunks, roots snaking over the floor, mossy rocks
            foreach (var (a, r, kind, yawOff) in new[] { (125f, 24.5f, "FallenLog_A", 70f), (320f, 23.5f, "FallenLog_B", -60f), (205f, 27f, "FallenLog_A", 20f),
                                                         (40f, 31f, "FallenLog_B", 95f), (260f, 45f, "FallenLog_A", 10f) })
            {
                var p = Polar(a, r);
                FP(kind, p, Tangent(a) + yawOff, underRoot, 1f, r < 28f ? Col.Mesh : Col.None, true, null, true, 0.2f);
                Take(p, 2.5f);
            }
            int roots = 0;
            // from the great kapok's buttresses out across the floor, toward the middle of the clearing
            foreach (var (x, z, yaw, s) in new[] { (-3.5f, 26.5f, 110f, 1.5f), (3.8f, 26f, 60f, 1.4f), (-1f, 24.5f, 88f, 1.2f), (6.5f, 27.5f, 35f, 1.3f), (-7f, 28f, 140f, 1.3f) })
            {
                FP("SurfaceRoot_A", new Vector3(x, 0, z), yaw, envRoot, s, Col.None, true, $"Root{roots++}");
            }
            for (int i = 0; i < 400 && roots < 16; i++)
            {
                var p = Polar(Random.Range(0f, 360f), Random.Range(7f, 18f));
                FP(Random.value < 0.5f ? "SurfaceRoot_A" : "SurfaceRoot_B", p, Random.Range(0f, 360f), envRoot, Random.Range(0.8f, 1.2f), Col.None, true, $"Root{roots++}");
            }
            int rocks = 0;
            for (int i = 0; i < 3000 && rocks < 30; i++)
            {
                var p = Polar(Random.Range(0f, 360f), Random.Range(21f, 75f));
                if (!Free(p, 1.2f)) continue;
                FP(Random.value < 0.5f ? "MossRock_A" : "MossRock_B", p, Random.Range(0f, 360f), stoneRoot, Random.Range(0.7f, 1.5f), p.magnitude < 30f ? Col.Mesh : Col.None, true, null, true, 0.25f);
                Take(p, 1.2f);
                rocks++;
            }

            // ---------------- the deep forest: more trees and thickets filling every gap toward the horizon. Placed after
            // everything above so the near forest keeps its layout; nothing tall goes in the sun's corridor (north-east,
            // where the light comes through the great kapok's crown), so the god rays are untouched.
            float sunAz = Mathf.Atan2(toSun.z, toSun.x) * Mathf.Rad2Deg;
            bool InSunCorridor(Vector3 p) => Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(p.z, p.x) * Mathf.Rad2Deg, sunAz)) < 38f && p.magnitude < 75f;
            int deepTrees = 0, deepThickets = 0, walls = 0, deepGiants = 0;
            // giant kapoks closing the upper canopy everywhere but toward the sun (their shadows fall away from the clearing)
            for (int i = 0; i < 4000 && deepGiants < 12; i++)
            {
                var p = Polar(Random.Range(0f, 360f), Random.Range(38f, 68f));
                if (Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(p.z, p.x) * Mathf.Rad2Deg, sunAz)) < 62f || !Free(p, 5f)) continue;
                string kind = Random.value < 0.5f ? "Kapok_A" : "Kapok_B";
                float s = Random.Range(0.95f, 1.2f);
                var g = FP(kind, p, Random.Range(0f, 360f), treeRoot, s, Col.None, true, $"{kind}_deep{deepGiants++}", sink: 0.3f);
                SwayCrown(g, 0.4f, 0.12f, 0.08f);
                Take(p, 6.5f * s);
            }
            for (int i = 0; i < 8000 && deepTrees < 190; i++)
            {
                float r = Random.Range(24f, 100f);
                var p = Polar(Random.Range(0f, 360f), r);
                if (InSunCorridor(p) || !Free(p, 1.0f)) continue;
                float roll = Random.value;
                string kind = roll < 0.18f ? new[] { "Emergent_A", "Emergent_B", "Emergent_C" }[Random.Range(0, 3)]
                            : roll < 0.4f ? (Random.value < 0.55f ? "AcaiPalm_A" : "AcaiPalm_B")
                            : new[] { "MidTree_A", "MidTree_B", "MidTree_C" }[Random.Range(0, 3)];
                float s = Random.Range(0.85f, 1.3f);
                var g = FP(kind, p, Random.Range(0f, 360f), treeRoot, s, Col.None, true, $"{kind}_deep{deepTrees++}", sink: 0.15f);
                if (r < 34f) TrunkCollider(g, kind.StartsWith("Emergent") ? 0.9f * s : 0.3f, 10f);
                if (kind.StartsWith("Emergent")) SwayCrown(g, 0.6f, 0.2f, 0.09f);
                else if (kind.StartsWith("Acai")) SwayCrown(g, 1.6f, 0.5f, 0.15f);
                Take(p, kind.StartsWith("Emergent") ? 3f : 1.6f);
            }
            for (int i = 0; i < 12000 && deepThickets < 480; i++)
            {
                float r = deepThickets < 120 ? Random.Range(23f, 34f) : Random.Range(28f, 95f);
                var p = Polar(Random.Range(0f, 360f), r);
                if (!Free(p, 0.8f)) continue;
                if (Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(p.z, p.x) * Mathf.Rad2Deg, 90f)) < 7f && r < 27f) continue;
                string kind = Random.value < 0.55f ? "Thicket_Tall" : new[] { "Bush_A", "Bush_B" }[Random.Range(0, 2)];
                FP(kind, p, Random.Range(0f, 360f), underRoot, Random.Range(0.9f, 1.4f), Col.None, true, null, true, 0.05f);
                deepThickets++;
            }
            // walls of canopy in rings far out: whatever shows between the trunks is more forest, not sky or empty haze.
            // They cast no shadows, so the light in the clearing stays exactly as it was.
            var wallRoot = new GameObject("Deep Forest Walls").transform;
            wallRoot.SetParent(treeRoot);
            foreach (var (r0, count, sMin, sMax) in new[] { (46f, 16, 0.9f, 1.1f), (60f, 22, 1.0f, 1.2f), (76f, 26, 1.15f, 1.35f), (94f, 30, 1.35f, 1.6f) })
            {
                float off = Random.Range(0f, 360f);
                for (int k = 0; k < count; k++)
                {
                    float a = off + k * 360f / count + Random.Range(-4f, 4f);
                    var p = Polar(a, r0 + Random.Range(-4f, 4f));
                    float yaw = Mathf.Atan2(-p.x, -p.z) * Mathf.Rad2Deg + Random.Range(-12f, 12f);    // local +Z (Blender -Y) toward the clearing
                    var w = FP(Random.value < 0.5f ? "CanopyWall_A" : "CanopyWall_B", p, yaw, wallRoot, Random.Range(sMin, sMax), Col.None, true, $"CanopyWall{walls++}", true, 0.3f);
                    foreach (var mr in w.GetComponentsInChildren<MeshRenderer>()) mr.shadowCastingMode = ShadowCastingMode.Off;
                }
            }

            // ---------------- wildlife: frogs in the litter, toucans in the branches, vultures over the gap
            int frogs = 0;
            for (int i = 0; i < 800 && frogs < 18; i++)
            {
                var p = Polar(Random.Range(0f, 360f), frogs < 12 ? Random.Range(5f, 18.5f) : Random.Range(19f, 26f));
                if (!Free(p, 0.6f)) continue;
                var f = FP(frogs % 3 == 2 ? "Frog_Dart" : "Frog_Green", p, Random.Range(0f, 360f), lifeRoot, Random.Range(1.0f, 1.5f), Col.None, true, $"Frog{frogs}", false);
                var fc = f.AddComponent<Frog>();
                fc.roam = p.magnitude < 19f ? 4f : 2.5f;
                frogs++;
            }
            var perches = perchTrees.SelectMany(t => t.GetComponentsInChildren<Transform>().Where(c => c.name.StartsWith("Perch"))).ToArray();
            int birds = 0;
            // start on the low, bare branches (in plain view); every perch stays free to fly to
            var shuffled = perches.Where(p => p.position.y < 10f).OrderBy(_ => Random.value).Concat(perches.Where(p => p.position.y >= 10f).OrderBy(_ => Random.value)).ToList();
            var used = new List<Vector3>();
            foreach (var perch in shuffled)
            {
                if (birds >= 16) break;
                if (used.Any(u => (u - perch.position).sqrMagnitude < 2.5f * 2.5f)) continue;    // one bird to a branch
                used.Add(perch.position);
                var b = FP("Toucan", perch.position, Random.Range(0f, 360f), lifeRoot, Random.Range(1.5f, 1.75f), Col.None, false, $"Toucan{birds++}", false);
                var fb = b.AddComponent<ForestBird>();
                fb.perches = perches;
                fb.startPerch = System.Array.IndexOf(perches, perch);
            }
            int vultures = 0;
            for (int i = 0; i < 5; i++)
            {
                var v = FP("Vulture", new Vector3(0, 50f, 0), 0f, lifeRoot, Random.Range(1.1f, 1.3f), Col.None, false, $"Vulture{vultures++}", false);
                var sb = v.AddComponent<SeaBird>();
                sb.center = new Vector3(Random.Range(-6f, 6f), 0, Random.Range(-6f, 10f));
                sb.radius = Random.Range(16f, 30f); sb.height = Random.Range(44f, 58f);
                sb.speed = Random.Range(5f, 6.5f); sb.direction = Random.value < 0.7f ? 1f : -1f;
                sb.flapRate = 1.4f;
            }

            // ---------------- fireflies: drifting glows in the shade at the clearing's edge (particles), a few that light the haze
            var fireRoot = new GameObject("Fireflies").transform;
            fireRoot.SetParent(lifeRoot);
            int swarms = 0;
            foreach (var (a, r, h) in new[] { (35f, 21f, 1.6f), (110f, 22f, 1.4f), (165f, 19f, 1.2f), (215f, 22f, 1.8f), (270f, 20f, 1.3f), (325f, 21f, 1.5f), (80f, 14f, 1.0f), (250f, 9f, 0.9f) })
            {
                var p = Polar(a, r);
                FirefliesSwarm(fireRoot, new Vector3(p.x, H(p.x, p.z) + h, p.z), r < 15f ? 3.5f : 6f, r < 15f ? 4f : 7f, $"Swarm{swarms++}");
            }
            int glows = 0;
            for (int i = 0; i < 14; i++)
            {
                var p = Polar(i * (360f / 14f) + Random.Range(-8f, 8f), Random.Range(15f, 23f));
                var go = new GameObject($"Firefly{glows++}");
                go.transform.SetParent(fireRoot);
                go.transform.position = new Vector3(p.x, H(p.x, p.z) + Random.Range(0.8f, 2.4f), p.z);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point; l.color = new Color(1f, 0.85f, 0.35f); l.intensity = 1.4f; l.range = 3.2f; l.shadows = LightShadows.None;
                go.AddComponent<UniversalAdditionalLightData>();
                go.AddComponent<FogLight>().scatter = 0.6f;
                var dot = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(dot.GetComponent<Collider>());
                dot.name = "Glow";
                dot.transform.SetParent(go.transform, false);
                dot.transform.localScale = Vector3.one * 0.22f;
                var mr = dot.GetComponent<MeshRenderer>();
                mr.sharedMaterial = M("M_FX_ForestFirefly");
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                go.AddComponent<Firefly>();
            }

            // ---------------- keep the fight in: an invisible ring just inside the stones
            var bounds = new GameObject("ArenaBounds").transform;
            bounds.SetParent(envRoot);
            for (int i = 0; i < 40; i++)
            {
                float a = i * 9f;
                var b = new GameObject($"Bound{i}");
                b.transform.SetParent(bounds);
                b.transform.SetPositionAndRotation(Polar(a, ForestWall + 0.3f) + Vector3.up * 3f, Quaternion.Euler(0, Tangent(a), 0));
                var bc = b.AddComponent<BoxCollider>();
                bc.size = new Vector3(3.4f, 10f, 0.6f);
            }

            // ---------------- fog: thin morning mist on the floor + volumetric haze cut into shafts by the canopy
            var gfGo = new GameObject("GroundFog");
            gfGo.transform.SetParent(lightRoot);
            var gf = gfGo.AddComponent<GroundFog>();
            gf.center = Vector3.zero; gf.size = 56f; gf.resolution = 84; gf.bottom = 0.05f; gf.top = 1.1f; gf.layers = 10;
            gf.ground = groundCol;
            gf.material = M("M_ForestGroundFog");

            var volGo = new GameObject("PostProcess");
            volGo.transform.SetParent(lightRoot);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = BuildForestPostProfile();
            volGo.AddComponent<PSXGlobals>();
            volGo.AddComponent<ShadowRange>().distance = 60f;     // the canopy shadows (and so the shafts) reach further out

            // ---------------- player, camera, HUD, bosses: the Druid is this stage's boss
            var rig = BuildCommon(sysRoot, envRoot, actorRoot, new Vector3(0, 0, -10f), 0f, false, "Druid");
            rig.hud.titleKicker = "Lair of the Druid";
            rig.hud.titleMain = "VERDANT";
            rig.hud.titleSub = "CATHEDRAL";
            foreach (var b in Object.FindObjectsByType<Enemies.DruidBoss>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { b.arenaCenter = Vector3.zero; EditorUtility.SetDirty(b); }
            var am = Object.FindAnyObjectByType<Audio.AudioManager>(FindObjectsInactive.Include);
            if (am != null) { am.ambienceLoop = SoundPicks.ForestAmbience(); EditorUtility.SetDirty(am); }

            // the Druid's hiding place and the path it tears out of the forest (its first-meeting cinematic)
            string lairLog = BuildDruidLair();

            EditorSceneManager.SaveScene(scene, ForestScenePath);
            RegisterBuildScenes();
            return $"Forest arena built: {giants} kapoks + the great one, {emergents} emergents, {mids} understorey trees, {palms} acai palms, {tfern} tree ferns, " +
                   $"{bushes} thickets, {ferns} ferns, {tufts} tufts, {curtains} liana curtains, {stones} stones, {roots} roots, {rocks} rocks, deep forest: {deepGiants} kapoks, {deepTrees} trees, {deepThickets} thickets, {walls} canopy walls, " +
                   $"{frogs} frogs, {birds} toucans ({perches.Length} perches), {vultures} vultures, {swarms} firefly swarms + {glows} lit fireflies; {lairLog}";
        }

        static void FirefliesSwarm(Transform parent, Vector3 pos, float radius, float rate, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.loop = true; m.playOnAwake = true; m.prewarm = true;
            m.duration = 8f;
            m.startLifetime = new ParticleSystem.MinMaxCurve(6f, 11f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.15f);    // a few pixels at the 0.4 render scale
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.92f, 0.5f), new Color(0.9f, 1f, 0.45f));
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.maxParticles = 120;
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius; sh.scale = new Vector3(1f, 0.35f, 1f);
            // lazy, wandering flight
            var nz = ps.noise; nz.enabled = true; nz.strength = 0.45f; nz.frequency = 0.25f; nz.scrollSpeed = 0.1f; nz.damping = true; nz.quality = ParticleSystemNoiseQuality.Medium;
            // the blink: on, off, on, long dark, on
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.05f, 0.22f), new GradientAlphaKey(1f, 0.34f),
                              new GradientAlphaKey(0.0f, 0.5f), new GradientAlphaKey(0.0f, 0.66f), new GradientAlphaKey(1f, 0.78f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = M("M_FX_ForestFirefly");
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
        }

        static VolumeProfile BuildForestPostProfile()
        {
            string path = Root + "/Settings/ForestPostProfile.asset";
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
            // deep green shade, gold where the sun gets through
            var tone = Add<Tonemapping>(); tone.mode.Override(TonemappingMode.ACES);
            var bloom = Add<Bloom>(); bloom.threshold.Override(0.9f); bloom.intensity.Override(0.6f); bloom.scatter.Override(0.7f);
            bloom.tint.Override(new Color(1f, 0.92f, 0.75f));
            var ca = Add<ColorAdjustments>(); ca.postExposure.Override(0.4f); ca.contrast.Override(18f); ca.saturation.Override(8f);
            ca.colorFilter.Override(new Color(0.98f, 1f, 0.96f));
            var smh = Add<ShadowsMidtonesHighlights>();
            smh.shadows.Override(new Vector4(0.92f, 1.04f, 0.98f, -0.04f));
            smh.midtones.Override(new Vector4(1f, 1.01f, 0.98f, 0f));
            smh.highlights.Override(new Vector4(1.04f, 1.0f, 0.92f, 0f));
            var wb = Add<WhiteBalance>(); wb.temperature.Override(2f); wb.tint.Override(-6f);
            var vig = Add<Vignette>(); vig.intensity.Override(0.34f); vig.smoothness.Override(0.45f); vig.color.Override(new Color(0.01f, 0.03f, 0.015f));
            var grain = Add<FilmGrain>(); grain.type.Override(FilmGrainLookup.Medium2); grain.intensity.Override(0.24f); grain.response.Override(0.7f);
            // the morning haze: a tall, fairly thick layer so the sun through the canopy shows as shafts
            var vf = Add<VolumetricFog>();
            vf.enabled.Override(true);
            vf.density.Override(0.013f);
            vf.baseHeight.Override(0f);
            vf.heightFalloff.Override(10f);
            vf.maxDistance.Override(55f);
            vf.steps.Override(32);
            vf.anisotropy.Override(0.5f);
            vf.noiseScale.Override(0.05f);
            vf.noiseStrength.Override(0.45f);
            vf.wind.Override(new Vector3(0.15f, 0.02f, 0.1f));
            vf.albedo.Override(new Color(0.92f, 0.94f, 0.8f));
            vf.ambient.Override(new Color(0.045f, 0.062f, 0.042f));
            vf.mainLightScatter.Override(0.55f);
            vf.localLightScatter.Override(0.6f);
            vf.groundClear.Override(0.3f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
