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
    /// The Fairy's arena: "Starlit Grotto". A great cavern lit only by what grows in it: giant blue and violet
    /// mushrooms with glowing gills and spots (from the user's three reference paintings), glowing flowers, glow-worm
    /// threads, and a dome of twinkling stars on the rock overhead. One hole in the roof lets a shaft of moonlight
    /// down through the haze onto the fight. Still puddles on the floor mirror all of it (PlanarReflection). Gnomes
    /// live here in two mushroom houses and watch the fight; frogs sit by the water. The whole grade leans purple.
    /// A ring of small mushrooms (a fairy ring) marks the edge of the fight.
    /// Models: Tools/Blender/build_cave.py -> Art/Models/Cave (frogs and ferns reuse the forest's). Menu: Elder Tree/9i-9j.
    /// </summary>
    public static partial class ElderTreeBuilder
    {
        public const string CaveScenePath = Root + "/Scenes/Cave_Fairy.unity";
        public const string CaveDir = Root + "/Art/Models/Cave";
        const float CaveWall = 20.2f;            // invisible ring that keeps the fight in (just outside the fairy ring)
        const float CaveH = 24f;                 // height of the dome at the moon-hole
        const float PuddleY = -0.03f;            // every puddle shares one water level (one mirror)
        const float OculusR = 4.2f;
        static readonly Vector2 OculusXZ = new(4f, 9f);
        static readonly Vector3 MoonTarget = new(0f, 0f, 1f);
        static readonly List<(Vector2 c, float r)> cavePuddles = new();

        static readonly Dictionary<string, string> CaveSlotMats = new()
        {
            { "M_MushCap", "M_MushCapBlue" }, { "M_MushCapAlt", "M_MushCapViolet" }, { "M_MushGill", "M_MushGillBlue" }, { "M_MushGillAlt", "M_MushGillViolet" },
            { "M_MushStalk", "M_MushStalk" }, { "M_CaveRock", "M_CaveRock" }, { "M_CaveMoss", "M_CaveMoss" }, { "M_CaveFlowers", "M_CaveFlowers" },
            { "M_GnomeCoat", "M_GnomeCoat" }, { "M_GnomeHat", "M_GnomeHat" }, { "M_GnomeCloth", "M_GnomeCloth" }, { "M_GnomeBoot", "M_GnomeBoot" },
            { "M_GnomeSkin", "M_GnomeSkin" }, { "M_GnomeNose", "M_GnomeNose" }, { "M_GnomeBeard", "M_GnomeBeard" }, { "M_GnomeWood", "M_GnomeWood" },
            { "M_GnomeWindow", "M_GnomeWindow" }, { "M_GnomeLanternGlow", "M_GnomeLanternGlow" }, { "M_Iron", "M_Iron" },
        };

        static GameObject CaveProp(string n) => AssetDatabase.LoadAssetAtPath<GameObject>($"{CaveDir}/{n}.fbx");
        static Vector3 MoonDir => (new Vector3(OculusXZ.x, CaveH, OculusXZ.y) - MoonTarget).normalized;   // toward the moon

        // ================================================================== art
        [MenuItem("Elder Tree/9i. Build Cave Art (textures, materials, imports)")]
        public static string BuildCaveArt()
        {
            var rock = SavePsx("T_CaveRock", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#241E32"), Hex("#62587E"), 0.3f + 0.45f * Fbm(u, v, 8, 8, 4, 901) + 0.2f * Gr(x, y, 201));
                Worley(u, v, 6, 902, out float f1, out float f2, out _);
                c *= 1f - 0.45f * Sst(0.04f, 0f, f2 - f1);                                                // cracks
                float band = Mathf.Abs(Mathf.Sin((v * 5f + Fbm(u, v, 4, 4, 2, 903) * 0.8f) * Mathf.PI));
                c *= 1f - 0.16f * Sst(0.1f, 0f, band);                                                    // strata
                c = Mx(c, Hex("#4C4668"), Sst(0.62f, 0.8f, Fbm(u, v, 3, 12, 3, 904)) * 0.4f);            // damp sheen running down
                c = Mx(c, Hex("#1C3036"), Sst(0.66f, 0.8f, Fbm(u, v, 5, 5, 3, 905)) * 0.5f);             // blue-green mineral stain
                if (Hash(x, y, 202) > 0.985f) c = Hex("#6A80B0");                                         // glinting flecks
                return c;
            });
            var moss = SavePsx("T_CaveMoss", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#0C1C22"), Hex("#2A5462"), 0.3f + 0.45f * Fbm(u, v, 12, 12, 4, 911) + 0.3f * Gr(x, y, 203));
                if (Hash(x, y, 204) > 0.965f) c = Hex("#5E9EB2");                                         // sporophytes catching the glow
                return Mx(c, Hex("#20183A"), Sst(0.66f, 0.8f, Fbm(u, v, 5, 5, 3, 912)) * 0.45f);
            });
            var floor = SavePsx("T_CaveFloor", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#1C1626"), Hex("#463C56"), 0.35f + 0.4f * Fbm(u, v, 8, 8, 4, 921) + 0.25f * Gr(x, y, 205));
                Worley(u, v, 10, 922, out float f1, out float f2, out int id);
                float peb = Sst(0.03f, 0.08f, f2 - f1) * (Hash(id, 1, 923) > 0.45f ? 1f : 0f);              // pebbles bedded in grit
                c = Mx(c, Mx(Hex("#3A3248"), Hex("#605670"), Hash(id, 2, 924)) * (0.85f + 0.3f * (1f - f1 * 3f)), peb * 0.85f);
                c = Mx(c, Hex("#1A2A30"), Sst(0.62f, 0.78f, Fbm(u, v, 4, 4, 3, 925)) * 0.45f);
                return c;
            });
            var floorMoss = SavePsx("T_CaveFloorMoss", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#0C1A20"), Hex("#245060"), 0.35f + 0.4f * Fbm(u, v, 10, 10, 4, 931) + 0.25f * Gr(x, y, 206));
                Worley(u, v, 12, 932, out float f1, out _, out int id);
                if (Hash(id, 3, 933) > 0.8f && f1 < 0.12f) c = Mx(c, Hex("#4A3A78"), 0.5f);                   // violet cushions
                if (Hash(x, y, 207) > 0.975f) c = Hex("#6AB4C4");
                return c;
            });
            var mud = SavePsx("T_CaveMud", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#08060C"), Hex("#1E1828"), 0.35f + 0.45f * Fbm(u, v, 8, 8, 4, 941) + 0.2f * Gr(x, y, 208));
                return Mx(c, Hex("#3A3456"), Sst(0.7f, 0.82f, Fbm(u, v, 12, 12, 2, 942)) * 0.5f);            // wet sheen
            });

            // mushrooms: caps and gills are mapped top-down, centred, so the spots and gills sit on every cap the same way
            var cap = SavePsx("T_MushCap", 128, (u, v, x, y) =>
            {
                float dx = u - 0.5f, dy = v - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) * 2f, th = Mathf.Atan2(dy, dx);
                float g = 0.42f + 0.28f * Fbm(u, v, 6, 6, 3, 951) + 0.18f * Sst(0.2f, 1f, r) + 0.12f * Gr(x, y, 209);
                g *= 1f - 0.12f * Sst(0.6f, 1f, r) * Mathf.Abs(Mathf.Sin(th * 23f + Fbm(u, v, 3, 3, 2, 952) * 3f));   // fine streaks to the rim
                var c = new Color(g, g, g * 1.04f);
                Worley(u, v, 7, 953, out float f1, out _, out int id);
                float rs = 0.07f + 0.07f * Hash(id, 1, 954);
                if (Hash(id, 2, 955) > 0.3f && f1 < rs)
                    c = f1 < rs - 0.025f ? Mx(Hex("#D8DCEC"), Hex("#F2F2FA"), Hash(x, y, 210)) : Hex("#8A8CA0");   // pale spots, rimmed
                return c;
            });
            var capGlow = SavePsx("T_MushCapGlow", 128, (u, v, x, y) =>
            {
                float dx = u - 0.5f, dy = v - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                Worley(u, v, 7, 953, out float f1, out _, out int id);
                float rs = 0.07f + 0.07f * Hash(id, 1, 954);
                float g = Hash(id, 2, 955) > 0.3f ? Sst(rs, rs - 0.03f, f1) : 0f;
                g = Mathf.Max(g, Sst(0.86f, 1f, r) * 0.35f);                                               // the rim glows a little
                return new Color(g, g, g);
            });
            var gill = SavePsx("T_MushGill", 128, (u, v, x, y) =>
            {
                float dx = u - 0.5f, dy = v - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) * 2f, th = Mathf.Atan2(dy, dx);
                float lines = Mathf.Abs(Mathf.Sin(th * 36f + Fbm(u, v, 4, 4, 2, 961) * 1.5f));
                float g = Mx(new Color(0.18f, 0.18f, 0.2f), new Color(1f, 1f, 1f), Sst(0.15f, 0.7f, lines)).r;
                g *= 0.35f + 0.65f * Sst(0.15f, 0.75f, r);                                                 // dark at the stalk, bright to the rim
                g *= 0.85f + 0.3f * Gr(x, y, 211);
                return new Color(g, g, g);
            });
            var stalk = SavePsx("T_MushStalk", 64, (u, v, x, y) =>
            {
                float g = 0.62f + 0.22f * Fbm(u, v, 4, 16, 3, 971) + 0.1f * Gr(x, y, 212);
                g *= 1f - 0.22f * Sst(0.15f, 0f, Mathf.Abs(Mathf.Sin((u * 9f + Fbm(u, v, 3, 3, 2, 972) * 1.2f) * Mathf.PI)));   // fibres
                var c = new Color(g * 0.94f, g * 0.92f, g);
                if (Hash(x, y, 213) > 0.97f) c *= 0.7f;                                                    // freckles
                return c;
            });
            var flowers = SavePsx("T_CaveFlowers", 256, (u, v, x, y) => CaveFlowers(u, v, x, y, false), mips: true, alpha: true);
            var flowersGlow = SavePsx("T_CaveFlowersGlow", 256, (u, v, x, y) => CaveFlowers(u, v, x, y, true), mips: true);
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath(TexDir + "/T_CaveFlowers.png");
                imp.mipMapsPreserveCoverage = true; imp.alphaTestReferenceValue = 0.5f; imp.SaveAndReimport();
            }
            var wool = SavePsx("T_GnomeWool", 64, (u, v, x, y) =>
            {
                float knit = Mathf.Abs(Mathf.Sin((u * 16f + (Mathf.Floor(v * 24f) % 2f) * 0.5f) * Mathf.PI)) * 0.15f;
                float g = 0.55f + 0.25f * Fbm(u, v, 6, 6, 3, 981) + knit + 0.12f * Gr(x, y, 214);
                return new Color(g, g, g);
            });
            var beard = SavePsx("T_GnomeBeard", 64, (u, v, x, y) =>
            {
                float s = Mathf.Abs(Mathf.Sin((u * 14f + Fbm(u, v, 4, 4, 2, 982) * 2f) * Mathf.PI));
                float g = 0.7f + 0.22f * s + 0.08f * Gr(x, y, 215);
                return new Color(g, g, g * 1.03f);
            });
            var skin = SavePsx("T_GnomeSkin", 64, (u, v, x, y) => Mx(Hex("#9C6E62"), Hex("#D2A28E"), 0.4f + 0.4f * Fbm(u, v, 5, 5, 3, 983) + 0.15f * Gr(x, y, 216)));
            var wood = SavePsx("T_GnomeWood", 64, (u, v, x, y) =>
            {
                var c = Mx(Hex("#2A1C12"), Hex("#5E4228"), 0.35f + 0.4f * Fbm(u, v, 3, 12, 3, 984) + 0.15f * Gr(x, y, 217));
                return c * (1f - 0.3f * Sst(0.1f, 0f, Mathf.Abs(Mathf.Sin(u * 6f * Mathf.PI))));                 // plank seams
            });
            var frog = SavePsx("T_CaveFrog", 64, (u, v, x, y) =>
            {
                var c = Mx(Hex("#140E2C"), Hex("#3A2C6C"), 0.35f + 0.4f * Fbm(u, v, 6, 6, 3, 991) + 0.2f * Gr(x, y, 218));
                Worley(u, v, 6, 992, out float f1, out _, out _);
                c = Mx(c, Hex("#08060E"), Sst(0.2f, 0.12f, f1) * 0.8f);
                Worley(u, v, 9, 993, out float g1, out _, out int gid);
                if (Hash(gid, 1, 994) > 0.5f && g1 < 0.07f) c = Hex("#8AE0F0");                              // glowing spots
                return c;
            });
            var frogGlow = SavePsx("T_CaveFrogGlow", 64, (u, v, x, y) =>
            {
                Worley(u, v, 9, 993, out float g1, out _, out int gid);
                float g = Hash(gid, 1, 994) > 0.5f ? Sst(0.07f, 0.04f, g1) : 0f;
                return new Color(g, g, g);
            });

            Lit("M_CaveRock", new Color(0.92f, 0.9f, 1f), rock);
            Lit("M_CaveMoss", Color.white, moss);
            Lit("M_CaveFloor", Color.white, floor);
            Lit("M_CaveFloorMoss", Color.white, floorMoss);
            Lit("M_CaveMud", Color.white, mud);
            Glow(Lit("M_MushCapBlue", new Color(0.26f, 0.5f, 1f), cap), new Color(0.4f, 1.7f, 2.6f), capGlow, cull: true);
            Glow(Lit("M_MushCapViolet", new Color(0.66f, 0.3f, 0.9f), cap), new Color(2.4f, 0.6f, 2.3f), capGlow, cull: true);
            Glow(Lit("M_MushGillBlue", new Color(0.55f, 0.85f, 1f), gill), new Color(0.4f, 1.4f, 2.2f), gill, cull: true);
            Glow(Lit("M_MushGillViolet", new Color(0.9f, 0.55f, 1f), gill), new Color(1.9f, 0.55f, 1.9f), gill, cull: true);
            Glow(Lit("M_MushStalk", new Color(0.78f, 0.76f, 0.92f), stalk), new Color(0.05f, 0.05f, 0.1f), null);
            var fl = Lit("M_CaveFlowers", Color.white, flowers, null, 0f, 1f, null, false);
            Cutout(fl);
            Glow(fl, new Color(1.8f, 1.8f, 1.8f), flowersGlow);
            Lit("M_GnomeCoat", new Color(0.28f, 0.32f, 0.55f), wool, null, 0.3f, 1f, null, false);
            Lit("M_GnomeCoatPlum", new Color(0.45f, 0.26f, 0.48f), wool, null, 0.3f, 1f, null, false);
            Lit("M_GnomeCoatSlate", new Color(0.32f, 0.38f, 0.42f), wool, null, 0.3f, 1f, null, false);
            Lit("M_GnomeCoatMoss", new Color(0.24f, 0.36f, 0.3f), wool, null, 0.3f, 1f, null, false);
            Lit("M_GnomeHat", new Color(0.52f, 0.24f, 0.7f), wool, null, 0.3f, 1f, null, false);
            Lit("M_GnomeHatBlue", new Color(0.22f, 0.34f, 0.78f), wool, null, 0.3f, 1f, null, false);
            Lit("M_GnomeHatTeal", new Color(0.16f, 0.5f, 0.56f), wool, null, 0.3f, 1f, null, false);
            Lit("M_GnomeHatCrimson", new Color(0.62f, 0.14f, 0.2f), wool, null, 0.3f, 1f, null, false);
            Lit("M_GnomeCloth", new Color(0.34f, 0.3f, 0.3f), wool, null, 0.3f, 1f, null, false);
            Lit("M_GnomeBoot", new Color(0.3f, 0.2f, 0.16f), wood, null, 0.3f, 1f, null, false);
            Lit("M_GnomeSkin", Color.white, skin, null, 0.3f, 1f, null, false);
            Lit("M_GnomeNose", new Color(1f, 0.72f, 0.72f), skin, null, 0.3f, 1f, null, false);
            Lit("M_GnomeBeard", new Color(0.92f, 0.92f, 0.98f), beard, null, 0.3f, 1f, null, false);
            Lit("M_GnomeWood", Color.white, wood, null, 0.3f, 1f, null, false);
            Glow(Lit("M_GnomeWindow", new Color(1f, 0.75f, 0.4f)), new Color(2.6f, 1.5f, 0.55f), null);
            Glow(Lit("M_GnomeLanternGlow", new Color(1f, 0.82f, 0.5f)), new Color(3.2f, 1.9f, 0.7f), null);
            Glow(Lit("M_CaveFrog", Color.white, frog, null, 0.3f, 1f, null, false), new Color(0.6f, 1.6f, 2f), frogGlow);
            var fronds = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/T_JungleFronds.png");
            Cutout(Lit("M_CaveFronds", new Color(0.3f, 0.42f, 0.62f), fronds, null, 0f, 1f, null, false));

            // the ceiling of stars over the rock
            var stars = AssetOf("M_CaveStars", "ElderTree/CaveStars");
            stars.SetVector("_BandDir", new Vector4(0.62f, 0.12f, 0.78f, 0));
            stars.SetFloat("_NebulaStrength", 0.6f);

            var puddle = AssetOf("M_CavePuddle", "ElderTree/Puddle");
            puddle.SetColor("_Tint", new Color(0.8f, 0.72f, 0.98f));

            // the night beyond the moon-hole
            var sky = AssetOf("M_CaveSky", "ElderTree/NightSky");
            sky.SetColor("_ZenithColor", new Color(0.03f, 0.02f, 0.08f));
            sky.SetColor("_HorizonColor", new Color(0.1f, 0.06f, 0.2f));
            sky.SetColor("_GroundColor", new Color(0.02f, 0.015f, 0.04f));
            sky.SetFloat("_StarBrightness", 2.4f);
            sky.SetFloat("_StarDensity", 340f);
            sky.SetColor("_MoonColor", new Color(0.9f, 0.88f, 1f));
            sky.SetFloat("_MoonSize", 0.06f);
            sky.SetFloat("_HaloStrength", 1.2f);
            sky.SetFloat("_CloudStrength", 0.15f);
            sky.SetFloat("_CloudLayer", 0f);

            var gfog = AssetOf("M_CaveGroundFog", "ElderTree/GroundFog");
            gfog.SetColor("_Color", new Color(0.42f, 0.32f, 0.68f));
            gfog.SetColor("_Ambient", new Color(0.05f, 0.03f, 0.09f));
            gfog.SetFloat("_Density", 0.035f);
            gfog.SetFloat("_MoonScatter", 0.5f);
            gfog.SetFloat("_Height", 0.8f);
            gfog.SetFloat("_SoftDistance", 0.9f);

            var soft = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/T_FX_SoftDot.png");
            Unlit("M_FX_CaveSporeBlue", new Color(0.7f, 1.4f, 3.2f, 1f), soft, additive: true);
            Unlit("M_FX_CaveSporeViolet", new Color(2.4f, 0.9f, 3.2f, 1f), soft, additive: true);
            Unlit("M_FX_MoonMote", new Color(1.4f, 1.4f, 2.0f, 1f), soft, additive: true);
            AssetDatabase.SaveAssets();

            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { CaveDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                imp.animationType = ModelImporterAnimationType.None;
                imp.importAnimation = false;
                imp.importCameras = false; imp.importLights = false;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                imp.addCollider = false;
                imp.isReadable = true;
                foreach (var kv in CaveSlotMats)
                {
                    var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{kv.Value}.mat");
                    if (mat) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), mat);
                }
                imp.SaveAndReimport();
                n++;
            }
            EnsureFogFeature();
            AssetDatabase.SaveAssets();
            return $"Cave art built ({n} models)";
        }

        /// <summary>Emission on a PSX lit material (HDR colour x map; null map = flat), optionally two-sided.</summary>
        static Material Glow(Material m, Color emission, Texture2D map, bool cull = false)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.SetTexture("_EmissionMap", map);
            if (cull) m.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>2x2 cutout atlas (glow = the emission map): bluebells (TL), star blossoms (TR), bell spikes (BL),
        /// glow-worm threads (BR, hanging from the top of the cell). Plants grow from the bottom of the cell.</summary>
        static Color CaveFlowers(float u, float v, int x, int y, bool glow)
        {
            float lu = (u % 0.5f) * 2f, lv = (v % 0.5f) * 2f;
            bool top = v >= 0.5f, right = u >= 0.5f;
            var clear = glow ? Color.black : new Color(0, 0, 0, 0);
            Color Stem(int s) { var c = Mx(Hex("#10221E"), Hex("#2C5048"), 0.4f + 0.4f * Hash(x, y, s)); c.a = 1; return glow ? Color.black : c; }
            Color Petal(Color dark, Color light, float t, Color glowCol, float g)
            {
                if (glow) return glowCol * g;
                var c = Mx(dark, light, t); c.a = 1; return c;
            }
            if (!right && top)
            {
                // bluebells: arching stems, bells hanging off the outer side, a few blades at the foot
                for (int k = 0; k < 4; k++)
                {
                    float x0 = 0.18f + 0.2f * k + 0.04f * Hash(k, 1, 31), h = 0.6f + 0.3f * Hash(k, 2, 32), bend = (k % 2 == 0 ? 1f : -1f) * (0.12f + 0.08f * Hash(k, 3, 33));
                    float sx = x0 + bend * lv * lv / (h * h);
                    if (lv < h && Mathf.Abs(lu - sx) < 0.014f) return Stem(41);
                    for (int b = 0; b < 4; b++)
                    {
                        float bv = h * (0.55f + 0.14f * b);
                        float bx = x0 + bend * bv * bv / (h * h) + Mathf.Sign(bend) * 0.035f;
                        float dx = (lu - bx) / 0.034f, dy = (lv - (bv - 0.05f)) / 0.05f;
                        if (dy > -1f && dy < 1f && Mathf.Abs(dx) < 0.55f + 0.45f * Sst(0.6f, -1f, dy))
                            return Petal(Hex("#22348A"), Hex("#7C9CF0"), 0.3f + 0.5f * (1f - Mathf.Abs(dx)) + 0.2f * Gr(x, y, 34), new Color(0.35f, 0.6f, 1f), Sst(0.4f, -1f, dy));
                    }
                }
                if (lv < 0.25f && Mathf.Abs(Mathf.Sin(lu * 23f)) < 0.18f * (1f - lv / 0.25f) + 0.05f) return Stem(42);
                return clear;
            }
            if (right && top)
            {
                // star blossoms: five-petalled violet flowers with glowing hearts on thin stems over a leaf rosette
                for (int k = 0; k < 6; k++)
                {
                    float fx = 0.15f + 0.7f * Hash(k, 4, 35), fy = 0.4f + 0.5f * Hash(k, 5, 36);
                    float dx = lu - fx, dy = lv - fy, r = Mathf.Sqrt(dx * dx + dy * dy), th = Mathf.Atan2(dy, dx) + k;
                    float pr = 0.075f * (0.5f + 0.5f * Mathf.Abs(Mathf.Cos(th * 2.5f)));
                    if (r < 0.018f) return Petal(Hex("#B0F0FF"), Hex("#E8FFFF"), 0.5f, new Color(0.7f, 1f, 1f), 1f);
                    if (r < pr) return Petal(Hex("#4A2490"), Hex("#C890FF"), 0.2f + 0.8f * (1f - r / pr) + 0.15f * Gr(x, y, 37), new Color(0.9f, 0.45f, 1f), 0.55f * (1f - r / pr));
                    float sx = fx + 0.03f * Mathf.Sin(lv * 9f + k);
                    if (lv < fy && Mathf.Abs(lu - sx) < 0.011f) return Stem(43);
                }
                float lx = (lu - 0.5f) / 0.45f, ly = lv / 0.22f;
                if (ly < 1f && Mathf.Abs(Mathf.Sin(Mathf.Atan2(ly, lx) * 4f)) * (1f - ly) > 0.25f && lx * lx + ly * ly < 1f) return Stem(44);
                return clear;
            }
            if (!right)
            {
                // bell spikes: tall stems hung with violet bells that shrink toward the tip
                for (int k = 0; k < 3; k++)
                {
                    float x0 = 0.22f + 0.28f * k + 0.05f * Hash(k, 6, 38), h = 0.75f + 0.22f * Hash(k, 7, 39);
                    float sx = x0 + 0.03f * Mathf.Sin(lv * 4f + k);
                    if (lv < h && Mathf.Abs(lu - sx) < 0.012f) return Stem(45);
                    for (int b = 0; b < 9; b++)
                    {
                        float bv = h * (0.3f + 0.075f * b);
                        float side = b % 2 == 0 ? 1f : -1f;
                        float s = 0.045f * (1f - 0.065f * b);
                        float dx = (lu - (sx + side * s * 0.9f)) / s, dy = (lv - bv) / (s * 1.2f);
                        if (dx * dx + dy * dy < 1f)
                            return Petal(Hex("#3A1868"), Hex("#B070E8"), 0.25f + 0.6f * (1f - dx * dx - dy * dy) + 0.15f * Gr(x, y, 46), new Color(0.75f, 0.35f, 1f), 0.45f * (1f - dx * dx - dy * dy));
                    }
                }
                if (lv < 0.2f && Mathf.Abs(Mathf.Sin(lu * 19f + 1f)) < 0.2f * (1f - lv / 0.2f) + 0.05f) return Stem(47);
                return clear;
            }
            {
                // glow-worm threads: silk lines from the top of the cell, beaded with drops of blue light
                for (int k = 0; k < 7; k++)
                {
                    float sx = 0.08f + 0.13f * k + 0.03f * Hash(k, 8, 48);
                    float end = 0.05f + 0.6f * Hash(k, 9, 49);
                    if (lv < end) continue;
                    float ph = (1f - lv) * 13f + Hash(k, 10, 50) * 3f;
                    float fb = ph - Mathf.Floor(ph);
                    float bead = Mathf.Abs(fb - 0.5f) < 0.16f ? 1f : 0f;
                    float bw = 0.018f + 0.01f * Hash(k, Mathf.FloorToInt(ph), 51);
                    if (Mathf.Abs(lu - sx) < bw && bead > 0f)
                        return Petal(Hex("#4A8AE0"), Hex("#D0F4FF"), 1f - Mathf.Abs(lu - sx) / bw, new Color(0.5f, 0.85f, 1f), 1f);
                    if (Mathf.Abs(lu - sx) < 0.008f) return glow ? new Color(0.08f, 0.12f, 0.2f) : new Color(0.5f, 0.62f, 0.8f, 1f);
                }
                return clear;
            }
        }

        // ================================================================== ground, shell, water
        static float CaveBaseHeight(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            float n1 = Mathf.PerlinNoise(x * 0.1f + 7.3f, z * 0.1f + 2.1f);
            float n2 = Mathf.PerlinNoise(x * 0.33f + 1.7f, z * 0.33f + 9.4f);
            return 0.03f + 0.05f * n2 + Sst(19f, 28f, r) * 0.55f * n1 + Sst(26f, 42f, r) * 3.2f;
        }

        static float CaveHeight(float x, float z)
        {
            float h = CaveBaseHeight(x, z);
            foreach (var (c, rad) in cavePuddles)
            {
                float dx = x - c.x, dz = z - c.y, d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > rad * 1.6f) continue;
                float wob = 1f + 0.28f * (Mathf.PerlinNoise(c.x + dx / Mathf.Max(d, 0.01f) * 1.4f + 30f, c.y + dz / Mathf.Max(d, 0.01f) * 1.4f + 30f) - 0.5f) * 2f;
                float k = d / (rad * wob);
                h = Mathf.Min(h, Mathf.Lerp(PuddleY - 0.14f, h, Sst(0.6f, 1.25f, k)));
            }
            return h;
        }

        static float PuddleDist(float x, float z)
        {
            float best = 99f;
            foreach (var (c, rad) in cavePuddles) best = Mathf.Min(best, Vector2.Distance(new Vector2(x, z), c) / rad);
            return best;
        }

        static GameObject BuildCaveGround(Transform parent, out MeshCollider collider)
        {
            const float Ext = 40f, Cell = 0.8f;
            int n = Mathf.CeilToInt(Ext * 2 / Cell) + 1;
            var verts = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = -Ext + i * Cell, z = -Ext + j * Cell;
                verts[j * n + i] = new Vector3(x, CaveHeight(x, z), z);
                uvs[j * n + i] = new Vector2(x, z) / 3.5f;
            }
            var subs = new[] { new List<int>(), new List<int>(), new List<int>() };   // floor, moss, mud
            void Tri(int a, int b, int c)
            {
                var m = (verts[a] + verts[b] + verts[c]) / 3f;
                float moss = Mathf.PerlinNoise(m.x * 0.11f + 21.3f, m.z * 0.11f + 4.7f);
                float r = new Vector2(m.x, m.z).magnitude;
                float pd = PuddleDist(m.x, m.z);
                var l = pd < 1.5f ? subs[2] : moss > (r < 19f ? 0.64f : 0.46f) ? subs[1] : subs[0];
                l.Add(a); l.Add(b); l.Add(c);
            }
            for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                if (((i + j) & 1) == 0) { Tri(a, c, d); Tri(a, d, b); }
                else { Tri(a, c, b); Tri(b, c, d); }
            }
            var mesh = new Mesh { name = "CaveGround", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts; mesh.uv = uvs;
            mesh.subMeshCount = 3;
            for (int s = 0; s < 3; s++) mesh.SetTriangles(subs[s], s);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject("CaveGround");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mesh, CaveDir + "/CaveGround.asset");
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { M("M_CaveFloor"), M("M_CaveFloorMoss"), M("M_CaveMud") };
            collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            go.AddComponent<GroundSurface>().kind = SurfaceKind.Earth;
            go.isStatic = true;
            return go;
        }

        static Mesh SaveMesh(Mesh mesh, string path)
        {
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) { old.Clear(); EditorUtility.CopySerialized(mesh, old); return old; }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static float Perlin3(Vector3 p) => (Mathf.PerlinNoise(p.x, p.y) + Mathf.PerlinNoise(p.y + 31.7f, p.z + 5.3f) + Mathf.PerlinNoise(p.z + 13.1f, p.x + 47.9f)) / 3f;

        /// <summary>The cavern: a lumpy dome from the floor up to the moon-hole (its rings drift toward the hole as they
        /// rise), then a short rock chimney leaning toward the moon. Faces look inward. UVs: around x up, with the
        /// tile count per ring following its circumference so the rock keeps its scale up the dome.</summary>
        static Mesh BuildCaveShellMesh()
        {
            const int NT = 72, NR = 28, NC = 5;
            int rows = NR + 1 + NC;
            var pos = new Vector3[NT + 1, rows];
            float Rb(float th) => 31f + 3.4f * (Mathf.PerlinNoise(Mathf.Cos(th) * 1.3f + 5f, Mathf.Sin(th) * 1.3f + 5f) - 0.5f) * 2f
                                      + 1.6f * (Mathf.PerlinNoise(Mathf.Cos(th) * 3.4f + 9f, Mathf.Sin(th) * 3.4f + 2f) - 0.5f) * 2f;
            var moon = MoonDir;
            for (int i = 0; i <= NT; i++)
            {
                float th = 2f * Mathf.PI * (i % NT) / NT;
                float rb = Rb(th), phiMax = Mathf.Acos(OculusR / rb);
                for (int j = 0; j <= NR; j++)
                {
                    float t = j / (float)NR;
                    float phi = Mathf.Lerp(-0.07f, phiMax, Mathf.Pow(t, 0.85f));
                    float r = rb * Mathf.Cos(phi);
                    float y = CaveH * Mathf.Sin(phi) / Mathf.Sin(phiMax);
                    float s = Mathf.Max(0f, Mathf.Sin(phi)) / Mathf.Sin(phiMax);
                    Vector2 c = OculusXZ * (s * s);
                    var p = new Vector3(c.x + Mathf.Cos(th) * r, y, c.y + Mathf.Sin(th) * r);
                    var nrm = new Vector3(Mathf.Cos(th) * Mathf.Cos(phi), Mathf.Sin(phi), Mathf.Sin(th) * Mathf.Cos(phi));
                    float amt = (Perlin3(p * 0.07f + new Vector3(3f, 7f, 1f)) - 0.5f) * 2f * 2.6f + (Perlin3(p * 0.23f) - 0.5f) * 2f * 0.8f;
                    float w = Sst(1f, 0.85f, t);                       // the hole's rim stays round
                    pos[i, j] = p + nrm * amt * w;
                }
                for (int k = 1; k <= NC; k++)
                {
                    var center = new Vector3(OculusXZ.x, CaveH, OculusXZ.y) + moon * (k * 1.7f);
                    float rr = OculusR * (1f + 0.07f * k) * (1f + 0.15f * (Perlin3(new Vector3(Mathf.Cos(th) * 2f, k * 0.7f, Mathf.Sin(th) * 2f)) - 0.5f));
                    pos[i, NR + k] = center + new Vector3(Mathf.Cos(th), 0, Mathf.Sin(th)) * rr;
                }
            }
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var arc = new float[NT + 1];
            for (int j = 0; j < rows; j++)
            {
                float circ = 0f;
                for (int i = 0; i < NT; i++) circ += Vector3.Distance(pos[i, j], pos[i + 1, j]);
                int tiles = Mathf.Max(2, Mathf.RoundToInt(circ / 6f));
                for (int i = 0; i <= NT; i++)
                {
                    if (j > 0) arc[i] += Vector3.Distance(pos[i, j], pos[i, j - 1]);
                    verts.Add(pos[i, j]);
                    uvs.Add(new Vector2(i / (float)NT * tiles, arc[i] / 6f));
                }
            }
            var tris = new List<int>();
            int W = NT + 1;
            for (int j = 0; j < rows - 1; j++)
            for (int i = 0; i < NT; i++)
            {
                int a = j * W + i, b = a + 1, c = a + W, d = c + 1;
                tris.AddRange(new[] { a, c, b, b, c, d });
            }
            // make the faces look into the cave
            var v0 = verts[tris[0]]; var n0 = Vector3.Cross(verts[tris[1]] - v0, verts[tris[2]] - v0);
            if (Vector3.Dot(n0, new Vector3(0, 6f, 0) - v0) < 0f)
                for (int k = 0; k < tris.Count; k += 3) (tris[k + 1], tris[k + 2]) = (tris[k + 2], tris[k + 1]);
            var mesh = new Mesh { name = "CaveShell" };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return SaveMesh(mesh, CaveDir + "/CaveShell.asset");
        }

        static GameObject BuildCavePuddles(Transform parent)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (var (c, rad) in cavePuddles)
            {
                int c0 = verts.Count;
                verts.Add(new Vector3(c.x, PuddleY, c.y));
                const int Seg = 28;
                for (int k = 0; k < Seg; k++)
                {
                    float a = k * Mathf.PI * 2f / Seg;
                    verts.Add(new Vector3(c.x + Mathf.Cos(a) * rad * 1.5f, PuddleY, c.y + Mathf.Sin(a) * rad * 1.5f));
                }
                for (int k = 0; k < Seg; k++) { tris.Add(c0); tris.Add(c0 + 1 + (k + 1) % Seg); tris.Add(c0 + 1 + k); }
            }
            var mesh = new Mesh { name = "CavePuddles" };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject("Puddles") { layer = 4 };     // Water: the mirror leaves itself out
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mesh, CaveDir + "/CavePuddles.asset");
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = M("M_CavePuddle");
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            go.AddComponent<PlanarReflection>();
            return go;
        }

        // ================================================================== placement
        static GameObject CPlace(GameObject prefab, Vector3 pos, float yaw, Transform parent, float scale = 1f, Col col = Col.None, bool ground = true,
                                 string name = null, bool isStatic = true, float sink = 0f)
        {
            if (prefab == null) { Debug.LogWarning("[Cave] missing prop"); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            if (ground) pos.y = CaveHeight(pos.x, pos.z) - sink;
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = Vector3.one * scale;
            if (name != null) go.name = name;
            if (col == Col.Mesh)
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    if (mf.GetComponent<Collider>() == null) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            if (isStatic) foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            return go;
        }

        static GameObject CP(string prop, Vector3 pos, float yaw, Transform parent, float scale = 1f, Col col = Col.None, bool ground = true,
                             string name = null, bool isStatic = true, float sink = 0f)
            => CPlace(CaveProp(prop), pos, yaw, parent, scale, col, ground, name, isStatic, sink);

        static void SwapMats(GameObject go, Dictionary<string, Material> map)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && map.TryGetValue(mats[i].name, out var to)) { mats[i] = to; changed = true; }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>Half the mushrooms swap blue and violet, so the two colours mix everywhere. Returns true when the
        /// main cap ended up violet.</summary>
        static bool TintMushroom(GameObject go, bool? violet = null)
        {
            bool flip = violet ?? Random.value < 0.45f;
            if (flip)
                SwapMats(go, new Dictionary<string, Material>
                {
                    { "M_MushCapBlue", M("M_MushCapViolet") }, { "M_MushCapViolet", M("M_MushCapBlue") },
                    { "M_MushGillBlue", M("M_MushGillViolet") }, { "M_MushGillViolet", M("M_MushGillBlue") },
                });
            return flip;
        }

        static readonly Color GlowBlue = new(0.3f, 0.72f, 1f), GlowViolet = new(0.85f, 0.38f, 1f);

        /// <summary>A light under the cap (the bounds' top part), breathing slowly, and seen in the haze.</summary>
        static Light CapLight(GameObject mush, Transform parent, bool violet, float intensity, float range, float under = 0.8f)
        {
            // the cap's centre: the middle of the top band of vertices (a bent stalk puts the bounds' centre beside it)
            var pts = mush.GetComponentsInChildren<MeshFilter>().SelectMany(mf => mf.sharedMesh.vertices.Select(v => mf.transform.TransformPoint(v))).ToList();
            float minY = pts.Min(q => q.y), maxY = pts.Max(q => q.y);
            float ty = Mathf.Lerp(minY, maxY, under);
            var band = pts.Where(q => Mathf.Abs(q.y - ty) < (maxY - minY) * 0.12f).ToList();
            if (band.Count == 0) band = pts;
            float cx = band.Average(q => q.x), cz = band.Average(q => q.z);
            var go = new GameObject(mush.name + "_Glow");
            go.transform.SetParent(parent);
            go.transform.position = new Vector3(cx, ty, cz);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = violet ? GlowViolet : GlowBlue; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
            go.AddComponent<UniversalAdditionalLightData>();
            go.AddComponent<FogLight>().scatter = 0.25f;
            var gp = go.AddComponent<GlowPulse>();
            gp.rate = Random.Range(0.1f, 0.22f); gp.depth = Random.Range(0.15f, 0.3f);
            return l;
        }

        static Light WarmLight(Transform parent, string name, Vector3 pos, float intensity, float range, bool flicker = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.7f, 0.38f); l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
            go.AddComponent<UniversalAdditionalLightData>();
            go.AddComponent<FogLight>().scatter = 0.6f;
            if (flicker) { var f = go.AddComponent<LanternFlicker>(); f.amount = 0.12f; f.speed = 5f; f.wobble = 0.005f; }
            return l;
        }

        // ================================================================== scene
        [MenuItem("Elder Tree/9j. Build Cave Arena (Fairy)")]
        public static string BuildCaveScene()
        {
            if (cues == null) BuildAudio();
            if (CaveProp("Mush_Giant_A") == null) return "Cave models missing: run Tools/Blender/build_cave.py first";
            Random.InitState(4127);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupPsxPipeline();

            // ---------------- the night beyond the hole, a purple cave ambient, violet haze
            var moon = MoonDir;
            var sky = M("M_CaveSky");
            sky.SetVector("_MoonDir", new Vector4(moon.x, moon.y, moon.z, 0));
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.46f, 0.66f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.4f, 0.58f);
            RenderSettings.ambientGroundColor = new Color(0.34f, 0.28f, 0.44f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.2f, 0.16f, 0.28f);
            RenderSettings.fogDensity = 0.0085f;
            RenderSettings.reflectionIntensity = 0.1f;

            var sysRoot = new GameObject("--- Systems ---").transform;
            var envRoot = new GameObject("--- Environment ---").transform;
            var mushRoot = new GameObject("--- Mushrooms ---").transform;
            var stoneRoot = new GameObject("--- Stone ---").transform;
            var plantRoot = new GameObject("--- Plants ---").transform;
            var lifeRoot = new GameObject("--- Wildlife ---").transform;
            var lightRoot = new GameObject("--- Lighting ---").transform;
            var actorRoot = new GameObject("--- Actors ---").transform;

            // ---------------- moonlight through the hole in the roof
            var moonGo = new GameObject("Moon");
            moonGo.transform.SetParent(lightRoot);
            moonGo.transform.rotation = Quaternion.LookRotation(-moon);
            var sun = moonGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.74f, 0.76f, 1f);
            sun.intensity = 2.4f;
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 1f;
            moonGo.AddComponent<UniversalAdditionalLightData>();
            RenderSettings.sun = sun;

            // ---------------- bounce: the moonbeam off the floor, the mushrooms' glow on the walls and up into the roof,
            // so the rock reads as a cave around the fight instead of an open night
            Light Fill(string n, Vector3 p, Color c, float intensity, float range)
            {
                var go = new GameObject(n);
                go.transform.SetParent(lightRoot);
                go.transform.position = p;
                var l = go.AddComponent<Light>();
                l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
                go.AddComponent<UniversalAdditionalLightData>();
                return l;
            }
            Fill("MoonBounce", MoonTarget + Vector3.up * 1.6f, new Color(0.72f, 0.66f, 1f), 9f, 16f);
            for (int i = 0; i < 10; i++)
            {
                var p = Polar(i * 36f + 12f, 25.5f) + Vector3.up * (3.5f + (i % 3) * 1.5f);
                Fill($"WallWash{i}", p, i % 2 == 0 ? GlowBlue : GlowViolet, 18f, 15f);
            }
            for (int i = 0; i < 5; i++)
                Fill($"RoofWash{i}", Polar(i * 72f + 40f, 16f) + Vector3.up * 13f, i % 2 == 0 ? GlowViolet : GlowBlue, 22f, 16f);

            // ---------------- puddles first (the floor dips under them), then floor, shell, water
            cavePuddles.Clear();
            foreach (var (x, z, r) in new[] { (-5.5f, -3f, 2.4f), (6.5f, -6f, 1.7f), (3f, 7.5f, 2.9f), (-8.5f, 8f, 1.6f), (11f, 3f, 2.1f), (-12f, -6.5f, 2.2f),
                                             (1.5f, -12.5f, 1.5f), (-2f, 13.5f, 1.4f), (13.5f, -9f, 1.3f), (-15f, 3.5f, 1.8f), (0f, -1f, 1.1f) })
                cavePuddles.Add((new Vector2(x, z), r));
            for (int i = 0, tries = 0; i < 9 && tries < 400; tries++)
            {
                var p = Polar(Random.Range(0f, 360f), Random.Range(21.5f, 27f));
                float r = Random.Range(1.2f, 2.3f);
                if (CaveBaseHeight(p.x, p.z) > 0.22f || cavePuddles.Any(q => Vector2.Distance(q.c, new Vector2(p.x, p.z)) < q.r + r + 1.5f)) continue;
                cavePuddles.Add((new Vector2(p.x, p.z), r));
                i++;
            }
            BuildCaveGround(envRoot, out var groundCol);
            float H(float x, float z) => CaveHeight(x, z);

            var shellMesh = BuildCaveShellMesh();
            var shell = new GameObject("CaveShell");
            shell.transform.SetParent(envRoot, false);
            shell.AddComponent<MeshFilter>().sharedMesh = shellMesh;
            var smr = shell.AddComponent<MeshRenderer>();
            smr.sharedMaterial = M("M_CaveRock");
            smr.shadowCastingMode = ShadowCastingMode.TwoSided;      // the roof keeps the moon out except through the hole
            shell.AddComponent<MeshCollider>().sharedMesh = shellMesh;
            shell.isStatic = true;
            var starGo = new GameObject("StarLayer");
            starGo.transform.SetParent(shell.transform, false);
            starGo.AddComponent<MeshFilter>().sharedMesh = shellMesh;
            var stmr = starGo.AddComponent<MeshRenderer>();
            stmr.sharedMaterial = M("M_CaveStars");
            stmr.shadowCastingMode = ShadowCastingMode.Off; stmr.receiveShadows = false;
            starGo.isStatic = true;
            BuildCavePuddles(envRoot);
            Physics.SyncTransforms();

            var taken = new List<(Vector3 p, float r)>();
            bool Free(Vector3 p, float r) => taken.All(t => new Vector2(t.p.x - p.x, t.p.z - p.z).magnitude > t.r + r)
                                             && cavePuddles.All(q => Vector2.Distance(q.c, new Vector2(p.x, p.z)) > q.r * 1.2f + r * 0.5f);
            void Take(Vector3 p, float r) => taken.Add((p, r));
            bool Ceiling(Vector3 p, out RaycastHit hit) => Physics.Raycast(new Vector3(p.x, 3f, p.z), Vector3.up, out hit, 60f, 1, QueryTriggerInteraction.Ignore) && hit.collider.gameObject == shell;
            bool UnderHole(Vector3 p, float pad) => Vector2.Distance(new Vector2(p.x, p.z), OculusXZ) < OculusR + pad;

            // ---------------- giant mushrooms around the fight, their caps leaning out over it
            int giants = 0, talls = 0, meds = 0, clusters = 0, lights = 0;
            foreach (var (a, r, kind, s) in new[] { (18f, 25f, "Mush_Giant_A", 1.0f), (80f, 26.5f, "Mush_Giant_B", 1.1f), (140f, 24.5f, "Mush_Giant_A", 0.9f),
                                                    (196f, 26f, "Mush_Giant_B", 1.0f), (252f, 25f, "Mush_Giant_A", 1.12f), (318f, 25.5f, "Mush_Giant_B", 0.95f) })
            {
                var p = Polar(a + Random.Range(-5f, 5f), r + Random.Range(-1f, 1f));
                var g = CP(kind, p, Random.Range(0f, 360f), mushRoot, s, Col.None, true, $"{kind}_{giants++}", sink: 0.1f);
                bool violet = TintMushroom(g);
                TrunkCollider(g, 0.6f * s, 9f);
                CapLight(g, lightRoot, violet, 14f, 15f, kind == "Mush_Giant_B" ? 0.36f : 0.84f).GetComponent<FogLight>().scatter = 0.15f; lights++;
                Take(p, 4f * s);
            }
            // ---------------- two gnome houses facing the fight
            var houses = new List<GameObject>();
            foreach (var (a, r, kind) in new[] { (112f, 25f, "MushHouse_A"), (286f, 24.5f, "MushHouse_B") })
            {
                var p = Polar(a, r);
                var hgo = CP(kind, p, Mathf.Atan2(-p.x, -p.z) * Mathf.Rad2Deg, mushRoot, 1f, Col.Mesh, true, kind, sink: 0.1f);
                houses.Add(hgo);
                foreach (var e in new[] { "WindowL", "WindowR" })
                {
                    var t = Find(hgo, e);
                    if (t != null) { WarmLight(lightRoot, $"{kind}_{e}", t.position, 1.3f, 4.5f); lights++; }
                }
                var lamp = Find(hgo, "Lamp");
                if (lamp != null) { WarmLight(lightRoot, $"{kind}_Lamp", lamp.position, 1.8f, 5.5f); lights++; }
                Take(p, 4.2f);
            }
            for (int i = 0; i < 4000 && talls < 12; i++)
            {
                var p = Polar(Random.Range(0f, 360f), Random.Range(21.2f, 30f));
                if (!Free(p, 1.8f)) continue;
                string kind = new[] { "Mush_Tall_A", "Mush_Tall_B", "Mush_Tall_C" }[Random.Range(0, 3)];
                float s = Random.Range(0.85f, 1.3f);
                var g = CP(kind, p, Random.Range(0f, 360f), mushRoot, s, Col.None, true, $"{kind}_{talls++}", sink: 0.05f);
                bool violet = TintMushroom(g);
                TrunkCollider(g, 0.3f * s, 4f);
                CapLight(g, lightRoot, violet, 7f, 9f, 0.78f); lights++;
                Take(p, 2.2f * s);
            }
            for (int i = 0; i < 6000 && meds < 34; i++)
            {
                var p = Polar(Random.Range(0f, 360f), Random.Range(20.8f, 32f));
                if (!Free(p, 0.8f)) continue;
                string kind = new[] { "Mush_Med_A", "Mush_Med_B", "Mush_Med_C", "Mush_Twin" }[Random.Range(0, 4)];
                var g = CP(kind, p, Random.Range(0f, 360f), mushRoot, Random.Range(0.8f, 1.35f), Col.None, true, $"{kind}_{meds++}", sink: 0.03f);
                bool violet = TintMushroom(g);
                if (meds % 3 == 0) { CapLight(g, lightRoot, violet, 2.5f, 4.5f, 0.75f); lights++; }
                Take(p, 1f);
            }
            for (int i = 0; i < 6000 && clusters < 90; i++)
            {
                bool inside = clusters < 22;
                var p = Polar(Random.Range(0f, 360f), inside ? Random.Range(4f, 18.5f) : Random.Range(20.6f, 33f));
                if (!Free(p, 0.5f)) continue;
                string kind = inside ? (Random.value < 0.6f ? "Mush_Tiny" : "Mush_Cluster_B")
                                     : new[] { "Mush_Cluster_A", "Mush_Cluster_B", "Mush_Cluster_C", "Mush_Tiny" }[Random.Range(0, 4)];
                var g = CP(kind, p, Random.Range(0f, 360f), mushRoot, Random.Range(0.8f, 1.4f), Col.None, true, null, true, 0.02f);
                TintMushroom(g);
                clusters++;
            }
            // the fairy ring: little mushrooms all the way round the edge of the fight
            int ring = 0;
            for (float a = 0f; a < 360f; a += 360f / 96f)
            {
                var p = Polar(a + Random.Range(-1.2f, 1.2f), 19.5f + Random.Range(-0.3f, 0.3f));
                var g = CP(Random.value < 0.65f ? "Mush_Tiny" : "Mush_Cluster_B", p, Random.Range(0f, 360f), mushRoot, Random.Range(0.9f, 1.3f), Col.None, true, $"FairyRing{ring++}", true, 0.02f);
                TintMushroom(g);
            }

            // ---------------- stone: columns, stalagmites, boulders, stalactites from the roof
            int columns = 0, mites = 0, boulders = 0, tites = 0, shelves = 0, threads = 0;
            for (int i = 0; i < 400 && columns < 4; i++)
            {
                var p = Polar(i * 97f % 360f + Random.Range(-10f, 10f), Random.Range(27.5f, 30.5f));
                if (!Free(p, 2.5f) || !Ceiling(p, out var hit)) continue;
                float gy = H(p.x, p.z);
                var c = CP("Column_A", p, Random.Range(0f, 360f), stoneRoot, 1f, Col.Mesh, true, $"Column{columns++}", sink: 0.2f);
                c.transform.localScale = new Vector3(1.15f, (hit.point.y - gy + 0.8f) / 11f, 1.15f);
                Take(p, 2.6f);
            }
            for (int i = 0; i < 3000 && mites < 22; i++)
            {
                var p = Polar(Random.Range(0f, 360f), Random.Range(21f, 33f));
                if (!Free(p, 1f)) continue;
                string kind = new[] { "Stalagmite_A", "Stalagmite_B", "Stalagmite_C", "Stalagmite_C" }[Random.Range(0, 4)];
                CP(kind, p, Random.Range(0f, 360f), stoneRoot, Random.Range(0.8f, 1.5f), p.magnitude < 27f ? Col.Mesh : Col.None, true, $"{kind}_{mites++}", sink: 0.1f);
                Take(p, 1.2f);
            }
            for (int i = 0; i < 3000 && boulders < 20; i++)
            {
                var p = Polar(Random.Range(0f, 360f), Random.Range(21f, 34f));
                if (!Free(p, 1.4f)) continue;
                string kind = new[] { "Boulder_A", "Boulder_B", "Boulder_C" }[Random.Range(0, 3)];
                CP(kind, p, Random.Range(0f, 360f), stoneRoot, Random.Range(0.7f, 1.3f), p.magnitude < 27f ? Col.Mesh : Col.None, true, $"{kind}_{boulders++}", sink: 0.3f);
                Take(p, 1.8f);
            }
            for (int i = 0; i < 4000 && tites < 80; i++)
            {
                var p = Polar(Random.Range(0f, 360f), Mathf.Sqrt(Random.value) * 33f);
                if (UnderHole(p, 2.5f) || !Ceiling(p, out var hit)) continue;
                string kind = Random.value < 0.25f ? "Stalactite_C" : Random.value < 0.5f ? "Stalactite_A" : "Stalactite_B";
                float len = kind == "Stalactite_C" ? 7f : kind == "Stalactite_A" ? 4.5f : 2.6f;
                float s = Random.Range(0.7f, 1.6f);
                float r = p.magnitude;
                if (r < 22f) s = Mathf.Min(s, (hit.point.y - 13.5f) / len);        // keep the air over the fight clear for her
                if (s < 0.45f) continue;
                CP(kind, hit.point + Vector3.up * 0.3f, Random.Range(0f, 360f), stoneRoot, s, Col.None, false, $"{kind}_{tites++}");
            }
            // bracket fungi up the walls
            for (int i = 0; i < 1200 && shelves < 64; i++)
            {
                float a = Random.Range(0f, 360f);
                var o = new Vector3(0, Random.Range(1.2f, 8f), 0);
                var d = Polar(a, 1f);
                if (!Physics.Raycast(o, d, out var hit, 60f, 1, QueryTriggerInteraction.Ignore) || hit.collider.gameObject != shell) continue;
                var nrm = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
                if (nrm.sqrMagnitude < 0.2f) continue;
                var g = CP(Random.value < 0.5f ? "Mush_Shelf_A" : "Mush_Shelf_B", hit.point - nrm.normalized * 0.15f, 0f, mushRoot, Random.Range(0.9f, 1.8f), Col.None, false, $"Shelf{shelves++}");
                g.transform.rotation = Quaternion.LookRotation(nrm.normalized, Vector3.up);
                bool sv = TintMushroom(g);
                if (shelves % 3 == 0)
                {
                    // a patch of the wall lit by the fungus on it
                    var lgo = new GameObject($"Shelf{shelves}_Glow");
                    lgo.transform.SetParent(lightRoot);
                    lgo.transform.position = hit.point + nrm.normalized * 1.2f + Vector3.up * 0.4f;
                    var l = lgo.AddComponent<Light>();
                    l.type = LightType.Point; l.color = sv ? GlowViolet : GlowBlue; l.intensity = 5f; l.range = 6f; l.shadows = LightShadows.None;
                    lgo.AddComponent<UniversalAdditionalLightData>();
                    lgo.AddComponent<GlowPulse>().rate = Random.Range(0.1f, 0.2f);
                }
            }
            // glow-worm threads hanging from the roof: they double the stars from below
            for (int i = 0; i < 4000 && threads < 150; i++)
            {
                var p = Polar(Random.Range(0f, 360f), Mathf.Sqrt(Random.value) * 33f);
                if (UnderHole(p, 1.5f) || !Ceiling(p, out var hit) || hit.point.y < 8f) continue;
                CP(Random.value < 0.5f ? "GlowThreads_A" : "GlowThreads_B", hit.point + Vector3.up * 0.2f, Random.Range(0f, 360f), plantRoot, Random.Range(0.5f, 1.0f), Col.None, false, null);
                threads++;
            }

            // ---------------- flowers, ferns and tufts (the forest's, tinted blue-teal)
            int flowers = 0, ferns = 0, tufts = 0;
            for (int i = 0; i < 12000 && flowers < 320; i++)
            {
                Vector3 p;
                if (flowers < 120)
                {
                    // around the puddles' rims
                    var (c, rad) = cavePuddles[Random.Range(0, cavePuddles.Count)];
                    var off = Polar(Random.Range(0f, 360f), rad * Random.Range(1.15f, 1.7f));
                    p = new Vector3(c.x + off.x, 0, c.y + off.z);
                }
                else p = Polar(Random.Range(0f, 360f), flowers < 170 ? Random.Range(3f, 19f) : Random.Range(20.4f, 33f));
                if (!Free(p, 0.25f)) continue;
                string kind = new[] { "Flowers_Bell", "Flowers_Star", "Flowers_Spike", "Flowers_Bed", "Flowers_Bell" }[Random.Range(0, 5)];
                CP(kind, p, Random.Range(0f, 360f), plantRoot, Random.Range(0.8f, 1.4f));
                flowers++;
            }
            var frondMap = new Dictionary<string, Material> { { "M_JungleFronds", M("M_CaveFronds") } };
            for (int i = 0; i < 8000 && ferns < 130; i++)
            {
                var p = Polar(Random.Range(0f, 360f), Random.Range(20.6f, 33f));
                if (!Free(p, 0.5f)) continue;
                var f = CPlace(ForestProp(Random.value < 0.5f ? "Fern_A" : "Fern_B"), p, Random.Range(0f, 360f), plantRoot, Random.Range(0.7f, 1.2f));
                SwapMats(f, frondMap);
                ferns++;
            }
            for (int i = 0; i < 8000 && tufts < 170; i++)
            {
                var p = Polar(Random.Range(0f, 360f), tufts < 70 ? Random.Range(3f, 19f) : Random.Range(19.5f, 32f));
                if (!Free(p, 0.3f)) continue;
                var f = CPlace(ForestProp(Random.value < 0.5f ? "Tuft_A" : "Tuft_B"), p, Random.Range(0f, 360f), plantRoot, Random.Range(0.8f, 1.3f));
                SwapMats(f, frondMap);
                tufts++;
            }

            // ---------------- gnomes and frogs
            var hats = new[] { "M_GnomeHat", "M_GnomeHatBlue", "M_GnomeHatTeal", "M_GnomeHatCrimson" };
            var coats = new[] { "M_GnomeCoat", "M_GnomeCoatPlum", "M_GnomeCoatSlate", "M_GnomeCoatMoss" };
            int gnomes = 0;
            GameObject SpawnGnome(string kind, Vector3 p, float yaw, bool walk, float roam)
            {
                var g = CP(kind, p, yaw, lifeRoot, Random.Range(1.0f, 1.15f), Col.None, true, $"{kind}_{gnomes}", false);
                SwapMats(g, new Dictionary<string, Material> { { "M_GnomeHat", M(hats[gnomes % hats.Length]) }, { "M_GnomeCoat", M(coats[(gnomes * 3 + 1) % coats.Length]) } });
                var gc = g.AddComponent<Gnome>();
                gc.canWalk = walk; gc.roam = roam;
                // the cave's glow on them: a soft fill that walks with the gnome, so it reads away from the lit spots
                var fill = new GameObject("GnomeFill");
                fill.transform.SetParent(g.transform, false);
                fill.transform.position = g.transform.position + g.transform.forward * 0.7f + Vector3.up * 0.9f;
                var fl = fill.AddComponent<Light>();
                fl.type = LightType.Point; fl.color = new Color(0.78f, 0.7f, 1f); fl.intensity = 1.1f; fl.range = 2.4f; fl.shadows = LightShadows.None;
                fill.AddComponent<UniversalAdditionalLightData>();
                var lt = Find(g, "Lantern");
                if (lt != null)
                {
                    var l = WarmLight(lt, "LanternLight", lt.position, kind == "Gnome_Sit" ? 0.4f : 1.0f, kind == "Gnome_Sit" ? 1.2f : 3.5f);
                    l.transform.SetParent(lt, true);
                }
                gnomes++;
                return g;
            }
            float Face(Vector3 p) => Mathf.Atan2(-p.x, -p.z) * Mathf.Rad2Deg;
            foreach (var h in houses)
            {
                var hp = h.transform.position;
                var fwd = h.transform.forward;
                var side = Vector3.Cross(Vector3.up, fwd);
                var p = hp + fwd * 2.4f + side * 1.1f;
                SpawnGnome("Gnome_Lantern", p, Face(p) + Random.Range(-30f, 30f), true, 1.6f);
                p = hp + fwd * 2.2f - side * 1.4f;
                SpawnGnome("Gnome_Sit", p, Face(p) + Random.Range(-15f, 15f), false, 0f);
            }
            foreach (var a in new[] { 40f, 165f, 228f, 342f })
            {
                var p = Polar(a + Random.Range(-6f, 6f), Random.Range(20.7f, 21.4f));
                SpawnGnome(a < 200f ? "Gnome_Sit" : "Gnome_Pick", p, Face(p) + Random.Range(-20f, 20f), a >= 200f, 1.5f);
            }
            for (int i = 0, tries = 0; i < 5 && tries < 400; tries++)
            {
                var p = Polar(Random.Range(0f, 360f), Random.Range(22f, 27f));
                if (!Free(p, 0.6f)) continue;
                SpawnGnome(i % 2 == 0 ? "Gnome_Pick" : "Gnome_Lantern", p, Random.Range(0f, 360f), true, 2.2f);
                i++;
            }
            int frogs = 0;
            var frogMap = new Dictionary<string, Material> { { "M_FrogSkin", M("M_CaveFrog") } };
            for (int i = 0; i < 800 && frogs < 16; i++)
            {
                var (c, rad) = cavePuddles[frogs % cavePuddles.Count];
                var off = Polar(Random.Range(0f, 360f), rad * Random.Range(1.1f, 1.5f));
                var p = new Vector3(c.x + off.x, 0, c.y + off.z);
                if (!Free(p, 0.4f)) continue;
                var f = CPlace(ForestProp(frogs % 3 == 2 ? "Frog_Dart" : "Frog_Green"), p, Random.Range(0f, 360f), lifeRoot, Random.Range(1.0f, 1.4f), Col.None, true, $"Frog{frogs}", false);
                SwapMats(f, frogMap);
                f.AddComponent<Frog>().roam = 2.5f;
                frogs++;
            }

            // ---------------- spores drifting up through the cave, motes turning in the moonbeam
            var fxRoot = new GameObject("Spores").transform;
            fxRoot.SetParent(lifeRoot);
            int swarms = 0;
            foreach (var (a, r, h, violet) in new[] { (20f, 24f, 2f, false), (85f, 25f, 2.5f, true), (150f, 23f, 1.8f, false), (210f, 25f, 2.2f, true),
                                                      (265f, 24f, 2f, false), (330f, 23f, 2.4f, true), (60f, 10f, 1.2f, false), (240f, 12f, 1.4f, true), (0f, 0f, 3f, false) })
            {
                var p = Polar(a, r);
                CaveSwarm(fxRoot, new Vector3(p.x, H(p.x, p.z) + h, p.z), r < 1f ? 14f : 7f, r < 1f ? 10f : 6f, M(violet ? "M_FX_CaveSporeViolet" : "M_FX_CaveSporeBlue"), $"Spores{swarms++}");
            }
            MoonMotes(fxRoot, moon);

            // ---------------- keep the fight in: an invisible ring just outside the fairy ring
            var bounds = new GameObject("ArenaBounds").transform;
            bounds.SetParent(envRoot);
            for (int i = 0; i < 40; i++)
            {
                float a = i * 9f;
                var b = new GameObject($"Bound{i}");
                b.transform.SetParent(bounds);
                b.transform.SetPositionAndRotation(Polar(a, CaveWall + 0.3f) + Vector3.up * 3f, Quaternion.Euler(0, Tangent(a), 0));
                b.AddComponent<BoxCollider>().size = new Vector3(3.4f, 10f, 0.6f);
            }

            // ---------------- a low violet mist, and the haze the moonbeam cuts through
            var gfGo = new GameObject("GroundFog");
            gfGo.transform.SetParent(lightRoot);
            var gf = gfGo.AddComponent<GroundFog>();
            gf.center = Vector3.zero; gf.size = 56f; gf.resolution = 84; gf.bottom = 0.05f; gf.top = 0.8f; gf.layers = 8;
            gf.ground = groundCol;
            gf.material = M("M_CaveGroundFog");

            var volGo = new GameObject("PostProcess");
            volGo.transform.SetParent(lightRoot);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = BuildCavePostProfile();
            volGo.AddComponent<PSXGlobals>();
            volGo.AddComponent<ShadowRange>().distance = 70f;

            // ---------------- player, camera, HUD: the Fairy is this stage's boss
            var rig = BuildCommon(sysRoot, envRoot, actorRoot, new Vector3(0, 0, -11f), 0f, false, "Fairy");
            // a soft fill riding with the camera: the player (and whatever is in front of you) never goes black against the glow
            var camGo = GameObject.FindWithTag("MainCamera");
            var camT = camGo != null ? camGo.transform : null;
            if (camT != null)
            {
                var fillGo = new GameObject("ViewFill");
                fillGo.transform.SetParent(camT, false);
                fillGo.transform.localPosition = new Vector3(0.4f, 0.8f, 0.6f);
                var vl = fillGo.AddComponent<Light>();
                vl.type = LightType.Point; vl.color = new Color(0.86f, 0.82f, 1f); vl.intensity = 6f; vl.range = 9f; vl.shadows = LightShadows.None;
                fillGo.AddComponent<UniversalAdditionalLightData>();
            }
            rig.hud.titleKicker = "Lair of the Fairy";
            rig.hud.titleMain = "STARLIT";
            rig.hud.titleSub = "GROTTO";
            foreach (var b in Object.FindObjectsByType<Enemies.FairyBoss>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { b.arenaCenter = Vector3.zero; EditorUtility.SetDirty(b); }
            var am = Object.FindAnyObjectByType<Audio.AudioManager>(FindObjectsInactive.Include);
            if (am != null) { am.ambienceLoop = SoundPicks.CaveAmbience(); EditorUtility.SetDirty(am); }

            EditorSceneManager.SaveScene(scene, CaveScenePath);
            RegisterBuildScenes();
            return $"Cave arena built: {giants} giant mushrooms, {talls} tall, {meds} medium, {clusters} clusters, {ring} in the fairy ring, {houses.Count} houses, " +
                   $"{columns} columns, {mites} stalagmites, {tites} stalactites, {boulders} boulders, {shelves} shelf fungi, {threads} glow threads, " +
                   $"{flowers} flowers, {ferns} ferns, {tufts} tufts, {gnomes} gnomes, {frogs} frogs, {cavePuddles.Count} puddles, {lights} glow lights, {swarms} spore swarms";
        }

        static void CaveSwarm(Transform parent, Vector3 pos, float radius, float rate, Material mat, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.loop = true; m.playOnAwake = true; m.prewarm = true;
            m.duration = 10f;
            m.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            m.gravityModifier = -0.004f;                   // spores rise, very slowly
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.maxParticles = 160;
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius; sh.scale = new Vector3(1f, 0.4f, 1f);
            var nz = ps.noise; nz.enabled = true; nz.strength = 0.3f; nz.frequency = 0.2f; nz.scrollSpeed = 0.08f; nz.damping = true; nz.quality = ParticleSystemNoiseQuality.Medium;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.35f, 0.4f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
        }

        /// <summary>Dust turning slowly in the moonbeam: a column along the light, from the hole to the floor.</summary>
        static void MoonMotes(Transform parent, Vector3 moon)
        {
            var top = new Vector3(OculusXZ.x, CaveH, OculusXZ.y);
            var go = new GameObject("MoonMotes");
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation((top + MoonTarget) * 0.5f, Quaternion.FromToRotation(Vector3.up, moon));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.loop = true; m.playOnAwake = true; m.prewarm = true;
            m.duration = 10f;
            m.startLifetime = new ParticleSystem.MinMaxCurve(7f, 12f);
            m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.maxParticles = 260;
            var em = ps.emission; em.rateOverTime = 24f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(OculusR * 1.6f, (top - MoonTarget).magnitude, OculusR * 1.6f);
            var nz = ps.noise; nz.enabled = true; nz.strength = 0.12f; nz.frequency = 0.15f; nz.scrollSpeed = 0.05f; nz.damping = true;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f); vel.y = new ParticleSystem.MinMaxCurve(-0.06f, -0.01f); vel.z = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.2f), new GradientAlphaKey(0.8f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = M("M_FX_MoonMote");
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
        }

        static VolumeProfile BuildCavePostProfile()
        {
            string path = Root + "/Settings/CavePostProfile.asset";
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
            // everything leans violet: violet shadows, lavender highlights, a lavender bloom off every glow
            var tone = Add<Tonemapping>(); tone.mode.Override(TonemappingMode.ACES);
            var bloom = Add<Bloom>(); bloom.threshold.Override(0.75f); bloom.intensity.Override(1.1f); bloom.scatter.Override(0.75f);
            bloom.tint.Override(new Color(0.92f, 0.86f, 1f));
            var ca = Add<ColorAdjustments>(); ca.postExposure.Override(1.2f); ca.contrast.Override(10f); ca.saturation.Override(0f);
            ca.colorFilter.Override(new Color(1f, 0.95f, 1f));
            var split = Add<SplitToning>(); split.shadows.Override(new Color(0.52f, 0.44f, 0.62f)); split.highlights.Override(new Color(0.56f, 0.55f, 0.62f)); split.balance.Override(0f);
            var smh = Add<ShadowsMidtonesHighlights>();
            smh.shadows.Override(new Vector4(1f, 0.97f, 1.03f, 0.05f));
            smh.midtones.Override(new Vector4(1f, 0.98f, 1.02f, 0.02f));
            smh.highlights.Override(new Vector4(1f, 1f, 1.02f, 0f));
            var wb = Add<WhiteBalance>(); wb.temperature.Override(2f); wb.tint.Override(8f);
            var vig = Add<Vignette>(); vig.intensity.Override(0.28f); vig.smoothness.Override(0.45f); vig.color.Override(new Color(0.05f, 0.03f, 0.08f));
            var grain = Add<FilmGrain>(); grain.type.Override(FilmGrainLookup.Medium2); grain.intensity.Override(0.24f); grain.response.Override(0.7f);
            // violet haze: the moonbeam shows as a shaft, the mushroom lights bloom in it
            var vf = Add<VolumetricFog>();
            vf.enabled.Override(true);
            vf.density.Override(0.009f);
            vf.baseHeight.Override(0f);
            vf.heightFalloff.Override(14f);
            vf.maxDistance.Override(50f);
            vf.steps.Override(32);
            vf.anisotropy.Override(0.55f);
            vf.noiseScale.Override(0.06f);
            vf.noiseStrength.Override(0.5f);
            vf.wind.Override(new Vector3(0.05f, 0.03f, 0.04f));
            vf.albedo.Override(new Color(0.88f, 0.82f, 1f));
            vf.ambient.Override(new Color(0.09f, 0.075f, 0.12f));
            vf.mainLightScatter.Override(0.5f);
            vf.localLightScatter.Override(1.0f);
            vf.groundClear.Override(0.2f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
