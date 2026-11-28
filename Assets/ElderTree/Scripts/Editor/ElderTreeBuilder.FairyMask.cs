using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    /// The Fairy Mask set (Fairy reward): the fae body (blue dress, white hair, four fluttering wings), twin white
    /// daggers, the pink Moon Chakram thrown like a boomerang, the Moonfall special and the mask item. Models come from
    /// Tools/Blender/build_fairy_form.py (exported into Art/Models/FairyMask). The body shares the Fairy boss's materials.
    ///
    /// Data assets (Data/Forms/Form_Fairy, Data/Equipment/Melee_FairyDaggers, Chakram_Moon, Special_Moonfall,
    /// Data/Masks/Mask_Fairy, Data/FairyDaggerMoveset) are only CREATED when missing; existing ones keep their tuned values.
    /// </summary>
    public static partial class ElderTreeBuilder
    {
        public const string FairyMaskDir = Root + "/Art/Models/FairyMask";
        public const string FairyFormModelPath = FairyMaskDir + "/FairyForm.fbx";
        public const string FairyFormWingsPath = FairyMaskDir + "/FairyFormWings.fbx";
        public const string FairyDaggerPath = FairyMaskDir + "/FairyDagger.fbx";
        public const string FairyChakramPath = FairyMaskDir + "/FairyChakram.fbx";
        public const string FairyMaskModelPath = FairyMaskDir + "/FairyMask.fbx";
        public const string ChakramPrefabPath = Root + "/Prefabs/Chakram.prefab";
        public const string FairyMovesetPath = DataDir + "/FairyDaggerMoveset.asset";
        public const string FormFairyPath = DataDir + "/Forms/Form_Fairy.asset";
        public const string MaskFairyPath = DataDir + "/Masks/Mask_Fairy.asset";
        public const string FairyControllerPath = AnimDir + "/ElderTree_Fairy.overrideController";
        public static Vector3 FairyFacePos = new Vector3(0f, 1.67f, 0.185f);
        public static float FairyMaskScale = 0.95f;

        static readonly string[] FairyFormMats =
        {
            "M_FairyDress", "M_FairySkin", "M_FairyHair", "M_FairyWing", "M_FairyEye", "M_FairyHollow", "M_FairyTrim", "M_FairyGem",
            "M_FairyBlade", "M_FairyGrip", "M_FairyChakram"
        };

        [MenuItem("Elder Tree/8d. Build Fairy Mask set")]
        public static string BuildFairyMaskSet()
        {
            BuildFairyMaskArt();
            ConfigureFairyMaskImport();
            BuildFairyOverride();
            BuildFairyMoveset();
            BuildFairyMaskPrefabs();
            BuildMoonDropPrefab();
            BuildFairyMaskData();
            AssetDatabase.SaveAssets();
            return "Fairy Mask set built";
        }

        // ------------------------------------------------------------------ textures + materials
        static void BuildFairyMaskArt()
        {
            var Hex = (System.Func<string, Color>)TextureForge.Hex;
            // dagger blade: bone-white with a faint ivory grain along the blade and a few hairline scratches
            var blade = TextureForge.SavePsx("T_FairyBlade", 64, (u, v, x, y) =>
            {
                float grain = TextureForge.Fbm(u, v, 2, 8, 3, 1701);
                var c = Mx(Hex("#BDB8AC"), Hex("#EEEBE3"), 0.35f + 0.6f * grain);
                if ((x * 3 + y) % 17 == 0) c *= 0.86f;
                return c * (0.95f + 0.1f * (H2(x, y, 1702) - 0.5f));
            }, mips: false);
            // grip: pale blue cloth wound round and round
            var grip = TextureForge.SavePsx("T_FairyGrip", 32, (u, v, x, y) =>
            {
                float wrap = Mathf.Abs(Mathf.Sin((u * 2f + v * 6f) * Mathf.PI));
                var c = Mx(Hex("#2A3A52"), Hex("#6C86A8"), Sstep(0.15f, 0.9f, wrap));
                return c * (0.92f + 0.16f * (H2(x, y, 1711) - 0.5f));
            }, mips: false);
            // chakram: u runs along the crescent, v across the band (0 outer edge .. 0.5 inner edge .. 1 outer edge,
            // the two faces). Rose-pink metal, the edges honed paler, a darker engraved band with runes in the middle third.
            var chak = TextureForge.SavePsx("T_FairyChakram", 128, (u, v, x, y) =>
            {
                float across = Mathf.Abs((v * 2f) % 1f - 0.5f) * 2f;          // 1 at the edges, 0 on the spine of the face
                var c = Mx(Hex("#8E4A60"), Hex("#C27A92"), 0.35f + 0.5f * TextureForge.Fbm(u, v, 8, 4, 3, 1721));
                c = Mx(c, Hex("#F0C4D2"), Sstep(0.72f, 0.95f, across));       // honed edges
                bool band = u > 0.36f && u < 0.64f && across < 0.55f;
                if (band)
                {
                    c = Mx(c, Hex("#5A2436"), 0.55f);
                    // runes: little strokes in cells along the band
                    float cu = (u - 0.36f) / 0.28f * 9f, cv = across / 0.55f;
                    int cell = Mathf.FloorToInt(cu);
                    float fu = cu - cell;
                    float r = H2(cell, 0, 1722);
                    bool stroke = (r < 0.5f ? Mathf.Abs(fu - 0.5f) < 0.12f : Mathf.Abs(cv - 0.5f) < 0.1f) || Mathf.Abs(fu - cv) < 0.1f && r > 0.7f;
                    if (stroke && cv > 0.15f && cv < 0.85f) c = Mx(c, Hex("#E8A6BC"), 0.8f);
                    if (Mathf.Abs(cu - Mathf.Round(cu)) < 0.04f) c *= 0.7f;    // cell borders
                }
                c = Mx(c, Hex("#3A1A26"), Sstep(0.7f, 0.85f, TextureForge.Fbm(u, v, 6, 6, 3, 1723)) * 0.35f);   // tarnish
                return c * (0.94f + 0.12f * (H2(x, y, 1724) - 0.5f));
            }, mips: false);
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(chak));
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
            Matte(Lit("M_FairyBlade", Color.white, blade, null, 0f, 1f, null, false));
            Matte(Lit("M_FairyGrip", Color.white, grip, null, 0f, 1f, null, false));
            var cm = Lit("M_FairyChakram", Color.white, chak, null, 0f, 1f, null, false);
            Matte(cm);
            cm.SetFloat("_Cull", 0f);
            cm.SetColor("_EmissionColor", new Color(0.12f, 0.03f, 0.06f));   // a faint warmth so it reads at night
            EditorUtility.SetDirty(cm);
            var dot = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureForge.TexDir + "/T_FX_SoftDot.png") ?? TextureForge.BuildSoftDot();
            Unlit("M_FX_FairyPink", new Color(1f, 0.55f, 0.74f, 1f), dot, additive: true);
        }

        static void ConfigureFairyMaskImport()
        {
            foreach (var path in new[] { FairyFormModelPath, FairyFormWingsPath, FairyDaggerPath, FairyChakramPath, FairyMaskModelPath })
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ConfigureHumanoid(FairyFormModelPath, FairyFormMats);
            foreach (var path in new[] { FairyFormWingsPath, FairyDaggerPath, FairyChakramPath, FairyMaskModelPath })
            {
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                if (imp == null) { Debug.LogError($"[ElderTree] Missing {path} (run Tools/Blender/build_fairy_form.py)"); continue; }
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
                foreach (var n in FairyFormMats)
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");
                    if (m) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), m);
                }
                imp.SaveAndReimport();
            }
        }

        /// <summary>Dual daggers: the pack's dual-wield parry for the guard layer (idle stays the 1H stance).</summary>
        static void BuildFairyOverride()
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ac == null) return;
            var oc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(FairyControllerPath);
            if (oc == null)
            {
                oc = new AnimatorOverrideController(ac);
                AssetDatabase.CreateAsset(oc, FairyControllerPath);
            }
            oc.runtimeAnimatorController = ac;
            var swaps = new Dictionary<string, AnimationClip>
            {
                { "HumanM@Parry1H01_R - Loop", K("Combat/1H/HumanM@ParryDW01 - Loop") },
                { "HumanM@Parry1H01_R - Hit", K("Combat/1H/HumanM@ParryDW01 - Hit") },
            };
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            oc.GetOverrides(pairs);
            for (int i = 0; i < pairs.Count; i++)
                pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, swaps.TryGetValue(pairs[i].Key.name, out var to) ? to : null);
            oc.ApplyOverrides(pairs);
            EditorUtility.SetDirty(oc);
        }

        // ------------------------------------------------------------------ dagger moveset
        public const int FairyMovesetVersion = 1;

        static void BuildFairyMoveset()
        {
            var ms = AssetDatabase.LoadAssetAtPath<Moveset>(FairyMovesetPath);
            bool create = ms == null;
            if (create) ms = ScriptableObject.CreateInstance<Moveset>();
            if (create || ms.version < FairyMovesetVersion)
            {
                FillFairyMoveset(ms);
                ms.version = FairyMovesetVersion;
            }
            if (create) AssetDatabase.CreateAsset(ms, FairyMovesetPath);
            foreach (var a in ms.lightCombo.Concat(ms.heavyCombo).Concat(new[] { ms.runningAttack, ms.jumpAttack, ms.dodgeAttack, ms.skill }))
                if (a != null) a.clipLength = StateClipLength(a.state);
            EditorUtility.SetDirty(ms);
        }

        /// <summary>
        /// Twin daggers: quick, light and cheap. Light alternates the hands (right, left, right, left) and ends in a
        /// both-blades flurry; heavy is the dual-wield set (a crossing double cut you can hold to charge, a big left
        /// sweep, the double flurry with a lunge). Hit windows are where the live hand moves fastest on the fairy rig
        /// (<see cref="AnalyzeFairyClip"/>), padded a little.
        /// </summary>
        static void FillFairyMoveset(Moveset ms)
        {
            AttackData L(string id, string state, WeaponHand hand, float hs, float he, float dmg, string next, float lunge = 0.55f,
                         ImpactWeight w = ImpactWeight.Light, float poise = 9f, float stamina = 10f) => new AttackData
            {
                id = id, state = state, hand = hand, damage = dmg, poiseDamage = poise, staminaCost = stamina, weight = w,
                hitStart = hs, hitEnd = he, comboOpen = he + 0.04f, cancelOpen = he + 0.08f, exitTime = Mathf.Min(0.8f, he + 0.32f),
                trackUntil = Mathf.Max(0.05f, hs - 0.04f), windupSpeed = 1.35f, activeSpeed = 1.45f, recoverySpeed = 1.5f,
                lungeDistance = lunge, lungeStart = Mathf.Max(0f, hs - 0.14f), lungeEnd = he, hitStop = 0.045f, shake = 0.25f,
                rumbleLow = 0.15f, rumbleHigh = 0.35f, next = next
            };
            ms.lightCombo = new[]
            {
                L("F_Light1", "Fairy_L1", WeaponHand.Main, FairyWin["Fairy_L1"].x, FairyWin["Fairy_L1"].y, 24f, "F_Light2"),
                L("F_Light2", "Fairy_L2", WeaponHand.Off, FairyWin["Fairy_L2"].x, FairyWin["Fairy_L2"].y, 24f, "F_Light3"),
                L("F_Light3", "Fairy_L3", WeaponHand.Main, FairyWin["Fairy_L3"].x, FairyWin["Fairy_L3"].y, 26f, "F_Light4"),
                L("F_Light4", "Fairy_L4", WeaponHand.Off, FairyWin["Fairy_L4"].x, FairyWin["Fairy_L4"].y, 26f, "F_Light5"),
                L("F_Light5", "Fairy_L5", WeaponHand.Both, FairyWin["Fairy_L5"].x, FairyWin["Fairy_L5"].y, 38f, "F_Light1", 0.8f, ImpactWeight.Heavy, 16f, 14f),
            };
            var h1 = L("F_Heavy1", "Fairy_H1", WeaponHand.Both, FairyWin["Fairy_H1"].x, FairyWin["Fairy_H1"].y, 52f, "F_Heavy2", 0.9f, ImpactWeight.Heavy, 26f, 18f);
            h1.chargeable = true; h1.chargeHold = Mathf.Max(0.1f, FairyWin["Fairy_H1"].x - 0.1f); h1.chargeMaxTime = 0.8f; h1.chargeDamageMultiplier = 1.6f;
            h1.windupSpeed = 1.0f; h1.activeSpeed = 1.25f; h1.recoverySpeed = 1.3f; h1.hitStop = 0.08f; h1.shake = 0.5f;
            var h2 = L("F_Heavy2", "Fairy_H2", WeaponHand.Off, FairyWin["Fairy_H2"].x, FairyWin["Fairy_H2"].y, 56f, "F_Heavy3", 1.0f, ImpactWeight.Heavy, 28f, 18f);
            h2.windupSpeed = 1.1f; h2.activeSpeed = 1.3f; h2.hitStop = 0.08f; h2.shake = 0.5f;
            var h3 = L("F_Heavy3", "Fairy_H3", WeaponHand.Both, FairyWin["Fairy_H3"].x, FairyWin["Fairy_H3"].y, 64f, "F_Heavy1", 1.6f, ImpactWeight.Heavy, 32f, 20f);
            h3.windupSpeed = 1.05f; h3.activeSpeed = 1.2f; h3.hitStop = 0.1f; h3.shake = 0.6f; h3.rumbleLow = 0.4f; h3.rumbleHigh = 0.6f;
            ms.heavyCombo = new[] { h1, h2, h3 };
            ms.runningAttack = L("F_Run", "Fairy_Run", WeaponHand.Main, FairyWin["Fairy_Run"].x, FairyWin["Fairy_Run"].y, 40f, "F_Light2", 2.6f, ImpactWeight.Heavy, 18f, 14f);
            ms.runningAttack.lungeStart = 0f;
            ms.dodgeAttack = L("F_Dodge", "Fairy_Dodge", WeaponHand.Off, FairyWin["Fairy_Dodge"].x, FairyWin["Fairy_Dodge"].y, 30f, "F_Light1", 1.4f, ImpactWeight.Light, 12f, 12f);
            ms.dodgeAttack.lungeStart = 0f;
            ms.jumpAttack = new AttackData { id = "F_Jump", state = "JumpAttack", hand = WeaponHand.Both, startOffset = 0.30f, damage = 50, poiseDamage = 22,
                staminaCost = 16, weight = ImpactWeight.Heavy, hitStart = 0.40f, hitEnd = 0.52f, comboOpen = 0.66f, cancelOpen = 0.66f,
                exitTime = 0.84f, trackUntil = 0.36f, windupSpeed = 1.2f, activeSpeed = 1.3f, recoverySpeed = 1.3f, lungeDistance = 0.5f,
                lungeStart = 0.3f, lungeEnd = 0.45f, hitStop = 0.08f, shake = 0.5f, next = "F_Light1" };
            // unused while the form has a special (light + heavy = Moonfall)
            ms.skill = L("F_Skill", "Fairy_H3", WeaponHand.Both, FairyWin["Fairy_H3"].x, FairyWin["Fairy_H3"].y, 64f, "", 1.6f, ImpactWeight.Heavy, 32f, 20f);
        }

        /// <summary>Hit windows (normalized) per dagger state, measured with <see cref="AnalyzeFairyClip"/> on the fairy rig.</summary>
        static readonly Dictionary<string, Vector2> FairyWin = new()
        {
            // live-hand speed peaks (m/s): 1H02_R 0.36 (17), 1H02_L 0.32 (26), 1H04_R/L 0.40 (19), DW01 0.24 (23, one snap),
            // DW02 two strikes 0.20-0.24 and 0.48-0.56 (the window spans both: either can land), 1H05_L 0.24 (16), 1H01_R/L 0.28 (19)
            { "Fairy_L1", new Vector2(0.29f, 0.43f) }, { "Fairy_L2", new Vector2(0.25f, 0.39f) },
            { "Fairy_L3", new Vector2(0.26f, 0.42f) }, { "Fairy_L4", new Vector2(0.26f, 0.42f) },
            { "Fairy_L5", new Vector2(0.18f, 0.30f) },
            { "Fairy_H1", new Vector2(0.17f, 0.58f) }, { "Fairy_H2", new Vector2(0.12f, 0.34f) }, { "Fairy_H3", new Vector2(0.18f, 0.30f) },
            { "Fairy_Run", new Vector2(0.18f, 0.31f) }, { "Fairy_Dodge", new Vector2(0.18f, 0.31f) },
        };

        /// <summary>
        /// Samples a pack clip on the fairy body and logs, per normalized time, how fast each hand moves (m/s) relative to
        /// the hips. The hit window is where the live hand is fastest.
        /// </summary>
        public static string AnalyzeFairyClip(string clipPath, int samples = 40)
        {
            var go = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FairyFormModelPath));
            go.hideFlags = HideFlags.DontSave;
            var sb = new StringBuilder();
            try
            {
                var a = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
                a.avatar = AssetDatabase.LoadAllAssetsAtPath(FairyFormModelPath).OfType<Avatar>().First();
                var clip = K(clipPath);
                var rh = a.GetBoneTransform(HumanBodyBones.RightHand);
                var lh = a.GetBoneTransform(HumanBodyBones.LeftHand);
                var hips = a.GetBoneTransform(HumanBodyBones.Hips);
                Vector3 pr = Vector3.zero, pl = Vector3.zero;
                sb.Append($"{clip.name} len {clip.length:F2}s\n");
                for (int i = 0; i <= samples; i++)
                {
                    float n = i / (float)samples;
                    clip.SampleAnimation(go, n * clip.length);
                    go.transform.position = Vector3.zero;
                    Vector3 r = rh.position - hips.position, l = lh.position - hips.position;
                    if (i > 0)
                    {
                        float dt = clip.length / samples;
                        sb.Append($"{n:F2} R {(r - pr).magnitude / dt,5:F2} L {(l - pl).magnitude / dt,5:F2}\n");
                    }
                    pr = r; pl = l;
                }
            }
            finally { Object.DestroyImmediate(go); }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ prefabs: the thrown chakram
        static void BuildFairyMaskPrefabs()
        {
            var root = new GameObject("Chakram");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FairyChakramPath));
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            var ch = root.AddComponent<Chakram>();
            ch.model = model.transform;
            // a ribbon off the rim: with the spin it smears into a pink ring
            var trailGo = Child("Trail", model.transform, new Vector3(0f, 0f, -0.27f));
            var trail = trailGo.AddComponent<TrailRenderer>();
            trail.time = 0.14f; trail.minVertexDistance = 0.04f;
            trail.widthCurve = AnimationCurve.EaseInOut(0, 0.12f, 1, 0f);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.75f, 0.85f), 0), new GradientColorKey(new Color(0.85f, 0.35f, 0.55f), 1) },
                      new[] { new GradientAlphaKey(0.75f, 0), new GradientAlphaKey(0f, 1) });
            trail.colorGradient = g;
            trail.sharedMaterial = M("M_FX_FairyPink");
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.emitting = false;
            ch.trail = trail;
            var lgo = Child("Glow", root.transform, Vector3.zero);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.55f, 0.72f); l.intensity = 1.2f; l.range = 3.5f;
            l.shadows = LightShadows.None; l.renderMode = LightRenderMode.Auto;
            ch.glow = l;
            ch.worldMask = 1;
            ch.hitMask = 1 << LayerHurtbox;
            PrefabUtility.SaveAsPrefabAsset(root, ChakramPrefabPath);
            Object.DestroyImmediate(root);
        }

        // ------------------------------------------------------------------ Moonfall: the falling moon
        public const string MoonDropPrefabPath = Root + "/Prefabs/MoonDrop.prefab";
        const string MoonMeshPath = FairyMaskDir + "/MoonSphere.asset";

        /// <summary>A faceted PS1 ball (18 x 12) with plain lat-long UVs for the moon texture.</summary>
        static Mesh MoonSphereMesh()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MoonMeshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "MoonSphere" };
                AssetDatabase.CreateAsset(mesh, MoonMeshPath);
            }
            const int Seg = 18, Rings = 12;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int r = 0; r <= Rings; r++)
            {
                float v = r / (float)Rings, lat = Mathf.PI * (v - 0.5f);
                for (int s = 0; s <= Seg; s++)
                {
                    float u = s / (float)Seg, lon = u * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon)) * 0.5f);
                    uvs.Add(new Vector2(u, v));
                }
            }
            for (int r = 0; r < Rings; r++)
                for (int s = 0; s < Seg; s++)
                {
                    int a = r * (Seg + 1) + s, b = a + Seg + 1;
                    tris.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            mesh.Clear();
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        static void BuildMoonDropPrefab()
        {
            var Hex = (System.Func<string, Color>)TextureForge.Hex;
            // the moon's face: pale blue-white highlands, darker grey seas, pocked with craters (dark floor, bright rim)
            var tex = TextureForge.SavePsx("T_FX_Moon", 128, (u, v, x, y) =>
            {
                var c = Mx(Hex("#AEB6C8"), Hex("#E4E8F0"), 0.35f + 0.6f * TextureForge.Fbm(u, v, 6, 4, 4, 1801));
                c = Mx(c, Hex("#6E7688"), Sstep(0.55f, 0.7f, TextureForge.Fbm(u, v, 3, 2, 3, 1802)) * 0.7f);   // seas
                TextureForge.Worley(u, v, 9, 1803, out float f1, out _, out int id);
                float cr = 0.05f + 0.06f * ((id * 37 % 11) / 11f);
                if (f1 < cr) c = Mx(c, Hex("#5A6274"), 0.55f * (1f - f1 / cr));                              // crater floor
                else if (f1 < cr * 1.35f) c = Mx(c, Hex("#F4F6FA"), 0.5f);                                    // bright rim
                return c * (0.95f + 0.1f * (H2(x, y, 1804) - 0.5f));
            }, mips: false);
            var mm = Lit("M_FX_Moon", Color.white, tex, null, 0f, 1f, null, false);
            Matte(mm);
            mm.SetTexture("_EmissionMap", tex);
            mm.SetColor("_EmissionColor", new Color(0.42f, 0.46f, 0.58f));   // it glows: its own light, not the scene's
            mm.EnableKeyword("_EMISSION");
            mm.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(mm);
            var glowM = M("M_FX_FairyGlow");

            var root = new GameObject("MoonDrop");
            var md = root.AddComponent<MoonDrop>();
            var moon = Child("Moon", root.transform, Vector3.zero);
            moon.AddComponent<MeshFilter>().sharedMesh = MoonSphereMesh();
            var mr = moon.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mm;
            mr.shadowCastingMode = ShadowCastingMode.On;
            md.moon = moon.transform;
            md.moonRenderer = mr;
            // a soft pale halo round it (scales with the moon)
            var halo = NewPS("Halo", glowM, out _);
            halo.transform.SetParent(moon.transform, false);
            var hm = halo.main; hm.loop = true; hm.playOnAwake = true; hm.duration = 1f; hm.startLifetime = 0.25f; hm.startSpeed = 0f;
            hm.startSize = new ParticleSystem.MinMaxCurve(1.6f, 1.8f);
            hm.startColor = new Color(0.62f, 0.75f, 1f, 0.45f);
            hm.simulationSpace = ParticleSystemSimulationSpace.Local;
            hm.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var hem = halo.emission; hem.rateOverTime = 16f;
            var hsh = halo.shape; hsh.enabled = false;
            var hc = halo.colorOverLifetime; hc.enabled = true; hc.color = Grad(Color.white, Color.white, 1f, 0f);
            halo.Play();
            // motes streaming off it as it falls (world space, so they trail behind)
            var trail = NewPS("Trail", glowM, out _);
            trail.transform.SetParent(moon.transform, false);
            var tm = trail.main; tm.loop = true; tm.duration = 1f;
            tm.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            tm.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
            tm.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            tm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.75f, 1f), new Color(0.95f, 0.97f, 1f));
            tm.scalingMode = ParticleSystemScalingMode.Shape;
            tm.maxParticles = 600;
            var tem = trail.emission; tem.rateOverTime = 160f;
            var tsh = trail.shape; tsh.shapeType = ParticleSystemShapeType.Sphere; tsh.radius = 0.5f;
            var tc = trail.colorOverLifetime; tc.enabled = true; tc.color = Grad(Color.white, Color.white, 1f, 0f);
            md.trail = trail;
            // the landing ring on the ground
            var marker = Child("Marker", root.transform, Vector3.up * 0.05f);
            marker.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            marker.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var mkr = marker.AddComponent<MeshRenderer>();
            mkr.sharedMaterial = M("M_FX_FairyRing");
            mkr.shadowCastingMode = ShadowCastingMode.Off;
            md.marker = marker.transform;
            md.markerRenderer = mkr;
            // its light: grows over the ground as it comes down, flares on impact
            var lgo = Child("Light", root.transform, Vector3.zero);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(0.7f, 0.8f, 1f); l.intensity = 4f; l.range = 20f;
            l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
            md.glow = l;
            // impact: pale shards and motes thrown up and out
            var shards = NewPS("Shards", glowM, out _);
            shards.transform.SetParent(root.transform, false);
            var sm = shards.main; sm.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(5f, 16f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
            sm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.75f, 1f), new Color(0.95f, 0.97f, 1f));
            sm.gravityModifier = 1.1f;
            sm.maxParticles = 300;
            Burst(shards, 180);
            var ssh = shards.shape; ssh.shapeType = ParticleSystemShapeType.Hemisphere; ssh.radius = 2f; ssh.rotation = new Vector3(-90f, 0f, 0f);
            var sc = shards.colorOverLifetime; sc.enabled = true; sc.color = Grad(Color.white, Color.white, 1f, 0f);
            md.shards = shards;
            // impact: a flat ring of light racing outward across the ground
            var shock = NewPS("Shockwave", glowM, out var shr);
            shock.transform.SetParent(root.transform, false);
            var shm = shock.main; shm.startLifetime = 0.6f; shm.startSpeed = new ParticleSystem.MinMaxCurve(16f, 20f);
            shm.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            shm.startColor = new Color(0.7f, 0.82f, 1f, 0.9f);
            shm.maxParticles = 200;
            Burst(shock, 120);
            var shs = shock.shape; shs.shapeType = ParticleSystemShapeType.Circle; shs.radius = 0.5f; shs.radiusThickness = 0f;
            shs.rotation = new Vector3(-90f, 0f, 0f);
            var shc = shock.colorOverLifetime; shc.enabled = true; shc.color = Grad(Color.white, Color.white, 1f, 0f);
            var shl = shock.limitVelocityOverLifetime; shl.enabled = true; shl.limit = 3f; shl.dampen = 0.08f;
            md.shockwave = shock;
            PrefabUtility.SaveAsPrefabAsset(root, MoonDropPrefabPath);
            Object.DestroyImmediate(root);
        }

        // ------------------------------------------------------------------ data assets (create once)
        static void BuildFairyMaskData()
        {
            var daggers = DataAsset<MeleeWeaponData>(DataDir + "/Equipment/Melee_FairyDaggers.asset", d =>
            {
                d.displayName = "Twin Fae Daggers";
                d.slashing = true; d.cutPower = 1; d.cutWidth = 0.9f;
                d.hitStopMultiplier = 0.8f; d.shakeMultiplier = 0.8f;
                d.slashColor = new Color(0.96f, 0.95f, 1f, 0.85f);
                d.slashEdgeColor = new Color(0.62f, 0.78f, 1f, 0.55f);
                d.slashArcWidth = 0.6f;
            });
            var chakram = DataAsset<ChakramData>(DataDir + "/Equipment/Chakram_Moon.asset", d => { d.displayName = "Moon Chakram"; });
            var moonfall = DataAsset<MoonfallData>(DataDir + "/Equipment/Special_Moonfall.asset", d => { d.displayName = "Moonfall"; d.staminaCost = 20f; });
            var form = DataAsset<FormData>(FormFairyPath, d =>
            {
                d.displayName = "Fairy"; d.rigId = "Fairy"; d.rangedKind = RangedKind.Chakram;
                d.moveSpeedMultiplier = 1.08f; d.rollDistanceMultiplier = 1.12f; d.damageTakenMultiplier = 1.1f; d.ignoreLightFlinch = false;
                d.roar = Audio.Sfx.SpellGather; d.roarPitch = 1.7f;
            });
            var mask = DataAsset<MaskData>(MaskFairyPath, d =>
            {
                d.id = "fairy"; d.displayName = "Fairy Mask";
                d.description = "A porcelain face that fell from the Fairy as she came apart. Wear it to become the fae: two white " +
                                "daggers, a pink crescent that always comes home, and the moon itself.";
                d.vortexA = new Color(0.62f, 0.78f, 1f);
                d.vortexB = new Color(0.78f, 0.36f, 0.56f);
                d.vortexC = new Color(0.06f, 0.05f, 0.14f);
                d.receivedTitle = "FAIRY MASK";
                d.receivedLine = "You put on the Fairy Mask.\nShe will not let them near you.\nRB / RT: twin daggers · LT + RT: Moon Chakram · RB + RT: Moonfall";
            });
            if (daggers.icon == null) daggers.icon = RenderIcon(FairyDaggerPath, "Icon_FairyDaggers", Quaternion.Euler(0, 0, -40));
            if (chakram.icon == null) chakram.icon = RenderIcon(FairyChakramPath, "Icon_FairyChakram", Quaternion.Euler(-70, 0, 0));
            if (mask.icon == null) mask.icon = RenderIcon(FairyMaskModelPath, "Icon_FairyMask", Quaternion.Euler(-10, 200, 0));
            if (form.moveset == null) form.moveset = AssetDatabase.LoadAssetAtPath<Moveset>(FairyMovesetPath);
            if (form.melee == null) form.melee = daggers;
            if (form.chakram == null) form.chakram = chakram;
            if (form.special == null) form.special = moonfall;
            if (mask.form == null) mask.form = form;
            if (mask.maskModel == null) mask.maskModel = AssetDatabase.LoadAssetAtPath<GameObject>(FairyMaskModelPath);
            foreach (var o in new Object[] { daggers, chakram, moonfall, form, mask }) EditorUtility.SetDirty(o);
        }

        // ------------------------------------------------------------------ player: fairy body
        /// <summary>Adds the fairy form rig under the player's Visual (called from AttachForms). Null if the model is missing.</summary>
        static FormRig AttachFairyForm(Transform visual, PlayerController pc)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(FairyFormModelPath);
            if (src == null) { Debug.LogError("[ElderTree] FairyForm.fbx missing: run build_fairy_form.py"); return null; }
            var model = (GameObject)PrefabUtility.InstantiatePrefab(src);
            model.name = "FairyForm";
            model.transform.SetParent(visual, false);
            var anim = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(FairyControllerPath);
            anim.runtimeAnimatorController = ctrl != null ? ctrl : AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            anim.avatar = AssetDatabase.LoadAllAssetsAtPath(FairyFormModelPath).OfType<Avatar>().First();
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.updateWhenOffscreen = true;
                smr.shadowCastingMode = ShadowCastingMode.On;
            }
            var chest = anim.GetBoneTransform(HumanBodyBones.Chest) ?? anim.GetBoneTransform(HumanBodyBones.Spine);
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            var rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            float rs = Mathf.Sign(anim.GetBoneTransform(HumanBodyBones.RightUpperLeg).position.x);   // which way is her right

            // ---- wings on the upper back, fluttering
            var wingsSrc = AssetDatabase.LoadAssetAtPath<GameObject>(FairyFormWingsPath);
            var wingList = new List<Transform>();
            if (wingsSrc != null)
            {
                var wgo = (GameObject)PrefabUtility.InstantiatePrefab(wingsSrc);
                wgo.transform.SetParent(model.transform, false);
                PrefabUtility.UnpackPrefabInstance(wgo, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var ws = wgo.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("FWing_")).ToArray();
                // the wings belong on her back (-Z while she faces +Z)
                if (ws.Length > 0 && ws.Average(t => t.GetComponent<Renderer>().bounds.center.z) > chest.position.z)
                    wgo.transform.localRotation = Quaternion.Euler(0, 180, 0);
                foreach (var t in ws)
                {
                    t.SetParent(chest, true);
                    var r = t.GetComponent<Renderer>();
                    if (r) r.shadowCastingMode = ShadowCastingMode.Off;
                    wingList.Add(t);
                }
                Object.DestroyImmediate(wgo);
            }
            var wings = model.AddComponent<FairyWings>();
            wings.wings = wingList.ToArray();
            wings.body = model.transform;
            wings.player = pc;

            // ---- twin daggers, gripped like the pack's own dual-wield props (sampled on the DW parry pose)
            var daggerSrc = AssetDatabase.LoadAssetAtPath<GameObject>(FairyDaggerPath);
            CalibrateDaggerGrips(out var rPos, out var rRot, out var lPos, out var lRot);
            (WeaponHitbox hb, Transform tip, WeaponTrail trail, SlashArc slash, Renderer rend, Transform socket) Dagger(Transform hand, Vector3 lp, Quaternion lr, string name)
            {
                var socket = new GameObject(name + "Socket").transform;
                socket.SetParent(hand, false);
                socket.localPosition = lp; socket.localRotation = lr;
                var d = (GameObject)PrefabUtility.InstantiatePrefab(daggerSrc);
                d.name = name;
                d.transform.SetParent(socket, false);
                foreach (var r in d.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
                var b = new GameObject("BladeBase").transform; b.SetParent(d.transform, false); b.localPosition = new Vector3(0, 0.04f, 0);
                var t = new GameObject("BladeTip").transform; t.SetParent(d.transform, false); t.localPosition = new Vector3(0, 0.31f, 0);
                var hb = d.AddComponent<WeaponHitbox>();
                hb.hurtboxMask = 1 << LayerHurtbox;
                hb.baseLocal = new Vector3(0, 0.03f, 0);
                hb.tipLocal = new Vector3(0, 0.33f, 0);
                hb.radius = 0.1f;
                hb.maxSubSteps = 8;
                var tr = d.AddComponent<WeaponTrail>();
                tr.baseRef = b; tr.tipRef = t;
                tr.material = M("M_FX_Trail");
                tr.lifetime = 0.14f;
                var sl = d.AddComponent<SlashArc>();
                sl.baseRef = b; sl.tipRef = t;
                sl.material = M("M_FX_Slash");
                return (hb, t, tr, sl, d.GetComponentInChildren<MeshRenderer>(), socket);
            }
            var right = Dagger(rHand, rPos, rRot, "DaggerR");
            var left = Dagger(lHand, lPos, lRot, "DaggerL");
            // while the chakram is in the right hand, the right dagger rides on the right hip, point down
            var daggerStow = Socket("DaggerStowSocket", hips, new Vector3(0.19f * rs, 0.9f, 0.04f), Quaternion.LookRotation(Vector3.forward, Vector3.down));

            // ---- the chakram: across the small of the back (horns up), or in the right hand
            var chakSrc = AssetDatabase.LoadAssetAtPath<GameObject>(FairyChakramPath);
            var chak = (GameObject)PrefabUtility.InstantiatePrefab(chakSrc);
            chak.name = "Chakram";
            PrefabUtility.UnpackPrefabInstance(chak, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var r in chak.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            var gripT = chak.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Grip");
            Vector3 gripLocal = gripT != null ? gripT.localPosition : new Vector3(0f, 0f, 0.19f);
            // grip -> centre runs along local -Z or +Z depending on the import: measure it
            Vector3 toCentreLocal = (-gripLocal).normalized;
            var chakStow = Socket("ChakramStowSocket", hips, new Vector3(0f, 1.0f, -0.25f),
                Quaternion.LookRotation(Vector3.down, Vector3.back));
            // stow pose: grip at the bottom, the crescent's centre above it -> centre along +up from the grip
            chakStow.rotation = Quaternion.FromToRotation(chakStow.rotation * toCentreLocal, Vector3.up) * chakStow.rotation;
            chakStow.position = new Vector3(0f, 0.92f, -0.26f) - chakStow.rotation * gripLocal;
            var chakHand = new GameObject("ChakramHandSocket").transform;
            chakHand.SetParent(rHand, false);
            CalibrateChakramGrip(chakHand, gripLocal, toCentreLocal);
            chak.transform.SetParent(chakStow, false);
            chak.transform.localPosition = Vector3.zero;
            chak.transform.localRotation = Quaternion.identity;

            var fr = model.AddComponent<FormRig>();
            fr.rigId = "Fairy";
            fr.animator = anim;
            fr.weapon = right.hb; fr.weaponTip = right.tip; fr.weaponRenderer = right.rend; fr.trail = right.trail; fr.slash = right.slash;
            fr.offWeapon = left.hb; fr.offTrail = left.trail; fr.offSlash = left.slash;
            fr.chakram = chak.transform;
            fr.chakramHandSocket = chakHand;
            fr.chakramStowSocket = chakStow;
            fr.mainWeaponStowSocket = daggerStow;
            fr.mainWeaponHandSocket = right.socket;
            fr.wings = wings;
            fr.faceSocket = Socket("FaceSocket", anim.GetBoneTransform(HumanBodyBones.Head), FairyFacePos, Quaternion.identity);
            fr.faceSocket.localScale = Vector3.one * FairyMaskScale;
            model.SetActive(false);

            pc.chakramPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChakramPrefabPath)?.GetComponent<Chakram>();
            pc.moonDropPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MoonDropPrefabPath)?.GetComponent<MoonDrop>();
            pc.chakramThrowClipLength = StateClipLength("Fairy_ChakramThrow");
            pc.chakramCatchClipLength = StateClipLength("Fairy_ChakramCatch");
            pc.chakramReleaseNorm = ChakramReleaseNorm;
            return fr;
        }

        /// <summary>Normalized frame of ThrowBoomerang01_R where the throwing hand is fastest (measured on the fairy rig).</summary>
        public static float ChakramReleaseNorm = 0.30f;   // hand speed peaks at 0.32

        /// <summary>
        /// Dagger grips from the pack itself: HumanM holds dual-wield weapons on its B-handProp.R / .L bones. Sample the DW
        /// parry pose on HumanM and on the fairy at the same frame, and re-express each prop frame on the fairy's hand
        /// (the blade runs along the prop's +Y), with the grip at the fist.
        /// </summary>
        static void CalibrateDaggerGrips(out Vector3 rPos, out Quaternion rRot, out Vector3 lPos, out Quaternion lRot)
        {
            var fairy = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FairyFormModelPath));
            var human = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PreviewRenderer.ReferenceModelPath));
            fairy.hideFlags = human.hideFlags = HideFlags.DontSave;
            try
            {
                var fa = fairy.GetComponent<Animator>() ?? fairy.AddComponent<Animator>();
                fa.avatar = AssetDatabase.LoadAllAssetsAtPath(FairyFormModelPath).OfType<Avatar>().First();
                var ts = human.GetComponentsInChildren<Transform>(true);
                var propR = ts.FirstOrDefault(t => t.name == "B-handProp.R");
                var propL = ts.FirstOrDefault(t => t.name == "B-handProp.L");
                var clip = K("Combat/1H/HumanM@ParryDW01 - Loop");
                clip.SampleAnimation(fairy, 0f); fairy.transform.position = Vector3.zero;
                clip.SampleAnimation(human, 0f); human.transform.position = Vector3.zero;
                var rh = fa.GetBoneTransform(HumanBodyBones.RightHand);
                var lh = fa.GetBoneTransform(HumanBodyBones.LeftHand);
                rRot = Quaternion.Inverse(rh.rotation) * propR.rotation;
                rPos = rh.InverseTransformPoint(FistCentre(fa, false));
                if (propL != null) lRot = Quaternion.Inverse(lh.rotation) * propL.rotation;
                else
                {
                    // no left prop bone: mirror the right grip's blade direction across the body
                    Vector3 up = propR.up; up.x = -up.x;
                    Vector3 fw = propR.forward; fw.x = -fw.x;
                    lRot = Quaternion.Inverse(lh.rotation) * Quaternion.LookRotation(fw, up);
                }
                lPos = lh.InverseTransformPoint(FistCentre(fa, true));
                Debug.Log($"[ElderTree] Dagger grips: R {rPos:F3}/{rRot.eulerAngles:F0}  L {lPos:F3}/{lRot.eulerAngles:F0} (left prop bone: {propL != null})");
            }
            finally { Object.DestroyImmediate(fairy); Object.DestroyImmediate(human); }
        }

        /// <summary>
        /// Chakram grip, on the boomerang hold pose: the wrapped grip in the right fist, its bar along the pack's prop axis
        /// (as a boomerang is held), the crescent reaching on beyond the fist, away from the wrist.
        /// </summary>
        static void CalibrateChakramGrip(Transform socket, Vector3 gripLocal, Vector3 toCentreLocal)
        {
            var fairy = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FairyFormModelPath));
            var human = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PreviewRenderer.ReferenceModelPath));
            fairy.hideFlags = human.hideFlags = HideFlags.DontSave;
            try
            {
                var fa = fairy.GetComponent<Animator>() ?? fairy.AddComponent<Animator>();
                fa.avatar = AssetDatabase.LoadAllAssetsAtPath(FairyFormModelPath).OfType<Avatar>().First();
                var prop = human.GetComponentsInChildren<Transform>(true).First(t => t.name == "B-handProp.R");
                var clip = K("Combat/Thrown/Boomerang/HumanM@ThrowBoomerang01_R - Hold");
                clip.SampleAnimation(fairy, 0f); fairy.transform.position = Vector3.zero;
                clip.SampleAnimation(human, 0f); human.transform.position = Vector3.zero;
                var rh = fa.GetBoneTransform(HumanBodyBones.RightHand);
                Vector3 fist = FistCentre(fa, false);
                Vector3 bar = prop.up;                                                   // the grip bar through the fist
                Vector3 beyond = Vector3.ProjectOnPlane(fist - rh.position, bar);         // on past the knuckles
                if (beyond.sqrMagnitude < 1e-6f) beyond = Vector3.ProjectOnPlane(fairy.transform.forward, bar);
                beyond.Normalize();
                // chakram local frame: X = the grip bar, toCentreLocal = towards the crescent's centre
                Quaternion local = Quaternion.LookRotation(toCentreLocal, Vector3.Cross(toCentreLocal, Vector3.right));
                Quaternion world = Quaternion.LookRotation(beyond, Vector3.Cross(beyond, bar));
                Quaternion rot = world * Quaternion.Inverse(local);
                Vector3 pos = fist - rot * gripLocal;
                socket.SetPositionAndRotation(rh.TransformPoint(rh.InverseTransformPoint(pos)), rot);
                socket.localPosition = rh.InverseTransformPoint(pos);
                socket.localRotation = Quaternion.Inverse(rh.rotation) * rot;
            }
            finally { Object.DestroyImmediate(fairy); Object.DestroyImmediate(human); }
        }

        // ------------------------------------------------------------------ scene
        static void PlaceFairyMaskSet(GameObject systems)
        {
            // the Fairy drops the Fairy Mask
            var enc = systems.GetComponent<BossEncounter>();
            var mask = AssetDatabase.LoadAssetAtPath<MaskData>(MaskFairyPath);
            if (enc != null)
                foreach (var b in enc.bosses)
                    if (b is FairyBoss f) { f.rewardMask = mask; EditorUtility.SetDirty(f); }
        }
    }
}
