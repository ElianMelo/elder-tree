using System.Collections.Generic;
using System.Linq;
using ElderTree.Enemies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ElderTree.EditorTools
{
    /// <summary>
    /// The Druid's first-meeting cinematic: its lair in the Verdant Cathedral (where it hides, the two trees it shoves
    /// aside, the tree it tears in two, where the halves land, the leaves that rain down) and its sounds.
    /// Menu 7c2 patches the existing prefab and forest scene; 7c and 9h do it as part of their full builds.
    /// </summary>
    public static partial class ElderTreeBuilder
    {
        /// <summary>Which way the Druid comes out of the forest: the shaded north-west, left of the great kapok (looking that
        /// way from the clearing you look away from the low sun, so the haze doesn't wash it out).</summary>
        const float DruidLairAngle = 124f;
        // the torn tree stands at the clearing's edge, off to the Druid's right, so the cameras in the clearing see past it
        const float LairHideR = 31f, LairPushR = 26.2f, LairPushSide = 3.0f, LairRipR = 21.4f, LairRipSide = 2.2f, LairEntryR = 13f;
        const string RipTreeProp = "MidTree_B";
        /// <summary>The torn tree is a big one (a sapling would look like a twig in its fists).</summary>
        const float RipTreeScale = 1.35f;
        /// <summary>Where the torn tree breaks: off its stump, then in two (m up the trunk).</summary>
        static readonly float[] RipCuts = { 2.0f, 5.0f };

        static void ApplyDruidIntro(DruidBoss boss)
        {
            boss.introMusic = SoundPicks.DruidIntroMusic();
            boss.introRevealClip = SoundPicks.DruidIntroReveal();
            boss.introRoarClip = SoundPicks.DruidIntroRoar();
        }

        /// <summary>Adds the intro cinematic to the existing Druid prefab and forest scene without rebuilding them.</summary>
        [MenuItem("Elder Tree/7c2. Druid intro cinematic (lair + sounds)")]
        public static string BuildDruidIntro()
        {
            var root = PrefabUtility.LoadPrefabContents(DruidPrefabPath);
            var boss = root.GetComponent<DruidBoss>();
            ApplyDruidIntro(boss);
            PrefabUtility.SaveAsPrefabAsset(root, DruidPrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(ForestScenePath);
            string lair = BuildDruidLair();
            EditorSceneManager.SaveScene(scene);
            return $"Druid intro: music {(boss.introMusic ? boss.introMusic.name : "MISSING")}, reveal {(boss.introRevealClip ? boss.introRevealClip.name : "MISSING")}, roar {(boss.introRoarClip ? boss.introRoarClip.name : "MISSING")}; {lair}";
        }

        /// <summary>
        /// Into the open forest scene: clears a path through the undergrowth from the hiding place to the stone circle,
        /// then places the lair. Safe to run again (the old lair is replaced; what was cleared stays cleared).
        /// </summary>
        static string BuildDruidLair()
        {
            foreach (var old in Object.FindObjectsByType<DruidLair>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(old.gameObject);
            Vector3 up = Vector3.up;
            Vector3 u = Polar(DruidLairAngle, 1f);                 // out of the clearing
            Vector3 D = -u;                                        // the Druid's way in
            Vector3 R = Vector3.Cross(up, D);                      // its right as it comes in
            Vector3 G(Vector3 p, float lift = 0f) { p.y = ForestHeight(p.x, p.z) + lift; return p; }

            // ---- clear its path: undergrowth, understorey trees and rocks between the hiding place and the stones
            Vector3 O = u * LairHideR;
            int cleared = 0;
            foreach (var rootName in new[] { "--- Trees ---", "--- Understorey ---", "--- Stones ---", "--- Environment ---" })
            {
                var rootGo = GameObject.Find(rootName);
                if (rootGo == null) continue;
                foreach (var t in rootGo.transform.Cast<Transform>().ToArray())
                {
                    string n = t.name;
                    if (n.StartsWith("Kapok") || n.StartsWith("Altar") || n.StartsWith("Stone") || n.StartsWith("Deep Forest") || n.StartsWith("Forest") ||
                        n.StartsWith("ArenaBounds") || n.StartsWith("Canopy") || n.StartsWith("Ground")) continue;
                    Vector3 rel = Flat(t.position - O);
                    float along = Vector3.Dot(rel, D), side = Mathf.Abs(Vector3.Dot(rel, R));
                    bool tall = n.StartsWith("MidTree") || n.StartsWith("Emergent") || n.StartsWith("Acai") || n.StartsWith("TreeFern") ||
                                n.StartsWith("Liana") || n.StartsWith("FallenLog");
                    // a lane out to the stones, wide enough for the cameras to see down it; the brush round the hiding place
                    // itself stays as a screen (it pushes through it), and so do the big emergents behind it
                    float width = n.StartsWith("Emergent") ? 2.5f : tall ? 4.6f : 5f;
                    float from = tall ? -3f : 2.5f;
                    if (along < from || along > LairHideR - 18f || side > width) continue;
                    Object.DestroyImmediate(t.gameObject);
                    cleared++;
                }
            }

            // ---- the lair
            var lairGo = new GameObject("DruidLair");
            var actors = GameObject.Find("--- Actors ---");
            lairGo.transform.SetParent(actors ? actors.transform : null);
            lairGo.transform.SetPositionAndRotation(G(O), Quaternion.LookRotation(D));
            var lair = lairGo.AddComponent<DruidLair>();
            Transform Mark(string n, Vector3 p, Quaternion r)
            {
                var go = new GameObject(n);
                go.transform.SetParent(lairGo.transform);
                go.transform.SetPositionAndRotation(p, r);
                return go.transform;
            }
            lair.hide = Mark("Hide", G(O), Quaternion.LookRotation(D));
            lair.entry = Mark("Entry", G(u * LairEntryR), Quaternion.LookRotation(D));

            // two trees it shoves aside (whole, movable, no colliders: they're out past the arena wall)
            Vector3 pushC = u * LairPushR;
            lair.pushTrees = new Transform[2];
            foreach (var (i, side, prop, scale, yawDeg) in new[] { (0, -1f, "MidTree_A", 0.82f, 35f), (1, 1f, "MidTree_C", 0.68f, 200f) })
            {
                var tree = (GameObject)PrefabUtility.InstantiatePrefab(ForestProp(prop));
                tree.name = $"PushTree{i}";
                tree.transform.SetParent(lairGo.transform);
                tree.transform.SetPositionAndRotation(G(pushC + R * side * LairPushSide, -0.1f), Quaternion.Euler(0f, yawDeg, 0f));
                tree.transform.localScale = Vector3.one * scale;
                foreach (var c in tree.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                lair.pushTrees[i] = tree.transform;
            }

            // the tree it tears in two: a stump that stays, a middle for its left fist, a crown for its right
            var mats = SplitRipTree(out var meshes, out var pivots);
            Vector3 stumpPos = G(u * LairRipR + R * LairRipSide, -0.1f);
            var rot = Quaternion.LookRotation(D);
            var ripRoot = new GameObject("RipTree").transform;
            ripRoot.SetParent(lairGo.transform);
            ripRoot.SetPositionAndRotation(stumpPos, rot);
            Transform Piece(string n, int k)
            {
                var go = new GameObject(n);
                go.transform.SetParent(ripRoot);
                go.transform.SetPositionAndRotation(stumpPos + rot * (pivots[k] * RipTreeScale), rot);
                go.transform.localScale = Vector3.one * RipTreeScale;
                go.AddComponent<MeshFilter>().sharedMesh = meshes[k];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = mats;
                mr.shadowCastingMode = ShadowCastingMode.On;
                return go.transform;
            }
            lair.ripStump = Piece("Stump", 0);
            lair.ripMid = Piece("Middle", 1);
            lair.ripTop = Piece("Crown", 2);
            lair.ripBreaks = new Vector2(RipCuts[0], RipCuts[1]) * RipTreeScale;

            // where the halves land: the clearest spots in the undergrowth either side, lying along the throw
            var obstacles = new List<Vector3>();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.parent == null) continue;
                string pn = t.parent.name;
                if (pn != "--- Trees ---" && pn != "--- Stones ---" && pn != "--- Environment ---" && pn != "--- Understorey ---") continue;
                string n = t.name;
                if (!(n.StartsWith("Kapok") || n.StartsWith("Altar") || n.StartsWith("Stone") || n.StartsWith("MidTree") || n.StartsWith("Emergent") ||
                      n.StartsWith("Acai") || n.StartsWith("TreeFern") || n.StartsWith("FallenLog") || n.StartsWith("Root") || n.StartsWith("MossRock"))) continue;
                obstacles.Add(t.position);
                if (n.StartsWith("Kapok_Mother")) for (int k = 0; k < 10; k++) obstacles.Add(t.position + Polar(k * 36f, 6f));   // its buttresses
            }
            foreach (var p in lair.pushTrees) obstacles.Add(p.position);
            obstacles.Add(stumpPos);
            lair.landing = new Transform[2];
            float bestL = 0f, bestR = 0f;
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? -1f : 1f;
                Vector3 best = stumpPos + R * s * 6f; float bestD = -1f;
                for (float side = 5f; side <= 9f; side += 0.5f)
                    for (float back = -4.5f; back <= 1.5f; back += 0.5f)
                    {
                        Vector3 p = stumpPos + R * s * side - D * back;
                        if (Flat(p).magnitude < ForestWall + 1.5f) continue;              // never inside the fight
                        float d = obstacles.Min(o => Flat(o - p).magnitude);
                        if (d > bestD) { bestD = d; best = p; }
                    }
                if (i == 0) bestL = bestD; else bestR = bestD;
                // lying with the broken end toward the Druid, the crown flung on out
                Vector3 lieDir = Flat(best - (stumpPos - D * 1.5f)).normalized;
                Quaternion lie = Quaternion.FromToRotation(up, lieDir) * rot;
                Vector3 landPos = G(best, (i == 0 ? 0.32f : 0.42f) * RipTreeScale);
                lair.landing[i] = Mark(i == 0 ? "LandMiddle" : "LandCrown", landPos, lie);
            }

            // leaves shaken down over the hiding place
            var rain = LeafPS("LeafRain", out var rr);
            rain.transform.SetParent(lairGo.transform);
            rain.transform.SetPositionAndRotation(G(O + D * 3f, 9.5f), Quaternion.LookRotation(D));
            var m = rain.main;
            m.loop = true; m.duration = 2f;
            m.startLifetime = new ParticleSystem.MinMaxCurve(5f, 7.5f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.42f);
            m.gravityModifier = 0.07f;
            m.maxParticles = 500;
            var shp = rain.shape; shp.enabled = true; shp.shapeType = ParticleSystemShapeType.Box; shp.scale = new Vector3(10f, 2.5f, 9f);
            var lim = rain.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 1.6f; lim.dampen = 0.2f;
            var noise = rain.noise; noise.enabled = true; noise.strength = 0.9f; noise.frequency = 0.35f; noise.scrollSpeed = 0.3f;
            var col = rain.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, Color.white, 1f, 0f);
            var em = rain.emission; em.rateOverTime = 0f;
            rr.renderMode = ParticleSystemRenderMode.Billboard;
            lair.leafRain = rain;

            EditorUtility.SetDirty(lair);
            return $"lair at {DruidLairAngle}° (cleared {cleared} props), landing clearance L {bestL:F1} m / R {bestR:F1} m";
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>
        /// Cuts <see cref="RipTreeProp"/> at <see cref="RipCuts"/> into a stump, a middle and a crown (saved as mesh assets),
        /// with splinters and a cap at each break. Pivots: the stump at the foot, the others at their lower break.
        /// </summary>
        static Material[] SplitRipTree(out Mesh[] meshes, out Vector3[] pivots)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(ForestProp(RipTreeProp));
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            inst.transform.localScale = Vector3.one;
            var mf = inst.GetComponentInChildren<MeshFilter>();
            var mats = mf.GetComponent<MeshRenderer>().sharedMaterials;
            var src = mf.sharedMesh;
            var toRoot = mf.transform.localToWorldMatrix;
            Object.DestroyImmediate(inst);

            var sv = src.vertices.Select(v => (Vector3)toRoot.MultiplyPoint3x4(v)).ToArray();
            var sn = src.normals.Select(n => toRoot.MultiplyVector(n).normalized).ToArray();
            var suv = src.uv;
            int bark = System.Array.FindIndex(mats, x => x != null && x.name.Contains("Bark"));
            if (bark < 0) bark = 0;

            // the trunk's axis and radius at each break (bark vertices near the cut, close to the trunk's foot)
            var barkVerts = new HashSet<int>(src.GetTriangles(bark));
            Vector3 foot = barkVerts.Where(i => sv[i].y < 1f).Aggregate(Vector3.zero, (a, i) => a + sv[i]) / Mathf.Max(1, barkVerts.Count(i => sv[i].y < 1f));
            var axis = new Vector3[RipCuts.Length];
            var radius = new float[RipCuts.Length];
            for (int c = 0; c < RipCuts.Length; c++)
            {
                float h = RipCuts[c];
                var near = barkVerts.Where(i => Mathf.Abs(sv[i].y - h) < 0.9f && Flat(sv[i] - foot).magnitude < 1.2f).ToList();
                Vector3 ctr = near.Count > 0 ? near.Aggregate(Vector3.zero, (a, i) => a + sv[i]) / near.Count : foot;
                ctr.y = h;
                axis[c] = ctr;
                radius[c] = near.Count > 0 ? near.Select(i => Flat(sv[i] - ctr).magnitude).OrderBy(x => x).ElementAt(near.Count / 2) : 0.3f;
            }

            int pieces = RipCuts.Length + 1;
            pivots = new Vector3[pieces];
            pivots[0] = new Vector3(foot.x, 0f, foot.z);
            for (int k = 1; k < pieces; k++) pivots[k] = axis[k - 1];
            meshes = new Mesh[pieces];
            var rnd = new System.Random(19);
            for (int k = 0; k < pieces; k++)
            {
                float lo = k == 0 ? float.MinValue : RipCuts[k - 1], hi = k == pieces - 1 ? float.MaxValue : RipCuts[k];
                var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
                var remap = new Dictionary<int, int>();
                var subs = new List<int>[mats.Length];
                for (int s = 0; s < mats.Length; s++)
                {
                    subs[s] = new List<int>();
                    var tris = src.GetTriangles(s);
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        float cy = (sv[tris[t]].y + sv[tris[t + 1]].y + sv[tris[t + 2]].y) / 3f;
                        if (cy < lo || cy >= hi) continue;
                        for (int j = 0; j < 3; j++)
                        {
                            int i = tris[t + j];
                            if (!remap.TryGetValue(i, out int ni))
                            {
                                ni = v.Count; remap[i] = ni;
                                v.Add(sv[i] - pivots[k]); n.Add(sn[i]); uv.Add(suv != null && suv.Length > i ? suv[i] : Vector2.zero);
                            }
                            subs[s].Add(ni);
                        }
                    }
                }
                // each break: a rough cap over the open trunk and a ring of splinters sticking out of it
                if (k > 0) Break(axis[k - 1] - pivots[k], radius[k - 1], -1f, v, n, uv, subs[bark], rnd);
                if (k < pieces - 1) Break(axis[k] - pivots[k], radius[k], 1f, v, n, uv, subs[bark], rnd);

                var mesh = new Mesh { name = $"DruidRipTree_{(k == 0 ? "Stump" : k == 1 ? "Middle" : "Crown")}" };
                mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv);
                mesh.subMeshCount = mats.Length;
                for (int s = 0; s < mats.Length; s++) mesh.SetTriangles(subs[s], s);
                mesh.RecalculateBounds();
                string path = $"{ForestDir}/{mesh.name}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing != null) { EditorUtility.CopySerialized(mesh, existing); meshes[k] = existing; }
                else { AssetDatabase.CreateAsset(mesh, path); meshes[k] = mesh; }
            }
            AssetDatabase.SaveAssets();
            return mats;
        }

        /// <summary>A break in a trunk at <paramref name="c"/>: a cap facing <paramref name="dir"/> (+1 up, -1 down) and splinters.</summary>
        static void Break(Vector3 c, float r, float dir, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tris, System.Random rnd)
        {
            const int sides = 8;
            Vector3 up = Vector3.up * dir;
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            // cap: a fan, slightly dished and jagged
            int center = v.Count;
            v.Add(c + up * R(-0.04f, 0.04f)); n.Add(up); uv.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                v.Add(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r * 0.97f + up * R(-0.06f, 0.08f));
                n.Add(up); uv.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.2f, 0.5f + Mathf.Sin(a) * 0.2f));
            }
            for (int i = 0; i < sides; i++)
            {
                int a = center + 1 + i, b = center + 1 + (i + 1) % sides;
                if (dir > 0f) { tris.Add(center); tris.Add(b); tris.Add(a); } else { tris.Add(center); tris.Add(a); tris.Add(b); }
            }
            // splinters: thin wedges of the trunk's rim, torn out along the grain
            int count = 7;
            for (int i = 0; i < count; i++)
            {
                float a = (i + R(-0.3f, 0.3f)) * Mathf.PI * 2f / count, w = R(0.22f, 0.4f);
                Vector3 d0 = new Vector3(Mathf.Cos(a - w), 0f, Mathf.Sin(a - w)), d1 = new Vector3(Mathf.Cos(a + w), 0f, Mathf.Sin(a + w));
                Vector3 dm = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 b0 = c + d0 * r, b1 = c + d1 * r, bi = c + dm * r * 0.55f;
                Vector3 tip = c + dm * r * R(0.7f, 1.05f) + up * R(0.25f, 0.75f);
                int s = v.Count;
                v.Add(b0); v.Add(b1); v.Add(bi); v.Add(tip);
                n.Add(dm); n.Add(dm); n.Add(-dm); n.Add(up);
                uv.Add(new Vector2(0.1f, 0f)); uv.Add(new Vector2(0.3f, 0f)); uv.Add(new Vector2(0.2f, 0.1f)); uv.Add(new Vector2(0.2f, 0.8f));
                // three faces, each both ways round (a sliver of wood: seen from anywhere)
                void T(int x, int y, int z) { tris.Add(s + x); tris.Add(s + y); tris.Add(s + z); tris.Add(s + x); tris.Add(s + z); tris.Add(s + y); }
                T(0, 3, 1); T(1, 3, 2); T(2, 3, 0);
            }
        }
    }
}
