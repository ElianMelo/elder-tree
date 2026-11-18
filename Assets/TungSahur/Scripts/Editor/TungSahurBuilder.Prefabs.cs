using System.IO;
using System.Linq;
using TungSahur.CameraSystem;
using TungSahur.Combat;
using TungSahur.Enemies;
using TungSahur.FX;
using TungSahur.InputHandling;
using TungSahur.Player;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TungSahur.EditorTools
{
    public static partial class TungSahurBuilder
    {
        public const string PrefabDir = Root + "/Prefabs";
        public const string PropsDir = Root + "/Art/Models/Props";
        public const int LayerPlayer = 8, LayerHurtbox = 9, LayerEnemy = 10;

        // ------------------------------------------------------------------ project setup
        [MenuItem("Tung Sahur/0. Project Setup (layers, physics)")]
        public static string SetupProject()
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            layers.GetArrayElementAtIndex(LayerPlayer).stringValue = "Player";
            layers.GetArrayElementAtIndex(LayerHurtbox).stringValue = "Hurtbox";
            layers.GetArrayElementAtIndex(LayerEnemy).stringValue = "Enemy";
            tm.ApplyModifiedProperties();
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(LayerHurtbox, i, true);
            Physics.IgnoreLayerCollision(LayerPlayer, LayerPlayer, true);

            // remap prop materials to project materials
            string[] names = { "M_Planks", "M_Burlap", "M_Rope", "M_DummyPaint", "M_Stone", "M_Iron", "M_LanternGlow", "M_Foliage", "M_Bark" };
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { PropsDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = (ModelImporter)AssetImporter.GetAtPath(path);
                imp.animationType = ModelImporterAnimationType.None;
                imp.importAnimation = false;
                imp.importTangents = ModelImporterTangents.CalculateMikk;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                imp.addCollider = false;
                foreach (var n in names)
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");
                    if (m) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), m);
                }
                imp.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            return "Project setup done";
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        static GameObject Prop(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PropsDir}/{name}.fbx");

        // ------------------------------------------------------------------ player prefab
        [MenuItem("Tung Sahur/4. Build Prefabs")]
        public static string BuildPrefabs()
        {
            Directory.CreateDirectory(PrefabDir);
            Directory.CreateDirectory(PrefabDir + "/FX");
            BuildFXPrefabs();
            BuildBowAssets();
            BuildPlayerPrefab();
            BuildDummyPrefab();
            AssetDatabase.SaveAssets();
            return "Prefabs built";
        }

        static void BuildPlayerPrefab()
        {
            var root = new GameObject("Player");
            root.tag = "Player";
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.85f; cc.radius = 0.3f; cc.center = new Vector3(0, 0.95f, 0);
            cc.stepOffset = 0.35f; cc.slopeLimit = 50f; cc.skinWidth = 0.04f; cc.minMoveDistance = 0f;
            var motor = root.AddComponent<PlayerMotor>();
            motor.groundMask = 1;
            var stats = root.AddComponent<PlayerStats>();
            var reader = root.AddComponent<PlayerInputReader>();
            var so = new SerializedObject(reader);
            so.FindProperty("actions").objectReferenceValue = AssetDatabase.LoadAssetAtPath<InputActionAsset>(Root + "/Input/TungControls.inputactions");
            so.ApplyModifiedPropertiesWithoutUndo();

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            model.transform.SetParent(visual.transform, false);
            var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            animator.avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().First();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.updateWhenOffscreen = true;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            // club in the right hand
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var club = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ClubPath));
            club.name = "Club";
            club.transform.SetParent(hand, false);
            ApplyClubGrip(club.transform);
            var clubBase = new GameObject("ClubBase").transform; clubBase.SetParent(club.transform, false); clubBase.localPosition = new Vector3(0, 0.12f, 0);
            var clubTip = new GameObject("ClubTip").transform; clubTip.SetParent(club.transform, false); clubTip.localPosition = new Vector3(0, 0.86f, 0);
            var hitbox = club.AddComponent<WeaponHitbox>();
            hitbox.hurtboxMask = 1 << LayerHurtbox;
            hitbox.radius = 0.15f;
            var trail = club.AddComponent<WeaponTrail>();
            trail.baseRef = clubBase; trail.tipRef = clubTip;
            trail.material = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/M_FX_Trail.mat");
            var clubRenderer = club.GetComponentInChildren<MeshRenderer>();

            var eye = new GameObject("Eye").transform;
            eye.SetParent(root.transform, false);
            eye.localPosition = new Vector3(0, 1.6f, 0);
            var lockOn = root.AddComponent<LockOnSystem>();
            lockOn.eye = eye;
            lockOn.occluders = 1;

            var impulse = root.AddComponent<CinemachineImpulseSource>();
            impulse.ImpulseDefinition.ImpulseChannel = 1;
            impulse.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
            impulse.ImpulseDefinition.ImpulseDuration = 0.22f;
            impulse.ImpulseDefinition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
            impulse.DefaultVelocity = Vector3.down * 0.4f;

            var pc = root.AddComponent<PlayerController>();
            pc.animator = animator; pc.visualRoot = visual.transform; pc.motor = motor; pc.stats = stats;
            pc.moveset = AssetDatabase.LoadAssetAtPath<Moveset>(MovesetPath);
            pc.weapon = hitbox; pc.lockOn = lockOn; pc.clubTip = clubTip; pc.clubRenderer = clubRenderer; pc.trail = trail;
            pc.hurtboxMask = 1 << LayerHurtbox; pc.groundMask = 1;
            pc.dodgeClipLength = StateClipLength("Dodge");
            pc.backstepClipLength = StateClipLength("Backstep");
            AttachBow(root, animator, pc, club.transform);

            var steps = root.AddComponent<FootstepFX>();
            steps.animator = animator; steps.motor = motor; steps.controller = pc;
            // masks: the Elder Tree becomes one form rig, the alligator body is added beside it
            AttachForms(root, visual.transform, pc, animator, hitbox, clubTip, clubRenderer, trail, steps);

            SetLayerRecursive(root, LayerPlayer);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/Player.prefab");
            Object.DestroyImmediate(root);
        }

        // ------------------------------------------------------------------ dummy prefab
        static void BuildDummyPrefab()
        {
            var root = new GameObject("TrainingDummy");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(Prop("Dummy"));
            model.transform.SetParent(root.transform, false);
            var body = model.GetComponentsInChildren<Transform>().First(t => t.name == "Dummy_Body");
            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.position = body.position;
            pivot.rotation = Quaternion.identity;
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            body.SetParent(pivot, true);

            var block = root.AddComponent<CapsuleCollider>();
            block.center = new Vector3(0, 1.0f, 0); block.height = 2.0f; block.radius = 0.32f;

            var hurt = new GameObject("Hurtbox");
            hurt.transform.SetParent(pivot, false);
            hurt.transform.localPosition = new Vector3(0, 0.95f, 0);
            var hc = hurt.AddComponent<CapsuleCollider>();
            hc.isTrigger = true; hc.height = 2.0f; hc.radius = 0.42f;
            hurt.AddComponent<Hurtbox>();
            hurt.layer = LayerHurtbox;

            var lockPoint = new GameObject("LockPoint").transform;
            lockPoint.SetParent(pivot, false);
            lockPoint.localPosition = new Vector3(0, 0.95f, 0);

            var dummy = root.AddComponent<TrainingDummy>();
            dummy.pivot = pivot;
            dummy.flashRenderers = body.GetComponentsInChildren<Renderer>();
            var tg = root.AddComponent<Targetable>();
            tg.lockPoint = lockPoint;
            tg.lockRadius = 0.5f;
            tg.displayName = "Training Dummy";
            root.layer = LayerEnemy;
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.gameObject.layer = LayerEnemy;
            PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/TrainingDummy.prefab");
            Object.DestroyImmediate(root);
        }

        // ------------------------------------------------------------------ particles
        static Material M(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");

        static ParticleSystem NewPS(string name, Material mat, out ParticleSystemRenderer r)
        {
            var go = new GameObject(name);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission; em.rateOverTime = 0;
            r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        static void Burst(ParticleSystem ps, int count, float time = 0f)
        {
            var em = ps.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(time, (short)count) });
        }

        static ParticleSystem.MinMaxGradient Grad(Color a, Color b, float a0 = 1f, float a1 = 0f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, 1) },
                      new[] { new GradientAlphaKey(a0, 0), new GradientAlphaKey(a0, 0.4f), new GradientAlphaKey(a1, 1) });
            return new ParticleSystem.MinMaxGradient(g);
        }

        static void SizeOverLife(ParticleSystem ps, float start, float end)
        {
            var s = ps.sizeOverLifetime; s.enabled = true;
            s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, start, 1, end));
        }

        static ParticleSystem SavePS(ParticleSystem ps, string file)
        {
            var path = $"{PrefabDir}/FX/{file}.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(ps.gameObject, path);
            Object.DestroyImmediate(ps.gameObject);
            return saved.GetComponent<ParticleSystem>();
        }

        static void BuildFXPrefabs()
        {
            // Hit spark: hot embers streaking out + a brief flash
            {
                var ps = NewPS("FX_HitSpark", M("M_FX_Spark"), out var r);
                var m = ps.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.32f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(4f, 10f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
                m.gravityModifier = 1.2f;
                m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.5f), new Color(1f, 0.55f, 0.2f));
                Burst(ps, 22);
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 50f; sh.radius = 0.05f;
                var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(new Color(1, 0.9f, 0.6f), new Color(1, 0.35f, 0.1f));
                r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.035f; r.lengthScale = 1.2f;
                var flash = NewPS("Flash", M("M_FX_Glow"), out var fr);
                flash.transform.SetParent(ps.transform, false);
                var fm = flash.main; fm.startLifetime = 0.08f; fm.startSpeed = 0f; fm.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.3f);
                fm.startColor = new Color(1f, 0.82f, 0.55f, 0.9f);
                Burst(flash, 1);
                var fs = flash.shape; fs.enabled = false;
                SizeOverLife(flash, 0.6f, 1.4f);
                var fc = flash.colorOverLifetime; fc.enabled = true; fc.color = Grad(Color.white, Color.white, 1f, 0f);
                SavePS(ps, "FX_HitSpark");
            }
            // Wood chips (from the club) & straw (from the dummy): tumbling bits that bounce
            void Chips(string name, Color a, Color b, int count, float size, float speed, bool splinter = true)
            {
                var ps = NewPS(name, M("M_FX_Chip"), out var r);
                var m = ps.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
                m.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
                m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                m.gravityModifier = 2.2f;
                m.startColor = new ParticleSystem.MinMaxGradient(a, b);
                Burst(ps, count);
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 55f; sh.radius = 0.08f;
                var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-12f, 12f);
                var c = ps.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World; c.dampen = 0.5f; c.bounce = 0.3f;
                c.lifetimeLoss = 0.1f; c.collidesWith = 1; c.quality = ParticleSystemCollisionQuality.Low;
                var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, Color.white, 1f, 0f);
                if (splinter) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.012f; r.lengthScale = 2.2f; }
                SavePS(ps, name);
            }
            Chips("FX_WoodChips", new Color(0.62f, 0.42f, 0.25f), new Color(0.42f, 0.27f, 0.15f), 12, 0.05f, 5.5f);
            Chips("FX_StrawBurst", new Color(0.78f, 0.66f, 0.38f), new Color(0.58f, 0.48f, 0.26f), 22, 0.05f, 4.8f);
            // Dust puffs
            void Dust(string name, int count, float size, float speed, float life, bool ring)
            {
                var ps = NewPS(name, M("M_FX_Dust"), out var r);
                var m = ps.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
                m.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
                m.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
                m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                m.gravityModifier = -0.05f;
                m.startColor = new Color(0.62f, 0.56f, 0.5f, 0.45f);
                Burst(ps, count);
                var sh = ps.shape;
                if (ring) { sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.25f; sh.rotation = new Vector3(90, 0, 0); sh.radiusThickness = 0.2f; }
                else { sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.2f; }
                var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.limit = 0.3f; lv.dampen = 0.15f;
                SizeOverLife(ps, 0.5f, 1.6f);
                var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
                var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, Color.white, 1f, 0f);
                SavePS(ps, name);
            }
            Dust("FX_Dust", 16, 1.0f, 2.6f, 1.6f, true);
            Dust("FX_DustSmall", 6, 0.45f, 1.0f, 0.8f, false);
            Dust("FX_LandDust", 12, 0.6f, 2.2f, 1.0f, true);
            // Slam shockwave ring (flat on the ground)
            {
                var ps = NewPS("FX_SlamRing", M("M_FX_Ring"), out var r);
                var m = ps.main;
                m.startLifetime = 0.42f; m.startSpeed = 0f; m.startSize = 4f;
                m.startColor = new Color(0.95f, 0.82f, 0.6f, 0.75f);
                Burst(ps, 1);
                var sh = ps.shape; sh.enabled = false;
                r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                SizeOverLife(ps, 0.15f, 1f);
                var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, Color.white, 1f, 0f);
                SavePS(ps, "FX_SlamRing");
            }
            // Slam debris: dark earth chunks flung up
            Chips("FX_SlamDebris", new Color(0.32f, 0.27f, 0.22f), new Color(0.48f, 0.44f, 0.4f), 26, 0.12f, 7f, false);
            // Charge release flash
            {
                var ps = NewPS("FX_ChargeFlash", M("M_FX_Glow"), out var r);
                var m = ps.main; m.startLifetime = 0.18f; m.startSpeed = 0f; m.startSize = 1.4f; m.startColor = new Color(1f, 0.75f, 0.35f, 1f);
                Burst(ps, 1);
                var sh = ps.shape; sh.enabled = false;
                SizeOverLife(ps, 0.3f, 1.6f);
                var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad(Color.white, Color.white, 1f, 0f);
                SavePS(ps, "FX_ChargeFlash");
            }
            // Fireflies (looping ambience)
            {
                var ps = NewPS("FX_Fireflies", M("M_FX_Firefly"), out var r);
                var m = ps.main; m.loop = true; m.playOnAwake = true; m.duration = 5f; m.prewarm = true;
                m.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.07f); m.maxParticles = 80;
                m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(0.85f, 1f, 0.5f));
                var em = ps.emission; em.rateOverTime = 6f;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(14, 2.5f, 14);
                var n = ps.noise; n.enabled = true; n.strength = 0.6f; n.frequency = 0.25f; n.scrollSpeed = 0.15f;
                var col = ps.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(0.2f, 0.45f), new GradientAlphaKey(1, 0.7f), new GradientAlphaKey(0, 1) });
                col.color = new ParticleSystem.MinMaxGradient(g);
                SavePS(ps, "FX_Fireflies");
            }
        }
    }
}
