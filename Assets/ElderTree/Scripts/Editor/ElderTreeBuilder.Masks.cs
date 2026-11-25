using System.IO;
using System.Linq;
using ElderTree.Combat;
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
    /// The Alligator Mask set (Crocodile reward): the humanoid alligator body, bomb hammer, croc cannon,
    /// the mask item, breakable boulders / cracked walls, their materials, prefabs and data assets.
    /// Models come from Tools/Blender/build_croc_form.py (copied into Art/Models/Croc).
    ///
    /// Data assets (Data/Forms, Data/Equipment, Data/Masks, Data/CrocHammerMoveset) are only CREATED
    /// here when missing; existing ones keep their tuned values (missing references are filled in).
    /// </summary>
    public static partial class ElderTreeBuilder
    {
        public const string CrocDir = Root + "/Art/Models/Croc";
        public const string CrocModelPath = CrocDir + "/CrocForm.fbx";
        public const string HammerPath = CrocDir + "/CrocHammer.fbx";
        public const string CannonPath = CrocDir + "/CrocCannon.fbx";
        public const string MaskModelPath = CrocDir + "/CrocMask.fbx";
        public const string BoulderPath = CrocDir + "/Boulder.fbx";
        public const string WallPath = CrocDir + "/CrackedWall.fbx";
        public const string ChunksPath = CrocDir + "/RockChunks.fbx";
        public const string DataDir = Root + "/Data";
        public const string CrocMovesetPath = DataDir + "/CrocHammerMoveset.asset";
        public const string ShellPrefabPath = Root + "/Prefabs/CannonShell.prefab";
        public const string PickupPrefabPath = Root + "/Prefabs/MaskPickup.prefab";
        public const string BreakableDir = Root + "/Prefabs/Breakables";
        public const string BoulderPrefabPath = BreakableDir + "/Boulder.prefab";
        public const string WallPrefabPath = BreakableDir + "/CrackedWall.prefab";

        static readonly string[] CrocMats =
        {
            "M_CrocHide", "M_CrocBelly", "M_CrocMouth", "M_CrocTeeth", "M_CrocEye", "M_BomberMetal", "M_BomberGlass",
            "M_Iron", "M_BombIron", "M_BombBand", "M_Leather", "M_CrackedStone"
        };

        [MenuItem("Elder Tree/8. Build Alligator Mask set")]
        public static string BuildMaskSet()
        {
            BuildMaskArt();
            ConfigureCrocImport();
            BuildCrocMoveset();
            BuildMaskPrefabs();
            BuildEquipmentData();
            AssetDatabase.SaveAssets();
            return "Alligator Mask set built";
        }

        // ------------------------------------------------------------------ textures + materials
        static void BuildMaskArt()
        {
            var Hex = (System.Func<string, Color>)TextureForge.Hex;
            // belly: rows of pale rectangular scutes (croc belly plates), stained
            var belly = TextureForge.SavePsx("T_CrocBelly", 128, (u, v, x, y) =>
            {
                // irregular belly scutes: Worley cells squashed into rows, olive-ochre, dark seams
                TextureForge.Worley(u, v * 2.2f, 7, 1200, out float f1, out float f2, out int id);
                float seam = Sstep(0.0f, 0.07f, f2 - f1);
                float bump = 1f - Mathf.Clamp01(f1 * 1.8f);
                var c = Mx(Hex("#33321F"), Hex("#686240"), 0.25f + 0.45f * bump + (id % 5) * 0.05f + 0.25f * TextureForge.Fbm(u, v, 6, 6, 3, 1201));
                c *= Mathf.Lerp(0.42f, 1f, seam);
                c = Mx(c, Hex("#3A3424"), Sstep(0.6f, 0.78f, TextureForge.Fbm(u, v, 5, 5, 3, 1202)) * 0.55f);   // grime
                return c * (0.9f + 0.2f * (H2(x, y, 1203) - 0.5f));
            });
            // cracked stone: big blocks with a web of dark fissures (reads "breakable" next to plain walls)
            var cracked = TextureForge.SavePsx("T_CrackedStone", 128, (u, v, x, y) =>
            {
                TextureForge.Worley(u, v, 5, 1211, out float f1, out float f2, out int id);
                float crack = 1f - Sstep(0.0f, 0.035f, f2 - f1);
                TextureForge.Worley(u * 1.7f + 0.3f, v * 1.7f, 9, 1212, out float g1, out float g2, out _);
                float fine = 1f - Sstep(0.0f, 0.02f, g2 - g1);
                float tone = (id % 5) / 5f;
                var c = Mx(Hex("#3A383C"), Hex("#6A666A"), 0.3f + 0.4f * TextureForge.Fbm(u, v, 8, 8, 3, 1213) + tone * 0.2f);
                c = Mx(c, Hex("#4A5236"), Sstep(0.62f, 0.8f, TextureForge.Fbm(u, v, 4, 4, 3, 1214)) * 0.5f);     // moss
                c *= 1f - 0.8f * crack;
                c *= 1f - 0.45f * fine * TextureForge.Fbm(u, v, 3, 3, 2, 1215);
                return c * (0.9f + 0.2f * (H2(x, y, 1216) - 0.5f));
            });
            Matte(Lit("M_CrocBelly", Color.white, belly, null, 0f, 1f, null, false));
            Matte(Lit("M_CrackedStone", Color.white, cracked, null, 0f, 1f, null, false));
            if (AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Leather.mat") == null)
                Lit("M_Leather", Color.white, TextureForge.BuildLeather());

            // vortex: swirling stripes, tileable both ways (scrolled + rotated at runtime)
            var vortex = TextureForge.Save("T_FX_Vortex", 128, 128, (x, y) =>
            {
                float u = x / 128f, v = y / 128f;
                float w = TextureForge.Fbm(u, v, 4, 4, 3, 1221);
                float band = 0.5f + 0.5f * Mathf.Sin((u * 3f + v * 5f + w * 1.6f) * Mathf.PI * 2f);
                float band2 = 0.5f + 0.5f * Mathf.Sin((u * 7f - v * 2f + w * 2.2f) * Mathf.PI * 2f);
                float g = Mathf.Pow(band, 2.2f) * 0.85f + Mathf.Pow(band2, 6f) * 0.5f;
                return TextureForge.Psx(new Color(g, g, g, 1f), x, y);
            }, aniso: 0);
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(vortex));
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.filterMode = FilterMode.Point;
                imp.mipmapEnabled = false;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }
            Unlit("M_FX_Vortex", Color.white, vortex);
            Unlit("M_FX_VortexGlow", new Color(1f, 1f, 1f, 0.5f), vortex, additive: true);
        }

        /// <summary>The bomb's ground shadow (soft dark disc, alpha-blended). Built with the boss art: the bomb prefab needs it.</summary>
        static void BuildBombShadowMaterial()
        {
            // bomb shadow: soft dark disc (alpha-blended; colour and opacity driven by the bomb)
            var shadow = TextureForge.Save("T_FX_Shadow", 64, 64, (x, y) =>
            {
                float u = x / 63f * 2 - 1, v = y / 63f * 2 - 1;
                float r = Mathf.Sqrt(u * u + v * v);
                return new Color(1, 1, 1, r < 1f ? Mathf.Pow(1f - Sstep(0.45f, 1f, r), 1.3f) : 0f);
            }, aniso: 0);
            var imp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(shadow));
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.alphaIsTransparency = true;
            imp.filterMode = FilterMode.Bilinear;
            imp.mipmapEnabled = false;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            var sm = Unlit("M_FX_BombShadow", new Color(0.02f, 0.01f, 0.03f, 0.8f), shadow);
            sm.renderQueue = (int)RenderQueue.Transparent - 20;
        }

        // ------------------------------------------------------------------ model import
        static void ConfigureCrocImport()
        {
            // humanoid alligator: same skeleton and mapping rules as the Elder Tree
            ConfigureHumanoid(CrocModelPath, CrocMats);
            foreach (var path in new[] { HammerPath, CannonPath, MaskModelPath, BoulderPath, WallPath, ChunksPath })
            {
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                if (imp == null) { Debug.LogError($"[ElderTree] Missing {path} (run Tools/Blender/build_croc_form.py)"); continue; }
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
                imp.isReadable = path == ChunksPath || path == BoulderPath || path == WallPath;
                foreach (var n in CrocMats)
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");
                    if (m) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), m);
                }
                imp.SaveAndReimport();
            }
        }

        /// <summary>Humanoid import with the explicit HumanTrait mapping (Blender bone = trait name without spaces).</summary>
        static void ConfigureHumanoid(string path, string[] mats)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            if (imp == null) { Debug.LogError($"[ElderTree] Missing {path}"); return; }
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
            imp.importNormals = ModelImporterNormals.Calculate;
            imp.normalSmoothingAngle = 40f;
            imp.importTangents = ModelImporterTangents.None;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (var n in mats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");
                if (m != null) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), m);
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var names = model.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToHashSet();
            var human = imp.humanDescription;
            human.human = HumanTrait.BoneName
                .Select(hn => new HumanBone { humanName = hn, boneName = hn.Replace(" ", ""), limit = new HumanLimit { useDefaultValues = true } })
                .Where(hb => names.Contains(hb.boneName))
                .ToArray();
            human.upperArmTwist = 0.5f; human.lowerArmTwist = 0.5f;
            human.upperLegTwist = 0.5f; human.lowerLegTwist = 0.5f;
            human.armStretch = 0.05f; human.legStretch = 0.05f;
            human.feetSpacing = 0f;
            human.hasTranslationDoF = false;
            human.skeleton = model.GetComponentsInChildren<Transform>(true)
                .Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale })
                .ToArray();
            imp.humanDescription = human;
            imp.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman) Debug.LogError($"[ElderTree] {path}: avatar invalid");
        }

        // ------------------------------------------------------------------ hammer moveset
        /// <summary>Bump to push a new hammer moveset into an existing CrocHammerMoveset.asset (once).</summary>
        public const int CrocMovesetVersion = 2;

        /// <summary>
        /// Two-handed bomb hammer: every connecting swing explodes (MeleeWeaponData). Both hands stay on the
        /// shaft (TwoHandGrip). Light: three quick 2H sweeps, opening fast. Heavy: two chargeable ground
        /// strikes into a leaping finisher. Windows from PreviewRenderer.AnalyzeCrocSwing (head speed and
        /// ground contact on the alligator with the calibrated grip). Created once; v2 replaced the v1
        /// combos (which reused the club's heavy states, so the hammer had no heavy chain of its own).
        /// </summary>
        static void BuildCrocMoveset()
        {
            var ms = AssetDatabase.LoadAssetAtPath<Moveset>(CrocMovesetPath);
            bool create = ms == null;
            if (create) ms = ScriptableObject.CreateInstance<Moveset>();
            if (create || ms.version < CrocMovesetVersion)
            {
                if (!create) Debug.Log($"[ElderTree] CrocHammerMoveset upgraded v{ms.version} -> v{CrocMovesetVersion} (two-handed light + heavy chains)");
                FillCrocMoveset(ms);
                ms.version = CrocMovesetVersion;
            }
            if (create) AssetDatabase.CreateAsset(ms, CrocMovesetPath);
            // clip lengths always follow the controller (they are measured, not tuned)
            foreach (var a in ms.lightCombo.Concat(ms.heavyCombo).Concat(new[] { ms.runningAttack, ms.jumpAttack, ms.dodgeAttack, ms.skill }))
                if (a != null) a.clipLength = StateClipLength(a.state);
            EditorUtility.SetDirty(ms);
        }

        static void FillCrocMoveset(Moveset ms)
        {
            ms.lightCombo = new[]
            {
                // Attack2H03: low sweep straight out of the guard; the head is moving by 0.17
                new AttackData { id = "H_Light1", state = "Hammer_L1", startOffset = 0.03f, damage = 42, poiseDamage = 18, staminaCost = 17,
                    weight = ImpactWeight.Light, hitStart = 0.15f, hitEnd = 0.29f, comboOpen = 0.40f, cancelOpen = 0.44f, exitTime = 0.64f,
                    trackUntil = 0.13f, windupSpeed = 1.15f, activeSpeed = 1.2f, recoverySpeed = 1.3f, lungeDistance = 0.7f, lungeStart = 0.05f,
                    lungeEnd = 0.26f, hitStop = 0.08f, shake = 0.55f, rumbleLow = 0.5f, rumbleHigh = 0.5f, next = "H_Light2" },
                // Attack2H01: high diagonal down to the hip
                new AttackData { id = "H_Light2", state = "Hammer_L2", damage = 48, poiseDamage = 22, staminaCost = 19, weight = ImpactWeight.Heavy,
                    hitStart = 0.29f, hitEnd = 0.44f, comboOpen = 0.55f, cancelOpen = 0.58f, exitTime = 0.78f, trackUntil = 0.26f,
                    windupSpeed = 1.05f, activeSpeed = 1.25f, recoverySpeed = 1.2f, lungeDistance = 0.8f, lungeStart = 0.18f, lungeEnd = 0.40f,
                    hitStop = 0.09f, shake = 0.7f, rumbleLow = 0.6f, rumbleHigh = 0.55f, next = "H_Light3" },
                // Attack2H02: wind round and slam the head into the ground
                new AttackData { id = "H_Light3", state = "Hammer_L3", damage = 58, poiseDamage = 28, staminaCost = 22, weight = ImpactWeight.Heavy,
                    hitStart = 0.39f, hitEnd = 0.52f, comboOpen = 0.66f, cancelOpen = 0.68f, exitTime = 0.84f, trackUntil = 0.36f,
                    windupSpeed = 1.0f, activeSpeed = 1.25f, recoverySpeed = 1.15f, lungeDistance = 1.0f, lungeStart = 0.28f, lungeEnd = 0.48f,
                    hitStop = 0.11f, shake = 0.85f, rumbleLow = 0.7f, rumbleHigh = 0.6f, slamRadius = 1.8f, slamDamageScale = 0.45f, next = "H_Light1" },
            };
            ms.heavyCombo = new[]
            {
                // Mining01 overhead: hammer high over the head (charge hold), then straight down in front
                new AttackData { id = "H_Heavy1", state = "Hammer_H1", startOffset = 0.12f, damage = 90, poiseDamage = 48, staminaCost = 28,
                    weight = ImpactWeight.Slam, hitStart = 0.46f, hitEnd = 0.58f, comboOpen = 0.70f, cancelOpen = 0.72f, exitTime = 0.88f,
                    trackUntil = 0.40f, windupSpeed = 1.15f, activeSpeed = 1.3f, recoverySpeed = 1.15f, chargeable = true, chargeHold = 0.42f,
                    chargeMaxTime = 1.0f, chargeDamageMultiplier = 1.7f, lungeDistance = 0.9f, lungeStart = 0.40f, lungeEnd = 0.56f,
                    hitStop = 0.13f, shake = 1.0f, rumbleLow = 0.9f, rumbleHigh = 0.6f, slamRadius = 2.0f, slamDamageScale = 0.5f, next = "H_Heavy2" },
                // AttackPolearm03: step in, hammer up, crash down low
                new AttackData { id = "H_Heavy2", state = "Hammer_H2", damage = 96, poiseDamage = 52, staminaCost = 30, weight = ImpactWeight.Slam,
                    hitStart = 0.34f, hitEnd = 0.46f, comboOpen = 0.68f, cancelOpen = 0.70f, exitTime = 0.86f, trackUntil = 0.30f,
                    windupSpeed = 1.0f, activeSpeed = 1.3f, recoverySpeed = 1.15f, chargeable = true, chargeHold = 0.30f, chargeMaxTime = 1.0f,
                    chargeDamageMultiplier = 1.7f, lungeDistance = 1.2f, lungeStart = 0.26f, lungeEnd = 0.44f, hitStop = 0.14f, shake = 1.1f,
                    rumbleLow = 1f, rumbleHigh = 0.7f, slamRadius = 2.2f, slamDamageScale = 0.5f, next = "H_Heavy3" },
                // Attack2H04: leap and drive the bomb into the ground (finisher; no charge while airborne)
                new AttackData { id = "H_Heavy3", state = "Hammer_H3", damage = 120, poiseDamage = 64, staminaCost = 34, weight = ImpactWeight.Slam,
                    hitStart = 0.31f, hitEnd = 0.44f, comboOpen = 0.74f, cancelOpen = 0.74f, exitTime = 0.88f, trackUntil = 0.28f,
                    windupSpeed = 1.0f, activeSpeed = 1.25f, recoverySpeed = 1.1f, lungeDistance = 2.0f, lungeStart = 0.05f, lungeEnd = 0.36f,
                    hitStop = 0.16f, shake = 1.3f, rumbleLow = 1f, rumbleHigh = 0.8f, slamRadius = 3.0f, slamDamageScale = 0.6f, next = "H_Heavy1" },
            };
            ms.runningAttack = new AttackData { id = "H_Run", state = "Hammer_Run", damage = 70, poiseDamage = 34, staminaCost = 24,
                weight = ImpactWeight.Heavy, hitStart = 0.24f, hitEnd = 0.37f, comboOpen = 0.60f, cancelOpen = 0.60f, exitTime = 0.78f,
                trackUntil = 0.20f, windupSpeed = 1.05f, activeSpeed = 1.2f, recoverySpeed = 1.2f, lungeDistance = 2.6f, lungeStart = 0.0f,
                lungeEnd = 0.34f, hitStop = 0.11f, shake = 0.8f, next = "H_Light2" };
            ms.jumpAttack = new AttackData { id = "H_Jump", state = "JumpAttack", startOffset = 0.30f, damage = 88, poiseDamage = 48,
                staminaCost = 24, weight = ImpactWeight.Slam, hitStart = 0.40f, hitEnd = 0.52f, comboOpen = 0.66f, cancelOpen = 0.66f,
                exitTime = 0.84f, trackUntil = 0.36f, windupSpeed = 1.05f, activeSpeed = 1.3f, recoverySpeed = 1.15f, lungeDistance = 0.4f,
                lungeStart = 0.3f, lungeEnd = 0.45f, hitStop = 0.13f, shake = 1.0f, rumbleLow = 0.9f, rumbleHigh = 0.6f, slamRadius = 2.2f,
                next = "H_Light1" };
            ms.dodgeAttack = new AttackData { id = "H_Dodge", state = "Hammer_Dodge", damage = 40, poiseDamage = 16, staminaCost = 18,
                weight = ImpactWeight.Heavy, hitStart = 0.24f, hitEnd = 0.35f, comboOpen = 0.55f, cancelOpen = 0.55f, exitTime = 0.72f,
                trackUntil = 0.20f, windupSpeed = 1.1f, activeSpeed = 1.2f, recoverySpeed = 1.2f, lungeDistance = 1.2f, lungeStart = 0.0f,
                lungeEnd = 0.3f, hitStop = 0.08f, shake = 0.5f, next = "H_Light2" };
            // unused while the form has a special (light + heavy = Sky Bombardment)
            ms.skill = new AttackData { id = "H_Skill", state = "Hammer_H3", damage = 110, poiseDamage = 60, staminaCost = 30, weight = ImpactWeight.Slam,
                hitStart = 0.31f, hitEnd = 0.44f, comboOpen = 0.74f, cancelOpen = 0.74f, exitTime = 0.88f, trackUntil = 0.28f, windupSpeed = 1.0f,
                activeSpeed = 1.25f, recoverySpeed = 1.1f, lungeDistance = 2.0f, lungeStart = 0.05f, lungeEnd = 0.36f, hitStop = 0.16f, shake = 1.3f,
                slamRadius = 3f, slamDamageScale = 0.7f, next = "" };
        }

        // ------------------------------------------------------------------ data assets (create once, then hands off)
        static T DataAsset<T>(string path, System.Action<T> init) where T : ScriptableObject
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null)
            {
                a = ScriptableObject.CreateInstance<T>();
                init(a);
                AssetDatabase.CreateAsset(a, path);
            }
            return a;
        }

        public const string FormElderPath = DataDir + "/Forms/Form_ElderTree.asset";
        public const string FormAlligatorPath = DataDir + "/Forms/Form_Alligator.asset";
        public const string MaskAlligatorPath = DataDir + "/Masks/Mask_Alligator.asset";
        public const string BowDataPath = DataDir + "/Equipment/Bow_Elder.asset";

        static void BuildEquipmentData()
        {
            var club = DataAsset<MeleeWeaponData>(DataDir + "/Equipment/Melee_ElderClub.asset", d => { d.displayName = "Elder Club"; });
            var hammer = DataAsset<MeleeWeaponData>(DataDir + "/Equipment/Melee_CrocodileHammer.asset", d =>
            {
                d.displayName = "Crocodile Hammer";
                d.explodeOnHit = true; d.explosionRadius = 2.4f; d.explosionDamage = 45f; d.explosionPoise = 20f;
                d.heavyExplosionScale = 1.4f; d.explodeOnGround = true; d.explosionFxScale = 0.75f; d.breakPower = 1;
                d.hitStopMultiplier = 1.15f; d.shakeMultiplier = 1.2f;
            });
            var bow = DataAsset<BowData>(BowDataPath, d => { d.displayName = "Elder Bow"; });
            var cannon = DataAsset<CannonData>(DataDir + "/Equipment/Cannon_Crocodile.asset", d => { d.displayName = "Crocodile Cannon"; });
            var special = DataAsset<SpecialAttackData>(DataDir + "/Equipment/Special_SkyBombardment.asset", d => { d.displayName = "Sky Bombardment"; });
            var elder = DataAsset<FormData>(FormElderPath, d => { d.displayName = "Elder Tree"; d.rigId = "Elder"; d.rangedKind = RangedKind.Bow; });
            var croc = DataAsset<FormData>(FormAlligatorPath, d =>
            {
                d.displayName = "Crocodile"; d.rigId = "Croc"; d.rangedKind = RangedKind.Cannon;
                d.moveSpeedMultiplier = 0.94f; d.rollDistanceMultiplier = 0.92f; d.damageTakenMultiplier = 0.85f; d.ignoreLightFlinch = true;
            });
            var mask = DataAsset<MaskData>(MaskAlligatorPath, d =>
            {
                d.id = "alligator"; d.displayName = "Alligator Mask";
                d.description = "Carved from the Crocodile's own hide. Wear it to become the bomber: a hammer that explodes on every blow, a cannon that outranges any bow, and the Sky Bombardment.";
            });

            // fill references that are still empty (never overwrite tuned values)
            if (club.icon == null) club.icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/UI/Icon_Club.png");
            if (bow.icon == null) bow.icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/UI/Icon_Bow.png");
            if (hammer.icon == null) hammer.icon = RenderIcon(HammerPath, "Icon_Hammer", Quaternion.Euler(0, 0, -45));
            if (cannon.icon == null) cannon.icon = RenderIcon(CannonPath, "Icon_Cannon", Quaternion.Euler(0, 90, -20));
            if (mask.icon == null) mask.icon = RenderIcon(MaskModelPath, "Icon_AlligatorMask", Quaternion.Euler(-10, 200, 0));
            if (elder.moveset == null) elder.moveset = AssetDatabase.LoadAssetAtPath<Moveset>(MovesetPath);
            if (elder.melee == null) elder.melee = club;
            if (elder.bow == null) elder.bow = bow;
            if (croc.moveset == null) croc.moveset = AssetDatabase.LoadAssetAtPath<Moveset>(CrocMovesetPath);
            if (croc.melee == null) croc.melee = hammer;
            if (croc.cannon == null) croc.cannon = cannon;
            if (croc.special == null) croc.special = special;
            if (mask.form == null) mask.form = croc;
            if (mask.maskModel == null) mask.maskModel = AssetDatabase.LoadAssetAtPath<GameObject>(MaskModelPath);
            foreach (var o in new Object[] { club, hammer, bow, cannon, special, elder, croc, mask }) EditorUtility.SetDirty(o);
        }

        /// <summary>Renders a model to a transparent HUD icon (Root/UI/name.png).</summary>
        static Texture2D RenderIcon(string modelPath, string name, Quaternion rotation)
        {
            const int S = 256;
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (src == null) return null;
            var go = (GameObject)Object.Instantiate(src);
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetPositionAndRotation(new Vector3(1000, 1000, 1000), rotation);
            const int IconLayer = 31;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = IconLayer;
            var camGo = new GameObject("IconCam") { hideFlags = HideFlags.DontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.cullingMask = 1 << IconLayer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            var rs = go.GetComponentsInChildren<Renderer>();
            var bounds = rs[0].bounds;
            foreach (var r in rs) bounds.Encapsulate(r.bounds);
            cam.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.1f;
            cam.transform.position = bounds.center - Vector3.forward * 5f;
            cam.transform.LookAt(bounds.center);
            var lgo = new GameObject("IconLight") { hideFlags = HideFlags.DontSave };
            var l = lgo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.8f; l.color = new Color(0.95f, 0.93f, 1f);
            l.cullingMask = 1 << IconLayer;
            lgo.transform.rotation = Quaternion.Euler(25, 25, 0);
            var rt = new RenderTexture(S, S, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, S, S), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            string path = $"{Root}/UI/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex); Object.DestroyImmediate(go); cam.targetTexture = null; Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lgo); rt.Release(); Object.DestroyImmediate(rt);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ prefabs
        static void BuildMaskPrefabs()
        {
            Directory.CreateDirectory(BreakableDir);

            // ---- cannon shell: a little drop bomb with a smoke trail
            {
                var root = new GameObject("CannonShell");
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{BossModelDir}/AerialBomb.fbx"));
                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                model.transform.localScale = Vector3.one * 0.42f;
                foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
                var trailGo = new GameObject("Trail");
                trailGo.transform.SetParent(root.transform, false);
                trailGo.transform.localPosition = new Vector3(0, 0, -0.2f);
                var trail = trailGo.AddComponent<TrailRenderer>();
                trail.time = 0.4f; trail.minVertexDistance = 0.1f;
                trail.widthCurve = AnimationCurve.EaseInOut(0, 0.22f, 1, 0.5f);
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.75f, 0.4f), 0), new GradientColorKey(new Color(0.3f, 0.27f, 0.3f), 0.2f), new GradientColorKey(new Color(0.2f, 0.18f, 0.2f), 1) },
                          new[] { new GradientAlphaKey(0.9f, 0), new GradientAlphaKey(0.5f, 0.3f), new GradientAlphaKey(0f, 1) });
                trail.colorGradient = g;
                trail.sharedMaterial = M("M_FX_Smoke");
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.emitting = false;
                var shell = root.AddComponent<CannonShell>();
                shell.trail = trail;
                shell.model = model.transform;
                shell.hitMask = (1 << 0) | (1 << LayerHurtbox) | (1 << LayerEnemy);
                PrefabUtility.SaveAsPrefabAsset(root, ShellPrefabPath);
                Object.DestroyImmediate(root);
            }

            // ---- mask pickup: hovering mask, toxic glow, rising motes
            {
                var root = new GameObject("MaskPickup");
                var modelRoot = Child("Model", root.transform, Vector3.up);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(MaskModelPath));
                model.transform.SetParent(modelRoot.transform, false);
                model.transform.localScale = Vector3.one * 1.8f;
                foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
                var lg = Child("Glow", modelRoot.transform, Vector3.zero).AddComponent<Light>();
                lg.type = LightType.Point; lg.color = TextureForge.Hex("#B8C03A"); lg.range = 6f; lg.intensity = 4f; lg.shadows = LightShadows.None;
                var ps = NewPS("Motes", M("M_FX_Firefly"), out _);
                ps.transform.SetParent(root.transform, false);
                ps.transform.localPosition = Vector3.up * 0.2f;
                var m = ps.main; m.loop = true; m.playOnAwake = true; m.duration = 2f;
                m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f); m.gravityModifier = -0.15f; m.maxParticles = 60;
                m.startColor = new ParticleSystem.MinMaxGradient(TextureForge.Hex("#C8D040"), TextureForge.Hex("#8A2A18"));
                var em = ps.emission; em.rateOverTime = 14f;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.6f; sh.rotation = new Vector3(-90, 0, 0);
                var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, Color.white, 1f, 0f);
                var pick = root.AddComponent<MaskPickup>();
                pick.model = modelRoot.transform;
                pick.glow = lg;
                pick.motes = ps;
                PrefabUtility.SaveAsPrefabAsset(root, PickupPrefabPath);
                Object.DestroyImmediate(root);
            }

            // ---- rock chunks (debris of a shattered obstacle)
            var chunkSrc = AssetDatabase.LoadAllAssetsAtPath(ChunksPath).OfType<Mesh>().OrderBy(x => x.name).ToArray();
            var chunkPrefabs = new GameObject[chunkSrc.Length];
            for (int i = 0; i < chunkSrc.Length; i++)
            {
                var go = new GameObject($"RockChunk{i}");
                go.AddComponent<MeshFilter>().sharedMesh = chunkSrc[i];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = M("M_CrackedStone");
                mr.shadowCastingMode = ShadowCastingMode.On;
                var mc = go.AddComponent<MeshCollider>(); mc.convex = true; mc.sharedMesh = chunkSrc[i];
                var rb = go.AddComponent<Rigidbody>(); rb.mass = 30f; rb.interpolation = RigidbodyInterpolation.Interpolate;
                go.layer = 2;
                chunkPrefabs[i] = PrefabUtility.SaveAsPrefabAsset(go, $"{BreakableDir}/RockChunk{i}.prefab");
                Object.DestroyImmediate(go);
            }

            Breakable MakeBreakable(string name, string modelPath, Vector3 hurtCenter, Vector3 hurtSize, Vector3 chunkCenter, Vector3 chunkArea, int chunks, float chunkScale)
            {
                var root = new GameObject(name);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
                model.name = "Intact";
                model.transform.SetParent(root.transform, false);
                foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
                var solids = new System.Collections.Generic.List<Collider>();
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
                {
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    solids.Add(mc);
                }
                var hurt = Child("Hurtbox", root.transform, Vector3.zero);
                hurt.layer = LayerHurtbox;
                var bc = hurt.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.center = hurtCenter; bc.size = hurtSize;
                hurt.AddComponent<Hurtbox>();
                var b = root.AddComponent<Breakable>();
                b.displayName = name;
                b.intact = model;
                b.solidColliders = solids.ToArray();
                b.chunkPrefabs = chunkPrefabs;
                b.chunkCenter = chunkCenter; b.chunkArea = chunkArea; b.chunkCount = chunks; b.chunkScale = chunkScale;
                b.requiredPower = 1;
                return b;
            }
            var boulder = MakeBreakable("Boulder", BoulderPath, new Vector3(0, 0.8f, 0), new Vector3(2.2f, 1.7f, 2.0f),
                new Vector3(0, 0.8f, 0), new Vector3(1.6f, 1.2f, 1.4f), 10, 1f);
            PrefabUtility.SaveAsPrefabAsset(boulder.gameObject, BoulderPrefabPath);
            Object.DestroyImmediate(boulder.gameObject);
            var wall = MakeBreakable("Cracked Wall", WallPath, new Vector3(0, 1.2f, 0), new Vector3(3.6f, 2.5f, 0.9f),
                new Vector3(0, 1.15f, 0), new Vector3(3.1f, 2.1f, 0.45f), 16, 1.1f);
            PrefabUtility.SaveAsPrefabAsset(wall.gameObject, WallPrefabPath);
            Object.DestroyImmediate(wall.gameObject);
        }

        // ------------------------------------------------------------------ player: alligator body + form rigs
        // Back slots in the alligator's bind space (feet at origin, facing +Z).
        public static Vector3 CannonBackPos = new Vector3(0.05f, 1.22f, -0.42f);
        public static Vector3 CannonBackUp = new Vector3(-0.35f, 1f, 0f);
        public static Vector3 HammerBackPos = new Vector3(0.12f, 0.78f, -0.42f);
        public static Vector3 HammerBackDir = new Vector3(-0.45f, 1f, 0f);
        public static Vector3 ElderFacePos = new Vector3(0f, 1.73f, 0.33f);
        public static float ElderMaskScale = 1.3f;

        /// <summary>Called by BuildPlayerPrefab: the Elder Tree rig description, the alligator body, the mask system.</summary>
        static void AttachForms(GameObject root, Transform visual, PlayerController pc, Animator elderAnim, WeaponHitbox club, Transform clubTip,
            Renderer clubRenderer, WeaponTrail clubTrail, FootstepFX steps)
        {
            // ---- the Elder Tree as a form rig
            var elder = elderAnim.gameObject.AddComponent<FormRig>();
            elder.rigId = "Elder";
            elder.animator = elderAnim;
            elder.weapon = club; elder.weaponTip = clubTip; elder.weaponRenderer = clubRenderer; elder.trail = clubTrail;
            elder.bowRig = pc.bowRig;
            var head = elderAnim.GetBoneTransform(HumanBodyBones.Head);
            elder.faceSocket = Socket("FaceSocket", head, ElderFacePos, Quaternion.identity);
            elder.faceSocket.localScale = Vector3.one * ElderMaskScale;

            // ---- the alligator
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(CrocModelPath);
            if (src == null) { Debug.LogError("[ElderTree] CrocForm.fbx missing: run build_croc_form.py"); pc.rigs = new[] { elder }; return; }
            var model = (GameObject)PrefabUtility.InstantiatePrefab(src);
            model.name = "CrocForm";
            model.transform.SetParent(visual, false);
            var anim = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            var crocCtrl = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(CrocControllerPath);
            anim.runtimeAnimatorController = crocCtrl != null ? crocCtrl : AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            anim.avatar = AssetDatabase.LoadAllAssetsAtPath(CrocModelPath).OfType<Avatar>().First();
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.updateWhenOffscreen = true;
                smr.shadowCastingMode = ShadowCastingMode.On;
            }

            // bomb hammer: two-handed, gripped like the pack's 2H weapons (prop bone, shaft along +Y)
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var hammer = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(HammerPath));
            hammer.name = "Hammer";
            hammer.transform.SetParent(hand, false);
            var grip = model.AddComponent<TwoHandGrip>();
            grip.animator = anim;
            grip.weapon = hammer.transform;
            CalibrateHammerGrip(hammer.transform, grip);
            var hBase = new GameObject("HammerBase").transform; hBase.SetParent(hammer.transform, false); hBase.localPosition = new Vector3(0, 0.5f, 0);
            var hTip = new GameObject("HammerTip").transform; hTip.SetParent(hammer.transform, false); hTip.localPosition = new Vector3(0, 1.12f, 0);
            var hb = hammer.AddComponent<WeaponHitbox>();
            hb.hurtboxMask = 1 << LayerHurtbox;
            hb.baseLocal = new Vector3(0, 0.5f, 0);
            hb.tipLocal = new Vector3(0, 1.12f, 0);
            hb.radius = 0.24f;
            var ht = hammer.AddComponent<WeaponTrail>();
            ht.baseRef = hBase; ht.tipRef = hTip;
            ht.material = M("M_FX_Trail");
            var hRenderer = hammer.GetComponentInChildren<MeshRenderer>();
            foreach (var r in hammer.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;

            // cannon: rides on the back, swaps to a grip calibrated on the Bazooka aim pose
            var chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            var cannon = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CannonPath));
            cannon.name = "Cannon";
            foreach (var r in cannon.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            var muzzle = cannon.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Muzzle");
            var backSocket = Socket("CannonBackSocket", chest, CannonBackPos,
                Quaternion.LookRotation(CannonBackUp.normalized, Vector3.back));
            var handSocket = new GameObject("CannonHandSocket").transform;
            handSocket.SetParent(hand, false);
            CalibrateCannonGrip(handSocket);
            cannon.transform.SetParent(backSocket, false);
            var hammerBack = Socket("HammerBackSocket", chest, HammerBackPos, Quaternion.FromToRotation(Vector3.up, HammerBackDir.normalized));

            var rig = model.AddComponent<CannonRig>();
            rig.player = pc;
            rig.animator = anim;
            rig.cannon = cannon.transform;
            rig.cannonHandSocket = handSocket;
            rig.cannonBackSocket = backSocket;
            rig.muzzle = muzzle != null ? muzzle : cannon.transform;
            rig.hammer = hammer.transform;
            rig.hammerBackSocket = hammerBack;

            var croc = model.AddComponent<FormRig>();
            croc.rigId = "Croc";
            croc.animator = anim;
            croc.weapon = hb; croc.weaponTip = hTip; croc.weaponRenderer = hRenderer; croc.trail = ht;
            croc.cannonRig = rig;
            croc.twoHandGrip = grip;
            croc.faceSocket = Socket("FaceSocket", anim.GetBoneTransform(HumanBodyBones.Head), new Vector3(0, 1.68f, 0.3f), Quaternion.identity);
            model.SetActive(false);

            // ---- controller + mask system
            var shark = AttachSharkForm(visual, pc);
            var druid = AttachDruidForm(visual, pc);
            var fairy = AttachFairyForm(visual, pc);
            pc.rigs = new[] { elder, croc, shark, druid, fairy }.Where(r => r != null).ToArray();
            pc.baseForm = AssetDatabase.LoadAssetAtPath<FormData>(FormElderPath);
            pc.bowData = AssetDatabase.LoadAssetAtPath<BowData>(BowDataPath);
            pc.shellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShellPrefabPath)?.GetComponent<CannonShell>();
            pc.footsteps = steps;
            var ms = root.AddComponent<MaskSystem>();
            ms.player = pc;
            ms.slots = new MaskData[4];
            ms.slots[0] = AssetDatabase.LoadAssetAtPath<MaskData>(MaskAlligatorPath);
            ms.slots[1] = AssetDatabase.LoadAssetAtPath<MaskData>(MaskSharkPath);
            ms.slots[2] = AssetDatabase.LoadAssetAtPath<MaskData>(MaskDruidPath);
            ms.slots[3] = AssetDatabase.LoadAssetAtPath<MaskData>(MaskFairyPath);
        }

        /// <summary>
        /// Puts the cannon grip where the Bazooka aim clip holds it: sample the pose on a temporary alligator,
        /// place the cannon at the right fist with the barrel straight down the character's forward (so the
        /// shells leave along the aim), and store that pose relative to the hand.
        /// </summary>
        static void CalibrateCannonGrip(Transform handSocket) => CalibrateCannonGrip(handSocket, CrocModelPath, "Combat/Bazooka/HumanM@Bazooka_Aim01");

        /// <summary>Shouldered-weapon grip on any form model, sampled on the given aim clip (cannon: Bazooka, crossbow: Rifle).</summary>
        static void CalibrateCannonGrip(Transform handSocket, string modelPath, string aimClip)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var tmp = (GameObject)Object.Instantiate(src);
            tmp.hideFlags = HideFlags.DontSave;
            try
            {
                var a = tmp.GetComponent<Animator>() ?? tmp.AddComponent<Animator>();
                a.avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().First();
                var clip = K(aimClip);
                clip.SampleAnimation(tmp, clip.length * 0.5f);
                var rh = a.GetBoneTransform(HumanBodyBones.RightHand);
                var lh = a.GetBoneTransform(HumanBodyBones.LeftHand);
                Vector3 fwd = tmp.transform.forward;
                Vector3 toLeft = lh.position - rh.position;
                // grip at the fist, barrel level and forward, rolled upright
                Vector3 gripPos = rh.position + Vector3.up * 0.02f;
                Quaternion want = Quaternion.LookRotation(fwd, Vector3.up);
                handSocket.localPosition = rh.InverseTransformPoint(gripPos);
                handSocket.localRotation = Quaternion.Inverse(rh.rotation) * want;
                Debug.Log($"[ElderTree] Shouldered grip ({System.IO.Path.GetFileNameWithoutExtension(modelPath)}) calibrated: hand->left hand {toLeft:F2}, local {handSocket.localPosition:F3} / {handSocket.localEulerAngles:F1}");
            }
            finally { Object.DestroyImmediate(tmp); }
        }

        /// <summary>Fist centre: between the wrist and the knuckles (index / middle / ring proximals).</summary>
        static Vector3 FistCentre(Animator a, bool left)
        {
            var idx = a.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            var mid = a.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            var ring = a.GetBoneTransform(left ? HumanBodyBones.LeftRingProximal : HumanBodyBones.RightRingProximal);
            var hand = a.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            return (idx.position + mid.position + ring.position) / 3f * 0.6f + hand.position * 0.4f;
        }

        /// <summary>
        /// Two-handed grip, taken from the pack itself: the pack's HumanM carries 2H weapons on its
        /// B-handProp.R bone (shaft along +Y). Sample CombatIdle2H01 on HumanM and on the alligator at the
        /// same frame (humanoid retargeting keeps hand orientations), re-express the prop frame on the
        /// alligator's right hand, and measure where the left fist and its grip axis sit, for TwoHandGrip.
        /// The alligator's wider shoulders leave its left fist ~35 cm off the shaft, so the IK does the rest.
        /// </summary>
        public static void CalibrateHammerGrip(Transform hammer, TwoHandGrip grip) => CalibrateHammerGrip(hammer, grip, CrocModelPath);

        public static void CalibrateHammerGrip(Transform hammer, TwoHandGrip grip, string modelPath)
        {
            var croc = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            var human = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PreviewRenderer.ReferenceModelPath));
            croc.hideFlags = human.hideFlags = HideFlags.DontSave;
            try
            {
                var ca = croc.GetComponent<Animator>() ?? croc.AddComponent<Animator>();
                ca.avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().First();
                var ha = human.GetComponent<Animator>();
                var prop = human.GetComponentsInChildren<Transform>(true).First(t => t.name == "B-handProp.R");
                var clip = K("Combat/2H/HumanM@CombatIdle2H01");
                clip.SampleAnimation(croc, 0f); croc.transform.position = Vector3.zero;
                clip.SampleAnimation(human, 0f); human.transform.position = Vector3.zero;
                var rh = ca.GetBoneTransform(HumanBodyBones.RightHand);
                var lh = ca.GetBoneTransform(HumanBodyBones.LeftHand);
                hammer.localRotation = Quaternion.Inverse(rh.rotation) * prop.rotation;
                hammer.localPosition = rh.InverseTransformPoint(FistCentre(ca, false));
                hammer.localScale = Vector3.one;
                grip.leftFistLocal = lh.InverseTransformPoint(FistCentre(ca, true));
                grip.leftGripAxisLocal = (Quaternion.Inverse(lh.rotation) * prop.up).normalized;
                // where HumanM's left fist rides on its weapon = the rest hold
                grip.restGrip = Mathf.Clamp(Vector3.Dot(FistCentre(ha, true) - prop.position, prop.up), -0.24f, -0.06f);
                Debug.Log($"[ElderTree] Hammer 2H grip: local {hammer.localPosition:F3} / {hammer.localEulerAngles:F1}, " +
                          $"left fist {grip.leftFistLocal:F3}, axis {grip.leftGripAxisLocal:F2}, rest {grip.restGrip:F2}");
            }
            finally { Object.DestroyImmediate(croc); Object.DestroyImmediate(human); }
        }

        // ------------------------------------------------------------------ scene
        static void PlaceMaskSet(Transform envRoot, GameObject systems, PlayerController pc, UI.HUD hud, bool yardProps = true)
        {
            // first-time transformation cinematic + the boss reward
            var cine = systems.AddComponent<MaskCinematic>();
            cine.player = pc; cine.hud = hud;
            cine.vortexMaterial = M("M_FX_Vortex");
            cine.vortexGlowMaterial = M("M_FX_VortexGlow");
            var ms = pc.GetComponent<MaskSystem>();
            if (ms != null) ms.cinematic = cine;
            var enc = systems.GetComponent<Enemies.BossEncounter>();
            if (enc != null)
            {
                enc.rewardMask = AssetDatabase.LoadAssetAtPath<MaskData>(MaskAlligatorPath);
                enc.pickupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PickupPrefabPath)?.GetComponent<MaskPickup>();
            }

            // breakable obstacles: a cracked wall plugs one gap in the ring, boulders around the yard
            if (!yardProps) return;
            var boulder = AssetDatabase.LoadAssetAtPath<GameObject>(BoulderPrefabPath);
            var wall = AssetDatabase.LoadAssetAtPath<GameObject>(WallPrefabPath);
            if (wall != null)
            {
                float a = 3 / 14f * Mathf.PI * 2f + 0.15f;
                Vector3 p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 16.5f;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(wall);
                go.transform.SetParent(envRoot, false);
                go.transform.SetPositionAndRotation(p, Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90f, 0));
                go.name = "CrackedWall";
            }
            if (boulder != null)
            {
                (Vector3 p, float yaw, float s)[] spots = { (new Vector3(11.2f, 0, -7.2f), 30f, 1f), (new Vector3(-8.2f, 0, -11.5f), 140f, 1.15f), (new Vector3(-12.6f, 0, 2.2f), 250f, 0.9f) };
                for (int i = 0; i < spots.Length; i++)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(boulder);
                    go.transform.SetParent(envRoot, false);
                    go.transform.SetPositionAndRotation(spots[i].p, Quaternion.Euler(0, spots[i].yaw, 0));
                    go.transform.localScale = Vector3.one * spots[i].s;
                    go.name = $"Boulder{i}";
                }
            }
        }
    }
}
