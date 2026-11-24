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
    /// Megaman-style boss select: portraits rendered in-game (each boss in its arena, through the game's camera,
    /// lighting, fog and post-processing at the PSX resolution), a silhouette derived from each, and the select scene.
    /// </summary>
    public static partial class ElderTreeBuilder
    {
        public const string BossSelectScenePath = Root + "/Scenes/BossSelect.unity";
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
            new Shot { id = "druid", scene = ScenePath, type = "DruidBoss", focus = "Hurt_Face", place = new Vector3(0, 0f, -7f), placeYaw = 10f, yaw = 22f, elev = -4f, span = 6.5f, lookUp = -0.4f },
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

        // ================================================================== the select scene
        [MenuItem("Elder Tree/9d. Build Boss Select Scene")]
        public static string BuildBossSelectScene()
        {
            if (cues == null) BuildAudio();
            var bg = SavePsx("T_SelectBG", 128, (u, v, x, y) =>
            {
                var c = Color.Lerp(Hex("#040705"), Hex("#121A13"), 0.3f + 0.5f * Fbm(u, v, 6, 6, 4, 401) + 0.2f * (Hash(x, y, 402) - 0.5f));
                float streak = Fbm(u, v, 24, 2, 2, 403);
                return Color.Lerp(c, Hex("#1C2A1C"), Mathf.Clamp01((streak - 0.62f) * 4f) * 0.5f);
            }, mips: false);
            var fogTex = Save("T_SelectFog", 256, 256, (x, y) =>
            {
                float u = x / 256f, v = y / 256f;
                float f = Fbm(u, v, 3, 3, 5, 411);
                float a = Mathf.Clamp01((f - 0.42f) * 2.4f);
                return new Color(1, 1, 1, a * a);
            });
            var fimp = (TextureImporter)AssetImporter.GetAtPath(TexDir + "/T_SelectFog.png");
            fimp.alphaIsTransparency = true; fimp.wrapMode = TextureWrapMode.Repeat; fimp.SaveAndReimport();

            var vig = Save("T_SelectVignette", 128, 128, (x, y) =>
            {
                float u = x / 127f * 2f - 1f, v = y / 127f * 2f - 1f;
                float d = Mathf.Sqrt(u * u * 0.8f + v * v);
                float a = Mathf.Clamp01((d - 0.45f) / 0.75f);
                return new Color(0.005f, 0.01f, 0.006f, a * a * (3f - 2f * a) * 0.92f);
            });
            var vimp = (TextureImporter)AssetImporter.GetAtPath(TexDir + "/T_SelectVignette.png");
            vimp.alphaIsTransparency = true; vimp.wrapMode = TextureWrapMode.Clamp; vimp.SaveAndReimport();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
            camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            camGo.AddComponent<AudioListener>();

            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

            var audioGo = new GameObject("Audio");
            var am = audioGo.AddComponent<AudioManager>();
            am.cues = cues;
            am.ambienceLoop = ambience;
            am.ambienceVolume = SoundPicks.AmbienceVolume;
            am.exploreMusic = SoundPicks.ExploreMusic();
            am.bossMusic = SoundPicks.BossMusic();
            GameMixer.Assign(am);

            var uiGo = new GameObject("BossSelect");
            var doc = uiGo.AddComponent<UIDocument>();
            doc.panelSettings = BuildPanelSettings();
            var screen = uiGo.AddComponent<BossSelectScreen>();
            screen.background = bg;
            screen.fogLayer = fogTex;
            screen.vignette = vig;
            screen.entries = new List<BossSelectScreen.Entry>
            {
                new() { id = "eldertree", displayName = "Elder Tree", epithet = "The watch begins. Training grounds.", scene = SceneFlow.Training,
                        alwaysRevealed = true, cell = 4, portrait = Portrait("Portrait_eldertree"), silhouette = Portrait("Portrait_eldertree") },
                new() { id = "crocodile", displayName = "Crocodile", epithet = "Bombardier of the Drowned Garrison", maskId = "alligator",
                        scene = SceneFlow.Swamp, cell = 1, portrait = Portrait("Portrait_crocodile"), silhouette = Portrait("Silhouette_crocodile") },
                new() { id = "shark", displayName = "Shark", epithet = "It swims where there is no water", maskId = "shark",
                        scene = SceneFlow.Beach, cell = 3, portrait = Portrait("Portrait_shark"), silhouette = Portrait("Silhouette_shark") },
                new() { id = "druid", displayName = "Druid", epithet = "The grove walks, and it is angry", maskId = "druid",
                        scene = SceneFlow.Training, bossArg = "Druid", cell = 5, portrait = Portrait("Portrait_druid"), silhouette = Portrait("Silhouette_druid") },
            };
            EditorSceneManager.SaveScene(scene, BossSelectScenePath);
            RegisterBuildScenes();
            return "Boss select built: " + BossSelectScenePath;
        }

        [MenuItem("Elder Tree/9. Build Boss Select + Arenas (all)")]
        public static string BuildBossStages()
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine(BuildSwampArt());
            log.AppendLine(BuildSwampScene());
            log.AppendLine(BuildBeachArt());
            log.AppendLine(BuildBeachScene());
            log.AppendLine(CaptureBossPortraits());
            log.AppendLine(BuildBossSelectScene());
            return log.ToString();
        }
    }
}
