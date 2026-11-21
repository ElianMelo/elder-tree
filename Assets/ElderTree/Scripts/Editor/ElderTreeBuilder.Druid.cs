using System.Collections.Generic;
using System.Linq;
using ElderTree.Combat;
using ElderTree.Enemies;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ElderTree.EditorTools
{
    /// <summary>
    /// Druid: textures, materials, model import, prefab (rigid chain, leaf clumps, hurtboxes with the root feet and the
    /// face as their own <see cref="DruidPart"/>s) and scene wiring.
    /// The model comes from Tools/Blender/build_druid.py (exported into Art/Models/Druid).
    /// </summary>
    public static partial class ElderTreeBuilder
    {
        public const string DruidModelDir = Root + "/Art/Models/Druid";
        public const string DruidPrefabPath = Root + "/Prefabs/Druid.prefab";

        static readonly string[] DruidMats = { "M_DruidBark", "M_DruidRoot", "M_DruidLeaf", "M_DruidEye", "M_DruidHollow" };

        /// <summary>Hip height in the model (HIP_Z in build_druid.py): the pivot sits here.</summary>
        const float DruidHipHeight = 5.2f;

        [MenuItem("Elder Tree/7c. Build Boss (Druid)")]
        public static string BuildDruidBoss()
        {
            BuildDruidArt();
            ConfigureDruidImport();
            BuildDruidPrefab();
            AssetDatabase.SaveAssets();
            return "Druid built";
        }

        // ------------------------------------------------------------------ textures + materials
        static void BuildDruidArt()
        {
            var Hex = (System.Func<string, Color>)TextureForge.Hex;
            // pale old wood (user's call, 2026-11-20: light wood, not dark bark): weathered ash-grey / honey plates split
            // by brown furrows, a little moss in the hollows, a few lichen spots
            var bark = TextureForge.SavePsx("T_DruidBark", 128, (u, v, x, y) =>
            {
                float plates = TextureForge.Fbm(u, v, 10, 2, 4, 801);
                float furrow = Mathf.Abs(plates - 0.5f) * 2f;
                float grain = TextureForge.Fbm(u, v, 24, 5, 3, 802);
                var c = Mx(Hex("#8A7258"), Hex("#C4AE8C"), 0.2f + 0.65f * grain);
                c = Mx(c, Hex("#4A3A2C"), (1f - Sstep(0.04f, 0.3f, furrow)) * 0.8f);             // furrows: brown, not black
                c *= 0.92f + 0.16f * (H2(x, y, 803) - 0.5f);
                float moss = Sstep(0.62f, 0.78f, TextureForge.Fbm(u, v, 4, 4, 4, 804)) * (1.1f - furrow * 0.5f);
                c = Mx(c, Mx(Hex("#5C7032"), Hex("#7C8E44"), H2(x / 2, y / 2, 805)), moss * 0.7f);
                TextureForge.Worley(u, v, 6, 806, out float f1, out _, out int id);
                if (id % 7 == 0 && f1 < 0.045f) c = Mx(c, Hex("#A9B09A"), 0.55f);
                return c;
            }, mips: false);
            // root bark: a shade warmer and knottier, caked with dirt from being in the ground
            var root = TextureForge.SavePsx("T_DruidRoot", 128, (u, v, x, y) =>
            {
                float plates = TextureForge.Fbm(u, v, 6, 3, 4, 811);
                float furrow = Mathf.Abs(plates - 0.5f) * 2f;
                var c = Mx(Hex("#755E46"), Hex("#AE9570"), 0.25f + 0.6f * TextureForge.Fbm(u, v, 16, 8, 3, 812));
                c = Mx(c, Hex("#3E3024"), (1f - Sstep(0.05f, 0.32f, furrow)) * 0.75f);
                c *= 0.9f + 0.2f * (H2(x, y, 813) - 0.5f);
                float dirt = Sstep(0.5f, 0.7f, TextureForge.Fbm(u, v, 5, 5, 4, 814));
                c = Mx(c, Hex("#4A3C30") * (0.9f + 0.2f * H2(x, y, 815)), dirt * 0.7f);
                TextureForge.Worley(u, v, 5, 816, out float f1, out _, out int id);
                if (id % 4 == 0 && f1 < 0.06f) c *= 0.6f;                                  // knots
                return c;
            }, mips: false);
            // leaves: a 2x2 atlas of hanging sprays, stems at the top of each cell. Bottom row (cells 0, 1): light fresh
            // greens with a few yellowing leaves. Top row (cells 2, 3): dark greens, the shadow layer under the clumps.
            // The Druid Mask form shares this atlas (build_druid.py / build_druid_form.py pick cells by row).
            var specs = new List<(int cell, Vector2 at, float ang, float len, float wid, Color col)>();
            var rnd = new System.Random(821);
            float R() => (float)rnd.NextDouble();
            Color[] light = { Hex("#7EA83E"), Hex("#90B848"), Hex("#A4C454"), Hex("#88AE42"), Hex("#B8CC66"), Hex("#A8A24E") };
            Color[] dark = { Hex("#26391A"), Hex("#2F4620"), Hex("#3A5226"), Hex("#22331A"), Hex("#405028"), Hex("#3A3420") };
            for (int cell = 0; cell < 4; cell++)
                for (int k = 0; k < 10; k++)
                {
                    float ang = Mathf.Lerp(-80f, 80f, (k + R() * 0.7f) / 10f) * Mathf.Deg2Rad;
                    var at = new Vector2(0.5f + (R() - 0.5f) * 0.2f, 0.93f - R() * 0.3f);
                    int ci = R() < 0.1f ? 5 : R() < 0.15f ? 4 : rnd.Next(4);
                    specs.Add((cell, at, ang, 0.3f + 0.17f * R(), 0.075f + 0.045f * R(), (cell < 2 ? light : dark)[ci]));
                }
            var leaf = TextureForge.SavePsx("T_DruidLeaf", 128, (u, v, x, y) =>
            {
                int cell = (u >= 0.5f ? 1 : 0) + (v >= 0.5f ? 2 : 0);
                var p = new Vector2(u * 2f % 1f, v * 2f % 1f);
                for (int i = specs.Count - 1; i >= 0; i--)
                {
                    var s = specs[i];
                    if (s.cell != cell) continue;
                    var dir = new Vector2(Mathf.Sin(s.ang), -Mathf.Cos(s.ang));
                    var d = p - s.at;
                    float along = Vector2.Dot(d, dir), across = d.x * dir.y - d.y * dir.x;
                    if (along <= 0f || along >= s.len) continue;
                    float half = s.wid * Mathf.Pow(Mathf.Sin(Mathf.PI * along / s.len), 0.75f);
                    if (Mathf.Abs(across) >= half) continue;
                    float edge = Mathf.Abs(across) / half;
                    var c = s.col * (1.12f - 0.4f * edge * edge) * (0.85f + 0.3f * (1f - along / s.len));
                    if (Mathf.Abs(across) < 0.007f) c *= 0.7f;                                  // midrib
                    c *= 0.9f + 0.2f * (H2(x, y, 822) - 0.5f);
                    c.a = 1f;
                    return c;
                }
                return new Color(0.1f, 0.12f, 0.07f, 0f);
            }, mips: false, alpha: true);

            Matte(Lit("M_DruidBark", Color.white, bark, null, 0f, 1f, null, false));
            Matte(Lit("M_DruidRoot", Color.white, root, null, 0f, 1f, null, false));
            Matte(Lit("M_DruidHollow", Hex("#0E0B09"), null, null, 0f));
            var lm = Lit("M_DruidLeaf", Color.white, leaf, null, 0f, 1f, null, false);
            Matte(lm);
            lm.SetFloat("_AlphaClip", 1f);
            lm.SetFloat("_Cutoff", 0.5f);
            lm.EnableKeyword("_ALPHATEST_ON");
            lm.SetFloat("_Cull", 0f);                   // cards are double sided
            lm.SetOverrideTag("RenderType", "TransparentCutout");
            lm.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(lm);
            // sunken eyes: a dim sickly green, the accent glow of the toxic-forest references
            Glow("M_DruidEye", Hex("#2E3A12"), 0f, Hex("#8FBF3A") * 1.3f);
        }

        static void ConfigureDruidImport()
        {
            string path = $"{DruidModelDir}/Druid.fbx";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            if (imp == null) { Debug.LogError($"[ElderTree] Missing {path} (run Tools/Blender/build_druid.py)"); return; }
            imp.globalScale = 1f;
            imp.useFileScale = true;
            imp.animationType = ModelImporterAnimationType.None;
            imp.importAnimation = false;
            imp.importBlendShapes = false;
            imp.importCameras = false;
            imp.importLights = false;
            imp.importNormals = ModelImporterNormals.Calculate;
            imp.normalSmoothingAngle = 40f;
            imp.importTangents = ModelImporterTangents.None;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.addCollider = false;
            foreach (var n in DruidMats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");
                if (m) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), m);
            }
            imp.SaveAndReimport();
        }

        // ------------------------------------------------------------------ prefab
        /// <summary>A trigger capsule a -> b (world) on <paramref name="bone"/>; with a <see cref="DruidPart"/> when it matters on its own.</summary>
        static GameObject DruidHurt(Transform bone, string name, Vector3 a, Vector3 b, float radius, DruidBoss boss = null,
                                    DruidBoss.Part part = DruidBoss.Part.Body)
        {
            var go = new GameObject(name) { layer = LayerHurtbox };
            go.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(b - a, Vector3.up));
            go.transform.SetParent(bone, true);
            var c = go.AddComponent<CapsuleCollider>();
            c.isTrigger = true; c.direction = 2; c.radius = radius; c.height = Vector3.Distance(a, b) + radius * 2f;
            if (boss != null && part != DruidBoss.Part.Body)
            {
                var dp = go.AddComponent<DruidPart>();
                dp.boss = boss; dp.part = part;
            }
            go.AddComponent<Hurtbox>();
            return go;
        }

        static void BuildDruidPrefab()
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{DruidModelDir}/Druid.fbx");
            if (fbx == null) { Debug.LogError("[ElderTree] Druid.fbx missing"); return; }
            var root = new GameObject("Druid");
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            var pivot = Child("Pivot", root.transform, Vector3.up * DruidHipHeight);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            model.name = "Model";
            model.transform.SetParent(pivot.transform, false);
            model.transform.localPosition = Vector3.down * DruidHipHeight;
            var all = model.GetComponentsInChildren<Transform>(true);
            Transform Part(string n) => all.First(t => t.name == n);
            // the face must look down +Z
            if (Part("Druid_Head").position.z < 0f) model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            // left = 0, right = 1, by where the part actually ended up (the FBX axis conversion may mirror names)
            Transform[] Pair(string n)
            {
                var a = Part(n + "_L"); var b = Part(n + "_R");
                return a.position.x < b.position.x ? new[] { a, b } : new[] { b, a };
            }
            var pelvis = Part("Druid_Pelvis");
            var torso = Part("Druid_Torso");
            var headT = Part("Druid_Head");
            var up = Pair("Druid_UpperArm"); var fo = Pair("Druid_Forearm"); var hd = Pair("Druid_Hand");
            var th = Pair("Druid_Thigh"); var sh = Pair("Druid_Shin"); var ft = Pair("Druid_Foot");

            // rigid chain
            torso.SetParent(pelvis, true);
            headT.SetParent(torso, true);
            for (int i = 0; i < 2; i++)
            {
                up[i].SetParent(torso, true); fo[i].SetParent(up[i], true); hd[i].SetParent(fo[i], true);
                th[i].SetParent(pelvis, true); sh[i].SetParent(th[i], true); ft[i].SetParent(sh[i], true);
            }
            // leaf clumps grow on their part: Druid_Leaf_<Part>_<n> -> Druid_<Part>
            var leaves = new List<Transform>();
            foreach (var t in all.Where(t => t.name.StartsWith("Druid_Leaf_")).ToArray())
            {
                string rest = t.name.Substring("Druid_Leaf_".Length);
                string partName = "Druid_" + rest.Substring(0, rest.LastIndexOf('_'));
                var parent = all.FirstOrDefault(p => p.name == partName);
                if (parent) t.SetParent(parent, true);
                var rend = t.GetComponent<Renderer>();
                if (rend) rend.shadowCastingMode = ShadowCastingMode.On;
                leaves.Add(t);
            }

            var boss = root.AddComponent<DruidBoss>();
            Vector3 fwd = root.transform.forward, upv = Vector3.up;

            // palms: where a grabbed player sits (palm centre, a little toward the curl of the fingers)
            var palms = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                var b = hd[i].GetComponent<Renderer>().bounds;
                float inward = i == 0 ? 1f : -1f;
                palms[i] = Child("Palm", hd[i], Vector3.zero).transform;
                palms[i].position = new Vector3(hd[i].position.x + inward * 0.25f, hd[i].position.y - 0.75f, b.center.z);
            }

            // hurtboxes. Body: torso, pelvis, limbs. Their own parts: the root feet and the face (with the nose).
            var tb = torso.GetComponent<Renderer>().bounds;
            DruidHurt(torso, "Hurt_Torso", new Vector3(0, tb.min.y + 0.9f, tb.center.z), new Vector3(0, tb.max.y - 0.7f, tb.center.z + 0.2f), 1.35f);
            DruidHurt(pelvis, "Hurt_Pelvis", new Vector3(-0.6f, DruidHipHeight, 0), new Vector3(0.6f, DruidHipHeight, 0), 1.05f);
            DruidHurt(headT, "Hurt_Face", new Vector3(0, 8.6f, 0.6f), new Vector3(0, 10f, 0.6f), 1.05f, boss, DruidBoss.Part.Head);
            DruidHurt(headT, "Hurt_Nose", new Vector3(0, 9.2f, 1.5f), new Vector3(0, 8f, 2.2f), 0.45f, boss, DruidBoss.Part.Head);
            for (int i = 0; i < 2; i++)
            {
                DruidHurt(up[i], "Hurt_UpperArm", up[i].position, fo[i].position, 0.42f);
                DruidHurt(fo[i], "Hurt_Forearm", fo[i].position, hd[i].position, 0.36f);
                DruidHurt(hd[i], "Hurt_Hand", hd[i].position, hd[i].position + Vector3.down * 1.5f, 0.5f);
                DruidHurt(th[i], "Hurt_Thigh", th[i].position, sh[i].position, 0.72f);
                DruidHurt(sh[i], "Hurt_Shin", sh[i].position, ft[i].position + upv * 0.6f, 0.6f);
                var part = i == 0 ? DruidBoss.Part.FootL : DruidBoss.Part.FootR;
                Vector3 a = ft[i].position;
                Vector3 g = new Vector3(a.x, 0.35f, a.z);
                // the root mass: ankle flare, toes forward, a cross spread for the side roots and heel
                DruidHurt(ft[i], "Hurt_Roots", g - fwd * 1.1f, g + fwd * 2f, 0.75f, boss, part);
                DruidHurt(ft[i], "Hurt_RootsWide", g - Vector3.right * 1.3f, g + Vector3.right * 1.3f, 0.6f, boss, part);
                DruidHurt(ft[i], "Hurt_Ankle", a + upv * 0.2f, new Vector3(a.x, 0.3f, a.z), 0.75f, boss, part);
            }
            // solid shins: you can't walk through its legs (off while that leg is in the air)
            var legCols = new Collider[2];
            for (int i = 0; i < 2; i++)
            {
                var go = Child("Solid", sh[i], Vector3.zero);
                go.transform.SetPositionAndRotation((sh[i].position + ft[i].position) * 0.5f, Quaternion.identity);
                var c = go.AddComponent<CapsuleCollider>();
                c.direction = 1; c.radius = 0.55f; c.height = Vector3.Distance(sh[i].position, ft[i].position) + 0.6f;
                legCols[i] = c;
            }

            var lockPoint = Child("LockPoint", root.transform, new Vector3(0, 5.8f, 0.5f));

            boss.pivot = pivot.transform;
            boss.torso = torso; boss.head = headT;
            boss.upperArm = up; boss.forearm = fo; boss.hand = hd; boss.palm = palms;
            boss.thigh = th; boss.shin = sh; boss.foot = ft;
            boss.leaves = leaves.ToArray();
            boss.hull = DruidHull(new[] { pelvis, torso, headT });
            boss.lockPoint = lockPoint.transform;
            var bodyRenderers = model.GetComponentsInChildren<Renderer>().Where(r => !r.name.StartsWith("Druid_Leaf_")).ToArray();
            boss.flashRenderers = bodyRenderers;
            boss.footRenderers = new[] { ft[0].GetComponent<Renderer>(), ft[1].GetComponent<Renderer>() };
            boss.legColliders = legCols;
            var tg = root.AddComponent<Targetable>();
            tg.lockPoint = lockPoint.transform; tg.lockRadius = 1.6f; tg.displayName = boss.displayName; tg.rangeScale = 1.8f;

            SetLayerRecursive(root, LayerEnemy);
            foreach (var h in root.GetComponentsInChildren<Hurtbox>(true)) h.gameObject.layer = LayerHurtbox;
            PrefabUtility.SaveAsPrefabAsset(root, DruidPrefabPath);
            Object.DestroyImmediate(root);
        }

        /// <summary>A few extreme vertices of each body part (in its local space): kept above the ground when it lies down.</summary>
        static DruidBoss.HullPoint[] DruidHull(Transform[] parts)
        {
            var dirs = new List<Vector3>();
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                        if (x != 0 || y != 0 || z != 0) dirs.Add(new Vector3(x, y, z).normalized);
            var hull = new List<DruidBoss.HullPoint>();
            foreach (var part in parts)
            {
                var mesh = part.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                var verts = mesh.vertices;
                var picked = new HashSet<int>();
                foreach (var d in dirs)
                {
                    int best = -1; float bestDot = float.MinValue;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        float dot = Vector3.Dot(part.TransformPoint(verts[i]), d);
                        if (dot > bestDot) { bestDot = dot; best = i; }
                    }
                    if (best >= 0 && picked.Add(best)) hull.Add(new DruidBoss.HullPoint { bone = part, local = verts[best] });
                }
            }
            return hull.ToArray();
        }

        // ------------------------------------------------------------------ scene
        static void PlaceDruidBoss(Transform actorRoot, BossEncounter enc)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DruidPrefabPath);
            if (prefab == null || enc == null) { Debug.LogWarning("[ElderTree] Druid prefab missing: run BuildDruidBoss()"); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(actorRoot);
            go.transform.position = new Vector3(0f, 0f, 45f);
            var boss = go.GetComponent<DruidBoss>();
            boss.arenaCenter = Vector3.zero;
            enc.bosses.Add(boss);
        }
    }
}
