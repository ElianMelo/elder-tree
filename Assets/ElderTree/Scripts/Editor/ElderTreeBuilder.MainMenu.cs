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
    /// which becomes the first scene of the build, plus the stage catalog its level select reads.
    /// </summary>
    public static partial class ElderTreeBuilder
    {
        public const string MainMenuScenePath = Root + "/Scenes/MainMenu.unity";
        public const string VistaDir = Root + "/UI/Vistas";
        // full HD and clean (user's call, 2026-11-27): no PSX low-res / dither / vertex snap, no grain or vignette, antialiased.
        // The stage's own lighting, fog, bloom and colour grade stay: that is the place, not a filter.
        const int VistaW = 1920, VistaH = 1080;

        struct Vista
        {
            public string id, scene;
            public Vector3 pos, look;
            public float fov;
        }

        // each looks across its arena toward the stage's landmark: just the place, nobody in it
        static readonly Vista[] Vistas =
        {
            new Vista { id = "training", scene = ScenePath, pos = new Vector3(-3.5f, 2.4f, -16f), look = new Vector3(-7f, 2.6f, 6f), fov = 50f },
            // in under the garrison: the watch tower framed by the white moon, lit tents and sandbags below
            new Vista { id = "crocodile", scene = SwampScenePath, pos = new Vector3(-6f, 2.6f, 4f), look = new Vector3(0f, 8.5f, 30f), fov = 58f },
            // from the dock's south-east corner over the lagoon to the wreck on the reef
            new Vista { id = "shark", scene = BeachScenePath, pos = new Vector3(2f, 2.2f, -10f), look = new Vector3(40f, 1.5f, -60f), fov = 50f },
            new Vista { id = "druid", scene = ForestScenePath, pos = new Vector3(-4f, 2.2f, -19f), look = new Vector3(1f, 9f, 10f), fov = 55f },
            new Vista { id = "fairy", scene = CaveScenePath, pos = new Vector3(-4f, 2.2f, -20f), look = new Vector3(1f, 4f, 6f), fov = 55f },
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
            // bosses stay a surprise and the Elder Tree stays out of it: the stage alone
            foreach (var b in Object.FindObjectsByType<Enemies.BossBase>(FindObjectsInactive.Include, FindObjectsSortMode.None)) b.gameObject.SetActive(false);
            foreach (var pc in Object.FindObjectsByType<Player.PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None)) pc.gameObject.SetActive(false);

            // no PSX look for the shot: full render scale, no vertex snap or 15-bit dither
            var rp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            float scale = rp != null ? rp.renderScale : 1f;
            if (rp != null) rp.renderScale = 1f;
            var psx = Object.FindObjectsByType<FX.PSXGlobals>(FindObjectsSortMode.None);
            foreach (var g in psx) g.enabled = false;
            Shader.SetGlobalVector("_PSXSnap", Vector4.zero);
            Shader.SetGlobalFloat("_PSXDither", 0f);

            // nor the camera filters: grain and vignette off on top of the stage's own volume
            var clean = new GameObject("VistaClean") { hideFlags = HideFlags.DontSave };
            var vol = clean.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1000f;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            prof.Add<FilmGrain>(true).intensity.Override(0f);
            prof.Add<Vignette>(true).intensity.Override(0f);
            prof.Add<ChromaticAberration>(true).intensity.Override(0f);
            prof.Add<LensDistortion>(true).intensity.Override(0f);
            vol.sharedProfile = prof;

            var main = Camera.main;
            var camGo = new GameObject("VistaCam") { hideFlags = HideFlags.DontSave };
            var cam = camGo.AddComponent<Camera>();
            if (main != null) cam.CopyFrom(main);
            cam.fieldOfView = v.fov;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            cam.allowMSAA = true;
            cam.aspect = (float)VistaW / VistaH;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.volumeLayerMask = ~0;
            camGo.transform.position = v.pos;
            camGo.transform.LookAt(v.look);

            foreach (var pr in Object.FindObjectsByType<FX.PlanarReflection>(FindObjectsSortMode.None)) pr.RenderFor(cam);

            var rt = new RenderTexture(VistaW, VistaH, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8, filterMode = FilterMode.Bilinear };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(VistaW, VistaH, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, VistaW, VistaH), 0, 0);
            var px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++) px[i].a = 255;
            tex.SetPixels32(px);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(clean);
            Object.DestroyImmediate(prof);
            if (rp != null) rp.renderScale = scale;
            foreach (var g in psx) if (g != null) g.enabled = true;

            string path = $"{VistaDir}/Vista_{v.id}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.filterMode = FilterMode.Bilinear;
            imp.mipmapEnabled = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.maxTextureSize = 2048;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.SaveAndReimport();
            return $"{v.id}: {path}";
        }

        static Texture2D VistaTex(string id) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{VistaDir}/Vista_{id}.png");

        // ================================================================== the stage catalog
        public const string StageCatalogPath = Root + "/Resources/" + StageCatalog.ResourceName + ".asset";

        static StageCatalog.Stage[] DefaultStages() => new StageCatalog.Stage[]
        {
            new()
            {
                region = "Training Grounds", epithet = "The watch begins.", scene = SceneFlow.Training, bossId = "",
                description = "A walled yard under the indigo night, its dummies standing in the mist. Learn the weight of the club, " +
                              "the timing of the roll and the reach of the bow before you go looking for trouble.",
            },
            new()
            {
                region = "Drowned Garrison", boss = "Crocodile", bossId = "crocodile", maskId = "alligator", maskName = "Alligator Mask",
                epithet = "Bombardier of the Drowned Garrison", scene = SceneFlow.Swamp,
                description = "A sunken wartime outpost in a green-lit swamp, its searchlights still sweeping the fog. Something sleeps " +
                              "on the airstrip. When it wakes it takes to the sky, and the cannonballs follow.",
            },
            new()
            {
                region = "Bleached Atoll", boss = "Shark", bossId = "shark", maskId = "shark", maskName = "Shark Mask",
                epithet = "It swims where there is no water", scene = SceneFlow.Beach,
                description = "A crescent of bone-white sand around a still lagoon, a whale skeleton half buried, a wreck on the reef. " +
                              "The fight is on the old dock. Watch the water, and then watch the planks.",
            },
            new()
            {
                region = "Verdant Cathedral", boss = "Druid", bossId = "druid", maskId = "druid", maskName = "Druid Mask",
                epithet = "The grove walks, and it is angry", scene = SceneFlow.Forest,
                description = "A clearing ringed by standing stones beneath a great kapok, morning light falling through the mist in " +
                              "shafts. The forest here is not still. Something in it is older than the stones.",
            },
            new()
            {
                region = "Starlit Grotto", boss = "Fairy", bossId = "fairy", maskId = "fairy", maskName = "Fairy Mask",
                epithet = "She will not let you near", scene = SceneFlow.Cave,
                description = "A cavern with stars for a ceiling, glowing mushrooms and mirror-still pools, gnomes keeping house at " +
                              "its edges. At its heart, a shaft of moonlight. She dances in it, and she does not want company.",
            },
        };

        /// <summary>
        /// The stage list the level select reads (Resources/StageCatalog). Created with the default texts only when
        /// missing, so edits to it survive; every build re-links the vistas, portraits and the view's styles.
        /// </summary>
        [MenuItem("Elder Tree/9k2. Build Stage Catalog")]
        public static string BuildStageCatalog()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StageCatalogPath));
            var cat = AssetDatabase.LoadAssetAtPath<StageCatalog>(StageCatalogPath);
            if (cat == null)
            {
                cat = ScriptableObject.CreateInstance<StageCatalog>();
                cat.stages = DefaultStages().ToList();
                AssetDatabase.CreateAsset(cat, StageCatalogPath);
            }
            foreach (var s in cat.stages)
            {
                string id = string.IsNullOrEmpty(s.bossId) ? "training" : s.bossId;
                s.vista = VistaTex(id);
                string face = string.IsNullOrEmpty(s.bossId) ? "eldertree" : s.bossId;
                s.portrait = Portrait($"Portrait_{face}");
                s.silhouette = Portrait($"Silhouette_{face}");
            }
            cat.style = AssetDatabase.LoadAssetAtPath<StyleSheet>(Root + "/UI/LevelSelect.uss");
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            return $"Stage catalog: {cat.stages.Count} stages ({StageCatalogPath})";
        }

        // ================================================================== the menu scene
        [MenuItem("Elder Tree/9l. Build Main Menu Scene")]
        public static string BuildMainMenuScene()
        {
            var catLog = BuildStageCatalog();
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
            EditorSceneManager.SaveScene(scene, MainMenuScenePath);
            RegisterBuildScenes();
            return catLog + " / Main menu built: " + MainMenuScenePath;
        }

        [MenuItem("Elder Tree/9m. Capture Vistas + Build Main Menu")]
        public static string BuildMainMenuAll()
        {
            var log = CaptureStageVistas(null);
            return log + BuildMainMenuScene();
        }
    }
}
