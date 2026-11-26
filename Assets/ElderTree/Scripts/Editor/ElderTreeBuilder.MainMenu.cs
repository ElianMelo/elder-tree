using System.Collections.Generic;
using System.IO;
using System.Linq;
using ElderTree.Audio;
using ElderTree.InputHandling;
using ElderTree.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace ElderTree.EditorTools
{
    /// <summary>
    /// The title / main menu: a wide in-game vista of every stage (through the game's camera, lighting, fog and
    /// post at the PSX resolution) for the menu's drifting background and level select, and the MainMenu scene,
    /// which becomes the first scene of the build.
    /// </summary>
    public static partial class ElderTreeBuilder
    {
        public const string MainMenuScenePath = Root + "/Scenes/MainMenu.unity";
        public const string VistaDir = Root + "/UI/Vistas";
        const int VistaW = 768, VistaH = 432;  // 1920 x 1080 at the game's 0.4 render scale

        struct Vista
        {
            public string id, scene;
            public Vector3 pos, look;
            public float fov;
            public float playerX, playerDist;  // the Elder Tree: across the frame (0..1) and metres out, facing into the shot
        }

        // each looks across its arena toward the stage's landmark. The Elder Tree stands small, left of centre, clear of the
        // title and the menu column, looking into the shot.
        static readonly Vista[] Vistas =
        {
            new Vista { id = "training", scene = ScenePath, pos = new Vector3(-3.5f, 2.4f, -16f), look = new Vector3(-7f, 2.6f, 6f), fov = 50f, playerX = 0.3f, playerDist = 8f },
            new Vista { id = "crocodile", scene = SwampScenePath, pos = new Vector3(-4f, 2.6f, -20f), look = new Vector3(3f, 5f, 8f), fov = 52f, playerX = 0.72f, playerDist = 7f },
            // from the dock's south-east corner over the lagoon to the wreck on the reef
            new Vista { id = "shark", scene = BeachScenePath, pos = new Vector3(2f, 2.2f, -10f), look = new Vector3(40f, 1.5f, -60f), fov = 50f, playerX = 0.3f, playerDist = 8f },
            new Vista { id = "druid", scene = ForestScenePath, pos = new Vector3(-4f, 2.2f, -19f), look = new Vector3(1f, 9f, 10f), fov = 55f, playerX = 0.3f, playerDist = 8f },
            new Vista { id = "fairy", scene = CaveScenePath, pos = new Vector3(-4f, 2.2f, -20f), look = new Vector3(1f, 4f, 6f), fov = 55f, playerX = 0.3f, playerDist = 8f },
        };

        [MenuItem("Elder Tree/9k. Capture Stage Vistas (in-game render)")]
        public static string CaptureStageVistas() => CaptureStageVistas(null);

        /// <summary>Renders the vistas (all, or only <paramref name="only"/>) into UI/Vistas.</summary>
        public static string CaptureStageVistas(string only)
        {
            Directory.CreateDirectory(VistaDir);
            var log = new System.Text.StringBuilder();
            foreach (var v in Vistas)
            {
                if (only != null && v.id != only) continue;
                if (!File.Exists(v.scene)) { log.AppendLine($"{v.id}: scene missing"); continue; }
                EditorSceneManager.OpenScene(v.scene, OpenSceneMode.Single);
                log.AppendLine(CaptureVista(v));
            }
            AssetDatabase.Refresh();
            return log.ToString();
        }

        static string CaptureVista(Vista v)
        {
            // bosses stay a surprise: none of them in the shot
            foreach (var b in Object.FindObjectsByType<Enemies.BossBase>(FindObjectsInactive.Include, FindObjectsSortMode.None)) b.gameObject.SetActive(false);
            var player = Object.FindAnyObjectByType<Player.PlayerController>(FindObjectsInactive.Include);
            if (player != null) player.gameObject.SetActive(false);

            var main = Camera.main;
            var camGo = new GameObject("VistaCam") { hideFlags = HideFlags.DontSave };
            var cam = camGo.AddComponent<Camera>();
            if (main != null) cam.CopyFrom(main);
            cam.fieldOfView = v.fov;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
            data.volumeLayerMask = ~0;
            camGo.transform.position = v.pos;
            camGo.transform.LookAt(v.look);

            // stand the Elder Tree on whatever ground is under its spot in the frame, facing where the camera looks
            if (player != null && v.playerDist > 0f)
            {
                cam.aspect = (float)VistaW / VistaH;
                var dir = Vector3.ProjectOnPlane(cam.ViewportPointToRay(new Vector3(v.playerX, 0.5f, 0f)).direction, Vector3.up).normalized;
                var spot = camGo.transform.position + dir * v.playerDist;
                Physics.SyncTransforms();
                if (Physics.Raycast(spot + Vector3.up * 30f, Vector3.down, out var hit, 80f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var fwd = Vector3.ProjectOnPlane(camGo.transform.forward, Vector3.up).normalized;
                    player.transform.SetPositionAndRotation(hit.point, Quaternion.LookRotation(fwd));
                    player.gameObject.SetActive(true);
                    // edit mode leaves the rig in its bind pose: stand it in the combat idle
                    var pc = player.GetComponent<Player.PlayerController>();
                    var anim = pc != null && pc.animator != null ? pc.animator : player.GetComponentInChildren<Animator>();
                    var idle = anim != null && anim.runtimeAnimatorController != null
                        ? anim.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name.Contains("CombatIdle1H")) : null;
                    if (idle != null) idle.SampleAnimation(anim.gameObject, idle.length * 0.3f);
                }
            }

            foreach (var pr in Object.FindObjectsByType<FX.PlanarReflection>(FindObjectsSortMode.None)) pr.RenderFor(cam);

            var rt = new RenderTexture(VistaW, VistaH, 24, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(VistaW, VistaH, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, VistaW, VistaH), 0, 0);
            var px = tex.GetPixels();
            for (int i = 0; i < px.Length; i++) px[i].a = 1f;
            tex.SetPixels(px);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);

            string path = $"{VistaDir}/Vista_{v.id}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.filterMode = FilterMode.Point;
            imp.mipmapEnabled = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.SaveAndReimport();
            return $"{v.id}: {path}";
        }

        static Texture2D VistaTex(string id) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{VistaDir}/Vista_{id}.png");

        // ================================================================== the menu scene
        [MenuItem("Elder Tree/9l. Build Main Menu Scene")]
        public static string BuildMainMenuScene()
        {
            if (cues == null) BuildAudio();
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

            // menu navigation, the device in use, and remapping in Settings > Controls
            var inputGo = new GameObject("Input");
            var reader = inputGo.AddComponent<PlayerInputReader>();
            var so = new SerializedObject(reader);
            so.FindProperty("actions").objectReferenceValue = AssetDatabase.LoadAssetAtPath<InputActionAsset>(Root + "/Input/ElderTreeControls.inputactions");
            so.ApplyModifiedPropertiesWithoutUndo();

            var uiGo = new GameObject("MainMenu");
            var doc = uiGo.AddComponent<UIDocument>();
            doc.panelSettings = BuildPanelSettings();
            var menu = uiGo.AddComponent<MainMenuScreen>();
            menu.hudStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(Root + "/UI/ElderTreeHUD.uss");
            menu.menuStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(Root + "/UI/MainMenu.uss");
            menu.stages = new List<MainMenuScreen.Stage>
            {
                new()
                {
                    region = "Training Grounds", epithet = "The watch begins.", scene = SceneFlow.Training, vista = VistaTex("training"),
                    description = "A walled yard under the indigo night, its dummies standing in the mist. Learn the weight of the club, " +
                                  "the timing of the roll and the reach of the bow before you go looking for trouble.",
                },
                new()
                {
                    region = "Drowned Garrison", boss = "Crocodile", bossId = "crocodile", maskId = "alligator", maskName = "Alligator Mask",
                    epithet = "Bombardier of the Drowned Garrison", scene = SceneFlow.Swamp, vista = VistaTex("crocodile"),
                    description = "A sunken wartime outpost in a green-lit swamp, its searchlights still sweeping the fog. Something sleeps " +
                                  "on the airstrip. When it wakes it takes to the sky, and the cannonballs follow.",
                },
                new()
                {
                    region = "Bleached Atoll", boss = "Shark", bossId = "shark", maskId = "shark", maskName = "Shark Mask",
                    epithet = "It swims where there is no water", scene = SceneFlow.Beach, vista = VistaTex("shark"),
                    description = "A crescent of bone-white sand around a still lagoon, a whale skeleton half buried, a wreck on the reef. " +
                                  "The fight is on the old dock. Watch the water, and then watch the planks.",
                },
                new()
                {
                    region = "Verdant Cathedral", boss = "Druid", bossId = "druid", maskId = "druid", maskName = "Druid Mask",
                    epithet = "The grove walks, and it is angry", scene = SceneFlow.Forest, vista = VistaTex("druid"),
                    description = "A clearing ringed by standing stones beneath a great kapok, morning light falling through the mist in " +
                                  "shafts. The forest here is not still. Something in it is older than the stones.",
                },
                new()
                {
                    region = "Starlit Grotto", boss = "Fairy", bossId = "fairy", maskId = "fairy", maskName = "Fairy Mask",
                    epithet = "She will not let you near", scene = SceneFlow.Cave, vista = VistaTex("fairy"),
                    description = "A cavern with stars for a ceiling, glowing mushrooms and mirror-still pools, gnomes keeping house at " +
                                  "its edges. At its heart, a shaft of moonlight. She dances in it, and she does not want company.",
                },
            };
            EditorSceneManager.SaveScene(scene, MainMenuScenePath);
            RegisterBuildScenes();
            return "Main menu built: " + MainMenuScenePath;
        }

        [MenuItem("Elder Tree/9m. Capture Vistas + Build Main Menu")]
        public static string BuildMainMenuAll()
        {
            var log = CaptureStageVistas(null);
            return log + BuildMainMenuScene();
        }
    }
}
