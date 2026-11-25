using System.Collections.Generic;
using System.IO;
using System.Linq;
using ElderTree.FX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static ElderTree.EditorTools.TextureForge;

namespace ElderTree.EditorTools
{
    /// <summary>
    /// The Crocodile's arena: "Drowned Garrison", a night swamp with a WWII-era military outpost.
    /// Black water with duckweed, a mud clearing ringed by sandbags, barbed wire and hedgehogs, gun nests,
    /// an AA gun, tents, a pillbox, a watchtower searchlight and floodlights; huge bald cypresses dripping
    /// Spanish moss close off the view. Sickly green sky, a white moon, Unity fog + ray-marched volumetric
    /// haze + OoT-remake-style ground fog that bodies and explosions push around.
    /// Models: Tools/Blender/build_swamp.py -> Art/Models/Swamp. Menu: Elder Tree/9a-9d.
    /// </summary>
    public static partial class ElderTreeBuilder
    {
        public const string SwampScenePath = Root + "/Scenes/Swamp_Crocodile.unity";
        public const string SwampDir = Root + "/Art/Models/Swamp";
        const string RendererDataPath = "Assets/Settings/PC_Renderer.asset";

        static readonly string[] SwampSlotMats =
        {
            "M_SwampBark", "M_SwampFlora", "M_LilyFlower", "M_Sandbag", "M_Canvas", "M_OliveMetal", "M_Concrete", "M_Mud",
            "M_Iron", "M_Planks", "M_Rope", "M_DarkWood", "M_LampLens", "M_LanternGlow"
        };

        static GameObject SwampProp(string n) => AssetDatabase.LoadAssetAtPath<GameObject>($"{SwampDir}/{n}.fbx");
        /// <summary>GLSL-style smoothstep threshold (Mathf.SmoothStep interpolates instead).</summary>
        static float Sst(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }
        static float Gr(int x, int y, int seed) => Hash(x, y, seed) - 0.5f;

        // ================================================================== art
        [MenuItem("Elder Tree/9a. Build Swamp Art (textures, materials, imports)")]
        public static string BuildSwampArt()
        {
            var bark = SavePsx("T_SwampBark", 128, (u, v, x, y) =>
            {
                float warp = Fbm(u, v, 4, 2, 3, 201) - 0.5f;
                float strips = Mathf.Abs(Mathf.Sin((u * 9f + warp * 2.6f) * Mathf.PI));
                var c = Mx(Hex("#2A2521"), Hex("#6E665A"), 0.3f + 0.5f * Fbm(u, v, 16, 4, 3, 202) + 0.22f * Gr(x, y, 21));
                c *= 1f - 0.6f * Sst(0.32f, 0f, strips);
                c = Mx(c, Hex("#80846F"), Sst(0.68f, 0.8f, Fbm(u, v, 10, 10, 3, 203)) * 0.4f);
                return Mx(c, Hex("#2C3B20"), Sst(0.55f, 0.72f, Fbm(u, v, 4, 4, 3, 204)) * 0.75f);
            });
            var flora = SavePsx("T_SwampFlora", 256, Flora, mips: false, alpha: true);
            var sand = SavePsx("T_Sandbag", 128, (u, v, x, y) =>
            {
                float weave = (Mathf.Sin(x * 1.6f) * Mathf.Sin(y * 1.6f)) * 0.5f + 0.5f;
                var c = Mx(Hex("#4E4733"), Hex("#857B58"), 0.35f + 0.4f * Fbm(u, v, 8, 8, 3, 211) + 0.25f * weave + 0.15f * Gr(x, y, 22));
                c = Mx(c, Hex("#2C281C"), Sst(0.58f, 0.75f, Fbm(u, v, 5, 5, 3, 212)) * 0.7f);   // wet mud stains
                bool seam = (y % 64) < 2;
                return seam ? c * 0.55f : c;
            });
            var canvas = SavePsx("T_Canvas", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#2E321D"), Hex("#50553A"), 0.4f + 0.4f * Fbm(u, v, 6, 6, 3, 221) + 0.16f * Gr(x, y, 23));
                c = Mx(c, Hex("#1C1F12"), Sst(0.6f, 0.78f, Fbm(u, v, 4, 8, 3, 222)) * 0.6f);   // damp streaks
                bool seam = (x % 32) < 1;
                return seam ? c * 0.6f : c;
            });
            var olive = SavePsx("T_OliveMetal", 64, (u, v, x, y) =>
            {
                var c = Mx(Hex("#333A22"), Hex("#59613C"), 0.4f + 0.4f * Fbm(u, v, 8, 8, 3, 231) + 0.2f * Gr(x, y, 24));
                float chip = Fbm(u, v, 12, 12, 3, 232);
                c = Mx(c, Hex("#211D18"), Sst(0.66f, 0.7f, chip));
                return Mx(c, Hex("#5C3018"), Sst(0.6f, 0.78f, Fbm(u, v, 5, 5, 3, 233)) * 0.55f);
            });
            var concrete = SavePsx("T_Concrete", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#45463F"), Hex("#77786E"), 0.4f + 0.35f * Fbm(u, v, 12, 12, 4, 241) + 0.2f * Gr(x, y, 25));
                c = Mx(c, Hex("#2F3129"), Sst(0.5f, 0.75f, Fbm(u, v, 16, 2, 3, 242)) * 0.6f);   // rain streaks
                Worley(u, v, 6, 243, out float f1, out float f2, out _);
                c *= 1f - 0.5f * Sst(0.04f, 0f, f2 - f1);                                  // cracks
                return Mx(c, Hex("#2F3D24"), Sst(0.6f, 0.75f, Fbm(u, v, 4, 4, 3, 244)) * 0.7f);
            });
            var mud = SavePsx("T_SwampMud", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#17130E"), Hex("#3A3125"), 0.35f + 0.45f * Fbm(u, v, 8, 8, 4, 251) + 0.25f * Gr(x, y, 26));
                float moss = Fbm(u, v, 4, 4, 4, 252);
                c = Mx(c, Mx(Hex("#243218"), Hex("#3E5027"), Hash(x, y, 27)), Sst(0.52f, 0.62f, moss) * 0.85f);
                c = Mx(c, Hex("#0B0A08"), Sst(0.62f, 0.75f, Fbm(u, v, 6, 6, 3, 253)) * 0.6f);  // wet dark patches
                if (Hash(x, y, 28) > 0.985f) c = Hex("#5A4A2C");                                   // dead leaf specks
                return c;
            });
            var weed = Save("T_Duckweed", 128, 128, (x, y) =>
            {
                float u = x / 128f, v = y / 128f;
                float blob = Fbm(u, v, 3, 3, 4, 261) * 0.8f + Fbm(u, v, 24, 24, 2, 262) * 0.2f;
                float leaf = 0.65f + 0.7f * Hash(x / 2, y / 2, 263);
                return new Color(leaf, leaf * (0.9f + 0.2f * Hash(x, y, 264)), leaf * 0.8f, blob);
            });
            var wimp = (TextureImporter)AssetImporter.GetAtPath(TexDir + "/T_Duckweed.png");
            wimp.filterMode = FilterMode.Point; wimp.alphaIsTransparency = false; wimp.textureCompression = TextureImporterCompression.Uncompressed;
            wimp.SaveAndReimport();

            Lit("M_SwampBark", Color.white, bark);
            var fl = Lit("M_SwampFlora", Color.white, flora, null, 0f, 1f, null, false);   // albedo alpha = cutout, not smoothness
            Cutout(fl);
            var lily = Lit("M_LilyFlower", Hex("#D7DCCF"), null);
            lily.SetColor("_EmissionColor", Hex("#D7DCCF") * 0.18f);
            Lit("M_Sandbag", Color.white, sand);
            Lit("M_Canvas", Color.white, canvas);
            Lit("M_OliveMetal", Color.white, olive);
            Lit("M_Concrete", Color.white, concrete);
            Lit("M_Mud", Color.white, mud, null, 0f, 1f, new Vector2(2, 2));
            Lit("M_SwampGround", Color.white, mud);
            var lens = Lit("M_LampLens", Hex("#FFF3D8"), null);
            lens.SetColor("_EmissionColor", Hex("#FFF3D8") * 6f);
            lens.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            var water = AssetOf("M_SwampWater", "ElderTree/SwampWater");
            water.SetTexture("_WeedTex", weed);
            water.SetFloat("_GlintStrength", 1.1f);
            water.SetFloat("_GlintPower", 420f);
            water.SetFloat("_LightGlint", 0.45f);
            var gfog = AssetOf("M_GroundFog", "ElderTree/GroundFog");
            gfog.SetColor("_Color", new Color(0.42f, 0.55f, 0.45f));
            gfog.SetColor("_Ambient", new Color(0.045f, 0.075f, 0.05f));
            gfog.SetFloat("_Density", 0.09f);     // per shell: 14 shells seen at a grazing angle add up fast
            gfog.SetFloat("_MoonScatter", 0.28f);
            gfog.SetFloat("_Height", 1.35f);
            gfog.SetFloat("_SoftDistance", 0.9f);   // wide fade where shells meet the (vertex-snapped) ground

            var sky = AssetOf("M_SwampSky", "ElderTree/NightSky");
            sky.SetColor("_ZenithColor", new Color(0.03f, 0.065f, 0.04f));
            sky.SetColor("_HorizonColor", new Color(0.2f, 0.36f, 0.22f));
            sky.SetColor("_GroundColor", Hex("#050805"));
            sky.SetColor("_CloudColor", Hex("#173A20"));
            sky.SetColor("_MoonColor", Hex("#F6F8F2"));
            sky.SetFloat("_MoonSize", 0.05f);
            sky.SetFloat("_HaloStrength", 1.1f);
            sky.SetFloat("_CloudStrength", 0.75f);
            sky.SetFloat("_StarBrightness", 0.9f);
            sky.SetFloat("_HorizonSharpness", 1.6f);
            // a drifting cloud deck that crosses the moon, edges silvered by it
            // dark masses against the green glow, silver edges toward the moon
            sky.SetFloat("_CloudLayer", 1f);
            sky.SetFloat("_CloudCover", 0.58f);
            sky.SetFloat("_CloudScale", 1.3f);
            sky.SetFloat("_CloudSpeed", 0.012f);
            sky.SetColor("_CloudColor", new Color(0.018f, 0.035f, 0.024f));
            sky.SetColor("_CloudLitColor", new Color(0.75f, 0.88f, 0.76f));

            Unlit("M_FX_Flame", new Color(1f, 0.62f, 0.25f, 1f), AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/T_FX_SoftDot.png"), additive: true);
            AssetDatabase.SaveAssets();

            // FBX imports: no animation, project materials by slot name
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { SwampDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                imp.animationType = ModelImporterAnimationType.None;
                imp.importAnimation = false;
                imp.importCameras = false; imp.importLights = false;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                imp.addCollider = false;
                imp.isReadable = true;
                foreach (var m in SwampSlotMats)
                {
                    var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{m}.mat");
                    if (mat) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m), mat);
                }
                imp.SaveAndReimport();
                n++;
            }
            BuildTorchFire();
            EnsureFogFeature();
            AssetDatabase.SaveAssets();
            return $"Swamp art built ({n} models)";
        }

        static Material AssetOf(string name, string shader)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find(shader);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Cutout(Material m)
        {
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 0f);
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(m);
        }

        /// <summary>2x2 cutout atlas: Spanish moss (TL), cypress foliage (TR), lily pad (BL), reeds (BR).</summary>
        static Color Flora(float u, float v, int x, int y)
        {
            float lu = (u % 0.5f) * 2f, lv = (v % 0.5f) * 2f;
            bool top = v >= 0.5f, right = u >= 0.5f;
            var clear = new Color(0, 0, 0, 0);
            if (top && !right)
            {
                // Spanish moss: grey-green strands hanging from the top edge, clumped, ragged lengths
                float col = lu * 18f;
                int ci = Mathf.FloorToInt(col + Mathf.Sin(lv * 9f + Mathf.Floor(col)) * 0.35f);
                float taper = 1f - Mathf.Pow(Mathf.Abs(lu - 0.5f) * 2f, 2f) * 0.75f;     // a beard, longest in the middle
                float len = (0.3f + 0.7f * Hash(ci, 3, 301) * (0.6f + 0.4f * Fbm(lu, 0.5f, 4, 1, 2, 302))) * taper;
                float fx = col - Mathf.Floor(col) + Mathf.Sin(lv * 23f + ci) * 0.18f;
                bool strand = Mathf.Abs(fx - 0.5f) < 0.3f + 0.14f * (lv);
                bool clump = Fbm(lu, lv, 6, 6, 3, 303) > 0.6f && lv > 0.55f;
                bool on = (strand && 1f - lv < len) || (clump && 1f - lv < len + 0.1f);
                if (!on || Hash(x, y, 304) < 0.12f) return clear;
                var c = Mx(Hex("#3E4838"), Hex("#98A288"), 0.35f + 0.5f * lv + 0.25f * Gr(x, y, 305));
                c.a = 1; return c;
            }
            if (top && right)
            {
                // cypress foliage: a feathery clump of fine needle sprays, lighter on top, holes all through
                float dx = lu - 0.5f, dy = lv - 0.45f;
                float d = Mathf.Sqrt(dx * dx * 1.1f + dy * dy * 1.5f);
                float edge = 0.42f + 0.16f * (Fbm(lu, lv, 5, 5, 3, 311) - 0.5f) * 2f;
                if (d > edge) return clear;
                float spray = Mathf.Abs(Mathf.Sin((lu * 34f + lv * 16f + Fbm(lu, lv, 8, 8, 2, 312) * 4f) * Mathf.PI));
                float spray2 = Mathf.Abs(Mathf.Sin((lu * 30f - lv * 18f + Fbm(lu, lv, 8, 8, 2, 313) * 4f) * Mathf.PI));
                bool needle = Mathf.Max(spray, spray2) > 0.7f;
                if (!needle || Hash(x, y, 314) < 0.3f + 0.5f * Sst(edge - 0.14f, edge, d) || Fbm(lu, lv, 7, 7, 2, 317) < 0.36f) return clear;
                var c = Mx(Hex("#121C10"), Hex("#3F5A2A"), 0.25f + 0.55f * lv + 0.25f * Gr(x, y, 315));
                if (Hash(x, y, 316) > 0.93f) c = Hex("#62803E");
                c.a = 1; return c;
            }
            if (!right)
            {
                // lily pad: round leaf with a notch, radial veins, rusty rim, decay spots
                float dx = lu - 0.5f, dy = lv - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx);
                if (d > 0.47f || (Mathf.Abs(ang) < 0.18f && d > 0.03f)) return clear;
                var c = Mx(Hex("#22381A"), Hex("#4C6A2E"), 0.4f + 0.3f * Fbm(lu, lv, 6, 6, 3, 321) + 0.2f * Gr(x, y, 322));
                float vein = Mathf.Abs(Mathf.Sin(ang * 9f));
                c *= 1f - 0.25f * Sst(0.15f, 0f, vein) * Sst(0.05f, 0.15f, d);
                c = Mx(c, Hex("#5A3A1E"), Sst(0.4f, 0.47f, d) * 0.8f);
                if (Fbm(lu, lv, 10, 10, 2, 323) > 0.68f) c = Mx(c, Hex("#3A2A16"), 0.7f);
                c.a = 1; return c;
            }
            {
                // reeds: tapering blades rising from the bottom, dry tips
                float col = lu * 12f;
                int ci = Mathf.FloorToInt(col);
                float h = 0.45f + 0.55f * Hash(ci, 7, 331);
                float lean = (Hash(ci, 9, 332) - 0.5f) * 0.6f * lv;
                float fx = col - ci - lean * 3f;
                float w = 0.32f * (1f - lv / h);
                if (lv > h || Mathf.Abs(fx - 0.5f) > w) return clear;
                var c = Mx(Hex("#26341A"), Hex("#58663A"), 0.3f + 0.6f * lv / h + 0.2f * Gr(x, y, 333));
                c = Mx(c, Hex("#7A6A42"), Sst(0.75f, 1f, lv / h));
                c.a = 1; return c;
            }
        }

        static void BuildTorchFire()
        {
            var ps = NewPS("FX_TorchFire", M("M_FX_Flame"), out var r);
            var m = ps.main;
            m.loop = true; m.playOnAwake = true; m.prewarm = true; m.duration = 2f;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.3f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.78f, 0.4f), new Color(1f, 0.5f, 0.18f));
            m.gravityModifier = -0.25f;
            m.maxParticles = 60;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 34f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 10f; sh.radius = 0.06f;
            sh.rotation = new Vector3(-90f, 0, 0);
            SizeOverLife(ps, 1f, 0.15f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.75f), 0), new GradientColorKey(new Color(1f, 0.55f, 0.15f), 0.4f), new GradientColorKey(new Color(0.6f, 0.12f, 0.04f), 1) },
                      new[] { new GradientAlphaKey(0f, 0), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.7f, 0.55f), new GradientAlphaKey(0f, 1) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 1.6f; noise.scrollSpeed = 1.2f;

            var embers = NewPS("Embers", M("M_FX_Spark"), out _);
            embers.transform.SetParent(ps.transform, false);
            var em2 = embers.main; em2.loop = true; em2.playOnAwake = true; em2.prewarm = true; em2.duration = 2f;
            em2.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2.2f); em2.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            em2.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.045f); em2.maxParticles = 20; em2.gravityModifier = -0.08f;
            var ee = embers.emission; ee.rateOverTime = 4f;
            var es = embers.shape; es.shapeType = ParticleSystemShapeType.Cone; es.angle = 25f; es.radius = 0.05f; es.rotation = new Vector3(-90f, 0, 0);
            var en = embers.noise; en.enabled = true; en.strength = 0.8f; en.frequency = 0.8f;
            var ec = embers.colorOverLifetime; ec.enabled = true; ec.color = Grad(new Color(1f, 0.7f, 0.3f), new Color(1f, 0.3f, 0.1f));

            var smoke = NewPS("Smoke", M("M_FX_Dust"), out _);
            smoke.transform.SetParent(ps.transform, false);
            smoke.transform.localPosition = new Vector3(0, 0.35f, 0);
            var sm = smoke.main; sm.loop = true; sm.playOnAwake = true; sm.prewarm = true; sm.duration = 3f;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.2f); sm.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.45f); sm.maxParticles = 14;
            sm.startColor = new Color(0.08f, 0.09f, 0.08f, 0.35f);
            var se = smoke.emission; se.rateOverTime = 3.5f;
            var ss = smoke.shape; ss.shapeType = ParticleSystemShapeType.Sphere; ss.radius = 0.05f;
            SizeOverLife(smoke, 0.6f, 2.2f);
            var sc = smoke.colorOverLifetime; sc.enabled = true; sc.color = Grad(Color.white, Color.white, 0.6f, 0f);
            SavePS(ps, "FX_TorchFire");
        }

        /// <summary>Add the volumetric fog renderer feature to the PC renderer (idle unless a profile enables the override).</summary>
        static void EnsureFogFeature()
        {
            foreach (var path in new[] { RendererDataPath, "Assets/Settings/Mobile_Renderer.asset" })
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null) continue;
                var shader = Shader.Find("Hidden/ElderTree/VolumetricFog");
                var existing = data.rendererFeatures.OfType<VolumetricFogFeature>().FirstOrDefault();
                if (existing != null) { existing.shader = shader; EditorUtility.SetDirty(existing); continue; }
                var f = ScriptableObject.CreateInstance<VolumetricFogFeature>();
                f.name = "VolumetricFog";
                f.shader = shader;
                AssetDatabase.AddObjectToAsset(f, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out _, out long localId);
                var so = new SerializedObject(data);
                var feats = so.FindProperty("m_RendererFeatures");
                var map = so.FindProperty("m_RendererFeatureMap");
                feats.arraySize++;
                feats.GetArrayElementAtIndex(feats.arraySize - 1).objectReferenceValue = f;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(data);
            }
            AssetDatabase.SaveAssets();
        }

        // ================================================================== terrain
        struct Mound { public Vector2 p; public float r, h; }

        const float WaterLevel = -0.14f;

        static float SwampHeight(float x, float z, List<Mound> mounds)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            float n1 = Mathf.PerlinNoise(x * 0.075f + 11.3f, z * 0.075f + 7.1f);
            float n2 = Mathf.PerlinNoise(x * 0.23f + 3.7f, z * 0.23f + 5.9f);
            float n3 = Mathf.PerlinNoise(x * 0.6f + 1.1f, z * 0.6f + 2.3f);
            // the clearing: firm mud with a few black pools (kept off the middle)
            float inner = 0.05f * (n2 - 0.5f) + 0.025f * (n3 - 0.5f);
            float pool = Sst(0.6f, 0.7f, n1) * 0.45f * Sst(6f, 9.5f, r);
            float hIn = inner - pool + Sst(15.5f, 18.5f, r) * 0.32f;               // berm under the sandbag ring
            // the swamp: black water between hummocks
            float hum = Sst(0.48f, 0.74f, Mathf.PerlinNoise(x * 0.11f + 40f, z * 0.11f + 40f)) * 1.0f + (n2 - 0.5f) * 0.3f;
            float hOut = -0.6f + hum;
            // the garrison side (north) stands on dry land
            float ang = Mathf.Atan2(z, x) * Mathf.Rad2Deg;
            float dAng = Mathf.Abs(Mathf.DeltaAngle(ang, 102f));
            float dry = Sst(48f, 30f, dAng) * Sst(36f, 30f, r);
            hOut = Mathf.Lerp(hOut, 0.18f + 0.1f * (n2 - 0.5f), dry);
            float h = Mathf.Lerp(hIn, hOut, Sst(19.5f, 24f, r));
            h += Sst(46f, 68f, r) * 4.5f;                                             // far banks close the bowl
            foreach (var m in mounds)
            {
                float d = Vector2.Distance(new Vector2(x, z), m.p) / m.r;
                if (d < 1.6f) h = Mathf.Max(h, m.h * Mathf.Exp(-d * d * 1.4f) - 0.15f);
            }
            // keep the ground clearly above or below the water (-0.14): a shore that grazes the water plane
            // z-fights and the pools flicker as the camera moves
            float w = h - WaterLevel;
            if (Mathf.Abs(w) < 0.09f) h = WaterLevel + (w < 0f ? -0.09f : 0.09f);
            return h;
        }

        static GameObject BuildSwampGround(Transform parent, List<Mound> mounds, out MeshCollider collider)
        {
            const float Ext = 72f, Cell = 1.3f;
            int n = Mathf.CeilToInt(Ext * 2 / Cell) + 1;
            var verts = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = -Ext + i * Cell, z = -Ext + j * Cell;
                verts[j * n + i] = new Vector3(x, SwampHeight(x, z, mounds), z);
                uvs[j * n + i] = new Vector2(x, z) / 4.5f;
            }
            var tris = new int[(n - 1) * (n - 1) * 6];
            int t = 0;
            for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                // alternate the split for a less regular PSX grid
                if (((i + j) & 1) == 0) { tris[t++] = a; tris[t++] = c; tris[t++] = d; tris[t++] = a; tris[t++] = d; tris[t++] = b; }
                else { tris[t++] = a; tris[t++] = c; tris[t++] = b; tris[t++] = b; tris[t++] = c; tris[t++] = d; }
            }
            var mesh = new Mesh { name = "SwampGround", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            string path = SwampDir + "/SwampGround.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) { old.Clear(); EditorUtility.CopySerialized(mesh, old); mesh = old; }
            else AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject("SwampGround");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = M("M_SwampGround");
            collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            go.isStatic = true;
            return go;
        }

        // ================================================================== placement helpers
        enum Col { None, Mesh, Box, Trunk }

        static GameObject SP(string prop, Vector3 pos, float yaw, Transform parent, float scale = 1f, Col col = Col.Mesh, List<Mound> mounds = null, string name = null)
        {
            var prefab = SwampProp(prop);
            if (prefab == null) { Debug.LogWarning("[Swamp] missing prop " + prop); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            if (mounds != null) pos.y = SwampHeight(pos.x, pos.z, mounds);
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
                    bc.size = go.transform.InverseTransformVector(b.size);
                    bc.size = new Vector3(Mathf.Abs(bc.size.x), Mathf.Abs(bc.size.y), Mathf.Abs(bc.size.z));
                    break;
                }
                case Col.Trunk:
                {
                    var cap = go.AddComponent<CapsuleCollider>();
                    cap.radius = prop == "Cypress_A" ? 1.0f : prop == "Cypress_B" ? 0.9f : 0.8f;
                    cap.height = 8f; cap.center = new Vector3(0, 4f, 0);
                    break;
                }
            }
            foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                if (r.sharedMaterials.Any(m => m != null && m.name == "M_SwampFlora")) r.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        static Vector3 Polar(float angDeg, float r) => new(Mathf.Cos(angDeg * Mathf.Deg2Rad) * r, 0, Mathf.Sin(angDeg * Mathf.Deg2Rad) * r);
        /// <summary>Yaw that turns the prop's local X along the ring's tangent at this angle.</summary>
        static float Tangent(float angDeg) => -angDeg + 90f;
        /// <summary>Yaw that makes the prop's forward (+Z) face away from the arena centre.</summary>
        static float Outward(float angDeg) => 90f - angDeg;

        static Light SwampLight(Transform parent, string name, LightType type, Vector3 pos, Color color, float intensity, float range,
            float scatter, bool shadows = false, float spot = 50f, float inner = 30f, bool flicker = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = type; l.color = color; l.intensity = intensity; l.range = range;
            l.shadows = shadows ? LightShadows.Hard : LightShadows.None;
            l.shadowNearPlane = 0.3f;
            if (type == LightType.Spot) { l.spotAngle = spot; l.innerSpotAngle = inner; }
            go.AddComponent<UniversalAdditionalLightData>();
            go.AddComponent<FogLight>().scatter = scatter;
            if (flicker) { var f = go.AddComponent<LanternFlicker>(); f.amount = 0.3f; f.speed = 9f; f.wobble = 0.03f; }
            return l;
        }

        static void AddFire(Transform at, float scale)
        {
            var fire = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/FX/FX_TorchFire.prefab"));
            fire.transform.SetParent(at, false);
            fire.transform.localPosition = Vector3.zero;
            fire.transform.localScale = Vector3.one * scale;
        }

        static Transform Find(GameObject root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        // ================================================================== scene
        [MenuItem("Elder Tree/9b. Build Swamp Arena (Crocodile)")]
        public static string BuildSwampScene()
        {
            if (cues == null) BuildAudio();
            if (SwampProp("Cypress_A") == null) return "Swamp models missing: run Tools/Blender/build_swamp.py first";
            Random.InitState(1913);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupPsxPipeline();

            // ---------------- sky, ambient, Unity fog
            var sky = M("M_SwampSky");
            var moonRot = Quaternion.Euler(31f, 205f, 0);
            Vector3 toMoon = -(moonRot * Vector3.forward);
            sky.SetVector("_MoonDir", new Vector4(toMoon.x, toMoon.y, toMoon.z, 0));
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.22f, 0.34f, 0.24f);
            RenderSettings.ambientEquatorColor = new Color(0.15f, 0.24f, 0.16f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.045f, 0.035f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Hex("#16261B");
            RenderSettings.fogDensity = 0.017f;
            RenderSettings.reflectionIntensity = 0.1f;

            var sysRoot = new GameObject("--- Systems ---").transform;
            var envRoot = new GameObject("--- Environment ---").transform;
            var milRoot = new GameObject("--- Garrison ---").transform;
            var floraRoot = new GameObject("--- Swamp ---").transform;
            var lightRoot = new GameObject("--- Lighting ---").transform;
            var actorRoot = new GameObject("--- Actors ---").transform;

            // ---------------- the white moon
            var moonGo = new GameObject("Moonlight");
            moonGo.transform.SetParent(lightRoot);
            moonGo.transform.rotation = moonRot;
            var moon = moonGo.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = Hex("#E9EFF0");
            moon.intensity = 1.6f;
            moon.shadows = LightShadows.Hard;
            moon.shadowStrength = 0.88f;
            moonGo.AddComponent<UniversalAdditionalLightData>();
            RenderSettings.sun = moon;

            // ---------------- trees first (the ground raises a mound under each)
            var mounds = new List<Mound>();
            var trees = new List<(Vector3 p, string kind, float s)>();
            void TryTree(Vector3 p, string kind, float s, float spacing = 5.2f)
            {
                foreach (var t in trees) if ((t.p - p).sqrMagnitude < spacing * spacing) return;
                trees.Add((p, kind, s));
                mounds.Add(new Mound { p = new Vector2(p.x, p.z), r = 2.6f * s, h = 0.35f + 0.25f * s });
            }
            for (int i = 0; i < 260 && trees.Count < 46; i++)
            {
                float a = Random.Range(0f, 360f);
                float r = Mathf.Lerp(22.5f, 48f, Mathf.Pow(Random.value, 0.8f));
                float dA = Mathf.Abs(Mathf.DeltaAngle(a, 102f));
                if (dA < 48f && r < 34f) continue;                    // the garrison clearing (and the moon above it)
                string kind = r < 30f ? (Random.value < 0.55f ? "Cypress_A" : "Cypress_B") : new[] { "Cypress_A", "Cypress_B", "Cypress_C" }[Random.Range(0, 3)];
                if (dA < 60f) kind = "Cypress_C";                     // short ones by the clearing: the moon clears them
                TryTree(Polar(a, r), kind, Random.Range(0.95f, 1.35f));
            }
            // a few trees beyond the garrison so the sky there still has silhouettes
            foreach (var (a, r) in new[] { (70f, 40f), (95f, 44f), (128f, 39f), (150f, 35f), (58f, 36f) })
                TryTree(Polar(a, r), "Cypress_C", 1.2f);
            // the outer treeline: a dense, bigger second ring that closes every sight line out of the swamp
            // (short cypresses under the moon, so it still clears them from the arena)
            for (int i = 0; i < 900 && trees.Count < 150; i++)
            {
                float a = Random.Range(0f, 360f);
                float r = Random.Range(36f, 64f);
                float dA = Mathf.Abs(Mathf.DeltaAngle(a, 102f));
                if (dA < 48f && r < 36f) continue;
                string kind = dA < 45f ? "Cypress_C" : new[] { "Cypress_A", "Cypress_B", "Cypress_A", "Cypress_C" }[Random.Range(0, 4)];
                TryTree(Polar(a, r), kind, Random.Range(1.15f, 1.7f), 4.2f);
            }

            var ground = BuildSwampGround(envRoot, mounds, out var groundCol);
            var water = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(water.GetComponent<Collider>());
            water.name = "SwampWater";
            water.transform.SetParent(envRoot);
            water.transform.SetPositionAndRotation(new Vector3(0, WaterLevel, 0), Quaternion.Euler(90, 0, 0));
            water.transform.localScale = new Vector3(144, 144, 1);
            var wr = water.GetComponent<MeshRenderer>();
            wr.sharedMaterial = M("M_SwampWater");
            wr.shadowCastingMode = ShadowCastingMode.Off;
            float H(float x, float z) => SwampHeight(x, z, mounds);

            foreach (var t in trees)
            {
                SP(t.kind, t.p, Random.Range(0f, 360f), floraRoot, t.s, Col.Trunk, mounds);
                if (Random.value < 0.6f)
                {
                    var k = t.p + Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * Random.Range(2.5f, 4f) * t.s;
                    SP("CypressKnees", k, Random.Range(0f, 360f), floraRoot, Random.Range(0.8f, 1.3f), Col.None, mounds);
                }
            }

            // ---------------- the garrison ring: sandbags with gaps, wire and hedgehogs facing the swamp
            var gaps = new[] { (178f, 14f), (62f, 10f), (122f, 10f), (215f, 6f), (318f, 16f) };   // pillbox, nests, searchlight, open water
            for (float a = 0f; a < 360f; a += 8.2f)
            {
                if (gaps.Any(g => Mathf.Abs(Mathf.DeltaAngle(a, g.Item1)) < g.Item2)) continue;
                var p = Polar(a, 18.6f);
                SP("SandbagWall", p, Tangent(a) + Random.Range(-3f, 3f), milRoot, 1f, Col.Mesh, mounds);
            }
            SP("Pillbox", Polar(178f, 19.6f), Random.Range(0f, 60f), milRoot, 1f, Col.Mesh, mounds, "Pillbox");
            SP("GunNest", Polar(62f, 17.4f), Outward(62f) + 90f, milRoot, 1f, Col.Mesh, mounds, "GunNest_East");
            SP("GunNest", Polar(122f, 17.4f), Outward(122f) + 90f, milRoot, 1f, Col.Mesh, mounds, "GunNest_West");
            SP("FieldGun", Polar(28f, 21.5f), Outward(28f) + 90f + 25f, milRoot, 1.1f, Col.Mesh, mounds, "AAGun");
            SP("FieldGun", Polar(150f, 22.5f), Outward(150f) + 90f - 20f, milRoot, 1.1f, Col.Mesh, mounds, "AAGun2");
            for (float a = 192f; a < 352f; a += 10.8f)
                SP("BarbedWire", Polar(a, 21.6f) + Vector3.up * 0.05f, Tangent(a) + Random.Range(-4f, 4f), milRoot, 1f, Col.Box, mounds);
            foreach (var (a, r) in new[] { (200f, 23f), (232f, 21f), (251f, 24f), (275f, 22.5f), (296f, 24.5f), (334f, 23f), (12f, 22f), (350f, 20.5f), (8f, 16.4f), (236f, 16.2f), (268f, 16.6f) })
                SP("Hedgehog", Polar(a, r), Random.Range(0f, 360f), milRoot, Random.Range(0.9f, 1.15f), Col.Mesh, mounds);
            foreach (var (a, r) in new[] { (318f, 19.6f), (330f, 20.2f), (306f, 20.4f), (6f, 20.1f) })
                SP("SpikeBarrier", Polar(a, r), Tangent(a), milRoot, 1f, Col.Mesh, mounds);

            // the camp behind the north wall
            SP("MilitaryTent", Polar(80f, 27.5f), -80f + Random.Range(-8f, 8f), milRoot, 1f, Col.Mesh, mounds, "Tent_A");
            SP("MilitaryTent", Polar(108f, 29f), -100f, milRoot, 1f, Col.Mesh, mounds, "Tent_B");
            SP("MilitaryTent", Polar(136f, 26.5f), -130f, milRoot, 1f, Col.Mesh, mounds, "Tent_C");
            SP("SupplyPile", Polar(72f, 21.8f), 30f, milRoot, 1f, Col.Mesh, mounds);
            SP("SupplyPile", Polar(146f, 21.5f), 110f, milRoot, 1f, Col.Mesh, mounds);
            SP("SupplyPile", Polar(200f, 20.6f), 200f, milRoot, 0.9f, Col.Mesh, mounds);
            SP("SupplyPile", Polar(94f, 31f), 70f, milRoot, 1f, Col.Mesh, mounds);
            SP("Duckboard", Polar(96f, 22f), Tangent(96f) + 90f, milRoot, 1f, Col.Mesh, mounds);
            SP("Duckboard", Polar(96f, 25.2f), Tangent(96f) + 90f, milRoot, 1f, Col.Mesh, mounds);
            SP("RadioMast", Polar(74f, 37f), 20f, milRoot, 1f, Col.Box, mounds, "RadioMast");

            // duckboards across the pools inside the ring
            int boards = 0;
            for (int i = 0; i < 400 && boards < 4; i++)
            {
                var p = Polar(Random.Range(0f, 360f), Random.Range(8f, 15f));
                if (H(p.x, p.z) > -0.22f) continue;
                SP("Duckboard", p + Vector3.up * 0.0f, Random.Range(0f, 180f), milRoot, 1f, Col.Mesh, null).transform.position = new Vector3(p.x, -0.1f, p.z);
                boards++;
            }

            // ---------------- military lighting
            // floodlights on the ring aimed into the clearing (spots, two cast shadows)
            var floodCol = Hex("#FFEBC8");
            int fl = 0;
            foreach (var a in new[] { 18f, 158f, 268f })
            {
                var go = SP("Floodlight", Polar(a, 19.8f), 0f, milRoot, 1f, Col.Box, mounds, $"Floodlight{fl}");
                // face the centre: try the four quarter turns and keep the one with the lamps nearest the middle
                var lamp0 = Find(go, "Lamp0"); var lamp1 = Find(go, "Lamp1");
                float best = float.MaxValue, bestYaw = 0f;
                for (int q = 0; q < 4; q++)
                {
                    go.transform.rotation = Quaternion.Euler(0, q * 90f + Outward(a), 0);
                    if (lamp0 == null || lamp1 == null) break;
                    var m = (lamp0.position + lamp1.position) * 0.5f;
                    float d = new Vector2(m.x, m.z).sqrMagnitude;
                    if (d < best) { best = d; bestYaw = q * 90f + Outward(a); }
                }
                go.transform.rotation = Quaternion.Euler(0, bestYaw, 0);
                Vector3 mid = lamp0 != null && lamp1 != null ? (lamp0.position + lamp1.position) * 0.5f : go.transform.position + Vector3.up * 5f;
                var l = SwampLight(lightRoot, $"FloodLight{fl}", LightType.Spot, mid + (Vector3.zero - mid).normalized * 0.3f, floodCol, 34f, 34f, 0.5f, fl < 2, 62f, 40f);
                l.transform.rotation = Quaternion.LookRotation((Polar(a, 7f) - mid).normalized, Vector3.up);
                fl++;
            }

            // searchlights: the watchtower over the camp and one on the south-west wall, sweeping the clearing
            Searchlight MakeSearch(GameObject go, string headName, string lensName, Vector3 aim)
            {
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var head = Find(go, headName);
                var lens = Find(go, lensName);
                if (head == null) return null;
                var pivot = new GameObject("HeadAim").transform;
                pivot.SetParent(head.parent, false);
                pivot.position = head.position;
                Vector3 fwd = lens != null ? (lens.position - head.position).normalized : go.transform.forward;
                pivot.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                head.SetParent(pivot, true);
                pivot.rotation = Quaternion.LookRotation((aim - pivot.position).normalized, Vector3.up);
                var l = SwampLight(pivot, "SearchBeam", LightType.Spot, pivot.position + pivot.forward * 0.55f, Hex("#EEF4FF"), 70f, 50f, 1.15f, false, 13f, 6f);
                l.transform.rotation = pivot.rotation;
                l.transform.SetParent(pivot, true);
                var sl = go.AddComponent<Searchlight>();
                sl.head = pivot; sl.target = aim;
                foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = false;
                return sl;
            }
            var tower = SP("WatchTower", Polar(100f, 23.6f), Random.Range(0f, 90f), milRoot, 1f, Col.Mesh, mounds, "WatchTower");
            var s1 = MakeSearch(tower, "WatchTower_Head", "WatchTower_Lens", new Vector3(0, 0, 2f));
            if (s1 != null) { s1.sweepDegrees = 70f; s1.period = 13f; }
            var search = SP("Searchlight", Polar(215f, 18.6f), 0f, milRoot, 1.2f, Col.Mesh, mounds, "Searchlight");
            var s2 = MakeSearch(search, "Searchlight_Head", "Searchlight_Lens", new Vector3(3f, 0, 4f));
            if (s2 != null) { s2.sweepDegrees = 80f; s2.period = 9.5f; s2.trackPlayerInFights = false; }

            // spare torches spread through the clearing (not just on the ring) + burn barrels: warm light spots
            var fireCol = Hex("#FF9A3C");
            int ti = 0;
            foreach (var (ta, tr) in new[] { (6f, 15.8f), (40f, 11.5f), (72f, 15.2f), (112f, 12.6f), (150f, 15.6f), (188f, 10.8f),
                                             (226f, 15.4f), (262f, 12.2f), (300f, 15.9f), (338f, 11.2f) })
            {
                var p = Polar(ta + Random.Range(-4f, 4f), tr + Random.Range(-0.6f, 0.4f));
                for (int k = 0; k < 8 && H(p.x, p.z) < -0.05f; k++) p = Polar(ta + Random.Range(-14f, 14f), tr + Random.Range(-2f, 1f));   // not in a pool
                var torch = SP("SwampTorch", p, Random.Range(0f, 360f), milRoot, 1f, Col.Box, mounds, $"Torch{ti}");
                var flame = Find(torch, "Flame");
                var fp = flame != null ? flame.position : torch.transform.position + Vector3.up * 2f;
                AddFire(flame != null ? flame : torch.transform, 1f);
                SwampLight(lightRoot, $"TorchLight{ti}", LightType.Point, fp + Vector3.up * 0.25f, fireCol, 8f, 10f, 0.9f, false, flicker: true);
                ti++;
            }
            int bi = 0;
            foreach (var (a, r) in new[] { (88f, 20.8f), (204f, 17.2f), (312f, 16.8f), (118f, 30f) })
            {
                var barrel = SP("BurnBarrel", Polar(a, r), Random.Range(0f, 360f), milRoot, 1f, Col.Mesh, mounds, $"BurnBarrel{bi}");
                var flame = Find(barrel, "Flame");
                AddFire(flame != null ? flame : barrel.transform, 1.5f);
                SwampLight(lightRoot, $"BarrelLight{bi}", LightType.Point, (flame != null ? flame.position : barrel.transform.position) + Vector3.up * 0.5f,
                    fireCol, 6f, 8f, 0.8f, false, flicker: true);
                bi++;
            }
            // ---------------- lanterns: hung on cypress trunks around the clearing and deeper in, and set by the garrison's kit
            var lanternCol = Hex("#FFB050");
            int li = 0;
            var innerAngles = new List<float>();
            var deepAngles = new List<float>();
            foreach (var t in trees.OrderBy(t => t.p.magnitude))
            {
                float r = t.p.magnitude;
                bool inner = r < 34f, deep = r >= 34f && r < 46f;
                if (!inner && !deep) continue;
                float ang = Mathf.Atan2(t.p.z, t.p.x) * Mathf.Rad2Deg;
                var used = inner ? innerAngles : deepAngles;
                if (used.Count >= (inner ? 14 : 8) || used.Any(x => Mathf.Abs(Mathf.DeltaAngle(x, ang)) < (inner ? 19f : 36f))) continue;
                used.Add(ang);
                float trunkR = (t.kind == "Cypress_A" ? 0.95f : t.kind == "Cypress_B" ? 0.85f : 0.75f) * t.s;
                Vector3 toCentre = new Vector3(-t.p.x, 0, -t.p.z).normalized;
                float hgt = Random.Range(2.6f, 3.8f);
                var basePos = new Vector3(t.p.x, H(t.p.x, t.p.z), t.p.z) + toCentre * trunkR + Vector3.up * hgt;
                var lan = SP("TreeLantern", basePos, 0f, floraRoot, 1f, Col.None, null, $"TreeLantern{li}");
                // turn the bracket away from the trunk: keep the yaw that puts the flame furthest from the trunk axis
                var fl0 = Find(lan, "Flame");
                float bestYaw = 0f, bestD = -1f;
                for (int q = 0; q < 24; q++)
                {
                    lan.transform.rotation = Quaternion.Euler(0, q * 15f, 0);
                    var fp0 = fl0 != null ? fl0.position : lan.transform.position;
                    float d = new Vector2(fp0.x - t.p.x, fp0.z - t.p.z).sqrMagnitude;
                    if (d > bestD) { bestD = d; bestYaw = q * 15f; }
                }
                lan.transform.rotation = Quaternion.Euler(0, bestYaw, 0);
                var lp = fl0 != null ? fl0.position : basePos;
                SwampLight(lightRoot, $"TreeLanternLight{li}", LightType.Point, lp, lanternCol, inner ? 4f : 3f, inner ? 8f : 7f, 0.85f, false, flicker: true)
                    .GetComponent<LanternFlicker>().amount = 0.12f;
                li++;
            }
            // standing lanterns by the kit: tent doors, nests, pillbox roof, the guns, supplies
            void KitLantern(Vector3 at, float lift, string name)
            {
                var pos = new Vector3(at.x, (lift > 0f ? at.y : H(at.x, at.z)) + lift, at.z);
                var lan = SP("Lantern", pos, Random.Range(0f, 360f), milRoot, 1f, Col.None, null, name);
                var f = Find(lan, "Flame");
                SwampLight(lightRoot, name + "Light", LightType.Point, (f != null ? f.position : pos + Vector3.up * 0.15f) + Vector3.up * 0.05f,
                    lanternCol, 3f, 6.5f, 0.8f, false, flicker: true).GetComponent<LanternFlicker>().amount = 0.12f;
            }
            Vector3 Inward(Vector3 p, float d) => p + new Vector3(-p.x, 0, -p.z).normalized * d;
            int ki = 0;
            foreach (var n in new[] { "Tent_A", "Tent_B", "Tent_C" })
            {
                var g = GameObject.Find(n);
                if (g != null) KitLantern(Inward(g.transform.position, 3.4f) + Vector3.Cross(Vector3.up, g.transform.position.normalized) * 1.2f, 0f, $"KitLantern{ki++}");
            }
            foreach (var n in new[] { "GunNest_East", "GunNest_West" })
            {
                var g = GameObject.Find(n);
                if (g != null) KitLantern(Inward(g.transform.position, -0.3f) + Vector3.Cross(Vector3.up, g.transform.position.normalized) * 1.4f, 0f, $"KitLantern{ki++}");
            }
            var pill = GameObject.Find("Pillbox");
            if (pill != null) KitLantern(Inward(pill.transform.position, 1.6f) + Vector3.up * 1.82f, 0.0001f, $"KitLantern{ki++}");
            foreach (var n in new[] { "AAGun", "AAGun2" })
            {
                var g = GameObject.Find(n);
                if (g != null) KitLantern(Inward(g.transform.position, 1.8f) + Vector3.Cross(Vector3.up, g.transform.position.normalized) * 1.5f, 0f, $"KitLantern{ki++}");
            }
            var tow = GameObject.Find("WatchTower");
            if (tow != null) KitLantern(Inward(tow.transform.position, 0.9f) + Vector3.up * 6.06f, 0.0001f, $"KitLantern{ki++}");

            // a red aircraft beacon on the mast
            var mast = GameObject.Find("RadioMast");
            if (mast != null)
            {
                var beacon = Find(mast, "Beacon");
                if (beacon != null) SwampLight(lightRoot, "MastBeacon", LightType.Point, beacon.position, Hex("#FF3020"), 3f, 6f, 1.5f, false, flicker: true);
            }

            // ---------------- underbrush: thickets between the trunks block the eye-level view out
            int bushes = 0;
            for (int i = 0; i < 3000 && bushes < 190; i++)
            {
                float a = Random.Range(0f, 360f), r = Random.Range(24f, 60f);
                float dA = Mathf.Abs(Mathf.DeltaAngle(a, 102f));
                if (dA < 48f && r < 34f) continue;                    // keep the camp clearing open
                var p = Polar(a, r);
                if (trees.Any(t => (t.p - p).sqrMagnitude < 2.2f * 2.2f)) continue;
                SP(Random.value < 0.5f ? "SwampBush_A" : "SwampBush_B", p, Random.Range(0f, 360f), floraRoot, Random.Range(0.9f, 1.6f), Col.None, mounds);
                bushes++;
            }

            // ---------------- swamp dressing
            for (int i = 0; i < 10; i++)
            {
                var p = Polar(Random.Range(0f, 360f), Random.Range(22f, 40f));
                SP(i % 2 == 0 ? "SwampStump" : "FallenLog", p, Random.Range(0f, 360f), floraRoot, Random.Range(0.85f, 1.25f), Col.Mesh, mounds);
            }
            SP("SwampStump", Polar(250f, 14.6f), 30f, floraRoot, 1f, Col.Mesh, mounds);
            SP("FallenLog", Polar(332f, 13.8f), Tangent(332f) + 12f, floraRoot, 0.9f, Col.Mesh, mounds, "FallenLog_Inner");
            SP("CypressKnees", Polar(300f, 15f), 0f, floraRoot, 1f, Col.None, mounds);
            SP("CypressKnees", Polar(25f, 14f), 50f, floraRoot, 0.8f, Col.None, mounds);
            int lilies = 0, reeds = 0, ferns = 0;
            for (int i = 0; i < 3000 && (lilies < 34 || reeds < 60 || ferns < 36); i++)
            {
                float a = Random.Range(0f, 360f), r = Random.Range(4f, 50f);
                var p = Polar(a, r);
                float h = H(p.x, p.z);
                if (h < -0.28f && lilies < 34) { SP("LilyPads", new Vector3(p.x, WaterLevel + 0.045f, p.z), Random.Range(0f, 360f), floraRoot, Random.Range(0.8f, 1.5f), Col.None); lilies++; }
                else if (h > -0.18f && h < 0.02f && r > 9f && reeds < 60) { SP("Reeds", p, Random.Range(0f, 360f), floraRoot, Random.Range(0.8f, 1.3f), Col.None, mounds); reeds++; }
                else if (h > 0.05f && r > 19.5f && ferns < 36) { SP("Ferns", p, Random.Range(0f, 360f), floraRoot, Random.Range(0.8f, 1.4f), Col.None, mounds); ferns++; }
            }

            // ---------------- keep the fight in: an invisible ring wall beyond the sandbags
            var bounds = new GameObject("ArenaBounds").transform;
            bounds.SetParent(envRoot);
            for (int i = 0; i < 40; i++)
            {
                float a = i * 9f;
                var b = new GameObject($"Bound{i}");
                b.transform.SetParent(bounds);
                b.transform.SetPositionAndRotation(Polar(a, 22.6f) + Vector3.up * 3f, Quaternion.Euler(0, Tangent(a), 0));
                var bc = b.AddComponent<BoxCollider>();
                bc.size = new Vector3(3.8f, 10f, 0.6f);
            }

            // ---------------- fog: OoT-remake ground fog + volumetric haze (+ the Unity fog above)
            var gfGo = new GameObject("GroundFog");
            gfGo.transform.SetParent(lightRoot);
            var gf = gfGo.AddComponent<GroundFog>();
            gf.center = Vector3.zero; gf.size = 64f; gf.resolution = 96; gf.bottom = 0.06f; gf.top = 1.55f; gf.layers = 14;
            gf.ground = groundCol;
            gf.material = M("M_GroundFog");

            var volGo = new GameObject("PostProcess");
            volGo.transform.SetParent(lightRoot);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = BuildSwampPostProfile();
            volGo.AddComponent<PSXGlobals>();

            // ---------------- player, camera, HUD, bosses: the Crocodile is this stage's boss
            var rig = BuildCommon(sysRoot, envRoot, actorRoot, new Vector3(0, 0, -10f), 0f, false, "Crocodile");
            rig.hud.titleKicker = "Lair of the Crocodile";
            rig.hud.titleMain = "DROWNED";
            rig.hud.titleSub = "GARRISON";
            foreach (var b in Object.FindObjectsByType<Enemies.CrocodileBoss>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { b.arenaCenter = Vector3.zero; EditorUtility.SetDirty(b); }
            BuildCrocodileAirstrip();

            EditorSceneManager.SaveScene(scene, SwampScenePath);
            RegisterBuildScenes();
            return $"Swamp arena built: {trees.Count} cypresses, {bushes} thickets, {lilies} lily rafts, {reeds} reed clumps, {ferns} ferns, {ti} torches, {li} tree lanterns, {ki} kit lanterns";
        }

        static VolumeProfile BuildSwampPostProfile()
        {
            string path = Root + "/Settings/SwampPostProfile.asset";
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
            // sickly swamp grade: green-black shadows, cold moonlit mids, warm fire highlights that bloom
            var tone = Add<Tonemapping>(); tone.mode.Override(TonemappingMode.ACES);
            var bloom = Add<Bloom>(); bloom.threshold.Override(0.8f); bloom.intensity.Override(0.95f); bloom.scatter.Override(0.7f);
            bloom.tint.Override(new Color(0.92f, 1f, 0.88f));
            var ca = Add<ColorAdjustments>(); ca.postExposure.Override(0.85f); ca.contrast.Override(14f); ca.saturation.Override(-8f);
            ca.colorFilter.Override(new Color(0.93f, 1f, 0.93f));
            var smh = Add<ShadowsMidtonesHighlights>();
            smh.shadows.Override(new Vector4(0.9f, 1.08f, 0.96f, 0f));
            smh.midtones.Override(new Vector4(0.96f, 1.02f, 0.98f, 0f));
            smh.highlights.Override(new Vector4(1.05f, 1.0f, 0.95f, 0f));
            var wb = Add<WhiteBalance>(); wb.temperature.Override(-6f); wb.tint.Override(-14f);
            var vig = Add<Vignette>(); vig.intensity.Override(0.46f); vig.smoothness.Override(0.45f); vig.color.Override(new Color(0.01f, 0.03f, 0.015f));
            var grain = Add<FilmGrain>(); grain.type.Override(FilmGrainLookup.Medium2); grain.intensity.Override(0.3f); grain.response.Override(0.7f);
            var vf = Add<VolumetricFog>();
            vf.enabled.Override(true);
            vf.density.Override(0.02f);
            vf.baseHeight.Override(0.3f);
            vf.heightFalloff.Override(3.2f);
            vf.maxDistance.Override(85f);
            vf.steps.Override(28);
            vf.anisotropy.Override(0.45f);
            vf.noiseScale.Override(0.08f);
            vf.noiseStrength.Override(0.65f);
            vf.wind.Override(new Vector3(0.3f, 0.03f, 0.12f));
            vf.albedo.Override(new Color(0.72f, 0.92f, 0.74f));
            vf.ambient.Override(new Color(0.02f, 0.034f, 0.024f));
            vf.mainLightScatter.Override(0.22f);
            vf.localLightScatter.Override(0.7f);
            vf.groundClear.Override(0.6f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
