using System.IO;
using ElderTree.Masks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ElderTree.EditorTools
{
    /// <summary>
    /// The pickup a felled boss leaves behind: it shows the mask it holds, strikes down in a column of light in the
    /// mask's colour, and flies to its player if left alone too long.
    /// </summary>
    public static partial class ElderTreeBuilder
    {
        [MenuItem("Elder Tree/8e. Mask Pickup (pickup prefab)")]
        public static string BuildMaskModels()
        {
            BuildMaskPickupPrefab();
            AssetDatabase.SaveAssets();
            return "Mask pickup prefab rebuilt";
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
