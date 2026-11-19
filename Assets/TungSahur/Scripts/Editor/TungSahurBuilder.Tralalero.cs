using System.Linq;
using TungSahur.Combat;
using TungSahur.Enemies;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TungSahur.EditorTools
{
    /// <summary>
    /// Tralalero Tralala: textures, materials, model import, prefab and scene wiring.
    /// The model comes from Tools/Blender/build_tralalero.py (exported into Art/Models/Tralalero).
    /// Uses the croc mouth / teeth materials from <see cref="BuildBoss"/>, so it runs after it.
    /// </summary>
    public static partial class TungSahurBuilder
    {
        public const string TralaleroModelDir = Root + "/Art/Models/Tralalero";
        public const string TralaleroPrefabPath = Root + "/Prefabs/Tralalero.prefab";

        static readonly string[] TralaleroMats =
            { "M_SharkSkin", "M_SharkBelly", "M_SharkEye", "M_CrocMouth", "M_CrocTeeth", "M_SneakerCloth", "M_SneakerSole" };
        static readonly string[] LegKeys = { "FL", "FR", "B" };

        [MenuItem("Tung Sahur/7b. Build Boss (Tralalero)")]
        public static string BuildTralalero()
        {
            BuildTralaleroArt();
            ConfigureTralaleroImport();
            BuildTralaleroPrefab();
            AssetDatabase.SaveAssets();
            return "Tralalero built";
        }

        // ------------------------------------------------------------------ textures + materials
        static void BuildTralaleroArt()
        {
            var Hex = (System.Func<string, Color>)TextureForge.Hex;
            // scarred slate hide: sandpaper denticles, darker saddle streaks, pale old bite scars, grime
            var skin = TextureForge.SavePsx("T_SharkSkin", 128, (u, v, x, y) =>
            {
                float n = TextureForge.Fbm(u, v, 5, 5, 4, 501);
                var c = Mx(Hex("#1E252B"), Hex("#46525A"), 0.35f + 0.45f * n);
                c *= 0.9f + 0.2f * (H2(x, y, 502) - 0.5f) + 0.06f * H2(x / 2, y / 2, 503);       // denticle grit
                float streak = TextureForge.Fbm(u * 3f, v * 0.6f, 4, 2, 3, 504);
                c *= 1f - Sstep(0.58f, 0.78f, streak) * 0.3f;
                // old scars: thin pale crescents (arcs of Worley cell borders) with dark edges
                TextureForge.Worley(u, v, 3, 505, out float f1, out float f2, out int id);
                float edge = f2 - f1;
                if (id % 5 == 0 && edge < 0.022f) c = Mx(c, Hex("#6A6A62"), 0.45f);
                else if (id % 5 == 0 && edge < 0.04f) c *= 0.8f;
                // dried mud spatter low on the hide
                c = Mx(c, Hex("#3A3024"), Sstep(0.66f, 0.8f, TextureForge.Fbm(u, v, 9, 9, 2, 506)) * 0.55f);
                return c;
            });
            var belly = TextureForge.SavePsx("T_SharkBelly", 128, (u, v, x, y) =>
            {
                float n = TextureForge.Fbm(u, v, 4, 4, 4, 511);
                var c = Mx(Hex("#5C584E"), Hex("#8E897A"), 0.3f + 0.5f * n);
                c *= 0.92f + 0.16f * (H2(x, y, 512) - 0.5f);
                c = Mx(c, Hex("#3C3226"), Sstep(0.55f, 0.75f, TextureForge.Fbm(u, v, 7, 7, 3, 513)) * 0.6f);   // dirt from the burrow
                TextureForge.Worley(u, v, 4, 514, out float f1, out float f2, out int id);
                if (id % 4 == 1 && f2 - f1 < 0.03f) c = Mx(c, Hex("#4A1A16"), 0.6f);                            // raw scratches
                return c;
            });
            // worn running-shoe mesh: faded blue weave, mud caked toward the sole
            var cloth = TextureForge.SavePsx("T_SneakerCloth", 64, (u, v, x, y) =>
            {
                bool weave = ((x / 2) + (y / 2)) % 2 == 0;
                var c = Mx(Hex("#1C3444"), Hex("#38627A"), 0.35f + 0.4f * TextureForge.Fbm(u, v, 4, 4, 3, 521));
                c *= weave ? 1.06f : 0.88f;
                c = Mx(c, Hex("#3A3024"), Sstep(0.45f, 0.7f, TextureForge.Fbm(u, v, 5, 5, 3, 522) * (1.2f - v * 0.6f)) * 0.75f);
                return c * (0.92f + 0.16f * (H2(x, y, 523) - 0.5f));
            });
            var sole = TextureForge.SavePsx("T_SneakerSole", 64, (u, v, x, y) =>
            {
                var c = Mx(Hex("#6E6A5E"), Hex("#A29C8A"), 0.35f + 0.45f * TextureForge.Fbm(u, v, 4, 4, 3, 531));
                if (y % 6 == 0) c *= 0.72f;                                                       // tread grooves
                c = Mx(c, Hex("#3A3024"), Sstep(0.5f, 0.72f, TextureForge.Fbm(u, v, 6, 6, 3, 532)) * 0.7f);
                return c * (0.92f + 0.16f * (H2(x, y, 533) - 0.5f));
            });
            Matte(Lit("M_SharkSkin", Color.white, skin, null, 0f, 1f, null, false));
            Matte(Lit("M_SharkBelly", Color.white, belly, null, 0f, 1f, null, false));
            Matte(Lit("M_SneakerCloth", Color.white, cloth, null, 0f, 1f, null, false));
            Matte(Lit("M_SneakerSole", Color.white, sole, null, 0f, 1f, null, false));
            Glow("M_SharkEye", TextureForge.Hex("#2A0E08"), 0f, TextureForge.Hex("#C8501E") * 0.35f);   // dead eye, ember glint
        }

        static void ConfigureTralaleroImport()
        {
            string path = $"{TralaleroModelDir}/Tralalero.fbx";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            if (imp == null) { Debug.LogError($"[TungSahur] Missing {path} (run Tools/Blender/build_tralalero.py)"); return; }
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
            foreach (var n in TralaleroMats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");
                if (m) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), m);
            }
            imp.SaveAndReimport();
        }

        // ------------------------------------------------------------------ prefab
        const float SharkPivotHeight = 2.1f;

        /// <summary>A trigger capsule along a bone (world points a -> b), parented to <paramref name="bone"/>.</summary>
        static void BoneHurtbox(Transform bone, string name, Vector3 a, Vector3 b, float radius)
        {
            var go = new GameObject(name) { layer = LayerHurtbox };
            go.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(b - a, Vector3.up));
            go.transform.SetParent(bone, true);
            var c = go.AddComponent<CapsuleCollider>();
            c.isTrigger = true; c.direction = 2; c.radius = radius; c.height = Vector3.Distance(a, b) + radius * 2f;
            go.AddComponent<Hurtbox>();
        }

        static void BuildTralaleroPrefab()
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{TralaleroModelDir}/Tralalero.fbx");
            if (fbx == null) { Debug.LogError("[TungSahur] Tralalero.fbx missing"); return; }
            var root = new GameObject("Tralalero");
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            var pivot = Child("Pivot", root.transform, Vector3.up * SharkPivotHeight);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            model.name = "Model";
            model.transform.SetParent(pivot.transform, false);
            model.transform.localPosition = Vector3.down * SharkPivotHeight;
            Transform Part(string n) => model.GetComponentsInChildren<Transform>().First(t => t.name == n);
            // the snout must point down +Z
            if (Part("Shark_Jaw").position.z < 0f) model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            // rigid chains: tail0 -> tail1, thigh -> shin -> foot
            var tail0 = Part("Shark_Tail0");
            var tail1 = Part("Shark_Tail1");
            tail1.SetParent(tail0, true);
            var thighs = LegKeys.Select(k => Part($"Shark_Thigh{k}")).ToArray();
            var shins = LegKeys.Select(k => Part($"Shark_Shin{k}")).ToArray();
            var feet = LegKeys.Select(k => Part($"Shark_Foot{k}")).ToArray();
            for (int i = 0; i < 3; i++)
            {
                shins[i].SetParent(thighs[i], true);
                feet[i].SetParent(shins[i], true);
            }

            // hurtboxes (pivot space: y = 0 is the body centreline, +Z the snout)
            AddHurtbox(pivot.transform, "Hurt_Body", new Vector3(0, 0f, 0.1f), null, 0.92f, 4.4f, 2);
            AddHurtbox(pivot.transform, "Hurt_Head", new Vector3(0, -0.2f, 2.6f), null, 0.7f, 2.6f, 2);
            AddHurtbox(pivot.transform, "Hurt_Fin", new Vector3(0, 1.3f, -0.2f), new Vector3(0.3f, 1.1f, 1.1f));
            Vector3 up = Vector3.up;
            BoneHurtbox(tail0, "Hurt_Tail", tail0.position + root.transform.forward * 0.2f, tail1.position, 0.45f);
            BoneHurtbox(tail1, "Hurt_Caudal", tail1.position, tail1.position - root.transform.forward * 1.1f + up * 0.6f, 0.35f);
            for (int i = 0; i < 3; i++)
            {
                BoneHurtbox(thighs[i], "Hurt_Thigh", thighs[i].position, shins[i].position, 0.3f);
                BoneHurtbox(shins[i], "Hurt_Shin", shins[i].position, feet[i].position + up * 0.15f, 0.26f);
            }
            // solid body while it stands still (stops the player walking through it)
            var solid = Child("Body_Solid", pivot.transform, Vector3.zero);
            var c1 = solid.AddComponent<CapsuleCollider>(); c1.center = new Vector3(0, 0f, 0.4f); c1.radius = 0.85f; c1.height = 5f; c1.direction = 2;

            var lockPoint = Child("LockPoint", root.transform, new Vector3(0, 2.25f, 0.9f));

            var dig = root.AddComponent<AudioSource>();
            dig.clip = SoundPicks.DigLoop();
            dig.outputAudioMixerGroup = GameMixer.Sfx;
            dig.loop = true; dig.playOnAwake = false; dig.spatialBlend = 0.85f; dig.rolloffMode = AudioRolloffMode.Linear;
            dig.minDistance = 6f; dig.maxDistance = 45f; dig.dopplerLevel = 0f; dig.volume = 0f;

            var boss = root.AddComponent<TralaleroBoss>();
            boss.pivot = pivot.transform;
            boss.jaw = Part("Shark_Jaw");
            boss.tail0 = tail0;
            boss.tail1 = tail1;
            boss.thighs = thighs;
            boss.shins = shins;
            boss.feet = feet;
            boss.lockPoint = lockPoint.transform;
            boss.flashRenderers = model.GetComponentsInChildren<Renderer>().ToArray();
            boss.bodyColliders = solid.GetComponents<Collider>();
            boss.digAudio = dig;
            var tg = root.AddComponent<Targetable>();
            tg.lockPoint = lockPoint.transform; tg.lockRadius = 1f; tg.displayName = boss.displayName; tg.rangeScale = 1.4f;

            SetLayerRecursive(root, LayerEnemy);
            foreach (var hb in root.GetComponentsInChildren<Hurtbox>(true)) hb.gameObject.layer = LayerHurtbox;
            PrefabUtility.SaveAsPrefabAsset(root, TralaleroPrefabPath);
            Object.DestroyImmediate(root);
        }

        // ------------------------------------------------------------------ scene
        static void PlaceTralalero(Transform actorRoot, BossEncounter enc)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TralaleroPrefabPath);
            if (prefab == null || enc == null) { Debug.LogWarning("[TungSahur] Tralalero prefab missing: run BuildTralalero()"); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(actorRoot);
            go.transform.position = new Vector3(0f, 0f, 40f);
            var boss = go.GetComponent<TralaleroBoss>();
            boss.arenaCenter = Vector3.zero;
            enc.bosses.Add(boss);
        }
    }
}
