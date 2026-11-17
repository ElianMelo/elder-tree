using System.Collections.Generic;
using System.IO;
using System.Linq;
using TungSahur.Combat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TungSahur.EditorTools
{
    public static partial class TungSahurBuilder
    {
        public const string AnimDir = Root + "/Animation";
        public const string ControllerPath = AnimDir + "/TungSahur.controller";
        public const string MovesetPath = Root + "/Data/TungMoveset.asset";

        // Human Mega Animations Pack (Kevin Iglesias) is the single source of truth for player motion:
        // locomotion, air, melee, guard, hit/knockdown/death and the archer set all come from it, so
        // every clip shares one skeleton, one style and one timing language. Do not mix in other packs.
        const string KI = "Assets/Kevin Iglesias/Human Animations/Animations/Male";

        static AnimationClip Clip(string path, string name = null)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview"));
            var clip = name == null ? clips.FirstOrDefault() : clips.FirstOrDefault(c => c.name == name);
            if (clip == null) Debug.LogError($"[TungSahur] Missing clip {path} {name}");
            return clip;
        }

        static AnimationClip K(string rel) => Clip($"{KI}/{rel}.fbx");

        static AvatarMask UpperBodyMask() => Mask("M_UpperBody", p =>
            p == AvatarMaskBodyPart.Body || p == AvatarMaskBodyPart.Head || p == AvatarMaskBodyPart.LeftArm ||
            p == AvatarMaskBodyPart.RightArm || p == AvatarMaskBodyPart.LeftFingers || p == AvatarMaskBodyPart.RightFingers);

        /// <summary>Legs only (no hips): the walk clips move the feet while the archer clip owns the torso.</summary>
        static AvatarMask LegsMask() => Mask("M_Legs", p =>
            p == AvatarMaskBodyPart.LeftLeg || p == AvatarMaskBodyPart.RightLeg ||
            p == AvatarMaskBodyPart.LeftFootIK || p == AvatarMaskBodyPart.RightFootIK);

        static AvatarMask Mask(string name, System.Func<AvatarMaskBodyPart, bool> on)
        {
            string path = $"{AnimDir}/{name}.mask";
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, path);
            }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, on((AvatarMaskBodyPart)i));
            EditorUtility.SetDirty(mask);
            return mask;
        }

        static readonly (string n, Vector2 d)[] Dirs =
        {
            ("Forward", new Vector2(0, 1)), ("ForwardRight", new Vector2(0.7071f, 0.7071f)), ("Right", new Vector2(1, 0)),
            ("BackwardRight", new Vector2(0.7071f, -0.7071f)), ("Backward", new Vector2(0, -1)),
            ("BackwardLeft", new Vector2(-0.7071f, -0.7071f)), ("Left", new Vector2(-1, 0)), ("ForwardLeft", new Vector2(-0.7071f, 0.7071f))
        };

        static BlendTree NewTree(AnimatorController ac, string name, BlendTreeType type, string px, string py = null)
        {
            var t = new BlendTree { name = name, blendType = type, blendParameter = px, useAutomaticThresholds = false };
            if (py != null) t.blendParameterY = py;
            AssetDatabase.AddObjectToAsset(t, ac);
            return t;
        }

        [MenuItem("Tung Sahur/3. Build Animator + Moveset")]
        public static string BuildAnimator()
        {
            Directory.CreateDirectory(AnimDir);
            Directory.CreateDirectory(Root + "/Data");
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
                AssetDatabase.DeleteAsset(ControllerPath);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            foreach (var p in new[] { "MoveX", "MoveZ", "LocoSpeed", "ActionSpeed", "VerticalSpeed", "AimPitch", "BowDrawTime", "BowMoveX", "BowMoveZ" })
                ac.AddParameter(p, AnimatorControllerParameterType.Float);
            ac.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            foreach (var p in ac.parameters)
                if (p.name == "LocoSpeed" || p.name == "ActionSpeed") p.defaultFloat = 1f;
            ac.parameters = ac.parameters.Select(p =>
            {
                if (p.name == "LocoSpeed" || p.name == "ActionSpeed") p.defaultFloat = 1f;
                return p;
            }).ToArray();

            var sm = ac.layers[0].stateMachine;
            sm.entryPosition = new Vector3(0, 0);
            sm.anyStatePosition = new Vector3(0, -80);

            // ---------------- Locomotion blend tree (in-place clips, speed driven from code)
            var loco = sm.AddState("Locomotion", new Vector3(300, 0));
            var tree = NewTree(ac, "LocomotionTree", BlendTreeType.FreeformDirectional2D, "MoveX", "MoveZ");
            tree.AddChild(K("Combat/1H/HumanM@CombatIdle1H01"), Vector2.zero);
            foreach (var (n, d) in Dirs) tree.AddChild(K($"Movement/Walk/HumanM@Walk01_{n}"), d * 1f);
            foreach (var (n, d) in Dirs) tree.AddChild(K($"Movement/Run/HumanM@Run01_{n}"), d * 2f);
            tree.AddChild(K("Movement/Sprint/HumanM@Sprint01_Forward"), new Vector2(0, 3f));
            tree.AddChild(K("Movement/Sprint/HumanM@Sprint01_ForwardLeft"), new Vector2(-2.12f, 2.12f));
            tree.AddChild(K("Movement/Sprint/HumanM@Sprint01_ForwardRight"), new Vector2(2.12f, 2.12f));
            loco.motion = tree;
            loco.speedParameter = "LocoSpeed";
            loco.speedParameterActive = true;
            loco.iKOnFeet = true;
            sm.defaultState = loco;

            // ---------------- Air
            Add(sm, "JumpStart", K("Movement/Jump/HumanM@Jump01 - Begin"), new Vector3(600, -200));
            Add(sm, "Fall", K("Movement/Jump/HumanM@Fall01"), new Vector3(600, -140));      // loops in the pack
            Add(sm, "Land", K("Movement/Jump/HumanM@Jump01 - Land"), new Vector3(600, -80));

            // ---------------- Actions (all entered from code with CrossFade)
            float y = 0;
            AnimatorState Act(string name, AnimationClip c)
            {
                var s = Add(sm, name, c, new Vector3(600, y));
                s.speedParameter = "ActionSpeed";
                s.speedParameterActive = true;
                y += 60;
                return s;
            }
            Act("Dodge", K("Combat/HumanM@Dodge01"));
            Act("Backstep", K("Combat/HumanM@Dodge01"));
            Act("HitLight", K("Combat/HumanM@CombatDamage01"));
            Act("HitHeavy", K("Combat/HumanM@CombatDamage02"));
            Act("Stagger", K("Combat/HumanM@Stun01"));
            Act("Death", K("Combat/HumanM@CombatDeath01"));
            // Slam hits: knocked off your roots -> on the ground -> stand up (code times the stand-up)
            var kdFall = Act("KnockdownFall", K("Combat/HumanM@Knockdown01 - Fall"));
            var kdGround = Act("KnockdownGround", K("Combat/HumanM@Knockdown01 - Ground"));
            Act("KnockdownStandUp", K("Combat/HumanM@Knockdown01 - StandUp"));
            var kdT = kdFall.AddTransition(kdGround);
            kdT.hasExitTime = true; kdT.exitTime = 0.95f; kdT.duration = 0.08f;
            Act("Light1", K("Combat/1H/HumanM@Attack1H02_R"));
            Act("Light2", K("Combat/1H/HumanM@Attack1H04_R"));
            Act("Light3", K("Combat/1H/HumanM@Attack1H05_R"));
            Act("Light4", K("Combat/1H/HumanM@Attack1H03_R"));
            Act("Heavy1", K("Combat/2H/HumanM@Attack2H02"));
            Act("Heavy2", K("Combat/2H/HumanM@Attack2H01"));
            Act("RunAttack", K("Combat/2H/HumanM@Attack2H03"));
            Act("JumpAttack", K("Combat/2H/HumanM@Attack2H02"));
            Act("DodgeAttack", K("Combat/1H/HumanM@Attack1H01_R"));
            Act("Skill", K("Combat/2H/HumanM@Attack2H04"));

            // ---------------- Archer (full body, entered from code). The pack keeps the root facing the
            // target and turns hips/chest ~85 deg inside the clip, so the stance comes from the animation.
            var draw = Act("BowDraw", K("Combat/Bow/HumanM@BowShot01 - Load"));
            draw.timeParameter = "BowDrawTime";          // scrubbed by the gameplay draw amount
            draw.timeParameterActive = true;
            var hold = sm.AddState("BowHold", new Vector3(900, 0));
            var pitchTree = NewTree(ac, "BowHoldPitch", BlendTreeType.Simple1D, "AimPitch");
            // thresholds = arrow pitch (deg) each clip actually aims at, measured on the rig
            pitchTree.AddChild(K("Combat/Bow/HumanM@BowShot01_Down - Hold"), -50f);
            pitchTree.AddChild(K("Combat/Bow/HumanM@BowShot01 - Hold"), -5f);
            pitchTree.AddChild(K("Combat/Bow/HumanM@BowShot01_Up - Hold"), 40f);
            hold.motion = pitchTree;
            hold.writeDefaultValues = true;
            Act("BowRelease", K("Combat/Bow/HumanM@BowShot01 - Release"));

            // ---------------- Guard layer (upper body)
            var guardLayer = new AnimatorControllerLayer
            {
                name = "Guard",
                defaultWeight = 0f,
                avatarMask = UpperBodyMask(),
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = new AnimatorStateMachine { name = "Guard", hideFlags = HideFlags.HideInHierarchy }
            };
            AssetDatabase.AddObjectToAsset(guardLayer.stateMachine, ac);
            ac.AddLayer(guardLayer);
            var gsm = ac.layers[1].stateMachine;
            var gEmpty = gsm.AddState("Empty", new Vector3(300, 0));
            gsm.defaultState = gEmpty;
            var gLoop = gsm.AddState("GuardLoop", new Vector3(300, 80));
            gLoop.motion = K("Combat/1H/HumanM@Parry1H01_R - Loop");
            var gHit = gsm.AddState("GuardHit", new Vector3(300, 160));
            gHit.motion = K("Combat/1H/HumanM@Parry1H01_R - Hit");

            // ---------------- Bow legs layer: pack walk clips on the legs while the archer clip owns the
            // torso. BowMoveX/Z are the move direction in the archer's (hips) frame, so stepping toward
            // the target plays a side step, like a real side-on stance.
            var legsLayer = new AnimatorControllerLayer
            {
                name = "BowLegs",
                defaultWeight = 0f,
                avatarMask = LegsMask(),
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = new AnimatorStateMachine { name = "BowLegs", hideFlags = HideFlags.HideInHierarchy }
            };
            AssetDatabase.AddObjectToAsset(legsLayer.stateMachine, ac);
            ac.AddLayer(legsLayer);
            var lsm = ac.layers[2].stateMachine;
            var walk = lsm.AddState("BowWalk", new Vector3(300, 0));
            var walkTree = NewTree(ac, "BowWalkTree", BlendTreeType.FreeformDirectional2D, "BowMoveX", "BowMoveZ");
            walkTree.AddChild(K("Combat/Bow/HumanM@BowShot01 - Hold"), Vector2.zero);
            foreach (var (n, d) in Dirs) walkTree.AddChild(K($"Movement/Walk/HumanM@Walk01_{n}"), d);
            walk.motion = walkTree;
            walk.speedParameter = "LocoSpeed";
            walk.speedParameterActive = true;
            lsm.defaultState = walk;

            // IK pass on the base layer: the bow places both hands on the aim line (BowRig.OnAnimatorIK)
            var layers = ac.layers;
            layers[0].iKPass = true;
            ac.layers = layers;

            EditorUtility.SetDirty(ac);
            BuildMoveset();
            AssetDatabase.SaveAssets();
            return $"Animator built: {ac.layers.Length} layers, {sm.states.Length} states";
        }

        static AnimatorState Add(AnimatorStateMachine sm, string name, Motion m, Vector3 pos)
        {
            var s = sm.AddState(name, pos);
            s.motion = m;
            s.writeDefaultValues = true;
            return s;
        }

        // ------------------------------------------------------------------ moveset tuning
        // Timings come from PreviewRenderer.AnalyzeSwing (club-tip speed peaks), then padded.
        public static void BuildMoveset()
        {
            var ms = AssetDatabase.LoadAssetAtPath<Moveset>(MovesetPath);
            if (ms == null)
            {
                ms = ScriptableObject.CreateInstance<Moveset>();
                AssetDatabase.CreateAsset(ms, MovesetPath);
            }

            ms.lightCombo = new[]
            {
                new AttackData { id = "Light1", state = "Light1", damage = 34, poiseDamage = 14, staminaCost = 15,
                    hitStart = 0.30f, hitEnd = 0.46f, comboOpen = 0.50f, cancelOpen = 0.56f, exitTime = 0.82f, trackUntil = 0.28f,
                    windupSpeed = 0.95f, activeSpeed = 1.15f, recoverySpeed = 1.25f, lungeDistance = 0.7f, lungeStart = 0.18f, lungeEnd = 0.42f,
                    hitStop = 0.065f, shake = 0.35f, next = "Light2" },
                new AttackData { id = "Light2", state = "Light2", damage = 36, poiseDamage = 14, staminaCost = 15,
                    hitStart = 0.27f, hitEnd = 0.44f, comboOpen = 0.48f, cancelOpen = 0.54f, exitTime = 0.80f, trackUntil = 0.26f,
                    windupSpeed = 1.0f, activeSpeed = 1.2f, recoverySpeed = 1.3f, lungeDistance = 0.6f, lungeStart = 0.12f, lungeEnd = 0.38f,
                    hitStop = 0.065f, shake = 0.35f, next = "Light3" },
                new AttackData { id = "Light3", state = "Light3", damage = 40, poiseDamage = 16, staminaCost = 16,
                    hitStart = 0.16f, hitEnd = 0.32f, comboOpen = 0.46f, cancelOpen = 0.52f, exitTime = 0.80f, trackUntil = 0.16f,
                    windupSpeed = 0.85f, activeSpeed = 1.2f, recoverySpeed = 1.25f, lungeDistance = 0.7f, lungeStart = 0.08f, lungeEnd = 0.28f,
                    hitStop = 0.075f, shake = 0.45f, next = "Light4" },
                new AttackData { id = "Light4", state = "Light4", damage = 52, poiseDamage = 24, staminaCost = 18, weight = ImpactWeight.Heavy,
                    hitStart = 0.17f, hitEnd = 0.34f, comboOpen = 0.62f, cancelOpen = 0.58f, exitTime = 0.78f, trackUntil = 0.16f,
                    windupSpeed = 0.8f, activeSpeed = 1.15f, recoverySpeed = 1.1f, lungeDistance = 1.3f, lungeStart = 0.08f, lungeEnd = 0.32f,
                    hitStop = 0.10f, shake = 0.6f, rumbleLow = 0.5f, rumbleHigh = 0.7f, next = "Light1" },
            };
            ms.heavyCombo = new[]
            {
                new AttackData { id = "Heavy1", state = "Heavy1", damage = 72, poiseDamage = 38, staminaCost = 26, weight = ImpactWeight.Slam,
                    hitStart = 0.38f, hitEnd = 0.50f, comboOpen = 0.62f, cancelOpen = 0.66f, exitTime = 0.84f, trackUntil = 0.34f,
                    windupSpeed = 0.85f, activeSpeed = 1.35f, recoverySpeed = 1.15f, chargeable = true, chargeHold = 0.30f,
                    chargeMaxTime = 0.9f, chargeDamageMultiplier = 1.65f, lungeDistance = 0.9f, lungeStart = 0.30f, lungeEnd = 0.48f,
                    hitStop = 0.12f, shake = 0.9f, rumbleLow = 0.8f, rumbleHigh = 0.6f, slamRadius = 1.8f, slamDamageScale = 0.45f, next = "Heavy2" },
                new AttackData { id = "Heavy2", state = "Heavy2", damage = 78, poiseDamage = 42, staminaCost = 28, weight = ImpactWeight.Heavy,
                    hitStart = 0.27f, hitEnd = 0.44f, comboOpen = 0.62f, cancelOpen = 0.64f, exitTime = 0.82f, trackUntil = 0.25f,
                    windupSpeed = 0.85f, activeSpeed = 1.3f, recoverySpeed = 1.15f, chargeable = true, chargeHold = 0.22f,
                    chargeMaxTime = 0.9f, chargeDamageMultiplier = 1.6f, lungeDistance = 1.0f, lungeStart = 0.22f, lungeEnd = 0.42f,
                    hitStop = 0.11f, shake = 0.8f, rumbleLow = 0.7f, rumbleHigh = 0.8f, next = "Heavy1" },
            };
            ms.runningAttack = new AttackData { id = "RunAttack", state = "RunAttack", damage = 60, poiseDamage = 30, staminaCost = 22,
                weight = ImpactWeight.Heavy, hitStart = 0.14f, hitEnd = 0.30f, comboOpen = 0.55f, cancelOpen = 0.56f, exitTime = 0.74f,
                trackUntil = 0.12f, windupSpeed = 1.1f, activeSpeed = 1.2f, recoverySpeed = 1.25f, lungeDistance = 2.6f, lungeStart = 0.0f,
                lungeEnd = 0.30f, hitStop = 0.10f, shake = 0.6f, next = "Light2" };
            ms.jumpAttack = new AttackData { id = "JumpAttack", state = "JumpAttack", startOffset = 0.30f, damage = 70, poiseDamage = 40,
                staminaCost = 20, weight = ImpactWeight.Slam, hitStart = 0.40f, hitEnd = 0.52f, comboOpen = 0.66f, cancelOpen = 0.66f,
                exitTime = 0.84f, trackUntil = 0.36f, windupSpeed = 1.1f, activeSpeed = 1.3f, recoverySpeed = 1.2f, lungeDistance = 0.4f,
                lungeStart = 0.3f, lungeEnd = 0.45f, hitStop = 0.12f, shake = 0.95f, rumbleLow = 0.8f, rumbleHigh = 0.6f, slamRadius = 2.0f,
                next = "Light1" };
            ms.dodgeAttack = new AttackData { id = "DodgeAttack", state = "DodgeAttack", damage = 32, poiseDamage = 10, staminaCost = 14,
                hitStart = 0.16f, hitEnd = 0.32f, comboOpen = 0.5f, cancelOpen = 0.5f, exitTime = 0.72f, trackUntil = 0.16f,
                windupSpeed = 1.2f, activeSpeed = 1.3f, recoverySpeed = 1.3f, lungeDistance = 1.2f, lungeStart = 0.0f, lungeEnd = 0.3f,
                hitStop = 0.06f, shake = 0.3f, next = "Light2" };
            ms.skill = new AttackData { id = "Skill", state = "Skill", damage = 110, poiseDamage = 70, staminaCost = 20, fpCost = 30,
                weight = ImpactWeight.Slam, hitStart = 0.32f, hitEnd = 0.45f, comboOpen = 0.7f, cancelOpen = 0.7f, exitTime = 0.86f,
                trackUntil = 0.30f, windupSpeed = 0.9f, activeSpeed = 1.2f, recoverySpeed = 1.15f, lungeDistance = 2.4f, lungeStart = 0.05f,
                lungeEnd = 0.36f, hitStop = 0.16f, shake = 1.3f, rumbleLow = 1f, rumbleHigh = 0.8f, slamRadius = 3.2f, slamDamageScale = 0.7f,
                next = "" };
            // clip lengths from the controller's states
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            float Len(string state)
            {
                var st = ac.layers[0].stateMachine.states.FirstOrDefault(s => s.state.name == state).state;
                return st != null && st.motion is AnimationClip c ? c.length : 1f;
            }
            foreach (var a in ms.lightCombo.Concat(ms.heavyCombo).Concat(new[] { ms.runningAttack, ms.jumpAttack, ms.dodgeAttack, ms.skill }))
                a.clipLength = Len(a.state);
            EditorUtility.SetDirty(ms);
        }

        public static float StateClipLength(string state)
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var st = ac.layers[0].stateMachine.states.FirstOrDefault(s => s.state.name == state).state;
            return st != null && st.motion is AnimationClip c ? c.length : 1f;
        }
    }
}
