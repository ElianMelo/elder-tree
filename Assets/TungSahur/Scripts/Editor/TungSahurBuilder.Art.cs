using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TungSahur.EditorTools
{
    /// <summary>
    /// One-click builder for the whole vertical slice. Every step is idempotent and can be
    /// run from the menu "Tung Sahur/..." or via script eval.
    /// </summary>
    public static partial class TungSahurBuilder
    {
        public const string Root = "Assets/TungSahur";
        // Player model: the low-poly PSX Elder Tree (Tools/Blender/build_elder_tree.py).
        // The old log character is kept at TungModelPath; both share the same Humanoid skeleton.
        public const string ModelPath = Root + "/Art/Models/ElderTree.fbx";
        public const string TungModelPath = Root + "/Art/Models/TungSahur.fbx";
        static readonly string[] CharacterMats =
            { "M_TungWood", "M_TungEye", "M_TungDarkWood", "M_TungMouth", "M_ElderBark", "M_ElderMoss", "M_ElderEye" };
        // PSX iron-banded cudgel (Tools/Blender/build_psx_weapons.py); the old bat stays at TungClubPath.
        public const string ClubPath = Root + "/Art/Models/ElderClub.fbx";
        public const string TungClubPath = Root + "/Art/Models/TungSahur_Club.fbx";
        public const string MatDir = Root + "/Art/Materials";

        // ------------------------------------------------------------------ materials
        public const string PsxShader = "TungSahur/PSXLit";

        public static Material Lit(string name, Color color, Texture2D albedo = null, Texture2D normal = null,
            float smoothness = 0.3f, float normalScale = 1f, Vector2? tiling = null, bool smoothFromAlbedo = true,
            float metallic = 0f)
        {
            Directory.CreateDirectory(MatDir);
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            // Every lit surface uses the PSX shader (Simple Lit + vertex snap + 15-bit dither).
            var shader = Shader.Find(PsxShader) ?? Shader.Find("Universal Render Pipeline/Simple Lit");
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetTexture("_BaseMap", albedo);
            var t = tiling ?? Vector2.one;
            mat.SetTextureScale("_BaseMap", t);
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.SetTextureScale("_BumpMap", t);
                mat.SetFloat("_BumpScale", normalScale);
                mat.EnableKeyword("_NORMALMAP");
            }
            else
            {
                mat.SetTexture("_BumpMap", null);
                mat.DisableKeyword("_NORMALMAP");
            }
            if (albedo != null && smoothFromAlbedo)
            {
                mat.SetFloat("_SmoothnessTextureChannel", 1);
                mat.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                mat.SetFloat("_Smoothness", smoothness);
            }
            else
            {
                mat.SetFloat("_SmoothnessTextureChannel", 0);
                mat.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                mat.SetFloat("_Smoothness", smoothness);
            }
            // matte PS1 surfaces: no specular, no normal maps; emission always available for flashes
            mat.SetFloat("_Smoothness", 0f);
            mat.SetColor("_SpecColor", Color.black);
            mat.DisableKeyword("_SPECULAR_COLOR");
            mat.DisableKeyword("_NORMALMAP");
            mat.EnableKeyword("_EMISSION");
            if (!mat.HasProperty("_EmissionColor") || mat.GetColor("_EmissionColor").maxColorComponent <= 0f)
                mat.SetColor("_EmissionColor", Color.black);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static Material Unlit(string name, Color color, Texture2D tex = null, bool additive = false,
            string shaderName = "Universal Render Pipeline/Particles/Unlit")
        {
            Directory.CreateDirectory(MatDir);
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find(shaderName);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", tex);
            // transparent
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", additive ? 2 : 0); // 0 alpha, 2 additive
            mat.SetFloat("_ZWrite", 0);
            mat.SetFloat("_Cull", 0);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive) mat.EnableKeyword("_BLENDMODE_ADD"); else mat.DisableKeyword("_BLENDMODE_ADD");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ step: textures + materials
        [MenuItem("Tung Sahur/1. Build Art (textures + materials)")]
        public static string BuildArt()
        {
            TextureForge.BuildTungWood(out var woodA, out var woodN);
            var eye = TextureForge.BuildEye();
            TextureForge.BuildClub(out var clubA, out var clubN);
            TextureForge.BuildGround(out var groundA, out var groundN);
            TextureForge.BuildPlanks(out var plankA, out var plankN);
            TextureForge.BuildStone(out var stoneA, out var stoneN);
            TextureForge.BuildBurlap(out var burA, out var burN);
            var dot = TextureForge.BuildSoftDot();
            var chip = TextureForge.BuildChip();
            var trail = TextureForge.BuildTrail();
            var ring = TextureForge.BuildRing();

            // Character textures are baked in Blender (sculpted face: albedo x AO + tangent normals).
            // Fall back to the procedural tileables if the bake has not been run.
            var bodyA = TextureForge.ImportBaked("T_TungBody_Albedo", false);
            var bodyN = TextureForge.ImportBaked("T_TungBody_Normal", true);
            var bakedEye = TextureForge.ImportBaked("T_TungEye_Sculpt", false, 1024);
            if (bodyA != null) Lit("M_TungWood", Color.white, bodyA, bodyN, 0.52f, 1f, null, false);
            else Lit("M_TungWood", Color.white, woodA, woodN, 0.85f, 0.6f);
            Lit("M_TungEye", Color.white, bakedEye != null ? bakedEye : eye, null, 0.86f, 1f, null, false);
            Lit("M_TungDarkWood", Palette.Brow * 1.6f, woodA, woodN, 0.6f, 0.4f, new Vector2(2, 2));
            Lit("M_TungMouth", Palette.Mouth, null, null, 0.2f);
            BuildElderMaterials();
            // ---- PSX dark-fantasy world (Docs/ART_DIRECTION.md): point-filtered dithered textures,
            // shading painted in, no normal maps, dark desaturated values; glow only as accents.
            var barkA = TextureForge.BuildBark();
            var foliageA = TextureForge.BuildFoliage();
            var darkWoodA = TextureForge.BuildDarkWood();
            var leatherA = TextureForge.BuildLeather();
            var ironA = TextureForge.BuildIron();
            Lit("M_ClubWood", Color.white, clubA);
            Lit("M_ClubIron", Color.white, ironA, null, 0f, 1f, new Vector2(2, 1));
            Lit("M_Ground", Color.white, groundA, null, 0f, 1f, new Vector2(10, 10));
            Lit("M_Planks", Color.white, plankA);
            Lit("M_Stone", Color.white, stoneA);
            Lit("M_Burlap", Color.white, burA, null, 0f, 1f, new Vector2(2, 2));
            Lit("M_Rope", TextureForge.Hex("#8C7A5C"), burA, null, 0f, 1f, new Vector2(1, 6));
            Lit("M_Iron", Color.white, ironA);
            var lamp = Lit("M_LanternGlow", Palette.Lantern, null, null, 0f);
            lamp.EnableKeyword("_EMISSION");
            lamp.SetColor("_EmissionColor", Palette.Lantern * 4f);
            lamp.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            Lit("M_DummyPaint", TextureForge.Hex("#4A1712"), burA, null, 0f, 1f, new Vector2(3, 3));
            Lit("M_Foliage", Color.white, foliageA, null, 0f, 1f, new Vector2(2, 2));
            Lit("M_Bark", Color.white, barkA, null, 0f, 1f, new Vector2(1, 1));
            Lit("M_DarkWood", Color.white, darkWoodA);
            Lit("M_Leather", Color.white, leatherA);

            Unlit("M_FX_Dust", new Color(0.36f, 0.33f, 0.32f, 0.5f), dot);
            Unlit("M_FX_Spark", Palette.Ember, dot, additive: true);
            Unlit("M_FX_Chip", new Color(0.36f, 0.27f, 0.2f, 1f), chip);
            Unlit("M_FX_Trail", new Color(0.72f, 0.74f, 0.86f, 0.42f), trail, additive: false);
            Unlit("M_FX_Ring", new Color(0.42f, 0.4f, 0.46f, 0.55f), ring);
            Unlit("M_FX_Glow", Palette.Lantern, dot, additive: true);
            Unlit("M_FX_Firefly", Palette.Wisp, dot, additive: true);
            AssetDatabase.SaveAssets();
            return "Art built";
        }

        /// <summary>PSX dark-fantasy materials for the Elder Tree (Docs/ART_DIRECTION.md): point-filtered
        /// 256px atlases with the shading painted in, no specular, no normal maps.</summary>
        public static void BuildElderMaterials()
        {
            var barkTex = TextureForge.ImportPixel("T_ElderBark");
            var mossTex = TextureForge.ImportPixel("T_ElderMoss", alpha: true);
            if (barkTex == null) return;   // run Tools/Blender/build_elder_tree.py --bake first
            var bark = Lit("M_ElderBark", Color.white, barkTex, null, 0.04f, 1f, null, false);
            bark.SetFloat("_SpecularHighlights", 0f); bark.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            bark.SetFloat("_EnvironmentReflections", 0f); bark.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            // emission keyword on so hit-flash style MaterialPropertyBlocks can light it up later
            bark.EnableKeyword("_EMISSION");
            bark.SetColor("_EmissionColor", Color.black);
            bark.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(bark);

            var moss = Lit("M_ElderMoss", Color.white, mossTex, null, 0.0f, 1f, null, false);
            moss.SetFloat("_AlphaClip", 1f);
            moss.SetFloat("_Cutoff", 0.5f);
            moss.EnableKeyword("_ALPHATEST_ON");
            moss.SetFloat("_Cull", 0f);                    // cards are double sided
            moss.SetOverrideTag("RenderType", "TransparentCutout");
            moss.renderQueue = (int)RenderQueue.AlphaTest;
            moss.SetFloat("_SpecularHighlights", 0f); moss.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            moss.SetFloat("_EnvironmentReflections", 0f); moss.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            EditorUtility.SetDirty(moss);

            // dim ember eyes: an accent, not a light bulb
            var glow = new Color(0.78f, 0.47f, 0.12f);
            var eye = Lit("M_ElderEye", glow, null, null, 0.0f);
            eye.EnableKeyword("_EMISSION");
            eye.SetColor("_EmissionColor", glow * 2.2f);
            eye.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(eye);
        }

        // ------------------------------------------------------------------ step: model import
        [MenuItem("Tung Sahur/2. Configure Character Import (Humanoid)")]
        public static string ConfigureCharacterImport()
        {
            var sb = new StringBuilder();
            var imp = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            imp.globalScale = 1f;
            imp.useFileScale = true;
            imp.importBlendShapes = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.importAnimation = false;
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.optimizeGameObjects = false;
            imp.skinWeights = ModelImporterSkinWeights.Standard;
            imp.importNormals = ModelImporterNormals.Import;
            imp.importTangents = ModelImporterTangents.CalculateMikk;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (var n in CharacterMats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");
                if (m != null) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), m);
            }

            // Explicit humanoid mapping (bone names authored in Blender match HumanTrait names).
            var human = imp.humanDescription;
            var map = HumanTrait.BoneName
                .Select(hn => new HumanBone { humanName = hn, boneName = hn, limit = new HumanLimit { useDefaultValues = true } })
                .Where(hb => BoneExistsInModel(hb.boneName))
                .ToArray();
            // A log does not bend like a human spine: shrink torso/neck ranges so retargeted
            // clips keep their intent (twist for swings) without turning the body into rubber.
            for (int i = 0; i < map.Length; i++)
            {
                Vector3? range = map[i].humanName switch
                {
                    // x = twist, y = left-right, z = front-back (degrees, symmetric)
                    "Spine" => new Vector3(22, 6, 9),
                    "Chest" => new Vector3(16, 4, 6),
                    "UpperChest" => new Vector3(10, 3, 4),
                    "Neck" => new Vector3(10, 4, 5),
                    "Head" => new Vector3(14, 6, 8),
                    _ => null
                };
                if (range is Vector3 r)
                    map[i].limit = new HumanLimit { useDefaultValues = false, min = -r, max = r, center = Vector3.zero };
            }
            human.human = map;
            human.upperArmTwist = 0.5f; human.lowerArmTwist = 0.5f;
            human.upperLegTwist = 0.5f; human.lowerLegTwist = 0.5f;
            human.armStretch = 0.05f; human.legStretch = 0.05f;
            human.feetSpacing = 0f;
            human.hasTranslationDoF = false;
            imp.humanDescription = human;
            imp.SaveAndReimport();

            // second pass: skeleton is now known; rebuild description with the skeleton poses
            imp = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            sb.AppendLine($"Avatar: {(avatar ? avatar.name : "none")} valid={avatar?.isValid} human={avatar?.isHuman} mapped={map.Length}");

            // Club
            var cimp = (ModelImporter)AssetImporter.GetAtPath(ClubPath);
            cimp.animationType = ModelImporterAnimationType.None;
            cimp.importAnimation = false;
            cimp.importTangents = ModelImporterTangents.CalculateMikk;
            cimp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            cimp.importNormals = ModelImporterNormals.Import;
            foreach (var n in new[] { "M_ClubWood", "M_ClubIron" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");
                if (m != null) cimp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), m);
            }
            cimp.SaveAndReimport();

            // report hierarchy
            if (go != null)
                foreach (var t in go.GetComponentsInChildren<Transform>())
                    if (t.name == "Hips" || t.name == "TungSahur" || t.name == "Armature" || t == go.transform)
                        sb.AppendLine($"  {t.name} pos={t.localPosition} rot={t.localEulerAngles} scale={t.localScale}");
            return sb.ToString();

            bool BoneExistsInModel(string bone)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                return model != null && model.GetComponentsInChildren<Transform>(true).Any(t => t.name == bone);
            }
        }
    }
}
