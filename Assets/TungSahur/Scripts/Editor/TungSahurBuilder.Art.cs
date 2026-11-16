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
        public const string ModelPath = Root + "/Art/Models/TungSahur.fbx";
        public const string ClubPath = Root + "/Art/Models/TungSahur_Club.fbx";
        public const string MatDir = Root + "/Art/Materials";

        // ------------------------------------------------------------------ materials
        public static Material Lit(string name, Color color, Texture2D albedo = null, Texture2D normal = null,
            float smoothness = 0.3f, float normalScale = 1f, Vector2? tiling = null, bool smoothFromAlbedo = true,
            float metallic = 0f)
        {
            Directory.CreateDirectory(MatDir);
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
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

            Lit("M_TungWood", Color.white, woodA, woodN, 0.85f, 0.6f);
            Lit("M_TungEye", Color.white, eye, null, 0.5f);
            Lit("M_TungDarkWood", Palette.Brow * 1.6f, woodA, woodN, 0.6f, 0.4f, new Vector2(2, 2));
            Lit("M_TungMouth", Palette.Mouth, null, null, 0.2f);
            Lit("M_ClubWood", Color.white, clubA, clubN, 0.9f, 0.8f);
            Lit("M_Ground", new Color(0.92f, 0.92f, 0.95f), groundA, groundN, 0.9f, 1.0f, new Vector2(10, 10));
            Lit("M_Planks", Color.white, plankA, plankN, 0.8f, 0.8f);
            Lit("M_Stone", Color.white, stoneA, stoneN, 0.8f, 1f);
            Lit("M_Burlap", Color.white, burA, burN, 0.5f, 0.8f, new Vector2(2, 2));
            Lit("M_Rope", TextureForge.Hex("#7A6440"), burA, burN, 0.3f, 1.2f, new Vector2(1, 6));
            Lit("M_Iron", TextureForge.Hex("#2B2B2E"), null, null, 0.55f, 1f, null, false, 0.85f);
            var lamp = Lit("M_LanternGlow", Palette.Lantern, null, null, 0.5f);
            lamp.EnableKeyword("_EMISSION");
            lamp.SetColor("_EmissionColor", Palette.Lantern * 6f);
            lamp.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            Lit("M_DummyPaint", TextureForge.Hex("#7B1E1E"), null, null, 0.25f);
            Lit("M_Foliage", TextureForge.Hex("#1F2B21"), burA, burN, 0.15f, 1.5f, new Vector2(3, 3));
            Lit("M_Bark", TextureForge.Hex("#7A6A5A"), plankA, plankN, 0.5f, 1.2f, new Vector2(0.5f, 1f));
            // materials that flash / glow through MaterialPropertyBlocks need the emission keyword on
            foreach (var n in new[] { "M_Burlap", "M_Rope", "M_DummyPaint", "M_Planks", "M_ClubWood" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                EditorUtility.SetDirty(m);
            }

            Unlit("M_FX_Dust", new Color(0.62f, 0.55f, 0.47f, 0.55f), dot);
            Unlit("M_FX_Spark", Palette.Lantern, dot, additive: true);
            Unlit("M_FX_Chip", new Color(0.85f, 0.62f, 0.38f, 1f), chip);
            Unlit("M_FX_Trail", new Color(1f, 0.86f, 0.62f, 0.55f), trail, additive: false);
            Unlit("M_FX_Ring", new Color(0.8f, 0.7f, 0.55f, 0.6f), ring);
            Unlit("M_FX_Glow", Palette.Lantern, dot, additive: true);
            Unlit("M_FX_Firefly", new Color(1f, 0.85f, 0.45f, 1f), dot, additive: true);
            AssetDatabase.SaveAssets();
            return "Art built";
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
            foreach (var n in new[] { "M_TungWood", "M_TungEye", "M_TungDarkWood", "M_TungMouth" })
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
            var clubMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_ClubWood.mat");
            foreach (var n in new[] { "M_ClubWood", "M_ClubGrip" })
                cimp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), clubMat);
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
