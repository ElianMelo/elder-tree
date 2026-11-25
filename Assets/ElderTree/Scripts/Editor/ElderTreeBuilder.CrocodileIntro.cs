using System.Collections.Generic;
using System.Linq;
using ElderTree.Enemies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ElderTree.EditorTools
{
    /// <summary>
    /// The Crocodile's first-meeting cinematic: its airstrip in the Drowned Garrison (where it sleeps, where it lines up,
    /// the takeoff run and the climbing turn) and its sounds. Menu 7a patches the existing prefab and swamp scene; 7 and 9b
    /// do it as part of their full builds.
    /// </summary>
    public static partial class ElderTreeBuilder
    {
        // The plan, in the swamp's frame (arena centre at the origin, you start at (0, -10) facing north):
        //  - it sleeps on the west side, between the pillbox and the middle;
        //  - it taxis out in a right turn onto a runway along the north side, heading east, ~19 m from you;
        //  - it lifts off before the sandbags and turns right into a climbing spiral round the arena, inside the tree line.
        // Torches standing where the plane would roll through are moved to the nearest spot clear of it.
        static readonly Vector3 CrocParkPos = new Vector3(-9f, 0f, 1f);
        const float CrocParkYaw = 35f;
        static readonly Vector3 CrocLineUpPos = new Vector3(-3f, 0f, 8.5f);
        const float CrocRunwayYaw = 90f;
        const float CrocRollDistance = 9f;

        static void ApplyCrocodileIntro(CrocodileBoss boss)
        {
            boss.introMusic = SoundPicks.CrocIntroMusic();
            boss.introRevealClip = SoundPicks.CrocIntroReveal();
            boss.introSnoreClip = SoundPicks.CrocIntroSnore();
            boss.introPassClip = SoundPicks.CrocIntroPass();
        }

        /// <summary>Adds the intro cinematic to the existing Crocodile prefab and swamp scene without rebuilding them.</summary>
        [MenuItem("Elder Tree/7a. Crocodile intro cinematic (airstrip + sounds)")]
        public static string BuildCrocodileIntro()
        {
            var root = PrefabUtility.LoadPrefabContents(BossPrefabPath);
            var boss = root.GetComponent<CrocodileBoss>();
            ApplyCrocodileIntro(boss);
            PrefabUtility.SaveAsPrefabAsset(root, BossPrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(SwampScenePath);
            string strip = BuildCrocodileAirstrip();
            EditorSceneManager.SaveScene(scene);
            string Clip(AudioClip c) => c ? c.name : "MISSING";
            return $"Crocodile intro: music {Clip(boss.introMusic)}, reveal {Clip(boss.introRevealClip)}, snore {Clip(boss.introSnoreClip)}, pass {Clip(boss.introPassClip)}; {strip}";
        }

        /// <summary>Into the open swamp scene: the airstrip markers (safe to run again: the old one is replaced).</summary>
        static string BuildCrocodileAirstrip()
        {
            foreach (var old in Object.FindObjectsByType<CrocodileAirstrip>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("CrocodileAirstrip");
            var actors = GameObject.Find("--- Actors ---");
            if (actors != null) go.transform.SetParent(actors.transform, false);
            go.transform.position = Vector3.zero;
            var strip = go.AddComponent<CrocodileAirstrip>();
            strip.park = Marker(go.transform, "Park", CrocParkPos, CrocParkYaw);
            strip.lineUp = Marker(go.transform, "LineUp", CrocLineUpPos, CrocRunwayYaw);
            strip.rollDistance = CrocRollDistance;
            strip.liftOffSpeed = 14f;
            strip.firstTurnRadius = 8.5f;
            EditorUtility.SetDirty(strip);
            string moved = ClearCrocodileAirstrip(strip);
            return $"airstrip: park {CrocParkPos} yaw {CrocParkYaw}, line-up {CrocLineUpPos} yaw {CrocRunwayYaw}; {moved}";
        }

        /// <summary>The ground poses the plane takes: parked, along the taxi curve (the same curve the cinematic drives), and the
        /// takeoff run up to lift-off (after that its wings clear a torch).</summary>
        static List<(Vector3 p, float yaw)> CrocGroundPoses(CrocodileAirstrip strip)
        {
            var poses = new List<(Vector3, float)>();
            Vector3 pk = strip.park.position, lu = strip.lineUp.position;
            Vector3 fk = strip.park.forward, h = strip.lineUp.forward;
            fk.y = h.y = 0f;
            float len = Vector3.Distance(new Vector3(pk.x, 0f, pk.z), new Vector3(lu.x, 0f, lu.z));
            Vector3 m0 = fk.normalized * len * 1.1f, m1 = h.normalized * len * 1.1f;
            poses.Add((pk, strip.park.eulerAngles.y));
            for (int i = 1; i <= 40; i++)
            {
                float s = i / 40f, s2 = s * s, s3 = s2 * s;
                Vector3 p = (2f * s3 - 3f * s2 + 1f) * pk + (s3 - 2f * s2 + s) * m0 + (-2f * s3 + 3f * s2) * lu + (s3 - s2) * m1;
                Vector3 d = (6f * s2 - 6f * s) * pk + (3f * s2 - 4f * s + 1f) * m0 + (-6f * s2 + 6f * s) * lu + (3f * s2 - 2f * s) * m1;
                poses.Add((p, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg));
            }
            for (float a = 0f; a <= strip.rollDistance; a += 0.5f) poses.Add((lu + h.normalized * a, strip.lineUp.eulerAngles.y));
            return poses;
        }

        /// <summary>Does the plane cover <paramref name="q"/> (flat) in any of these poses? Its fuselage and head down the middle,
        /// its box-cell wings across the back (they clear the sandbags but not a torch).</summary>
        static bool InCrocFootprint(Vector3 q, List<(Vector3 p, float yaw)> poses, float margin)
        {
            foreach (var (p, yaw) in poses)
            {
                Vector3 l = Quaternion.Euler(0f, -yaw, 0f) * (q - p);
                if (Mathf.Abs(l.x) < 5.8f + margin && l.z > -4.7f - margin && l.z < -2.4f + margin) return true;
                if (Mathf.Abs(l.x) < 1.8f + margin && l.z > -5.2f - margin && l.z < 6.1f + margin) return true;
            }
            return false;
        }

        /// <summary>
        /// Moves every torch the plane would roll through (with its light) to the nearest spot clear of it: on dry ground, inside
        /// the ring, clear of the other torches, of your start and of anything solid. Safe to run again.
        /// </summary>
        static string ClearCrocodileAirstrip(CrocodileAirstrip strip)
        {
            var garrison = GameObject.Find("--- Garrison ---");
            var lighting = GameObject.Find("--- Lighting ---");
            if (garrison == null) return "no garrison to clear";
            Physics.SyncTransforms();
            var poses = CrocGroundPoses(strip);
            var torches = garrison.transform.Cast<Transform>().Where(t => System.Text.RegularExpressions.Regex.IsMatch(t.name, @"^Torch\d+$")).ToList();
            var moved = new List<string>();
            foreach (var t in torches)
            {
                if (!InCrocFootprint(t.position, poses, 0.3f)) continue;
                // the nearest free spot round where it stood, so the clearing's light stays where it was
                var slots = new List<Vector3>();
                for (float r = 1.5f; r <= 9f; r += 0.75f)
                    for (int k = 0; k < 24; k++) slots.Add(t.position + Quaternion.Euler(0f, k * 15f, 0f) * Vector3.forward * r);
                Vector3 best = Vector3.zero;
                float bestD = float.MaxValue;
                foreach (var q in slots)
                {
                    if (InCrocFootprint(q, poses, 1f)) continue;
                    if (new Vector2(q.x, q.z).magnitude > 17.2f) continue;
                    if (Vector3.Distance(new Vector3(q.x, 0f, q.z), new Vector3(0f, 0f, -10f)) < 4f) continue;
                    if (torches.Any(o => o != t && Vector2.Distance(new Vector2(o.position.x, o.position.z), new Vector2(q.x, q.z)) < 3.5f)) continue;
                    if (!Physics.Raycast(q + Vector3.up * 20f, Vector3.down, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (!hit.collider.name.Contains("Ground") || hit.point.y < -0.05f) continue;      // dry ground, nothing on top
                    if (Physics.OverlapBox(hit.point + Vector3.up * 1.2f, new Vector3(0.7f, 1f, 0.7f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)
                        .Any(cl => !cl.name.Contains("Ground") && !cl.transform.IsChildOf(t))) continue;
                    float d = Vector3.Distance(q, t.position);
                    if (d < bestD) { bestD = d; best = hit.point; }
                }
                if (bestD == float.MaxValue) { moved.Add(t.name + " (no free spot!)"); continue; }
                Vector3 from = t.position;
                t.position = best;
                var light = lighting != null ? lighting.transform.Find(t.name.Replace("Torch", "TorchLight")) : null;
                if (light != null) light.position += best - from;
                EditorUtility.SetDirty(t.gameObject);
                moved.Add($"{t.name} {from:F1} -> {best:F1}");
            }
            return moved.Count == 0 ? "no torches in the way" : "moved " + string.Join(", ", moved);
        }

        static Transform Marker(Transform parent, string name, Vector3 pos, float yaw)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            return t;
        }
    }
}
