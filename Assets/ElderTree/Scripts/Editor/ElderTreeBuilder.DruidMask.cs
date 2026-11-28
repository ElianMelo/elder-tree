using System.Collections.Generic;
using System.IO;
using System.Linq;
using ElderTree.Combat;
using ElderTree.Enemies;
using ElderTree.Equipment;
using ElderTree.FX;
using ElderTree.Masks;
using ElderTree.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace ElderTree.EditorTools
{
    /// <summary>
    /// The Druid Mask set (Druid reward): the druid body (pale wood under a swaying leaf robe), its staff, the Vine
    /// Seeker sorcery (homing vines, leaf explosion, vines bursting out where it hits), the Verdant Orb (a big green
    /// energy ball thrown from both hands) and the mask item. Models come from Tools/Blender/build_druid_form.py
    /// (exported into Art/Models/DruidMask). The body shares its bark / root / leaf materials with the Druid boss.
    ///
    /// Data assets (Data/Forms/Form_Druid, Data/Equipment/Spell_VineSeeker, Orb_Verdant, Data/Masks/Mask_Druid) are only
    /// CREATED when missing; existing ones keep their tuned values (missing references are filled in).
    /// </summary>
    public static partial class ElderTreeBuilder
    {
        public const string DruidMaskDir = Root + "/Art/Models/DruidMask";
        public const string DruidFormModelPath = DruidMaskDir + "/DruidForm.fbx";
        public const string DruidFormLeavesPath = DruidMaskDir + "/DruidFormLeaves.fbx";
        public const string DruidStaffPath = DruidMaskDir + "/DruidStaff.fbx";
        public const string DruidOrbModelPath = DruidMaskDir + "/DruidOrb.fbx";
        public const string DruidMaskModelPath = DruidMaskDir + "/DruidMask.fbx";
        public const string VineSeekerPrefabPath = Root + "/Prefabs/VineSeeker.prefab";
        public const string VineBurstPrefabPath = Root + "/Prefabs/VineBurst.prefab";
        public const string DruidOrbPrefabPath = Root + "/Prefabs/DruidOrb.prefab";
        public const string VineRingPrefabPath = Root + "/Prefabs/VineRing.prefab";
        public const string SpecialGrovePath = DataDir + "/Equipment/Special_WrathOfTheGrove.asset";
        public const string SpellGatherPrefabPath = Root + "/Prefabs/FX/FX_SpellGather.prefab";
        public const string FormDruidPath = DataDir + "/Forms/Form_Druid.asset";
        public const string MaskDruidPath = DataDir + "/Masks/Mask_Druid.asset";
        public const string SpellVineSeekerPath = DataDir + "/Equipment/Spell_VineSeeker.asset";
        public const string OrbVerdantPath = DataDir + "/Equipment/Orb_Verdant.asset";

        static readonly string[] DruidFormMats = { "M_DruidBark", "M_DruidRoot", "M_DruidLeaf", "M_DruidEye", "M_DruidHollow", "M_DruidGlow" };

        /// <summary>Where the mask sits on the druid's face (bind space: feet at the origin, facing +Z).</summary>
        public static Vector3 DruidFacePos = new Vector3(0f, 1.70f, 0.29f);   // past the drooping nose
        static readonly Color DruidGreen = new Color(0.5f, 1f, 0.32f, 1f);

        [MenuItem("Elder Tree/8c. Build Druid Mask set")]
        public static string BuildDruidMaskSet()
        {
            BuildDruidMaskArt();
            ConfigureDruidMaskImport();
            BuildDruidMaskFX();
            BuildDruidMaskPrefabs();
            BuildDruidMaskData();
            AssetDatabase.SaveAssets();
            return "Druid Mask set built";
        }

        // ------------------------------------------------------------------ textures + materials
        static void BuildDruidMaskArt()
        {
            var Hex = (System.Func<string, Color>)TextureForge.Hex;
            // leafy vine strip for the spell (LineRenderer: u runs along the vine and tiles, v across): a twisted green
            // stalk down the middle with leaves sprouting off both sides, light and dark, alpha cutout around them
            const int W = 64, H = 32;
            var vine = TextureForge.Save("T_DruidVine", W, H, (x, y) =>
            {
                float u = x / (float)W, v = y / (float)H;
                float wob = 0.03f * Mathf.Sin(u * Mathf.PI * 4f);
                float d = Mathf.Abs(v - 0.5f - wob);
                Color c = new Color(0, 0, 0, 0);
                if (d < 0.11f)
                {
                    float twist = 0.5f + 0.5f * Mathf.Sin((u * 6f + v * 3f) * Mathf.PI * 2f);
                    c = Mx(Hex("#3E5222"), Hex("#7A8A3E"), 0.25f + 0.6f * twist) * (1f - d * 2.2f);
                    c = Mx(c, Hex("#5A4630"), Sstep(0.6f, 0.8f, TextureForge.Fbm(u, v, 4, 2, 3, 1801)) * 0.5f);
                    c.a = 1f;
                }
                // four leaves along each tile, alternating sides; two light, two dark
                for (int k = 0; k < 4; k++)
                {
                    float cu = (k + 0.5f) / 4f;
                    float side = k % 2 == 0 ? 1f : -1f;
                    var stem = new Vector2(cu, 0.5f + side * 0.08f);
                    var dir = new Vector2(0.55f, side).normalized;
                    var p = new Vector2(u, v) - stem;
                    if (p.x > 0.5f) p.x -= 1f; else if (p.x < -0.5f) p.x += 1f;
                    float along = Vector2.Dot(p, dir), across = p.x * dir.y - p.y * dir.x;
                    const float len = 0.42f;
                    if (along <= 0f || along >= len) continue;
                    float half = 0.085f * Mathf.Pow(Mathf.Sin(Mathf.PI * along / len), 0.8f);
                    if (Mathf.Abs(across) >= half) continue;
                    var col = k < 2 ? Mx(Hex("#6E9234"), Hex("#9AB650"), H2(k, 3, 1802)) : Mx(Hex("#2C4418"), Hex("#3E5826"), H2(k, 5, 1803));
                    c = col * (1.1f - 0.35f * Mathf.Abs(across) / half) * (0.85f + 0.3f * (1f - along / len));
                    if (Mathf.Abs(across) < 0.012f) c *= 0.75f;    // midrib
                    c.a = 1f;
                }
                var q = TextureForge.Psx(c, x, y);
                q.a = c.a > 0.5f ? 1f : 0f;
                return q;
            }, aniso: 0);
            var vimp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(vine));
            vimp.filterMode = FilterMode.Point;
            vimp.mipmapEnabled = false;
            vimp.alphaSource = TextureImporterAlphaSource.FromInput;
            vimp.alphaIsTransparency = true;
            vimp.textureCompression = TextureImporterCompression.Uncompressed;
            vimp.wrapModeU = TextureWrapMode.Repeat;
            vimp.wrapModeV = TextureWrapMode.Clamp;
            vimp.SaveAndReimport();

            var vm = Lit("M_DruidVine", Color.white, vine, null, 0f, 1f, null, false);
            Matte(vm);
            vm.SetFloat("_AlphaClip", 1f);
            vm.SetFloat("_Cutoff", 0.5f);
            vm.EnableKeyword("_ALPHATEST_ON");
            vm.SetFloat("_Cull", 0f);
            vm.SetOverrideTag("RenderType", "TransparentCutout");
            vm.renderQueue = (int)RenderQueue.AlphaTest;
            vm.SetColor("_EmissionColor", new Color(0.14f, 0.26f, 0.06f));     // a faint life in it: reads at night
            EditorUtility.SetDirty(vm);

            // the seed in the staff, the orb core, the necklace and belt charms: a living green glow
            Glow("M_DruidGlow", Hex("#7CE04A"), 0f, new Color(0.55f, 1.2f, 0.32f) * 1.6f);
            var dot = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureForge.TexDir + "/T_FX_SoftDot.png") ?? TextureForge.BuildSoftDot();
            Unlit("M_FX_GreenGlow", new Color(0.45f, 1f, 0.28f, 1f), dot, additive: true);
            // loose leaves for particles: the druid leaf atlas, unlit cutout, tinted down to sit at night-time values
            // (the lit particle shader flared them into cyan squares under the moon)
            var leafTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureForge.TexDir + "/T_DruidLeaf.png");
            var lp = Unlit("M_FX_Leaf", new Color(0.62f, 0.68f, 0.56f, 1f), leafTex, false);
            lp.SetFloat("_AlphaClip", 1f);
            lp.SetFloat("_Cutoff", 0.5f);
            lp.EnableKeyword("_ALPHATEST_ON");
            EditorUtility.SetDirty(lp);
        }

        // ------------------------------------------------------------------ import
        static void ConfigureDruidMaskImport()
        {
            var all = new[] { DruidFormModelPath, DruidFormLeavesPath, DruidStaffPath, DruidOrbModelPath, DruidMaskModelPath };
            foreach (var path in all) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ConfigureHumanoid(DruidFormModelPath, DruidFormMats);
            foreach (var path in all.Skip(1))
            {
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                if (imp == null) { Debug.LogError($"[ElderTree] Missing {path} (run Tools/Blender/build_druid_form.py)"); continue; }
                imp.globalScale = 1f;
                imp.useFileScale = true;
                imp.animationType = ModelImporterAnimationType.None;
                imp.importAnimation = false;
                imp.importBlendShapes = false;
                imp.importCameras = false;
                imp.importLights = false;
                imp.importNormals = ModelImporterNormals.Calculate;
                imp.normalSmoothingAngle = 35f;
                imp.importTangents = ModelImporterTangents.None;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                imp.addCollider = false;
                foreach (var n in DruidFormMats)
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");
                    if (m) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), m);
                }
                imp.SaveAndReimport();
            }
        }

        // ------------------------------------------------------------------ FX
        /// <summary>Loose leaves: the druid atlas, a random cell per particle (light and dark), tumbling.</summary>
        static ParticleSystem LeafPS(string name, out ParticleSystemRenderer r)
        {
            var ps = NewPS(name, M("M_FX_Leaf"), out r);
            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.numTilesX = 2; tsa.numTilesY = 2;
            tsa.animation = ParticleSystemAnimationType.WholeSheet;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
            tsa.cycleCount = 1;
            var m = ps.main;
            m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-7f, 7f);
            return ps;
        }

        static void BuildDruidMaskFX()
        {
            Directory.CreateDirectory($"{PrefabDir}/FX");
            // leaf explosion (Vine Seeker hits, the cast flash): a green flash, a burst of torn leaves flung out and
            // drifting down, a ring of green motes
            {
                var root = NewPS("FX_LeafExplosion", M("M_FX_GreenGlow"), out _);
                var m = root.main; m.startLifetime = 0.12f; m.startSpeed = 0f; m.startSize = 0.85f;
                m.startColor = new Color(0.4f, 1f, 0.22f, 0.6f);
                Burst(root, 1);
                var sh = root.shape; sh.enabled = false;
                SizeOverLife(root, 0.5f, 1.3f);
                var col = root.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, Color.white, 1f, 0f);

                var leaves = LeafPS("Leaves", out _);
                leaves.transform.SetParent(root.transform, false);
                var lm = leaves.main;
                lm.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.9f);
                lm.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 7f);
                lm.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
                lm.gravityModifier = 0.35f;
                Burst(leaves, 26);
                var lsh = leaves.shape; lsh.shapeType = ParticleSystemShapeType.Hemisphere; lsh.radius = 0.2f;
                var lim = leaves.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 1.4f; lim.dampen = 0.2f;
                var noise = leaves.noise; noise.enabled = true; noise.strength = 0.9f; noise.frequency = 0.7f;
                var lc = leaves.colorOverLifetime; lc.enabled = true; lc.color = Grad(Color.white, Color.white, 1f, 0f);

                var motes = NewPS("Motes", M("M_FX_GreenGlow"), out var mr);
                motes.transform.SetParent(root.transform, false);
                var mm = motes.main;
                mm.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
                mm.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
                mm.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
                mm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 1f, 0.35f), new Color(0.85f, 1f, 0.55f));
                Burst(motes, 22);
                var msh = motes.shape; msh.shapeType = ParticleSystemShapeType.Sphere; msh.radius = 0.15f;
                var mc = motes.colorOverLifetime; mc.enabled = true; mc.color = Grad(Color.white, new Color(0.4f, 0.8f, 0.3f));
                mr.renderMode = ParticleSystemRenderMode.Stretch; mr.velocityScale = 0.04f; mr.lengthScale = 1.5f;
                SavePS(root, "FX_LeafExplosion");
            }
            // orb burst: a big green flash, a shell of green streaks, a soft glow that lingers, leaves and motes
            {
                var root = NewPS("FX_OrbBurst", M("M_FX_GreenGlow"), out _);
                var m = root.main; m.startLifetime = 0.2f; m.startSpeed = 0f; m.startSize = 3.8f;
                m.startColor = new Color(0.65f, 1f, 0.45f, 1f);
                Burst(root, 1);
                var sh = root.shape; sh.enabled = false;
                SizeOverLife(root, 0.35f, 1.5f);
                var col = root.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, Color.white, 1f, 0f);

                var linger = NewPS("Linger", M("M_FX_GreenGlow"), out _);
                linger.transform.SetParent(root.transform, false);
                var gm = linger.main; gm.startLifetime = 0.6f; gm.startSpeed = 0f; gm.startSize = 2.4f;
                gm.startColor = new Color(0.35f, 0.8f, 0.25f, 0.55f);
                Burst(linger, 1);
                var gsh = linger.shape; gsh.enabled = false;
                SizeOverLife(linger, 0.8f, 1.6f);
                var gc = linger.colorOverLifetime; gc.enabled = true; gc.color = Grad(Color.white, Color.white, 1f, 0f);

                var streaks = NewPS("Streaks", M("M_FX_GreenGlow"), out var sr);
                streaks.transform.SetParent(root.transform, false);
                var stm = streaks.main;
                stm.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
                stm.startSpeed = new ParticleSystem.MinMaxCurve(8f, 17f);
                stm.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
                stm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 1f, 0.35f), new Color(0.9f, 1f, 0.6f));
                stm.gravityModifier = 0.3f;
                Burst(streaks, 40);
                var ssh = streaks.shape; ssh.shapeType = ParticleSystemShapeType.Sphere; ssh.radius = 0.2f;
                var sc = streaks.colorOverLifetime; sc.enabled = true; sc.color = Grad(Color.white, new Color(0.3f, 0.7f, 0.2f));
                sr.renderMode = ParticleSystemRenderMode.Stretch; sr.velocityScale = 0.06f; sr.lengthScale = 2f;

                var leaves = LeafPS("Leaves", out _);
                leaves.transform.SetParent(root.transform, false);
                var lm = leaves.main;
                lm.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
                lm.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
                lm.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
                lm.gravityModifier = 0.4f;
                Burst(leaves, 14);
                var lsh = leaves.shape; lsh.shapeType = ParticleSystemShapeType.Sphere; lsh.radius = 0.3f;
                var lim = leaves.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 1.5f; lim.dampen = 0.2f;
                var lc = leaves.colorOverLifetime; lc.enabled = true; lc.color = Grad(Color.white, Color.white, 1f, 0f);
                SavePS(root, "FX_OrbBurst");
            }
            // leaf fall: a puff of loose druid leaves drifting down (no flash): fizzles, sprouting, the growth
            {
                var ps = LeafPS("FX_LeafFall", out _);
                var m = ps.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 3f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
                m.gravityModifier = 0.3f;
                Burst(ps, 14);
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.25f;
                var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 1.3f; lim.dampen = 0.15f;
                var noise = ps.noise; noise.enabled = true; noise.strength = 0.8f; noise.frequency = 0.8f;
                var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, Color.white, 1f, 0f);
                SavePS(ps, "FX_LeafFall");
            }
            // spell gather (lives on the staff's focus, loops while a spell gathers): green motes and a few leaves
            // drawn in from all around into the seed, and a breathing glow at its heart
            {
                var root = NewPS("FX_SpellGather", M("M_FX_GreenGlow"), out _);
                var m = root.main; m.loop = true; m.duration = 1f; m.startLifetime = 0.25f; m.startSpeed = 0f;
                m.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
                m.startColor = new Color(0.55f, 1f, 0.35f, 0.7f);
                m.simulationSpace = ParticleSystemSimulationSpace.Local;
                var em = root.emission; em.rateOverTime = 14f;
                var sh = root.shape; sh.enabled = false;
                var col = root.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, Color.white, 1f, 0f);

                var motes = NewPS("Motes", M("M_FX_GreenGlow"), out _);
                motes.transform.SetParent(root.transform, false);
                var mm = motes.main; mm.loop = true; mm.duration = 1f;
                mm.startLifetime = 0.45f; mm.startSpeed = 0f;
                mm.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
                mm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 1f, 0.35f), new Color(0.85f, 1f, 0.55f));
                mm.simulationSpace = ParticleSystemSimulationSpace.Local;
                var mem = motes.emission; mem.rateOverTime = 40f;
                var msh = motes.shape; msh.shapeType = ParticleSystemShapeType.Sphere; msh.radius = 0.7f; msh.radiusThickness = 0f;
                var vel = motes.velocityOverLifetime; vel.enabled = true; vel.radial = new ParticleSystem.MinMaxCurve(-1.5f);
                vel.orbitalY = new ParticleSystem.MinMaxCurve(3f);
                var mc = motes.colorOverLifetime; mc.enabled = true; mc.color = Grad(Color.white, Color.white, 0.2f, 1f);

                var leaves = LeafPS("Leaves", out _);
                leaves.transform.SetParent(root.transform, false);
                var lm = leaves.main; lm.loop = true; lm.duration = 1f;
                lm.startLifetime = 0.55f; lm.startSpeed = 0f;
                lm.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.11f);
                lm.simulationSpace = ParticleSystemSimulationSpace.Local;
                var lem = leaves.emission; lem.rateOverTime = 9f;
                var lsh = leaves.shape; lsh.shapeType = ParticleSystemShapeType.Sphere; lsh.radius = 0.85f; lsh.radiusThickness = 0f;
                var lv = leaves.velocityOverLifetime; lv.enabled = true; lv.radial = new ParticleSystem.MinMaxCurve(-1.5f);
                lv.orbitalY = new ParticleSystem.MinMaxCurve(4f);
                var lc = leaves.colorOverLifetime; lc.enabled = true; lc.color = Grad(Color.white, Color.white, 0f, 1f);
                SavePS(root, "FX_SpellGather");
            }
        }

        // ------------------------------------------------------------------ prefabs: Vine Seeker, burst, orb
        static LineRenderer VineLine(string name, Transform parent, AnimationCurve widthCurve, bool world)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = M("M_DruidVine");
            lr.useWorldSpace = world;
            lr.positionCount = 0;
            lr.widthCurve = widthCurve;
            lr.widthMultiplier = 0.3f;
            lr.textureMode = LineTextureMode.Tile;
            lr.textureScale = new Vector2(1.5f, 1f);
            lr.alignment = LineAlignment.View;
            lr.numCornerVertices = 0;
            lr.numCapVertices = 0;
            lr.generateLightingData = true;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = true;
            return lr;
        }

        static Light GreenLight(Transform parent, Vector3 local, float intensity, float range)
        {
            var go = new GameObject("Glow");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.55f, 1f, 0.35f);
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            return l;
        }

        static void BuildDruidMaskPrefabs()
        {
            // ---- the burst: vines growing out of whatever the spell struck (thick at the root, thin at the tip)
            {
                var root = new GameObject("VineBurst");
                var vb = root.AddComponent<VineBurst>();
                vb.template = VineLine("Tendril", root.transform, new AnimationCurve(new Keyframe(0, 1.1f), new Keyframe(0.6f, 0.8f), new Keyframe(1, 0.2f)), false);
                PrefabUtility.SaveAsPrefabAsset(root, VineBurstPrefabPath);
                Object.DestroyImmediate(root);
            }
            // ---- the giant's stomp ring: vines rising out of the dirt (thick at the root, thin at the tip)
            {
                var root = new GameObject("VineRing");
                var ring = root.AddComponent<VineRing>();
                ring.template = VineLine("Vine", root.transform, new AnimationCurve(new Keyframe(0, 1.2f), new Keyframe(0.55f, 0.85f), new Keyframe(1, 0.15f)), true);
                PrefabUtility.SaveAsPrefabAsset(root, VineRingPrefabPath);
                Object.DestroyImmediate(root);
            }
            // ---- Vine Seeker: the braided bundle (thin at the head, full through the body, tapering tail)
            {
                var root = new GameObject("VineSeeker");
                var vs = root.AddComponent<VineSeeker>();
                vs.strandTemplate = VineLine("Strand", root.transform,
                    new AnimationCurve(new Keyframe(0, 0.35f), new Keyframe(0.15f, 1f), new Keyframe(0.7f, 0.85f), new Keyframe(1, 0.12f)), true);
                var trail = LeafPS("LeafTrail", out _);
                trail.transform.SetParent(root.transform, false);
                var tm = trail.main;
                tm.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.7f);
                tm.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1f);
                tm.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.13f);
                tm.gravityModifier = 0.25f;
                tm.loop = true;
                var tem = trail.emission; tem.rateOverDistance = 5f;
                var tsh = trail.shape; tsh.shapeType = ParticleSystemShapeType.Sphere; tsh.radius = 0.15f;
                var tn = trail.noise; tn.enabled = true; tn.strength = 0.6f; tn.frequency = 0.8f;
                var tc = trail.colorOverLifetime; tc.enabled = true; tc.color = Grad(Color.white, Color.white, 1f, 0f);
                vs.leafTrail = trail;
                vs.glowLight = GreenLight(root.transform, Vector3.zero, 2.2f, 5f);
                vs.burstPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VineBurstPrefabPath)?.GetComponent<VineBurst>();
                vs.hitMask = (1 << 0) | (1 << LayerHurtbox) | (1 << LayerEnemy);
                PrefabUtility.SaveAsPrefabAsset(root, VineSeekerPrefabPath);
                Object.DestroyImmediate(root);
            }
            // ---- the orb: faceted glowing core, two leafy vine rings, a halo, a green light, a trail of motes
            {
                var root = new GameObject("DruidOrb");
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DruidOrbModelPath));
                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                // the FBX is modelled at ~0.5 m core radius: fit it to the gameplay size (0.32 m)
                model.transform.localScale = Vector3.one * 0.64f;
                foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
                var orb = root.AddComponent<DruidOrb>();
                var ts = model.GetComponentsInChildren<Transform>(true);
                orb.core = ts.FirstOrDefault(t => t.name == "Core");
                orb.rings = ts.Where(t => t.name.StartsWith("Ring")).ToArray();

                var halo = NewPS("Halo", M("M_FX_GreenGlow"), out var hr);
                halo.transform.SetParent(root.transform, false);
                var hm = halo.main; hm.loop = true; hm.playOnAwake = true; hm.duration = 1f; hm.startLifetime = 0.16f; hm.startSpeed = 0f;
                hm.startSize = new ParticleSystem.MinMaxCurve(1.4f, 1.8f);
                hm.startColor = new Color(0.5f, 1f, 0.32f, 0.6f);
                hm.simulationSpace = ParticleSystemSimulationSpace.Local;
                hm.scalingMode = ParticleSystemScalingMode.Hierarchy;
                var hem = halo.emission; hem.rateOverTime = 24f;
                var hsh = halo.shape; hsh.enabled = false;
                var hc = halo.colorOverLifetime; hc.enabled = true; hc.color = Grad(Color.white, Color.white, 1f, 0f);
                halo.Play();
                orb.glowSprite = hr;

                orb.glow = GreenLight(root.transform, Vector3.zero, 2.4f, 7f);

                var trailGo = new GameObject("Trail");
                trailGo.transform.SetParent(root.transform, false);
                var trail = trailGo.AddComponent<TrailRenderer>();
                trail.time = 0.32f; trail.minVertexDistance = 0.08f;
                trail.widthCurve = AnimationCurve.EaseInOut(0, 0.55f, 1, 0f);
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(0.7f, 1f, 0.5f), 0), new GradientColorKey(new Color(0.25f, 0.6f, 0.15f), 1) },
                          new[] { new GradientAlphaKey(0.7f, 0), new GradientAlphaKey(0f, 1) });
                trail.colorGradient = g;
                trail.sharedMaterial = M("M_FX_GreenGlow");
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.emitting = false;
                orb.trail = trail;

                var wisps = NewPS("Wisps", M("M_FX_GreenGlow"), out _);
                wisps.transform.SetParent(root.transform, false);
                var wm = wisps.main; wm.loop = true; wm.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
                wm.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
                wm.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
                wm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 1f, 0.35f), new Color(0.85f, 1f, 0.55f));
                var wem = wisps.emission; wem.rateOverDistance = 9f;
                var wsh = wisps.shape; wsh.shapeType = ParticleSystemShapeType.Sphere; wsh.radius = 0.3f;
                var wn = wisps.noise; wn.enabled = true; wn.strength = 1f; wn.frequency = 1.2f;
                var wc = wisps.colorOverLifetime; wc.enabled = true; wc.color = Grad(Color.white, Color.white, 1f, 0f);
                orb.wisps = wisps;
                orb.hitMask = (1 << 0) | (1 << LayerHurtbox) | (1 << LayerEnemy);
                PrefabUtility.SaveAsPrefabAsset(root, DruidOrbPrefabPath);
                Object.DestroyImmediate(root);
            }
        }

        // ------------------------------------------------------------------ data assets (create once)
        static void BuildDruidMaskData()
        {
            var spell = DataAsset<SpellData>(SpellVineSeekerPath, d => { d.displayName = "Vine Seeker"; });
            var orb = DataAsset<OrbData>(OrbVerdantPath, d => { d.displayName = "Verdant Orb"; });
            var grove = DataAsset<GrowthStompData>(SpecialGrovePath, d => { d.displayName = "Wrath of the Grove"; d.staminaCost = 25f; });
            var form = DataAsset<FormData>(FormDruidPath, d =>
            {
                d.displayName = "Druid"; d.rigId = "Druid"; d.rangedKind = RangedKind.Orb;
                d.moveSpeedMultiplier = 0.98f; d.rollDistanceMultiplier = 1f; d.damageTakenMultiplier = 1.05f; d.ignoreLightFlinch = false;
                d.roar = Audio.Sfx.WoodGroan; d.roarPitch = 1.55f;
            });
            var mask = DataAsset<MaskData>(MaskDruidPath, d =>
            {
                d.id = "druid"; d.displayName = "Druid Mask";
                d.description = "Carved from the felled Druid's face, still sprouting. Wear it to become the druid: vines that hunt " +
                                "down what you point at, and a ball of the forest's green fire.";
                d.vortexA = new Color(0.46f, 0.68f, 0.22f);
                d.vortexB = new Color(0.12f, 0.26f, 0.08f);
                d.vortexC = new Color(0.03f, 0.06f, 0.03f);
                d.receivedTitle = "DRUID MASK";
                d.receivedLine = "You put on the Druid Mask.\nThe forest answers you now.\nRB / RT: cast (hold RT to charge) · LT + RT: Verdant Orb · RB + RT: Wrath of the Grove";
            });
            if (spell.icon == null) spell.icon = RenderIcon(DruidStaffPath, "Icon_DruidStaff", Quaternion.Euler(0, 0, -40));
            if (orb.icon == null) orb.icon = RenderIcon(DruidOrbModelPath, "Icon_DruidOrb", Quaternion.Euler(-20, 30, 0));
            if (mask.icon == null) mask.icon = RenderIcon(DruidMaskModelPath, "Icon_DruidMask", Quaternion.Euler(-10, 200, 0));
            if (form.magic == null) form.magic = spell;
            if (form.orb == null) form.orb = orb;
            if (form.special == null) form.special = grove;
            if (mask.form == null) mask.form = form;
            if (mask.maskModel == null) mask.maskModel = AssetDatabase.LoadAssetAtPath<GameObject>(DruidMaskModelPath);
            foreach (var o in new Object[] { spell, orb, grove, form, mask }) EditorUtility.SetDirty(o);
        }

        // ------------------------------------------------------------------ player: druid body
        /// <summary>Adds the druid form rig under the player's Visual (called from AttachForms). Null if the model is missing.</summary>
        static FormRig AttachDruidForm(Transform visual, PlayerController pc)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(DruidFormModelPath);
            if (src == null) { Debug.LogError("[ElderTree] DruidForm.fbx missing: run build_druid_form.py"); return null; }
            var model = (GameObject)PrefabUtility.InstantiatePrefab(src);
            model.name = "DruidForm";
            model.transform.SetParent(visual, false);
            var anim = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            anim.avatar = AssetDatabase.LoadAllAssetsAtPath(DruidFormModelPath).OfType<Avatar>().First();
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.updateWhenOffscreen = true;
                smr.shadowCastingMode = ShadowCastingMode.On;
            }

            // the leaf robe: each clump rides the bone it grows on (by name, or its mirror twin if the import swapped sides)
            var leavesSrc = AssetDatabase.LoadAssetAtPath<GameObject>(DruidFormLeavesPath);
            var leafList = new List<Transform>();
            if (leavesSrc != null)
            {
                var leavesGo = (GameObject)PrefabUtility.InstantiatePrefab(leavesSrc);
                leavesGo.transform.SetParent(model.transform, false);
                PrefabUtility.UnpackPrefabInstance(leavesGo, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var clumps = leavesGo.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("DLeaf_")).ToArray();
                // the leaves must face the same way as the body: every clump against the bone it is named after (the
                // arms and legs make this unambiguous; the head alone is front/back symmetric and fooled the old test)
                var named = clumps.Select(t => (t, bone: BoneByName(anim, LeafBoneName(t.name)))).Where(p => p.bone != null).ToArray();
                if (named.Length > 0)
                {
                    float Err() => named.Average(p => Vector3.Distance(p.t.position, p.bone.position));
                    float e0 = Err();
                    leavesGo.transform.localRotation = Quaternion.Euler(0, 180, 0);
                    float e1 = Err();
                    if (e1 >= e0) leavesGo.transform.localRotation = Quaternion.identity;
                    Debug.Log($"[ElderTree] Druid leaves: mean clump-to-bone {Mathf.Min(e0, e1):F2} m ({(e1 < e0 ? "turned 180" : "as exported")})");
                }
                foreach (var t in clumps)
                {
                    var bone = BoneByName(anim, LeafBoneName(t.name));
                    if (bone == null) { Debug.LogWarning($"[ElderTree] No bone for {t.name}"); continue; }
                    t.SetParent(bone, true);
                    var r = t.GetComponent<Renderer>();
                    if (r) r.shadowCastingMode = ShadowCastingMode.On;
                    leafList.Add(t);
                }
                Object.DestroyImmediate(leavesGo);
            }
            var sway = model.AddComponent<LeafSway>();
            sway.leaves = leafList.ToArray();

            // the staff, held upright in the right fist (calibrated on the idle pose: shaft up, tipped a little forward)
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var staff = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DruidStaffPath));
            staff.name = "DruidStaff";
            staff.transform.SetParent(hand, false);
            CalibrateStaffGrip(staff.transform);
            foreach (var r in staff.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            var focus = staff.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Focus") ?? staff.transform;
            var gatherSrc = AssetDatabase.LoadAssetAtPath<GameObject>(SpellGatherPrefabPath);
            ParticleSystem gather = null;
            if (gatherSrc != null)
            {
                var g = (GameObject)PrefabUtility.InstantiatePrefab(gatherSrc);
                g.transform.SetParent(focus, false);
                gather = g.GetComponent<ParticleSystem>();
            }
            var focusLight = GreenLight(focus, Vector3.zero, 0.4f, 3.2f);

            var fr = model.AddComponent<FormRig>();
            fr.rigId = "Druid";
            fr.animator = anim;
            fr.spellFocus = focus;
            fr.spellGather = gather;
            fr.focusLight = focusLight;
            fr.leafSway = sway;
            fr.faceSocket = Socket("FaceSocket", anim.GetBoneTransform(HumanBodyBones.Head), DruidFacePos, Quaternion.identity);
            model.SetActive(false);

            pc.vineSeekerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VineSeekerPrefabPath)?.GetComponent<VineSeeker>();
            pc.orbPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DruidOrbPrefabPath)?.GetComponent<DruidOrb>();
            pc.castClipLength = StateClipLength("Druid_Cast");
            pc.chargeCastClipLength = StateClipLength("Druid_ChargeCast");
            pc.orbCastClipLength = StateClipLength("Druid_OrbCast");
            pc.vineRingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VineRingPrefabPath)?.GetComponent<VineRing>();
            pc.stompRaiseClipLength = StateClipLength("Druid_StompRaise_R");
            return fr;
        }

        /// <summary>
        /// Staff grip: sample the 1H idle on a temporary druid, stand the shaft up at the right fist (tipped 12 deg forward
        /// and a little out to the side, crook forward), and store that relative to the hand. Every clip then carries the
        /// staff with the hand.
        /// </summary>
        static void CalibrateStaffGrip(Transform staff)
        {
            var tmp = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(DruidFormModelPath));
            tmp.hideFlags = HideFlags.DontSave;
            try
            {
                var a = tmp.GetComponent<Animator>() ?? tmp.AddComponent<Animator>();
                a.avatar = AssetDatabase.LoadAllAssetsAtPath(DruidFormModelPath).OfType<Avatar>().First();
                var clip = K("Combat/1H/HumanM@CombatIdle1H01");
                clip.SampleAnimation(tmp, 0f);
                tmp.transform.position = Vector3.zero;
                var rh = a.GetBoneTransform(HumanBodyBones.RightHand);
                Vector3 fwd = tmp.transform.forward;
                Vector3 outward = rh.position.x - tmp.transform.position.x >= 0 ? tmp.transform.right : -tmp.transform.right;
                Vector3 up = (Vector3.up + fwd * 0.21f + outward * 0.08f).normalized;
                Quaternion want = Quaternion.LookRotation(Vector3.ProjectOnPlane(fwd, up).normalized, up);
                staff.localPosition = rh.InverseTransformPoint(FistCentre(a, false));
                staff.localRotation = Quaternion.Inverse(rh.rotation) * want;
                staff.localScale = Vector3.one;
            }
            finally { Object.DestroyImmediate(tmp); }
        }

        /// <summary>"DLeaf_LeftUpperArm_012" -> "LeftUpperArm".</summary>
        static string LeafBoneName(string clump)
        {
            string rest = clump.Substring("DLeaf_".Length);
            return rest.Substring(0, rest.LastIndexOf('_'));
        }

        /// <summary>The humanoid bone named e.g. "LeftUpperArm" (the FBX bone names are the human names without spaces).</summary>
        static Transform BoneByName(Animator anim, string name)
        {
            if (System.Enum.TryParse<HumanBodyBones>(name, out var hb))
            {
                var b = anim.GetBoneTransform(hb);
                if (b != null) return b;
            }
            return anim.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        }

        // ------------------------------------------------------------------ scene
        static void PlaceDruidSet(GameObject systems)
        {
            // the Druid now drops the Druid Mask
            var enc = systems.GetComponent<BossEncounter>();
            var mask = AssetDatabase.LoadAssetAtPath<MaskData>(MaskDruidPath);
            if (enc != null)
                foreach (var b in enc.bosses)
                    if (b is DruidBoss d) { d.rewardMask = mask; EditorUtility.SetDirty(d); }
        }
    }
}
