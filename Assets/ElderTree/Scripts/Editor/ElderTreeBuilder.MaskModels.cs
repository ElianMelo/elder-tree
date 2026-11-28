using System.IO;
using ElderTree.Equipment;
using ElderTree.Masks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ElderTree.EditorTools
{
    /// <summary>
    /// What the four reward masks share: the carved wood inside every mask (M_MaskWood, see Tools/Blender/mask_shell.py
    /// for the plate itself), their HUD icons, and the pickup a felled boss leaves behind (it shows the mask it holds,
    /// strikes down in a column of light in the mask's colour, and flies to its player if left alone too long).
    /// Run after the four mask sets have imported their models (BuildAll does).
    /// </summary>
    public static partial class ElderTreeBuilder
    {
        static string[] AllMaskModelPaths => new[] { MaskModelPath, SharkMaskModelPath, DruidMaskModelPath, FairyMaskModelPath };

        [MenuItem("Elder Tree/8e. Mask Models + Pickup (wood, icons, pickup prefab)")]
        public static string BuildMaskModels()
        {
            var wood = BuildMaskWood();
            int remapped = 0;
            foreach (var path in AllMaskModelPaths)
            {
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null) { Debug.LogError($"[ElderTree] Missing {path}"); continue; }
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_MaskWood"), wood);
                imp.SaveAndReimport();
                remapped++;
            }
            // icons follow the new faces
            int icons = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:MaskData"))
            {
                var m = AssetDatabase.LoadAssetAtPath<MaskData>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || m.maskModel == null) continue;
                string name = m.icon != null ? m.icon.name : "Icon_" + m.name.Replace("Mask_", "") + "Mask";
                var tex = RenderIcon(AssetDatabase.GetAssetPath(m.maskModel), name, Quaternion.Euler(-10, 200, 0));
                if (tex != null) { m.icon = tex; EditorUtility.SetDirty(m); icons++; }
            }
            BuildMaskPickupPrefab();
            AssetDatabase.SaveAssets();
            return $"Mask models: wood on {remapped} masks, {icons} icons, pickup prefab rebuilt";
        }

        /// <summary>Bare carved wood: warm honey-brown grain running up the mask, gouge marks from the carving knife, and
        /// the darker patina where it has been worn against a face.</summary>
        static Material BuildMaskWood()
        {
            var Hex = (System.Func<string, Color>)TextureForge.Hex;
            var tex = TextureForge.SavePsx("T_MaskWood", 128, (u, v, x, y) =>
            {
                float warp = TextureForge.Fbm(u, v, 3, 2, 3, 901) - 0.5f;
                float rings = Mathf.Sin((u * 7f + warp * 2.6f) * Mathf.PI * 2f) * 0.5f + 0.5f;
                float fib = TextureForge.Fbm(u, v, 40, 3, 3, 902);
                var c = Color.Lerp(Hex("#3E2716"), Hex("#8C5F37"), 0.25f + 0.4f * rings + 0.35f * fib);
                // knife gouges: shallow horizontal scoops, each darker along its lower lip
                float row = v * 9f + (TextureForge.Fbm(u, v, 5, 3, 2, 903) - 0.5f) * 0.9f;
                float fr = row - Mathf.Floor(row);
                c *= 0.82f + 0.24f * Mathf.Sin(fr * Mathf.PI) - 0.2f * Mathf.Clamp01((0.12f - fr) / 0.12f);
                // worn patina and a few knots
                float wear = TextureForge.Fbm(u, v, 4, 4, 3, 904);
                c = Color.Lerp(c, Hex("#24160D"), Mathf.Clamp01((wear - 0.58f) * 3f) * 0.6f);
                TextureForge.Worley(u, v, 3, 905, out float f1, out _, out int id);
                if (id % 3 == 0 && f1 < 0.06f) c = Color.Lerp(c, Hex("#1E120A"), 0.7f * (1f - f1 / 0.06f));
                return c;
            }, mips: false);
            var mat = Lit("M_MaskWood", Color.white, tex, null, 0f, 1f, null, false);
            Matte(mat);
            return mat;
        }

        /// <summary>A soft vertical column: bright core fading to the edges, fading in at its foot and out as it rises (additive).</summary>
        static Texture2D BuildBeamTexture()
        {
            const int W = 64, H = 128;
            var t = TextureForge.Save("T_FX_MaskBeam", W, H, (x, y) =>
            {
                float u = x / (float)(W - 1) * 2f - 1f, v = y / (float)(H - 1);
                float core = Mathf.Exp(-u * u * 9f) * 0.75f + Mathf.Exp(-u * u * 60f) * 0.6f;
                float k0 = Mathf.Clamp01(v / 0.035f), fade = Mathf.Pow(1f - v, 1.6f) * k0 * k0 * (3f - 2f * k0);   // soft start over the mask
                return new Color(1f, 1f, 1f, Mathf.Clamp01(core * fade));
            }, aniso: 0);
            var imp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t));
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(t));
        }

        static void BuildMaskPickupPrefab()
        {
            var beamMat = Unlit("M_FX_MaskBeam", Color.white, BuildBeamTexture(), additive: true);
            var ringTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureForge.TexDir + "/T_FX_Ring.png") ?? TextureForge.BuildRing();
            var ringMat = Unlit("M_FX_MaskRing", Color.white, ringTex, additive: true);

            var root = new GameObject("MaskPickup");
            var modelRoot = Child("Model", root.transform, Vector3.up);
            var slot = Child("Mesh", modelRoot.transform, Vector3.zero);
            // over the mask, not inside the plate (from inside it burns the mask white)
            var lg = Child("Glow", modelRoot.transform, Vector3.up * 0.8f).AddComponent<Light>();
            lg.type = LightType.Point; lg.color = Color.white; lg.range = 7f; lg.intensity = 5f; lg.shadows = LightShadows.None;

            MeshRenderer Flat(string name, Material mat, Vector3 at)
            {
                var go = Child(name, root.transform, at);
                go.AddComponent<MeshFilter>();
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                return r;
            }
            var beam = Flat("Beam", beamMat, Vector3.zero);
            var ring = Flat("Ring", ringMat, Vector3.up * 0.04f);
            var pulse = Flat("Pulse", ringMat, Vector3.up * 0.05f);

            var ps = NewPS("Motes", M("M_FX_Firefly"), out _);
            ps.transform.SetParent(root.transform, false);
            ps.transform.localPosition = Vector3.up * 0.2f;
            var m = ps.main; m.loop = true; m.playOnAwake = true; m.duration = 2f;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.6f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f); m.gravityModifier = -0.2f; m.maxParticles = 120;
            m.startColor = new ParticleSystem.MinMaxGradient(Color.white, Color.white);
            var em = ps.emission; em.rateOverTime = 26f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.9f; sh.rotation = new Vector3(-90, 0, 0);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, Color.white, 1f, 0f);

            var pick = root.AddComponent<MaskPickup>();
            pick.model = modelRoot.transform;
            pick.meshSlot = slot.transform;
            pick.glow = lg;
            pick.motes = ps;
            pick.beam = beam;
            pick.ring = ring;
            pick.pulse = pulse;
            Directory.CreateDirectory(Path.GetDirectoryName(PickupPrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, PickupPrefabPath);
            Object.DestroyImmediate(root);
        }
    }
}
