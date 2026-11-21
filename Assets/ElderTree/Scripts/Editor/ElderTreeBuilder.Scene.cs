using System.IO;
using System.Linq;
using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.Enemies;
using ElderTree.FX;
using ElderTree.Player;
using ElderTree.UI;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace ElderTree.EditorTools
{
    public static partial class ElderTreeBuilder
    {
        public const string ScenePath = Root + "/Scenes/Arena.unity";

        [MenuItem("Elder Tree/Build EVERYTHING")]
        public static string BuildAll()
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine(BuildArt());
            log.AppendLine(ConfigureCharacterImport());
            log.AppendLine(SetupProject());
            log.AppendLine(BuildAnimator());
            log.AppendLine(BuildBoss());            // boss materials are shared by the mask set
            log.AppendLine(BuildSharkBoss());       // reuses the croc mouth / teeth materials
            log.AppendLine(BuildDruidBoss());
            log.AppendLine(BuildMaskSet());         // data + models the player prefab needs
            log.AppendLine(BuildSharkSet());        // the Shark's reward: shark body, long sword, crossbow, vines
            log.AppendLine(BuildDruidMaskSet());    // the Druid's reward: druid body, staff, Vine Seeker, Verdant Orb
            log.AppendLine(BuildPrefabs());
            log.AppendLine(BuildAudio());
            log.AppendLine(RenderClubIcon());
            log.AppendLine(ImportBowIcon());
            log.AppendLine(BuildScene());
            return log.ToString();
        }

        // ------------------------------------------------------------------ audio assets
        public static AudioManager.Cue[] cues;
        static AudioClip ambience;

        /// <summary>Wire every cue and the ambience bed to clips from the Gamemaster Audio package (see SoundPicks).</summary>
        [MenuItem("Elder Tree/5. Build Audio")]
        public static string BuildAudio()
        {
            cues = SoundPicks.Cues();
            ambience = SoundPicks.Ambience();
            int clips = cues.Sum(c => c.clips.Length), own = cues.Sum(c => c.clips.Count(a => AssetDatabase.GetAssetPath(a).StartsWith(SoundPicks.OwnDir)));
            return $"Audio built: {cues.Length} cues, {clips - own} Gamemaster Audio clips + {own} own (cutscene)";
        }

        // ------------------------------------------------------------------ club icon for the HUD
        public static string RenderClubIcon()
        {
            const int S = 256;
            var go = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ClubPath));
            go.hideFlags = HideFlags.DontSave;
            go.transform.position = new Vector3(1000, 1000, 1000);
            go.transform.rotation = Quaternion.Euler(0, 0, -45);
            const int IconLayer = 31;   // render only the club, nothing else in the open scene
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = IconLayer;
            var camGo = new GameObject("IconCam") { hideFlags = HideFlags.DontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.cullingMask = 1 << IconLayer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            var bounds = go.GetComponentInChildren<Renderer>().bounds;
            cam.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.08f;
            cam.transform.position = bounds.center - Vector3.forward * 3f;
            cam.transform.LookAt(bounds.center);
            var lgo = new GameObject("IconLight") { hideFlags = HideFlags.DontSave };
            var l = lgo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.7f; l.color = new Color(0.92f, 0.92f, 1f);
            l.cullingMask = 1 << IconLayer;
            lgo.transform.rotation = Quaternion.Euler(20, 30, 0);
            var rt = new RenderTexture(S, S, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, S, S), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory(Root + "/UI");
            string path = Root + "/UI/Icon_Club.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex); Object.DestroyImmediate(go); cam.targetTexture = null; Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lgo); rt.Release(); Object.DestroyImmediate(rt);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
            return "Club icon rendered";
        }

        // ------------------------------------------------------------------ scene
        static GameObject Place(GameObject prefab, Vector3 pos, float yaw, Transform parent, float scale = 1f, bool collider = true, string name = null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one * scale;
            if (name != null) go.name = name;
            if (collider)
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    if (mf.GetComponent<Collider>() == null && !mf.name.Contains("Glow")) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            return go;
        }

        /// <summary>Internal resolution scale (0.4 of 1080p ~ 768x432): pixels read at 1080p like a PS1 on a CRT.</summary>
        public static float PsxRenderScale = 0.4f;

        [MenuItem("Elder Tree/6. Build Arena Scene")]
        public static string BuildScene()
        {
            if (cues == null) BuildAudio();
            Directory.CreateDirectory(Root + "/Scenes");
            Directory.CreateDirectory(Root + "/Settings");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------------- render pipeline: PSX output (Docs/ART_DIRECTION.md)
            // Low internal resolution upscaled with nearest filtering, no MSAA, hard low-res shadows.
            // UI Toolkit draws on top at native resolution, so text stays crisp.
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (rp != null)
            {
                rp.shadowDistance = 40f;
                rp.msaaSampleCount = 1;
                rp.renderScale = PsxRenderScale;
                rp.upscalingFilter = UpscalingFilterSelection.Point;
                var so = new SerializedObject(rp);
                so.FindProperty("m_MainLightShadowmapResolution").intValue = 1024;
                so.FindProperty("m_AdditionalLightsShadowmapResolution").intValue = 512;
                so.FindProperty("m_SoftShadowsSupported").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(rp);
            }

            // ---------------- sky, ambient, fog
            var skyMat = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/M_NightSky.mat");
            if (skyMat == null) { skyMat = new Material(Shader.Find("ElderTree/NightSky")); AssetDatabase.CreateAsset(skyMat, MatDir + "/M_NightSky.mat"); }
            skyMat.shader = Shader.Find("ElderTree/NightSky");
            skyMat.SetColor("_ZenithColor", Palette.SkyTop);
            skyMat.SetColor("_HorizonColor", Palette.SkyHorizon);
            skyMat.SetColor("_GroundColor", Palette.Shadow);
            skyMat.SetColor("_CloudColor", TextureForge.Hex("#3A2A5E"));
            skyMat.SetColor("_MoonColor", TextureForge.Hex("#D8DCFF"));
            skyMat.SetFloat("_CloudStrength", 0.7f);
            skyMat.SetFloat("_StarBrightness", 1.9f);
            skyMat.SetFloat("_HorizonSharpness", 2.0f);
            // the visible moon sits low on the horizon opposite the light's travel direction
            Vector3 moonDir = Quaternion.Euler(-11f, 18f, 0) * Vector3.forward;
            skyMat.SetVector("_MoonDir", new Vector4(moonDir.x, moonDir.y, moonDir.z, 0));
            RenderSettings.skybox = skyMat;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Palette.SkyHorizon * 2.1f;
            RenderSettings.ambientEquatorColor = Palette.Fog * 2.3f;
            RenderSettings.ambientGroundColor = new Color(0.06f, 0.05f, 0.07f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Palette.Fog;
            RenderSettings.fogDensity = 0.03f;
            RenderSettings.reflectionIntensity = 0.15f;

            var sysRoot = new GameObject("--- Systems ---").transform;
            var envRoot = new GameObject("--- Environment ---").transform;
            var lightRoot = new GameObject("--- Lighting ---").transform;
            var actorRoot = new GameObject("--- Actors ---").transform;

            // ---------------- moon light
            var moonGo = new GameObject("Moonlight");
            moonGo.transform.SetParent(lightRoot);
            var moon = moonGo.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = Palette.Moon;
            moon.intensity = 1.8f;
            moon.shadows = LightShadows.Hard;
            moon.shadowStrength = 0.9f;
            moonGo.transform.rotation = Quaternion.Euler(34f, 198f, 0);
            RenderSettings.sun = moon;
            var moonData = moonGo.AddComponent<UniversalAdditionalLightData>();

            // warm rim/fill from the hut direction (no shadows) keeps the hero readable
            var fillGo = new GameObject("WarmFill");
            fillGo.transform.SetParent(lightRoot);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional; fill.color = Palette.Lantern; fill.intensity = 0.18f; fill.shadows = LightShadows.None;
            fillGo.transform.rotation = Quaternion.Euler(12f, 40f, 0);

            // ---------------- ground
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(envRoot);
            ground.transform.localScale = new Vector3(14, 1, 14);
            ground.GetComponent<Renderer>().sharedMaterial = M("M_Ground");
            ground.isStatic = true;
            var gm = M("M_Ground");
            gm.SetTextureScale("_BaseMap", new Vector2(26, 26));   // ~5.4 m per 256 px tile: chunky PSX texels
            gm.SetColor("_BaseColor", new Color(1.2f, 1.2f, 1.25f));

            // ---------------- the Pos Ronda (hero landmark) + lanterns
            var hut = Place(Prop("PosRonda"), new Vector3(-6.5f, 0, 10.5f), 160f, envRoot, 1f, true, "PosRonda");
            AddSign(hut);
            AddPointLight(hut.transform, new Vector3(0, 2.2f, 0.2f), 20f, 13f, true, "HutLantern");
            var hutLanternGlow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(hutLanternGlow.GetComponent<Collider>());
            hutLanternGlow.name = "HutLanternGlow";
            hutLanternGlow.transform.SetParent(hut.transform, false);
            hutLanternGlow.transform.localPosition = new Vector3(0.0f, 2.3f, -0.6f);
            hutLanternGlow.transform.localScale = new Vector3(0.14f, 0.2f, 0.14f);
            hutLanternGlow.GetComponent<Renderer>().sharedMaterial = M("M_LanternGlow");

            Vector3[] lampPos = { new(6.5f, 0, 7.5f), new(9.5f, 0, -3.5f), new(-9.8f, 0, -1.5f), new(1.5f, 0, -11f), new(-3f, 0, 14.5f) };
            for (int i = 0; i < lampPos.Length; i++)
            {
                var p = lampPos[i];
                float yaw = Mathf.Atan2(-p.x, -p.z) * Mathf.Rad2Deg - 90f;
                var lamp = Place(Prop("LampPost"), p, yaw, envRoot, 1f, true, $"LampPost{i}");
                var glow = lamp.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name.Contains("Glow"));
                var l = AddPointLight(lamp.transform, new Vector3(0.58f, 2.3f, 0), 15f, 12f, i < 2, $"LampLight{i}");
                l.GetComponent<LanternFlicker>().glowRenderer = glow;
            }

            // ---------------- arena ring: stone walls, fences, crates, rocks
            for (int i = 0; i < 14; i++)
            {
                float a = i / 14f * Mathf.PI * 2f + 0.15f;
                if (i == 3 || i == 10) continue; // gaps
                Vector3 p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 16.5f;
                float yaw = -a * Mathf.Rad2Deg + 90f;
                Place(Prop(i % 3 == 0 ? "Fence" : "StoneWall"), p, yaw + Random.Range(-6f, 6f), envRoot, 1f + (i % 2) * 0.1f);
            }
            Vector3[] crates = { new(-4.2f, 0, 11.6f), new(-3.3f, 0, 12.4f), new(7.8f, 0, 9.5f), new(-11.5f, 0, 4f) };
            foreach (var c in crates) Place(Prop("Crate"), c, Random.Range(0, 90f), envRoot, Random.Range(0.85f, 1.1f));
            Place(Prop("Crate"), new Vector3(-3.7f, 0.7f, 12.0f), 23f, envRoot, 0.8f);
            Vector3[] barrels = { new(-8.8f, 0, 12.4f), new(-9.4f, 0, 11.6f), new(8.4f, 0, 8.6f), new(10.5f, 0, -5.2f) };
            foreach (var b in barrels) Place(Prop("Barrel"), b, Random.Range(0, 360f), envRoot);
            Vector3[] rocks = { new(12f, 0, 3f), new(-13f, 0, -6f), new(4f, 0, -14.5f), new(-12f, 0, 9.5f), new(13.5f, 0, -9f), new(-5.5f, 0, -13f) };
            for (int i = 0; i < rocks.Length; i++) Place(Prop(i % 2 == 0 ? "Rock1" : "Rock2"), rocks[i], Random.Range(0, 360f), envRoot, Random.Range(0.9f, 1.6f));
            // tree line beyond the walls
            Random.InitState(11);
            for (int i = 0; i < 34; i++)
            {
                float a = i / 34f * Mathf.PI * 2f + Random.Range(-0.08f, 0.08f);
                float r = Random.Range(20f, 30f);
                Place(Prop(i % 2 == 0 ? "Tree1" : "Tree2"), new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r), Random.Range(0, 360f), envRoot,
                    Random.Range(1.1f, 1.8f), false);
            }

            // ---------------- ambient particles
            var ff = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/FX/FX_Fireflies.prefab"));
            ff.transform.SetParent(envRoot); ff.transform.position = new Vector3(-3f, 1.4f, 6f);
            var ff2 = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/FX/FX_Fireflies.prefab"));
            ff2.transform.SetParent(envRoot); ff2.transform.position = new Vector3(6f, 1.2f, -5f);

            // ---------------- reflection probe
            var probeGo = new GameObject("ReflectionProbe");
            probeGo.transform.SetParent(lightRoot);
            probeGo.transform.position = new Vector3(0, 2f, 2f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.size = new Vector3(60, 20, 60);
            probe.resolution = 128;

            // ---------------- post-processing
            var volGo = new GameObject("PostProcess");
            volGo.transform.SetParent(lightRoot);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = BuildPostProfile();
            volGo.AddComponent<PSXGlobals>();

            // ---------------- actors
            var spawn = new GameObject("PlayerSpawn").transform;
            spawn.SetParent(actorRoot);
            spawn.position = new Vector3(0, 0, -6f);
            var playerGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Player.prefab"));
            playerGo.transform.SetParent(actorRoot);
            playerGo.transform.position = spawn.position;
            var dummyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/TrainingDummy.prefab");
            Place(dummyPrefab, new Vector3(0f, 0, 1.5f), 180f, actorRoot, 1f, false, "TrainingDummy_A");
            Place(dummyPrefab, new Vector3(-4.6f, 0, 4.8f), 150f, actorRoot, 1f, false, "TrainingDummy_B");
            Place(dummyPrefab, new Vector3(4.8f, 0, 4.2f), 215f, actorRoot, 1f, false, "TrainingDummy_C");
            foreach (var d in actorRoot.GetComponentsInChildren<Transform>()) d.gameObject.isStatic = false;

            // ---------------- camera rig
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(sysRoot);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.08f;
            cam.farClipPlane = 400f;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;
            var brain = camGo.AddComponent<CinemachineBrain>();
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.4f);

            var pivot = new GameObject("CameraPivot").transform;
            pivot.SetParent(sysRoot);
            var aim = new GameObject("CameraAim").transform;
            aim.SetParent(sysRoot);

            var vcamGo = new GameObject("CM ThirdPerson");
            vcamGo.transform.SetParent(sysRoot);
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Follow = pivot;
            vcam.LookAt = aim;
            var lens = vcam.Lens; lens.FieldOfView = 52f; lens.NearClipPlane = 0.08f; lens.FarClipPlane = 400f; vcam.Lens = lens;
            var orbit = vcamGo.AddComponent<CinemachineOrbitalFollow>();
            orbit.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
            orbit.Radius = 4.6f;
            orbit.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace;
            orbit.TrackerSettings.PositionDamping = Vector3.zero;
            orbit.HorizontalAxis.Range = new Vector2(-180, 180); orbit.HorizontalAxis.Wrap = true; orbit.HorizontalAxis.Value = 0;
            orbit.VerticalAxis.Range = new Vector2(-30, 62); orbit.VerticalAxis.Value = 12f; orbit.VerticalAxis.Center = 12f;
            orbit.RadialAxis.Range = new Vector2(1, 1); orbit.RadialAxis.Value = 1;
            var composer = vcamGo.AddComponent<CinemachineRotationComposer>();
            composer.Damping = Vector2.zero;
            composer.TargetOffset = new Vector3(0, 0.15f, 0);
            var comp = composer.Composition; comp.ScreenPosition = new Vector2(0f, 0.04f); comp.DeadZone.Enabled = false; comp.HardLimits.Enabled = false;
            composer.Composition = comp;
            var deocc = vcamGo.AddComponent<CinemachineDeoccluder>();
            deocc.CollideAgainst = 1;
            deocc.MinimumDistanceFromTarget = 0.6f;
            deocc.AvoidObstacles.Enabled = true;
            deocc.AvoidObstacles.CameraRadius = 0.25f;
            deocc.AvoidObstacles.Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PullCameraForward;
            deocc.AvoidObstacles.Damping = 0.25f;
            deocc.AvoidObstacles.DampingWhenOccluded = 0.05f;
            var listener = vcamGo.AddComponent<CinemachineImpulseListener>();
            listener.Gain = 1f;
            listener.ChannelMask = 1;
            listener.Use2DDistance = true;
            listener.ReactionSettings.AmplitudeGain = 1f;
            listener.ReactionSettings.FrequencyGain = 1f;
            listener.ReactionSettings.Duration = 0.8f;

            var director = camGo.AddComponent<CameraDirector>();
            director.vcam = vcam; director.orbit = orbit; director.player = playerGo.transform;
            director.followPivot = pivot; director.aimPoint = aim;
            director.lockOn = playerGo.GetComponent<LockOnSystem>();
            director.impulse = playerGo.GetComponent<CinemachineImpulseSource>();
            pivot.position = playerGo.transform.position + Vector3.up * 1.5f;
            aim.position = pivot.position;
            camGo.transform.position = pivot.position - Vector3.forward * 4.6f + Vector3.up * 1f;

            // ---------------- systems
            var sys = new GameObject("GameSystems");
            sys.transform.SetParent(sysRoot);
            sys.AddComponent<TimeDirector>();
            var fx = sys.AddComponent<FXManager>();
            FXManager.Entry E(FXType t, string n, int pre) => new FXManager.Entry
                { type = t, prefab = AssetDatabase.LoadAssetAtPath<ParticleSystem>($"{PrefabDir}/FX/{n}.prefab"), prewarm = pre };
            fx.effects = new[]
            {
                E(FXType.HitSpark, "FX_HitSpark", 4), E(FXType.WoodChips, "FX_WoodChips", 4), E(FXType.StrawBurst, "FX_StrawBurst", 4),
                E(FXType.Dust, "FX_Dust", 2), E(FXType.DustSmall, "FX_DustSmall", 8), E(FXType.SlamRing, "FX_SlamRing", 2),
                E(FXType.SlamDebris, "FX_SlamDebris", 2), E(FXType.LandDust, "FX_LandDust", 2), E(FXType.ChargeFlash, "FX_ChargeFlash", 2),
                E(FXType.Explosion, "FX_Explosion", 6), E(FXType.BombPop, "FX_BombPop", 3), E(FXType.Smoke, "FX_Smoke", 2),
                E(FXType.SlashHit, "FX_SlashHit", 4), E(FXType.LeafBurst, "FX_LeafBurst", 4),
                E(FXType.LeafExplosion, "FX_LeafExplosion", 4), E(FXType.OrbBurst, "FX_OrbBurst", 2), E(FXType.LeafFall, "FX_LeafFall", 8)
            };
            var audioGo = new GameObject("Audio");
            audioGo.transform.SetParent(sysRoot);
            var am = audioGo.AddComponent<AudioManager>();
            am.cues = cues;
            am.ambienceLoop = ambience;
            am.ambienceVolume = SoundPicks.AmbienceVolume;
            am.exploreMusic = SoundPicks.ExploreMusic();
            am.bossMusic = SoundPicks.BossMusic();
            GameMixer.Assign(am);

            // ---------------- HUD
            var hudGo = new GameObject("HUD");
            hudGo.transform.SetParent(sysRoot);
            var doc = hudGo.AddComponent<UIDocument>();
            doc.panelSettings = BuildPanelSettings();
            var hud = hudGo.AddComponent<HUD>();
            hud.styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(Root + "/UI/ElderTreeHUD.uss");
            hud.clubIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/UI/Icon_Club.png");
            hud.bowIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/UI/Icon_Bow.png");
            var pc = playerGo.GetComponent<PlayerController>();
            hud.stats = pc.stats; hud.player = pc; hud.lockOn = pc.lockOn;

            var boot = sys.AddComponent<GameBootstrap>();
            boot.player = pc; boot.hud = hud; boot.spawnPoint = spawn;
            PlaceBoss(actorRoot, sys, pc, hud, spawn);
            PlaceMaskSet(envRoot, sys, pc, hud);
            PlaceSharkSet(envRoot, sys);
            PlaceDruidSet(sys);

            EditorSceneManager.SaveScene(scene, ScenePath);
            // set as the only build scene + open by default
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            return "Scene built: " + ScenePath;
        }

        static Light AddPointLight(Transform parent, Vector3 local, float intensity, float range, bool shadows, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = Palette.Lantern;
            l.intensity = intensity;
            l.range = range;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            l.shadowNearPlane = 0.2f;
            go.AddComponent<UniversalAdditionalLightData>();
            go.AddComponent<LanternFlicker>();
            return l;
        }

        /// <summary>"POS RONDA" lettering on the hut sign, as a world-space TextMesh in the sign colour.</summary>
        static void AddSign(GameObject hut)
        {
            var sign = hut.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Sign");
            if (sign == null) return;
            var r = sign.GetComponent<Renderer>();
            var c = r.bounds.center;
            var go = new GameObject("SignText");
            go.transform.SetParent(hut.transform, true);
            var tm = go.AddComponent<TextMesh>();
            tm.text = "POS RONDA";
            tm.fontSize = 64;
            tm.characterSize = 0.022f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.96f, 0.88f, 0.7f);
            tm.fontStyle = FontStyle.Bold;
            // place on the front face of the sign (hut front faces local -Z after import)
            Vector3 front = hut.transform.forward;
            go.transform.position = c + front * 0.045f;
            go.transform.rotation = Quaternion.LookRotation(-front, Vector3.up);
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        static PanelSettings BuildPanelSettings()
        {
            string path = Root + "/UI/ElderTreePanelSettings.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, path);
            }
            string themePath = Root + "/UI/ElderTreeRuntimeTheme.tss";
            if (!File.Exists(themePath))
            {
                File.WriteAllText(themePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(themePath);
            }
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            ps.sortingOrder = 10;
            EditorUtility.SetDirty(ps);
            return ps;
        }

        static VolumeProfile BuildPostProfile()
        {
            string path = Root + "/Settings/ElderTreePostProfile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile != null) AssetDatabase.DeleteAsset(path);
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            T Add<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }

            // PSX dark-fantasy grade: crushed violet shadows, cold mids, amber glows bloom softly.
            var tone = Add<Tonemapping>(); tone.mode.Override(TonemappingMode.ACES);
            var bloom = Add<Bloom>(); bloom.threshold.Override(0.85f); bloom.intensity.Override(0.75f); bloom.scatter.Override(0.65f);
            bloom.tint.Override(new Color(1f, 0.88f, 0.8f));
            var ca = Add<ColorAdjustments>(); ca.postExposure.Override(0.95f); ca.contrast.Override(10f); ca.saturation.Override(-4f);
            var smh = Add<ShadowsMidtonesHighlights>();
            smh.shadows.Override(new Vector4(0.95f, 0.86f, 1.18f, 0f));
            smh.midtones.Override(new Vector4(0.95f, 0.97f, 1.06f, 0f));
            smh.highlights.Override(new Vector4(1.06f, 1.0f, 0.92f, 0f));
            var wb = Add<WhiteBalance>(); wb.temperature.Override(-8f); wb.tint.Override(10f);
            var vig = Add<Vignette>(); vig.intensity.Override(0.42f); vig.smoothness.Override(0.45f); vig.color.Override(new Color(0.03f, 0.01f, 0.05f));
            var grain = Add<FilmGrain>(); grain.type.Override(FilmGrainLookup.Medium2); grain.intensity.Override(0.28f); grain.response.Override(0.7f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
