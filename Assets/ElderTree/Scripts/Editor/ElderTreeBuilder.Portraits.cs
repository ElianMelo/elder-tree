using System.Collections.Generic;
using System.IO;
using System.Linq;
using ElderTree.Audio;
using ElderTree.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using static ElderTree.EditorTools.TextureForge;

namespace ElderTree.EditorTools
{
    /// <summary>
    /// Boss portraits for the level select: each boss rendered in-game in its arena (through the game's camera,
    /// lighting, fog and post-processing at the PSX resolution), and a silhouette derived from each, shown until felled.
    /// </summary>
    public static partial class ElderTreeBuilder
    {
        public const string PortraitDir = Root + "/UI/Portraits";
        const int PortraitSize = 144;
        const int ShotLayer = 31;

        struct Shot
        {
            public string id, scene, type, focus;
            public Vector3 place;
            public float placeYaw, yaw, elev, span, lookUp;
        }

        // Megaman-style bust shots: each framed on its head (span = metres across the frame)
        static readonly Shot[] Shots =
        {
            // each faces the moon (both arenas have it in the north-north-east) so the key light is on its face
            new Shot { id = "crocodile", scene = SwampScenePath, type = "CrocodileBoss", focus = "Bomber_Jaw", place = new Vector3(0, 4.5f, -3f), placeYaw = 18f, yaw = 32f, elev = 4f, span = 6.8f, lookUp = 0.3f },
            // the shark half-faces the sun (south-south-west) on its own dock, the east lagoon and the sea behind it
            new Shot { id = "shark", scene = BeachScenePath, type = "SharkBoss", focus = "Shark_Jaw", place = new Vector3(-9f, 1.25f, 3f), placeYaw = 235f, yaw = 38f, elev = 9f, span = 4.2f, lookUp = 0f },
            // the druid in its own clearing, facing the low sun (north-north-east) so the key light is on its face
            new Shot { id = "druid", scene = ForestScenePath, type = "DruidBoss", focus = "Hurt_Face", place = new Vector3(0, 0f, -4f), placeYaw = 20f, yaw = 22f, elev = -4f, span = 6.5f, lookUp = -0.4f },
            // the fairy hovering in the moonbeam of her grotto, the glowing mushrooms behind her
            new Shot { id = "fairy", scene = CaveScenePath, type = "FairyBoss", focus = "Fairy_Head", place = new Vector3(0, 0.6f, 3f), placeYaw = 200f, yaw = 24f, elev = 4f, span = 1.35f, lookUp = 0.1f },
            new Shot { id = "eldertree", scene = SwampScenePath, type = "Player", focus = "Head", place = new Vector3(1f, 0f, -5f), placeYaw = 16f, yaw = 26f, elev = 4f, span = 2.3f, lookUp = 0.05f },
        };

        [MenuItem("Elder Tree/9c. Capture Boss Portraits (in-game render)")]
        public static string CaptureBossPortraits()
        {
            Directory.CreateDirectory(PortraitDir);
            var log = new System.Text.StringBuilder();
            foreach (var s in Shots)
            {
                if (!File.Exists(s.scene)) { log.AppendLine($"{s.id}: scene missing"); continue; }
                EditorSceneManager.OpenScene(s.scene, OpenSceneMode.Single);
                log.AppendLine(Capture(s));
            }
            AssetDatabase.Refresh();
            return log.ToString();
        }

        static string Capture(Shot s)
        {
            GameObject subject = null;
            if (s.type == "Player") subject = Object.FindAnyObjectByType<Player.PlayerController>(FindObjectsInactive.Include)?.gameObject;
            else
                subject = Object.FindObjectsByType<Enemies.BossBase>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .FirstOrDefault(b => b.GetType().Name == s.type)?.gameObject;
            if (subject == null) return $"{s.id}: {s.type} not found";

            // other bosses out of the shot
            foreach (var b in Object.FindObjectsByType<Enemies.BossBase>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b.gameObject != subject) b.gameObject.SetActive(false);
            for (var t = subject.transform; t != null; t = t.parent) t.gameObject.SetActive(true);
            subject.transform.SetPositionAndRotation(s.place, Quaternion.Euler(0, s.placeYaw, 0));

            var rends = subject.GetComponentsInChildren<Renderer>()
                .Where(r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer) && !(r is TrailRenderer) && !(r is LineRenderer)
                            && !r.name.Contains("Shadow") && !r.name.Contains("FX") && !r.name.Contains("Glow")).ToArray();
            if (rends.Length == 0) return $"{s.id}: no renderers";
            var bounds = rends[0].bounds;
            foreach (var r in rends) bounds.Encapsulate(r.bounds);

            var main = Camera.main;
            var camGo = new GameObject("PortraitCam") { hideFlags = HideFlags.DontSave };
            var cam = camGo.AddComponent<Camera>();
            if (main != null) cam.CopyFrom(main);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 300f;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
            data.volumeLayerMask = ~0;
            var focusT = subject.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == s.focus && t.gameObject.activeInHierarchy);
            Vector3 look = (focusT != null ? focusT.position : bounds.center) + Vector3.up * s.lookUp;
            float dist = s.span * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var dir = Quaternion.AngleAxis(s.yaw, Vector3.up) * subject.transform.forward;
            dir = Quaternion.AngleAxis(-s.elev, Vector3.Cross(dir, Vector3.up).normalized) * dir;
            camGo.transform.position = look + dir.normalized * dist;
            camGo.transform.LookAt(look);

            var rt = new RenderTexture(PortraitSize, PortraitSize, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            cam.targetTexture = rt;
            cam.Render();
            var portrait = Read(rt);

            // mask pass: only the subject, on a magenta clear, no post / fog
            var layers = new Dictionary<GameObject, int>();
            foreach (var r in rends) { layers[r.gameObject] = r.gameObject.layer; r.gameObject.layer = ShotLayer; }
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            cam.cullingMask = 1 << ShotLayer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(1, 0, 1, 1);
            data.renderPostProcessing = false;
            cam.Render();
            var maskPx = Read(rt);
            RenderSettings.fog = fog;
            foreach (var kv in layers) kv.Key.layer = kv.Value;

            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);

            int S = PortraitSize;
            var mask = new bool[S * S];
            int count = 0;
            for (int i = 0; i < S * S; i++)
            {
                var c = maskPx[i];
                mask[i] = Mathf.Abs(c.r - 1f) + c.g + Mathf.Abs(c.b - 1f) > 0.12f;
                if (mask[i]) count++;
            }
            var rim = Hex("#8FBF5A");
            var sil = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                int i = y * S + x;
                var p = portrait[i];
                float lum = p.r * 0.3f + p.g * 0.59f + p.b * 0.11f;
                var bg = new Color(lum * 0.32f, lum * 0.5f, lum * 0.36f, 1f);
                bool edge = false, near = false;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = Mathf.Clamp(x + dx, 0, S - 1), yy = Mathf.Clamp(y + dy, 0, S - 1);
                    bool m = mask[yy * S + xx];
                    if (mask[i] && !m) edge = true;
                    if (!mask[i] && m) near = true;
                }
                sil[i] = mask[i] ? (edge ? rim : Hex("#040605")) : near ? Color.Lerp(bg, rim, 0.4f) : bg;
                sil[i].a = 1f;
            }
            SavePortrait($"Portrait_{s.id}", portrait);
            SavePortrait($"Silhouette_{s.id}", sil);
            return $"{s.id}: captured ({count * 100 / (S * S)}% coverage)";
        }

        static Color[] Read(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels();
            Object.DestroyImmediate(tex);
            for (int i = 0; i < px.Length; i++) px[i].a = 1f;
            return px;
        }

        static void SavePortrait(string name, Color[] px)
        {
            int S = PortraitSize;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            string path = $"{PortraitDir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.filterMode = FilterMode.Point;
            imp.mipmapEnabled = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.SaveAndReimport();
        }

        static Texture2D Portrait(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{PortraitDir}/{name}.png");

        [MenuItem("Elder Tree/9. Build Arenas + Portraits + Main Menu (all)")]
        public static string BuildBossStages()
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine(BuildSwampArt());
            log.AppendLine(BuildSwampScene());
            log.AppendLine(BuildBeachArt());
            log.AppendLine(BuildBeachScene());
            log.AppendLine(BuildForestArt());
            log.AppendLine(BuildForestScene());
            log.AppendLine(BuildCaveArt());
            log.AppendLine(BuildCaveScene());
            log.AppendLine(CaptureBossPortraits());
            log.AppendLine(BuildMainMenuAll());
            return log.ToString();
        }
    }
}
