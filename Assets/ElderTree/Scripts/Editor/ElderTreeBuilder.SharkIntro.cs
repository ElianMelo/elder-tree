using ElderTree.Enemies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static ElderTree.EditorTools.TextureForge;

namespace ElderTree.EditorTools
{
    /// <summary>
    /// The Shark's first-meeting cinematic: its run in from the open sea on the Bleached Atoll (where the fin rises, the two
    /// breaches, where it leaps onto the dock and lands), the foam its fin and splashes leave on the water, and its sounds.
    /// Menu 7b2 patches the existing prefab and beach scene; 7b and 9f do it as part of their full builds.
    /// </summary>
    public static partial class ElderTreeBuilder
    {
        // The route, in the beach's frame (arena centre at the origin, you start at (0, -9) on the dock, the open sea to the south):
        //  - the fin rises ~100 m out, straight out to sea from you between the two piers (so the dock's opening shot sees it
        //    past the pilings), and swims east across the bay;
        //  - it breaches broadside to the dock, then curves north and breaches again, spinning, heading for the dock;
        //  - it charges in east of the short pier and leaps over the south-east rail onto the planks, ~8.5 m to your right.
        static readonly Vector3 SharkRisePos = new Vector3(-20f, 0f, -108f);
        const float SharkRiseYaw = 35f;
        static readonly Vector3 SharkBreach1Pos = new Vector3(12f, 0f, -74f);
        const float SharkBreach1Yaw = 85f;
        static readonly Vector3 SharkBreach2Pos = new Vector3(28f, 0f, -52f);
        const float SharkBreach2Yaw = -26f;
        static readonly Vector3 SharkLaunchPos = new Vector3(10.6f, 0f, -27f);
        static readonly Vector3 SharkLandPos = new Vector3(8.5f, 0f, -9.5f);

        static void ApplySharkIntro(SharkBoss boss)
        {
            boss.introMusic = SoundPicks.SharkIntroMusic();
            boss.introDroneClip = SoundPicks.SharkIntroDrone();
            boss.introRevealClip = SoundPicks.SharkIntroReveal();
            boss.introBuildClip = SoundPicks.SharkIntroBuild();
            boss.introPassClip = SoundPicks.SharkIntroPass();
            boss.introFoamMaterial = BuildSeaFoamArt();
        }

        /// <summary>Adds the intro cinematic to the existing Shark prefab and beach scene without rebuilding them.</summary>
        [MenuItem("Elder Tree/7b2. Shark intro cinematic (sea lane + foam + sounds)")]
        public static string BuildSharkIntro()
        {
            var root = PrefabUtility.LoadPrefabContents(SharkBossPrefabPath);
            var boss = root.GetComponent<SharkBoss>();
            ApplySharkIntro(boss);
            PrefabUtility.SaveAsPrefabAsset(root, SharkBossPrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(BeachScenePath);
            string lane = BuildSharkSeaLane();
            EditorSceneManager.SaveScene(scene);
            string Clip(AudioClip c) => c ? c.name : "MISSING";
            return $"Shark intro: music {Clip(boss.introMusic)}, drone {Clip(boss.introDroneClip)}, reveal {Clip(boss.introRevealClip)}, " +
                   $"build {Clip(boss.introBuildClip)}, pass {Clip(boss.introPassClip)}, foam {(boss.introFoamMaterial ? boss.introFoamMaterial.name : "MISSING")}; {lane}";
        }

        /// <summary>Into the open beach scene: the sea lane's markers (safe to run again: the old one is replaced).</summary>
        static string BuildSharkSeaLane()
        {
            foreach (var old in Object.FindObjectsByType<SharkSeaLane>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("SharkSeaLane");
            var actors = GameObject.Find("--- Actors ---");
            if (actors != null) go.transform.SetParent(actors.transform, false);
            go.transform.position = Vector3.zero;
            var lane = go.AddComponent<SharkSeaLane>();
            float sea = -1f;
            Vector3 OnSea(Vector3 p) => new Vector3(p.x, sea, p.z);
            Vector3 toLand = SharkLandPos - SharkLaunchPos;
            lane.rise = Marker(go.transform, "Rise", OnSea(SharkRisePos), SharkRiseYaw);
            lane.breach1 = Marker(go.transform, "Breach1", OnSea(SharkBreach1Pos), SharkBreach1Yaw);
            lane.breach2 = Marker(go.transform, "Breach2", OnSea(SharkBreach2Pos), SharkBreach2Yaw);
            lane.launch = Marker(go.transform, "Launch", OnSea(SharkLaunchPos), Mathf.Atan2(toLand.x, toLand.z) * Mathf.Rad2Deg);
            lane.land = Marker(go.transform, "Land", SharkLandPos, Mathf.Atan2(toLand.x, toLand.z) * Mathf.Rad2Deg);
            EditorUtility.SetDirty(lane);
            // how deep the water is along the way (the body runs ~2 m under the surface)
            float Depth(Vector3 p) => sea - Blast.Floor(p).y;
            return $"sea lane: rise {SharkRisePos} ({Depth(SharkRisePos):F1} m deep), breach1 {SharkBreach1Pos} ({Depth(SharkBreach1Pos):F1} m), " +
                   $"breach2 {SharkBreach2Pos} ({Depth(SharkBreach2Pos):F1} m), launch {SharkLaunchPos} ({Depth(SharkLaunchPos):F1} m), land {SharkLandPos}";
        }

        /// <summary>
        /// Sea foam for wakes and splash rings (<see cref="FX.SeaFoam"/>): a band across u, lacy at both edges (the arms of the
        /// wake's V) with a churned middle; rings use the left edge. Cutout lace, faded by vertex alpha.
        /// </summary>
        static Material BuildSeaFoamArt()
        {
            var tex = SavePsx("T_FX_SeaFoam", 128, (u, v, x, y) =>
            {
                Worley(u, v, 9, 611, out float f1, out float f2, out _);
                float lace = Sst(0.09f, 0.0f, f2 - f1);                             // foam strings along the cell borders
                float blob = Sst(0.55f, 0.75f, Fbm(u, v, 6, 6, 3, 612));
                float edge = Mathf.Max(Sst(0.26f, 0.1f, Mathf.Abs(u - 0.12f)), Sst(0.26f, 0.1f, Mathf.Abs(u - 0.88f)));
                float mid = 0.55f * Sst(0.42f, 0.2f, Mathf.Abs(u - 0.5f));
                float dens = Mathf.Max(edge, mid);
                float foam = Mathf.Max(lace, blob * 0.8f) * dens + Gr(x, y, 613) * 0.25f;
                var c = Mx(Hex("#A9B8BC"), Hex("#E2E9EA"), 0.5f + 0.5f * Fbm(u, v, 8, 8, 2, 614));
                c.a = foam > 0.3f ? 1f : 0f;
                return c;
            }, mips: true, alpha: true);
            var imp = (TextureImporter)AssetImporter.GetAtPath(TexDir + "/T_FX_SeaFoam.png");
            imp.wrapModeU = TextureWrapMode.Clamp;
            imp.wrapModeV = TextureWrapMode.Repeat;
            imp.mipMapsPreserveCoverage = true;
            imp.alphaTestReferenceValue = 0.5f;
            imp.SaveAndReimport();
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/T_FX_SeaFoam.png");
            var mat = Unlit("M_FX_SeaFoam", new Color(0.9f, 0.94f, 0.95f, 1f), tex);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
