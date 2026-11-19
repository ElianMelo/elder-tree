using System.Collections.Generic;
using System.IO;
using System.Linq;
using ElderTree.Combat;
using ElderTree.Enemies;
using ElderTree.Equipment;
using ElderTree.FX;
using ElderTree.Masks;
using ElderTree.Player;
using ElderTree.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace ElderTree.EditorTools
{
    /// <summary>
    /// The Shark Mask set (Shark reward): the humanoid shark body, the long sword (Monster Hunter style,
    /// slashes and cuts vines), the crossbow and its bolts, the Shark Tornado spin special, the mask item,
    /// the cuttable vines (a curtain plugging an arena gap and bramble thickets), slash / leaf effects.
    /// Models come from Tools/Blender/build_shark_form.py (exported into Art/Models/Shark).
    ///
    /// Data assets (Data/Forms/Form_Shark, Data/Equipment/*Shark*, Data/Masks/Mask_Shark, Data/SharkSwordMoveset)
    /// are only CREATED when missing; existing ones keep their tuned values (missing references are filled in).
    /// </summary>
    public static partial class ElderTreeBuilder
    {
        public const string SharkDir = Root + "/Art/Models/Shark";
        public const string SharkModelPath = SharkDir + "/SharkForm.fbx";
        public const string SwordPath = SharkDir + "/SharkSword.fbx";
        public const string CrossbowPath = SharkDir + "/SharkCrossbow.fbx";
        public const string BoltModelPath = SharkDir + "/SharkBolt.fbx";
        public const string SharkMaskModelPath = SharkDir + "/SharkMask.fbx";
        public const string VinesPath = SharkDir + "/Vines.fbx";
        public const string SharkMovesetPath = DataDir + "/SharkSwordMoveset.asset";
        public const string BoltPrefabPath = Root + "/Prefabs/Bolt.prefab";
        public const string CuttableDir = Root + "/Prefabs/Cuttables";
        public const string VineCurtainPrefabPath = CuttableDir + "/VineCurtain.prefab";
        public const string VineThicketPrefabPath = CuttableDir + "/VineThicket.prefab";
        public const string FormSharkPath = DataDir + "/Forms/Form_Shark.asset";
        public const string MaskSharkPath = DataDir + "/Masks/Mask_Shark.asset";

        static readonly string[] SharkMats =
        {
            "M_SharkSkin", "M_SharkBelly", "M_SharkEye", "M_CrocMouth", "M_CrocTeeth", "M_SneakerCloth", "M_SneakerSole",
            "M_Leather", "M_Iron", "M_Rope", "M_DarkWood", "M_BowString", "M_SharkSteel", "M_SharkEdge", "M_Vine", "M_VineLeaf", "M_Bark"
        };

        // Bind-space slots on the shark (feet at the origin, facing +Z). The dorsal fin owns the middle of the
        // back, so the sword rides at the left hip like a samurai's and the crossbow hangs on the right thigh.
        public static Vector3 SwordHipPos = new Vector3(0.26f, 0.98f, 0.06f);
        public static Vector3 SwordHipDir = new Vector3(0.12f, -0.45f, -1f);
        public static Vector3 CrossbowHipPos = new Vector3(-0.27f, 1.0f, -0.08f);
        public static Vector3 CrossbowHipFwd = new Vector3(0.08f, -1f, -0.45f);
        public static Vector3 SharkFacePos = new Vector3(0f, 1.72f, 0.24f);

        [MenuItem("Elder Tree/8b. Build Shark Mask set")]
        public static string BuildSharkSet()
        {
            BuildSharkArt();
            ConfigureSharkImport();
            BuildSharkMoveset();
            BuildSharkFX();
            BuildSharkPrefabs();
            BuildSharkData();
            AssetDatabase.SaveAssets();
            return "Shark Mask set built";
        }

        // ------------------------------------------------------------------ textures + materials
        static void BuildSharkArt()
        {
            var Hex = (System.Func<string, Color>)TextureForge.Hex;
            // blade steel: dark blue-grey with a faint folded-steel grain running along the blade
            var steel = TextureForge.SavePsx("T_SharkSteel", 128, (u, v, x, y) =>
            {
                float w = TextureForge.Fbm(u, v, 3, 6, 3, 1501);
                float grain = 0.5f + 0.5f * Mathf.Sin((v * 9f + w * 2.4f) * Mathf.PI * 2f);
                var c = Mx(Hex("#232A31"), Hex("#4C5862"), 0.3f + 0.35f * grain + 0.2f * TextureForge.Fbm(u, v, 6, 6, 3, 1502));
                c = Mx(c, Hex("#3A2A22"), Sstep(0.68f, 0.82f, TextureForge.Fbm(u, v, 7, 7, 3, 1503)) * 0.45f);   // old rust bloom
                return c * (0.92f + 0.16f * (H2(x, y, 1504) - 0.5f));
            });
            // the edge: pale honed steel with fine scratches
            var edge = TextureForge.SavePsx("T_SharkEdge", 64, (u, v, x, y) =>
            {
                var c = Mx(Hex("#7E888C"), Hex("#B4BCBE"), 0.4f + 0.4f * TextureForge.Fbm(u, v, 4, 4, 3, 1511));
                if ((x + y * 3) % 11 == 0) c *= 0.82f;
                return c * (0.94f + 0.12f * (H2(x, y, 1512) - 0.5f));
            });
            // vine stalk: twisted green-brown fibres
            var vine = TextureForge.SavePsx("T_Vine", 64, (u, v, x, y) =>
            {
                float twist = 0.5f + 0.5f * Mathf.Sin((u * 4f + v * 7f + TextureForge.Fbm(u, v, 3, 3, 2, 1521)) * Mathf.PI * 2f);
                var c = Mx(Hex("#1B2210"), Hex("#3E4422"), 0.25f + 0.55f * twist);
                c = Mx(c, Hex("#2C2216"), Sstep(0.55f, 0.75f, TextureForge.Fbm(u, v, 5, 5, 3, 1522)) * 0.6f);
                return c * (0.9f + 0.2f * (H2(x, y, 1523) - 0.5f));
            });
            // leaves: dark, waxy, with pale veins
            var leaf = TextureForge.SavePsx("T_VineLeaf", 64, (u, v, x, y) =>
            {
                var c = Mx(Hex("#14200C"), Hex("#2E4318"), 0.3f + 0.5f * TextureForge.Fbm(u, v, 4, 4, 3, 1531));
                float vein = Mathf.Abs(Mathf.Sin((u * 5f + v * 2f) * Mathf.PI));
                if (vein < 0.08f) c = Mx(c, Hex("#4E5A2C"), 0.6f);
                c = Mx(c, Hex("#3A2416"), Sstep(0.7f, 0.85f, TextureForge.Fbm(u, v, 6, 6, 2, 1532)) * 0.5f);   // blight
                return c * (0.9f + 0.2f * (H2(x, y, 1533) - 0.5f));
            });
            Matte(Lit("M_SharkSteel", Color.white, steel, null, 0f, 1f, null, false));
            Matte(Lit("M_SharkEdge", Color.white, edge, null, 0f, 1f, null, false));
            Matte(Lit("M_Vine", Color.white, vine, null, 0f, 1f, null, false));
            Matte(Lit("M_VineLeaf", Color.white, leaf, null, 0f, 1f, null, false));

            // slash crescent: bright along the outer (cutting) edge, falling off toward the inside, streaked
            var slash = TextureForge.Save("T_FX_Slash", 128, 32, (x, y) =>
            {
                float u = x / 127f, v = y / 31f;
                float core = Mathf.Pow(v, 2.2f);
                float line = Mathf.Exp(-Mathf.Pow((v - 0.9f) / 0.06f, 2f));
                float streak = 0.75f + 0.25f * TextureForge.Fbm(u * 3f, v * 0.5f, 8, 2, 3, 1541);
                float a = Mathf.Clamp01((core * 0.7f + line) * streak) * Mathf.Clamp01(Mathf.Min(u, 1f - u) * 12f);
                return new Color(1f, 1f, 1f, a);
            }, aniso: 0);
            var imp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(slash));
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.alphaIsTransparency = true;
            imp.filterMode = FilterMode.Bilinear;
            imp.mipmapEnabled = false;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            Unlit("M_FX_Slash", Color.white, slash, additive: true);
        }

        // ------------------------------------------------------------------ import
        static void ConfigureSharkImport()
        {
            foreach (var path in new[] { SharkModelPath, SwordPath, CrossbowPath, BoltModelPath, SharkMaskModelPath, VinesPath })
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ConfigureHumanoid(SharkModelPath, SharkMats);
            foreach (var path in new[] { SwordPath, CrossbowPath, BoltModelPath, SharkMaskModelPath, VinesPath })
            {
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                if (imp == null) { Debug.LogError($"[ElderTree] Missing {path} (run Tools/Blender/build_shark_form.py)"); continue; }
                imp.globalScale = 1f;
                imp.useFileScale = true;
                imp.animationType = ModelImporterAnimationType.None;
                imp.importAnimation = false;
                imp.importBlendShapes = false;
                imp.importCameras = false;
                imp.importLights = false;
                imp.importNormals = ModelImporterNormals.Calculate;
                imp.normalSmoothingAngle = 30f;
                imp.importTangents = ModelImporterTangents.None;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                imp.addCollider = false;
                foreach (var n in SharkMats)
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");
                    if (m) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), m);
                }
                imp.SaveAndReimport();
            }
        }

        // ------------------------------------------------------------------ long sword moveset
        public const int SharkMovesetVersion = 5;

        /// <summary>
        /// Long sword, after Monster Hunter's. Light alternates directions (kesa right-to-left, the same kesa mirrored
        /// left-to-right, thrust, rising right-to-left) and builds spirit. Heavy is katana work: iai (stance at the
        /// hip, hold to charge, then draw and cut with a lunge), helm-splitter, kesa from the upper right, then the
        /// Spirit Roundslash (spirited and landing, it raises the gauge level). Run attack = diagonal lunge, roll attack =
        /// fade slash. bladeRoll per swing keeps the edge leading the cut.
        /// Hit windows and sweep directions were measured on the shark rig (blade-tip yaw / speed per clip).
        /// </summary>
        static void BuildSharkMoveset()
        {
            var ms = AssetDatabase.LoadAssetAtPath<Moveset>(SharkMovesetPath);
            bool create = ms == null;
            if (create) ms = ScriptableObject.CreateInstance<Moveset>();
            if (create || ms.version < SharkMovesetVersion)
            {
                FillSharkMoveset(ms);
                ms.version = SharkMovesetVersion;
            }
            if (create) AssetDatabase.CreateAsset(ms, SharkMovesetPath);
            foreach (var a in ms.lightCombo.Concat(ms.heavyCombo).Concat(new[] { ms.runningAttack, ms.jumpAttack, ms.dodgeAttack, ms.skill }))
                if (a != null) a.clipLength = StateClipLength(a.state);
            EditorUtility.SetDirty(ms);
        }

        static void FillSharkMoveset(Moveset ms)
        {
            ms.lightCombo = new[]
            {
                // kesa: high right, down across to the left hip
                new AttackData { id = "S_Light1", state = "Sword_L1", damage = 42, poiseDamage = 15, staminaCost = 14, weight = ImpactWeight.Light,
                    hitStart = 0.29f, hitEnd = 0.44f, comboOpen = 0.48f, cancelOpen = 0.52f, exitTime = 0.72f, trackUntil = 0.26f,
                    windupSpeed = 1.2f, activeSpeed = 1.35f, recoverySpeed = 1.3f, lungeDistance = 0.9f, lungeStart = 0.18f, lungeEnd = 0.40f,
                    hitStop = 0.065f, shake = 0.4f, next = "S_Light2" },
                // the same kesa mirrored: high left, down across to the right hip (answers Light 1 stroke for stroke)
                new AttackData { id = "S_Light2", state = "Sword_L2", damage = 42, poiseDamage = 15, staminaCost = 14, weight = ImpactWeight.Light,
                    hitStart = 0.29f, hitEnd = 0.44f, comboOpen = 0.48f, cancelOpen = 0.52f, exitTime = 0.72f, trackUntil = 0.26f,
                    windupSpeed = 1.2f, activeSpeed = 1.35f, recoverySpeed = 1.3f, lungeDistance = 0.9f, lungeStart = 0.18f, lungeEnd = 0.40f,
                    hitStop = 0.065f, shake = 0.4f, bladeRoll = 120f, next = "S_Light3" },   // mirrored hand turns the blade over
                // thrust: the long reach of the blade
                new AttackData { id = "S_Light3", state = "Sword_L3", damage = 40, poiseDamage = 14, staminaCost = 14, weight = ImpactWeight.Light,
                    hitStart = 0.24f, hitEnd = 0.35f, comboOpen = 0.48f, cancelOpen = 0.52f, exitTime = 0.72f, trackUntil = 0.2f,
                    windupSpeed = 1.15f, activeSpeed = 1.3f, recoverySpeed = 1.3f, lungeDistance = 1.3f, lungeStart = 0.05f, lungeEnd = 0.30f,
                    hitStop = 0.06f, shake = 0.35f, next = "S_Light4" },
                // rising slash: low right, sweeping up across to the left
                new AttackData { id = "S_Light4", state = "Sword_L4", startOffset = 0.03f, damage = 50, poiseDamage = 19, staminaCost = 16,
                    weight = ImpactWeight.Heavy, hitStart = 0.15f, hitEnd = 0.29f, comboOpen = 0.42f, cancelOpen = 0.46f, exitTime = 0.66f,
                    trackUntil = 0.13f, windupSpeed = 1.1f, activeSpeed = 1.2f, recoverySpeed = 1.3f, lungeDistance = 0.7f, lungeStart = 0.05f,
                    lungeEnd = 0.26f, hitStop = 0.075f, shake = 0.5f, next = "S_Light1" },
            };
            ms.heavyCombo = new[]
            {
                // iai: the hand settles on the hilt at the left hip with the blade sheathed along the leg (hold heavy to stay
                // in the stance and charge), then the blade is drawn straight up over the shoulder and cut down across
                // with a deep lunge
                new AttackData { id = "S_Iai", state = "Sword_H1", startOffset = 0.22f, damage = 72, poiseDamage = 32, staminaCost = 20,
                    weight = ImpactWeight.Heavy, hitStart = 0.25f, hitEnd = 0.34f, comboOpen = 0.55f, cancelOpen = 0.58f, exitTime = 0.74f,
                    trackUntil = 0.24f, trackTurnSpeed = 720f, windupSpeed = 1.0f, activeSpeed = 0.9f, recoverySpeed = 1.25f, chargeable = true,
                    chargeMaxTime = 1.1f, chargeDamageMultiplier = 1.8f, lungeDistance = 2.2f, lungeStart = 0.22f, lungeEnd = 0.33f,
                    hitStop = 0.11f, shake = 0.7f, rumbleLow = 0.55f, rumbleHigh = 0.7f,
                    stanceState = "Sword_IaiStance", stanceNorm = 0.5f, stanceTime = 0.2f, drawFade = 0.07f, next = "S_Helm" },
                // helm-splitter: straight overhead cleave (the slam swings spine-first, so the blade turns over: edge down)
                new AttackData { id = "S_Helm", state = "Sword_H2", startOffset = 0.1f, damage = 76, poiseDamage = 34, staminaCost = 21,
                    weight = ImpactWeight.Slam, hitStart = 0.26f, hitEnd = 0.38f, comboOpen = 0.5f, cancelOpen = 0.55f, exitTime = 0.68f,
                    trackUntil = 0.24f, windupSpeed = 1.3f, activeSpeed = 0.8f, recoverySpeed = 1.4f, lungeDistance = 1.0f, lungeStart = 0.2f,
                    lungeEnd = 0.34f, hitStop = 0.12f, shake = 0.8f, rumbleLow = 0.65f, rumbleHigh = 0.6f, bladeRoll = 180f, next = "S_KesaR" },
                // the answering kesa from the upper right: the two cuts cross in an X
                new AttackData { id = "S_KesaR", state = "Sword_H3", startOffset = 0.3f, damage = 82, poiseDamage = 36, staminaCost = 23,
                    weight = ImpactWeight.Slam, hitStart = 0.44f, hitEnd = 0.52f, comboOpen = 0.6f, cancelOpen = 0.62f, exitTime = 0.72f,
                    trackUntil = 0.42f, windupSpeed = 1.3f, activeSpeed = 0.5f, recoverySpeed = 1.4f, lungeDistance = 1.1f, lungeStart = 0.4f,
                    lungeEnd = 0.5f, hitStop = 0.12f, shake = 0.85f, rumbleLow = 0.7f, rumbleHigh = 0.6f, bladeRoll = 10f, next = "S_Roundslash" },
                // Spirit Roundslash: the blade wheels round the body while the body turns with it (clockwise, with the cut);
                // a spirited hit raises the gauge level
                new AttackData { id = "S_Roundslash", state = "Sword_H4", startOffset = 0.06f, damage = 92, poiseDamage = 40, staminaCost = 25,
                    weight = ImpactWeight.Heavy, hitStart = 0.17f, hitEnd = 0.3f, comboOpen = 0.5f, cancelOpen = 0.5f, exitTime = 0.66f,
                    trackUntil = 0.12f, windupSpeed = 1.2f, activeSpeed = 0.75f, recoverySpeed = 1.5f, lungeDistance = 0.8f, lungeStart = 0.12f,
                    lungeEnd = 0.3f, hitStop = 0.12f, shake = 0.9f, rumbleLow = 0.8f, rumbleHigh = 0.7f, spinDegrees = 360f, levelsSpirit = true,
                    next = "S_Iai" },
            };
            ms.runningAttack = new AttackData { id = "S_Run", state = "Sword_Run", damage = 56, poiseDamage = 22, staminaCost = 20,
                weight = ImpactWeight.Heavy, hitStart = 0.25f, hitEnd = 0.33f, comboOpen = 0.55f, cancelOpen = 0.55f, exitTime = 0.72f,
                trackUntil = 0.2f, windupSpeed = 1.15f, activeSpeed = 0.9f, recoverySpeed = 1.3f, lungeDistance = 2.8f, lungeStart = 0.0f,
                lungeEnd = 0.32f, hitStop = 0.09f, shake = 0.6f, next = "S_Light2" };
            ms.jumpAttack = new AttackData { id = "S_Jump", state = "JumpAttack", startOffset = 0.30f, damage = 72, poiseDamage = 34,
                staminaCost = 20, weight = ImpactWeight.Slam, hitStart = 0.40f, hitEnd = 0.52f, comboOpen = 0.66f, cancelOpen = 0.66f,
                exitTime = 0.84f, trackUntil = 0.36f, windupSpeed = 1.1f, activeSpeed = 1.3f, recoverySpeed = 1.2f, lungeDistance = 0.5f,
                lungeStart = 0.3f, lungeEnd = 0.45f, hitStop = 0.11f, shake = 0.85f, rumbleLow = 0.8f, rumbleHigh = 0.6f, next = "S_Light1" };
            // fade slash: cuts while stepping back out of reach (negative lunge)
            ms.dodgeAttack = new AttackData { id = "S_Fade", state = "Sword_Dodge", startOffset = 0.03f, damage = 46, poiseDamage = 16,
                staminaCost = 16, weight = ImpactWeight.Light, hitStart = 0.15f, hitEnd = 0.29f, comboOpen = 0.45f, cancelOpen = 0.45f,
                exitTime = 0.66f, trackUntil = 0.13f, windupSpeed = 1.15f, activeSpeed = 1.2f, recoverySpeed = 1.3f, lungeDistance = -1.4f,
                lungeStart = 0.05f, lungeEnd = 0.32f, hitStop = 0.07f, shake = 0.4f, next = "S_Light1" };
            // unused while the form has a special (light + heavy = Shark Tornado)
            ms.skill = new AttackData { id = "S_Skill", state = "Sword_H4", startOffset = 0.06f, damage = 92, poiseDamage = 40, staminaCost = 25,
                weight = ImpactWeight.Heavy, hitStart = 0.17f, hitEnd = 0.3f, comboOpen = 0.5f, cancelOpen = 0.5f, exitTime = 0.66f,
                trackUntil = 0.12f, spinDegrees = 360f, next = "" };
        }

        // ------------------------------------------------------------------ FX
        static void BuildSharkFX()
        {
            Directory.CreateDirectory($"{PrefabDir}/FX");
            // slash hit: pale streaks flung along the cut, a thin flash line, a few dark droplets
            {
                var ps = NewPS("FX_SlashHit", M("M_FX_Spark"), out var r);
                var m = ps.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(7f, 16f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
                m.gravityModifier = 0.4f;
                m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.95f, 1f), new Color(0.55f, 0.8f, 0.95f));
                Burst(ps, 18);
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 14f; sh.radius = 0.04f;
                var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, new Color(0.5f, 0.75f, 0.9f));
                r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 2f;
                // the line of the cut itself: a stretched flash along the swing
                var line = NewPS("Line", M("M_FX_Glow"), out var lr);
                line.transform.SetParent(ps.transform, false);
                var lm = line.main; lm.startLifetime = 0.09f; lm.startSpeed = 0.01f;
                lm.startSize3D = true;
                lm.startSizeX = new ParticleSystem.MinMaxCurve(1.6f); lm.startSizeY = new ParticleSystem.MinMaxCurve(0.09f); lm.startSizeZ = new ParticleSystem.MinMaxCurve(1f);
                lm.startColor = new Color(0.9f, 0.97f, 1f, 0.95f);
                Burst(line, 1);
                var ls = line.shape; ls.enabled = false;
                lr.renderMode = ParticleSystemRenderMode.Billboard;
                lr.alignment = ParticleSystemRenderSpace.Local;
                var lc = line.colorOverLifetime; lc.enabled = true; lc.color = Grad(Color.white, Color.white, 1f, 0f);
                var lsz = line.sizeOverLifetime; lsz.enabled = true; lsz.separateAxes = true;
                lsz.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.6f, 1, 1.3f));
                lsz.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 1f, 1, 0.1f));
                lsz.z = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0, 1, 1f));
                var drops = NewPS("Drops", M("M_FX_Chip"), out var dr);
                drops.transform.SetParent(ps.transform, false);
                var dm = drops.main; dm.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f); dm.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
                dm.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.045f); dm.gravityModifier = 2f;
                dm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.22f, 0.04f, 0.04f), new Color(0.12f, 0.03f, 0.05f));
                Burst(drops, 8);
                var dsh = drops.shape; dsh.shapeType = ParticleSystemShapeType.Cone; dsh.angle = 40f; dsh.rotation = new Vector3(-90, 0, 0);
                SavePS(ps, "FX_SlashHit");
            }
            // leaf burst: torn leaves and bits of stalk drifting down
            {
                var ps = NewPS("FX_LeafBurst", M("M_FX_Chip"), out var r);
                var m = ps.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 2.2f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3.6f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
                m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                m.gravityModifier = 0.35f;
                m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.2f, 0.3f, 0.12f), new Color(0.32f, 0.36f, 0.16f));
                Burst(ps, 18);
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.25f;
                var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
                var noise = ps.noise; noise.enabled = true; noise.strength = 0.8f; noise.frequency = 0.8f;
                var vel = ps.limitVelocityOverLifetime; vel.enabled = true; vel.limit = 1.6f; vel.dampen = 0.15f;
                var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, Color.white, 1f, 0f);
                SavePS(ps, "FX_LeafBurst");
            }
        }

        // ------------------------------------------------------------------ prefabs: bolt, vines
        static GameObject VinePiece(string name)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(VinesPath);
            var child = src.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
            if (child == null) { Debug.LogError($"[ElderTree] {name} missing in Vines.fbx"); return new GameObject(name); }
            var go = Object.Instantiate(child.gameObject);
            go.name = name;
            go.transform.localPosition = Vector3.zero;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        static void BuildSharkPrefabs()
        {
            Directory.CreateDirectory(CuttableDir);

            // ---- crossbow bolt: an ArrowProjectile with the tooth-headed bolt (short, heavy, flat flight)
            {
                var root = new GameObject("Bolt");
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BoltModelPath));
                AlignArrow(root.transform, model);
                foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
                var trailGo = new GameObject("Trail");
                trailGo.transform.SetParent(root.transform, false);
                trailGo.transform.localPosition = new Vector3(0, 0, 0.1f);
                var trail = trailGo.AddComponent<TrailRenderer>();
                trail.time = 0.18f; trail.minVertexDistance = 0.05f;
                trail.widthCurve = AnimationCurve.EaseInOut(0, 0.045f, 1, 0f);
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(0.85f, 0.95f, 1f), 0), new GradientColorKey(new Color(0.45f, 0.7f, 0.85f), 1) },
                          new[] { new GradientAlphaKey(0.65f, 0), new GradientAlphaKey(0f, 1) });
                trail.colorGradient = g;
                trail.sharedMaterial = M("M_FX_Trail");
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.emitting = false;
                var proj = root.AddComponent<ArrowProjectile>();
                proj.trail = trail;
                proj.length = 0.6f;
                proj.hitMask = (1 << 0) | (1 << LayerHurtbox) | (1 << LayerEnemy);
                PrefabUtility.SaveAsPrefabAsset(root, BoltPrefabPath);
                Object.DestroyImmediate(root);
            }

            const float Seg = 0.5f;
            // ---- vine curtain: a mossy lintel on two posts with vines hanging to the ground
            {
                var root = new GameObject("VineCurtain");
                var beam = VinePiece("VineBeam");
                beam.transform.SetParent(root.transform, false);
                foreach (var mf in beam.GetComponentsInChildren<MeshFilter>()) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                var strands = new List<Cuttable.Strand>();
                var rng = new System.Random(77);
                int idx = 0;
                for (float x = -1.7f; x <= 1.71f; x += 0.24f)
                {
                    for (int row = 0; row < 2; row++)
                    {
                        if (row == 1 && rng.NextDouble() < 0.35) continue;
                        float top = 2.7f;
                        var sroot = new GameObject($"Strand{idx++}").transform;
                        sroot.SetParent(root.transform, false);
                        sroot.localPosition = new Vector3(x + (row == 1 ? 0.12f : 0f) + (float)(rng.NextDouble() - 0.5) * 0.08f, top, (row == 0 ? -0.05f : 0.07f));
                        int count = 5 + (rng.NextDouble() < 0.4 ? 0 : -1 + rng.Next(0, 2));
                        var segs = new Transform[count];
                        for (int k = 0; k < count; k++)
                        {
                            var seg = VinePiece($"VineSegment{rng.Next(0, 3)}");
                            seg.name = $"Seg{k}";
                            seg.transform.SetParent(sroot, false);
                            seg.transform.localPosition = new Vector3(0f, -Seg * k, 0f);
                            seg.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                            segs[k] = seg.transform;
                        }
                        strands.Add(new Cuttable.Strand { root = sroot, segments = segs });
                    }
                }
                var solid = root.AddComponent<BoxCollider>();
                solid.center = new Vector3(0f, 1.35f, 0f);
                solid.size = new Vector3(3.6f, 2.7f, 0.35f);
                var hurt = Child("Hurtbox", root.transform, Vector3.zero);
                hurt.layer = LayerHurtbox;
                var hb = hurt.AddComponent<BoxCollider>(); hb.isTrigger = true; hb.center = new Vector3(0f, 1.35f, 0f); hb.size = new Vector3(3.7f, 2.7f, 0.9f);
                hurt.AddComponent<Hurtbox>();
                var cut = root.AddComponent<Cuttable>();
                cut.displayName = "Vine curtain";
                cut.anchoredTop = true;
                cut.strands = strands.ToArray();
                cut.solidColliders = new Collider[] { solid };
                cut.clearFraction = 0.55f;
                PrefabUtility.SaveAsPrefabAsset(root, VineCurtainPrefabPath);
                Object.DestroyImmediate(root);
            }

            // ---- bramble thicket: a root mound with thorny stalks standing up out of it
            {
                var root = new GameObject("VineThicket");
                var mound = VinePiece("ThicketMound");
                mound.transform.SetParent(root.transform, false);
                var strands = new List<Cuttable.Strand>();
                var rng = new System.Random(91);
                for (int i = 0; i < 14; i++)
                {
                    float a = i / 14f * Mathf.PI * 2f + (float)rng.NextDouble() * 0.4f;
                    float rad = 0.15f + (float)rng.NextDouble() * 0.6f;
                    var sroot = new GameObject($"Stalk{i}").transform;
                    sroot.SetParent(root.transform, false);
                    sroot.localPosition = new Vector3(Mathf.Cos(a) * rad, 0.12f, Mathf.Sin(a) * rad);
                    // the segments hang -Y; flipped, they grow up (leaning outward a little)
                    sroot.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f) * Quaternion.Euler(180f - 10f * rad, 0f, 0f);
                    int count = 3 + rng.Next(0, 2);
                    var segs = new Transform[count];
                    for (int k = 0; k < count; k++)
                    {
                        var seg = VinePiece($"VineSegment{rng.Next(0, 3)}");
                        seg.name = $"Seg{k}";
                        seg.transform.SetParent(sroot, false);
                        seg.transform.localPosition = new Vector3(0f, -Seg * k, 0f);
                        seg.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                        segs[k] = seg.transform;
                    }
                    strands.Add(new Cuttable.Strand { root = sroot, segments = segs });
                }
                var solid = root.AddComponent<CapsuleCollider>();
                solid.center = new Vector3(0f, 1f, 0f); solid.radius = 0.85f; solid.height = 2f;
                var hurt = Child("Hurtbox", root.transform, Vector3.zero);
                hurt.layer = LayerHurtbox;
                var hb = hurt.AddComponent<BoxCollider>(); hb.isTrigger = true; hb.center = new Vector3(0f, 1f, 0f); hb.size = new Vector3(2f, 2f, 2f);
                hurt.AddComponent<Hurtbox>();
                var cut = root.AddComponent<Cuttable>();
                cut.displayName = "Bramble thicket";
                cut.anchoredTop = false;
                cut.strands = strands.ToArray();
                cut.solidColliders = new Collider[] { solid };
                cut.clearFraction = 0.6f;
                PrefabUtility.SaveAsPrefabAsset(root, VineThicketPrefabPath);
                Object.DestroyImmediate(root);
            }
        }

        // ------------------------------------------------------------------ data assets (create once)
        static void BuildSharkData()
        {
            var sword = DataAsset<MeleeWeaponData>(DataDir + "/Equipment/Melee_SharkLongSword.asset", d =>
            {
                d.displayName = "Shark Long Sword";
                d.slashing = true; d.cutPower = 1; d.cutWidth = 2.4f;
                d.spiritGauge = true;
                d.hitStopMultiplier = 0.85f;
            });
            var xbow = DataAsset<CrossbowData>(DataDir + "/Equipment/Crossbow_Shark.asset", d => { d.displayName = "Shark Crossbow"; });
            var spin = DataAsset<SpinSlashData>(DataDir + "/Equipment/Special_SharkTornado.asset", d =>
            {
                d.displayName = "Shark Tornado"; d.manaCost = 40f; d.staminaCost = 30f;
            });
            var form = DataAsset<FormData>(FormSharkPath, d =>
            {
                d.displayName = "Shark"; d.rigId = "Shark"; d.rangedKind = RangedKind.Crossbow;
                d.moveSpeedMultiplier = 1.04f; d.rollDistanceMultiplier = 1.05f; d.damageTakenMultiplier = 1f; d.ignoreLightFlinch = false;
                d.roar = Audio.Sfx.SharkRoar; d.roarPitch = 1.15f;
            });
            var mask = DataAsset<MaskData>(MaskSharkPath, d =>
            {
                d.id = "shark"; d.displayName = "Shark Mask";
                d.description = "Peeled from the Shark's scarred hide. Wear it to become the shark: a long sword that cuts through anything green, " +
                                "a crossbow that hits like a bite, and the Shark Tornado.";
                d.vortexA = new Color(0.24f, 0.5f, 0.58f);
                d.vortexB = new Color(0.04f, 0.1f, 0.2f);
                d.vortexC = new Color(0.02f, 0.03f, 0.07f);
                d.receivedTitle = "SHARK MASK";
                d.receivedLine = "You put on the Shark Mask.\nThe Shark's hunger is now yours.";
            });
            if (sword.icon == null) sword.icon = RenderIcon(SwordPath, "Icon_SharkSword", Quaternion.Euler(0, 0, -45));
            if (xbow.icon == null) xbow.icon = RenderIcon(CrossbowPath, "Icon_Crossbow", Quaternion.Euler(-60, 90, 0));
            if (mask.icon == null) mask.icon = RenderIcon(SharkMaskModelPath, "Icon_SharkMask", Quaternion.Euler(-10, 200, 0));
            if (form.moveset == null) form.moveset = AssetDatabase.LoadAssetAtPath<Moveset>(SharkMovesetPath);
            if (form.melee == null) form.melee = sword;
            if (form.crossbow == null) form.crossbow = xbow;
            if (form.special == null) form.special = spin;
            if (mask.form == null) mask.form = form;
            if (mask.maskModel == null) mask.maskModel = AssetDatabase.LoadAssetAtPath<GameObject>(SharkMaskModelPath);
            foreach (var o in new Object[] { sword, xbow, spin, form, mask }) EditorUtility.SetDirty(o);
        }

        // ------------------------------------------------------------------ player: shark body
        /// <summary>Adds the shark form rig under the player's Visual (called from AttachForms). Null if the model is missing.</summary>
        static FormRig AttachSharkForm(Transform visual, PlayerController pc)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(SharkModelPath);
            if (src == null) { Debug.LogError("[ElderTree] SharkForm.fbx missing: run build_shark_form.py"); return null; }
            var model = (GameObject)PrefabUtility.InstantiatePrefab(src);
            model.name = "SharkForm";
            model.transform.SetParent(visual, false);
            var anim = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            // the sword is two-handed: same 2H idle / guard override as the alligator
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(CrocControllerPath);
            anim.runtimeAnimatorController = ctrl != null ? ctrl : AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            anim.avatar = AssetDatabase.LoadAllAssetsAtPath(SharkModelPath).OfType<Avatar>().First();
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.updateWhenOffscreen = true;
                smr.shadowCastingMode = ShadowCastingMode.On;
            }

            // long sword: gripped like the pack's 2H weapons, left fist pinned on the handle by TwoHandGrip
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var sword = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SwordPath));
            sword.name = "LongSword";
            sword.transform.SetParent(hand, false);
            var grip = model.AddComponent<TwoHandGrip>();
            grip.animator = anim;
            grip.weapon = sword.transform;
            CalibrateHammerGrip(sword.transform, grip, SharkModelPath);
            var sBase = new GameObject("BladeBase").transform; sBase.SetParent(sword.transform, false); sBase.localPosition = new Vector3(0, 0.22f, 0);
            var sTip = new GameObject("BladeTip").transform; sTip.SetParent(sword.transform, false); sTip.localPosition = new Vector3(0, 1.58f, 0);
            var hb = sword.AddComponent<WeaponHitbox>();
            hb.hurtboxMask = 1 << LayerHurtbox;
            hb.baseLocal = new Vector3(0, 0.3f, 0);
            hb.tipLocal = new Vector3(0, 1.58f, 0);
            hb.radius = 0.14f;
            hb.maxSubSteps = 10;
            var trail = sword.AddComponent<WeaponTrail>();
            trail.baseRef = sBase; trail.tipRef = sTip;
            trail.material = M("M_FX_Trail");
            trail.lifetime = 0.2f;
            var slash = sword.AddComponent<SlashArc>();
            slash.baseRef = sBase; slash.tipRef = sTip;
            slash.material = M("M_FX_Slash");
            var sRenderer = sword.GetComponentInChildren<MeshRenderer>();
            foreach (var r in sword.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;

            // crossbow: hangs on the right thigh, raised on the Rifle aim pose
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            var xbow = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CrossbowPath));
            xbow.name = "Crossbow";
            foreach (var r in xbow.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            var muzzle = xbow.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Muzzle");
            var loaded = xbow.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "LoadedBolt");
            // which way is the shark's right in this import (Blender's -X): ask the skeleton
            float rs = Mathf.Sign(anim.GetBoneTransform(HumanBodyBones.RightUpperLeg).position.x);
            Vector3 Side(Vector3 v, float sgn) => new Vector3(Mathf.Abs(v.x) * sgn, v.y, v.z);
            var hipSocket = Socket("CrossbowHipSocket", hips, Side(CrossbowHipPos, rs),
                Quaternion.LookRotation(Side(CrossbowHipFwd, rs).normalized, Vector3.right * rs));
            var handSocket = new GameObject("CrossbowHandSocket").transform;
            handSocket.SetParent(hand, false);
            CalibrateCannonGrip(handSocket, SharkModelPath, "Combat/Rifle/HumanM@Rifle_Aim01");
            xbow.transform.SetParent(hipSocket, false);
            var swordHip = Socket("SwordHipSocket", hips, Side(SwordHipPos, -rs), Quaternion.FromToRotation(Vector3.up, Side(SwordHipDir, -rs).normalized));

            var rig = model.AddComponent<CannonRig>();
            rig.player = pc;
            rig.animator = anim;
            rig.cannon = xbow.transform;
            rig.cannonHandSocket = handSocket;
            rig.cannonBackSocket = hipSocket;
            rig.muzzle = muzzle != null ? muzzle : xbow.transform;
            rig.loadedRound = loaded != null ? loaded.gameObject : null;
            rig.hammer = sword.transform;
            rig.hammerBackSocket = swordHip;
            rig.kickDegrees = 7f;
            rig.spinePitchShare = 0.45f;

            var fr = model.AddComponent<FormRig>();
            fr.rigId = "Shark";
            fr.animator = anim;
            fr.weapon = hb; fr.weaponTip = sTip; fr.weaponRenderer = sRenderer; fr.trail = trail; fr.slash = slash;
            fr.cannonRig = rig;
            fr.twoHandGrip = grip;
            fr.faceSocket = Socket("FaceSocket", anim.GetBoneTransform(HumanBodyBones.Head), SharkFacePos, Quaternion.identity);
            model.SetActive(false);

            pc.boltPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoltPrefabPath)?.GetComponent<ArrowProjectile>();
            pc.crossbowReloadClipLength = StateClipLength("CrossbowReload");
            pc.spinClipLength = StateClipLength("Sword_Spin");
            return fr;
        }

        // ------------------------------------------------------------------ scene
        static void PlaceSharkSet(Transform envRoot, GameObject systems)
        {
            // Shark now drops the Shark Mask
            var enc = systems.GetComponent<BossEncounter>();
            var mask = AssetDatabase.LoadAssetAtPath<MaskData>(MaskSharkPath);
            if (enc != null)
                foreach (var b in enc.bosses)
                    if (b is SharkBoss t) { t.rewardMask = mask; EditorUtility.SetDirty(t); }

            // a vine curtain hangs in the other gap of the arena ring; thickets choke a few spots in the yard
            var curtain = AssetDatabase.LoadAssetAtPath<GameObject>(VineCurtainPrefabPath);
            if (curtain != null)
            {
                float a = 10 / 14f * Mathf.PI * 2f + 0.15f;
                Vector3 p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 16.5f;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(curtain);
                go.transform.SetParent(envRoot, false);
                go.transform.SetPositionAndRotation(p, Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90f, 0));
                go.name = "VineCurtain";
            }
            var thicket = AssetDatabase.LoadAssetAtPath<GameObject>(VineThicketPrefabPath);
            if (thicket != null)
            {
                (Vector3 p, float yaw)[] spots = { (new Vector3(7.5f, 0, 9.5f), 20f), (new Vector3(-10.5f, 0, 7.8f), 160f), (new Vector3(3.2f, 0, -12.4f), 300f) };
                for (int i = 0; i < spots.Length; i++)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(thicket);
                    go.transform.SetParent(envRoot, false);
                    go.transform.SetPositionAndRotation(spots[i].p, Quaternion.Euler(0, spots[i].yaw, 0));
                    go.name = $"VineThicket{i}";
                }
            }
        }
    }
}
