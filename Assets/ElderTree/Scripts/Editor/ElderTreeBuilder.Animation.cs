using System.Collections.Generic;
using System.IO;
using System.Linq;
using ElderTree.Combat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ElderTree.EditorTools
{
    public static partial class ElderTreeBuilder
    {
        public const string AnimDir = Root + "/Animation";
        public const string ControllerPath = AnimDir + "/ElderTree.controller";
        public const string MovesetPath = Root + "/Data/ElderTreeMoveset.asset";
        /// <summary>The alligator's controller: the shared one with the pack's 2H idle and 2H guard swapped in.</summary>
        public const string CrocControllerPath = AnimDir + "/ElderTree_Croc.overrideController";

        // Human Mega Animations Pack (Kevin Iglesias) is the single source of truth for player motion:
        // locomotion, air, melee, guard, hit/knockdown/death and the archer set all come from it, so
        // every clip shares one skeleton, one style and one timing language. Do not mix in other packs.
        const string KI = "Assets/Kevin Iglesias/Human Animations/Animations/Male";

        static AnimationClip Clip(string path, string name = null)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview"));
            var clip = name == null ? clips.FirstOrDefault() : clips.FirstOrDefault(c => c.name == name);
            if (clip == null) Debug.LogError($"[ElderTree] Missing clip {path} {name}");
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

        /// <summary>Arms and fingers only: the 2H guard hold rides on top of walk/run legs and torso.</summary>
        static AvatarMask ArmsMask() => Mask("M_Arms", p =>
            p == AvatarMaskBodyPart.LeftArm || p == AvatarMaskBodyPart.RightArm ||
            p == AvatarMaskBodyPart.LeftFingers || p == AvatarMaskBodyPart.RightFingers);

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

        [MenuItem("Elder Tree/3. Build Animator + Moveset")]
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
            Act("Dodge", K("Movement/HumanM@Roll01"));          // grounded forward roll (in place; travel from Roll01 [RM])
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
            // Alligator bomb hammer, both hands on the shaft. Light: quick sweeps (2H). Heavy: overhead
            // ground strikes (Mining overhead, Polearm slam) into the leaping 2H finisher.
            Act("Hammer_L1", K("Combat/2H/HumanM@Attack2H03"));
            Act("Hammer_L2", K("Combat/2H/HumanM@Attack2H01"));
            Act("Hammer_L3", K("Combat/2H/HumanM@Attack2H02"));
            Act("Hammer_H1", K("Work/Mining/HumanM@Mining01 - Loop Ground"));
            Act("Hammer_H2", K("Combat/Polearm/HumanM@AttackPolearm03"));
            Act("Hammer_H3", K("Combat/2H/HumanM@Attack2H04"));
            Act("Hammer_Run", K("Combat/Polearm/HumanM@AttackPolearm02"));
            Act("Hammer_Dodge", K("Combat/Polearm/HumanM@AttackPolearm01"));
            // Shark Mask long sword (Monster Hunter style). Light alternates the cut direction: kesa right-to-left,
            // the same kesa mirrored (left-to-right), thrust, rising right-to-left. Heavy is
            // katana work with none of the hammer's heavies: iai (stance with the hand on the hilt at the hip, then the
            // blade is drawn up and cut down across with a deep lunge), helm-splitter, kesa from the upper right, roundslash.
            // Sweep directions and edge leads measured on the shark rig.
            Act("Sword_L1", K("Combat/2H/HumanM@Attack2H01"));
            Act("Sword_L2", K("Combat/2H/HumanM@Attack2H01")).mirror = true;   // Light 1 mirrored: the same kesa, left to right
            Act("Sword_L3", K("Combat/Polearm/HumanM@AttackPolearm01"));
            Act("Sword_L4", K("Combat/2H/HumanM@Attack2H03"));
            Act("Sword_IaiStance", K("Misc/Unsheathe/HumanM@UnsheatheHips01_R"));
            Act("Sword_H1", K("Combat/Polearm/HumanM@AttackPolearm02"));
            Act("Sword_H2", K("Combat/2H/HumanM@Attack2H02"));
            Act("Sword_H3", K("Work/Chopping/HumanM@TreeChopping01 - Loop"));
            Act("Sword_H4", K("Combat/Polearm/HumanM@AttackPolearm04"));
            Act("Sword_Run", K("Combat/Polearm/HumanM@AttackPolearm02"));
            Act("Sword_Dodge", K("Combat/2H/HumanM@Attack2H03"));
            // spin special: held poses per turn (blade low / at the waist / overhead)
            Act("Sword_Spin", K("Combat/2H/HumanM@Attack2H03"));
            Act("Sword_SpinMid", K("Combat/Polearm/HumanM@AttackPolearm04"));
            Act("Sword_SpinHigh", K("Combat/Polearm/HumanM@AttackPolearm04"));
            // Shark Mask crossbow: shouldered like a rifle, cranked between bolts
            Act("CrossbowAim", K("Combat/Rifle/HumanM@Rifle_Aim01"));
            Act("CrossbowShoot", K("Combat/Rifle/HumanM@Rifle_Aim01_Shoot01"));
            Act("CrossbowReload", K("Combat/Rifle/HumanM@Rifle_Reload01"));
            // Alligator Mask: Sky Bombardment (two-handed overhead hurl), the shoulder cannon, transformations
            Act("SpecialHold", K("Combat/Thrown/Rock/HumanM@ThrowBigRock01 - Hold"));
            Act("SpecialThrow", K("Combat/Thrown/Rock/HumanM@ThrowBigRock01"));
            Act("CannonAim", K("Combat/Bazooka/HumanM@Bazooka_Aim01"));
            Act("CannonShoot", K("Combat/Bazooka/HumanM@Bazooka_Aim01_Shoot01"));
            // Druid Mask (caster): the pack's spellcasting set. The Load clips loop while a spell gathers / the orb is
            // held up; the Cast clips release it.
            Act("Druid_CastLoad", K("Combat/Spellcasting/MagicAttacks/Directional/HumanM@MagicAttackDirect1H01_R - Load"));
            Act("Druid_Cast", K("Combat/Spellcasting/MagicAttacks/Directional/HumanM@MagicAttackDirect1H01_R - Cast"));
            Act("Druid_ChargeLoad", K("Combat/Spellcasting/MagicAttacks/Directional/HumanM@MagicAttackDirect2H01 - Load"));
            Act("Druid_ChargeCast", K("Combat/Spellcasting/MagicAttacks/Directional/HumanM@MagicAttackDirect2H01 - Cast"));
            Act("Druid_OrbAim", K("Combat/Spellcasting/MagicAttacks/Directional/HumanM@MagicAttackDirect2H02 - Load"));
            Act("Druid_OrbCast", K("Combat/Spellcasting/MagicAttacks/Directional/HumanM@MagicAttackDirect2H02 - Cast"));
            // Wrath of the Grove (druid special): the kick's knee-up chamber, held, then cut into the heavy landing = a stomp
            Act("Druid_StompRaise_R", K("Combat/Unarmed/HumanM@AttackKick02_R"));
            Act("Druid_StompRaise_L", K("Combat/Unarmed/HumanM@AttackKick02_L"));
            // Fairy Mask: twin daggers on the pack's 1H right / left and dual-wield clips (light alternates the hands),
            // the boomerang set for the Moon Chakram, and the sky-calling cast for Moonfall
            Act("Fairy_L1", K("Combat/1H/HumanM@Attack1H02_R"));
            Act("Fairy_L2", K("Combat/1H/HumanM@Attack1H02_L"));
            Act("Fairy_L3", K("Combat/1H/HumanM@Attack1H04_R"));
            Act("Fairy_L4", K("Combat/1H/HumanM@Attack1H04_L"));
            Act("Fairy_L5", K("Combat/1H/HumanM@AttackDW01"));
            Act("Fairy_H1", K("Combat/1H/HumanM@AttackDW02"));
            Act("Fairy_H2", K("Combat/1H/HumanM@Attack1H05_L"));
            Act("Fairy_H3", K("Combat/1H/HumanM@AttackDW01"));
            Act("Fairy_Run", K("Combat/1H/HumanM@Attack1H01_R"));
            Act("Fairy_Dodge", K("Combat/1H/HumanM@Attack1H01_L"));
            Act("Fairy_ChakramHold", K("Combat/Thrown/Boomerang/HumanM@ThrowBoomerang01_R - Hold"));
            Act("Fairy_ChakramThrow", K("Combat/Thrown/Boomerang/HumanM@ThrowBoomerang01_R"));
            Act("Fairy_ChakramWait", K("Combat/Thrown/Boomerang/HumanM@ThrowBoomerang01_R - Wait"));
            Act("Fairy_ChakramCatch", K("Combat/Thrown/Boomerang/HumanM@ThrowBoomerang01_R - Catch"));
            Act("Fairy_CallLoad", K("Combat/Spellcasting/MagicAttacks/Call/HumanM@MagicAttackCall1H01_R - Load"));
            Act("Fairy_CallCast", K("Combat/Spellcasting/MagicAttacks/Call/HumanM@MagicAttackCall1H01_R - Cast"));
            Act("MaskDon", K("Misc/Drink/HumanM@Drink01_R"));
            Act("MaskScream", K("Social/Emotions/HumanM@Pain01"));
            Act("MaskRoar", K("Social/Emotions/HumanM@Angry01"));

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

            // ---------------- Two-hand hold (arms only): the 2H guard pose on the arms while the legs walk,
            // run or jump. Weighted by code for two-handed forms; TwoHandGrip then pins the left fist.
            var armsLayer = new AnimatorControllerLayer
            {
                name = "TwoHandArms",
                defaultWeight = 0f,
                avatarMask = ArmsMask(),
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = new AnimatorStateMachine { name = "TwoHandArms", hideFlags = HideFlags.HideInHierarchy }
            };
            AssetDatabase.AddObjectToAsset(armsLayer.stateMachine, ac);
            ac.AddLayer(armsLayer);
            var asm = ac.layers[3].stateMachine;
            var hold2H = asm.AddState("Hold2H", new Vector3(300, 0));
            hold2H.motion = K("Combat/2H/HumanM@CombatIdle2H01");
            asm.defaultState = hold2H;

            // ---------------- Drink (upper body, on top of everything): the health potion
            AddDrinkLayer(ac);

            // IK pass on the base layer: the bow places both hands on the aim line (BowRig.OnAnimatorIK)
            var layers = ac.layers;
            layers[0].iKPass = true;
            ac.layers = layers;

            EditorUtility.SetDirty(ac);
            BuildCrocOverride(ac);
            BuildMoveset();
            AssetDatabase.SaveAssets();
            return $"Animator built: {ac.layers.Length} layers, {sm.states.Length} states";
        }

        /// <summary>
        /// Health potion: the pack's left-handed drink on the upper body (the right hand keeps the weapon) while the legs
        /// keep the locomotion of the base layer. Last layer, so it wins over the guard and the two-hand hold. Weighted by
        /// PlayerController; "DrinkSpeed" is its playback speed.
        /// </summary>
        static void AddDrinkLayer(AnimatorController ac)
        {
            if (ac.layers.Any(l => l.name == "Drink")) return;
            if (!ac.parameters.Any(p => p.name == "DrinkSpeed"))
            {
                ac.AddParameter("DrinkSpeed", AnimatorControllerParameterType.Float);
                ac.parameters = ac.parameters.Select(p => { if (p.name == "DrinkSpeed") p.defaultFloat = 1f; return p; }).ToArray();
            }
            var layer = new AnimatorControllerLayer
            {
                name = "Drink",
                defaultWeight = 0f,
                // spine, head and the left arm: the right arm keeps the weapon pose from the base layer
                avatarMask = Mask("M_DrinkUpper", p =>
                    p == AvatarMaskBodyPart.Body || p == AvatarMaskBodyPart.Head ||
                    p == AvatarMaskBodyPart.LeftArm || p == AvatarMaskBodyPart.LeftFingers),
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = new AnimatorStateMachine { name = "Drink", hideFlags = HideFlags.HideInHierarchy }
            };
            AssetDatabase.AddObjectToAsset(layer.stateMachine, ac);
            ac.AddLayer(layer);
            var dsm = ac.layers[ac.layers.Length - 1].stateMachine;
            var empty = dsm.AddState("Empty", new Vector3(300, 0));
            dsm.defaultState = empty;
            var drink = dsm.AddState("Drink", new Vector3(300, 80));
            drink.motion = K("Misc/Drink/HumanM@Drink01_L");
            drink.speedParameter = "DrinkSpeed";
            drink.speedParameterActive = true;
            EditorUtility.SetDirty(ac);
        }

        /// <summary>Adds the drink layer to the existing controller (no rebuild, so references to it survive).</summary>
        [MenuItem("Elder Tree/Dev/Add Drink Layer To Animator")]
        public static string EnsureDrinkLayer()
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ac == null) return "No controller";
            bool had = ac.layers.Any(l => l.name == "Drink");
            AddDrinkLayer(ac);
            AssetDatabase.SaveAssets();
            return had ? "Drink layer already there" : $"Drink layer added: {ac.layers.Length} layers";
        }

        /// <summary>
        /// The alligator holds its hammer in both hands even at rest: same controller and state names,
        /// with the pack's 2H idle under locomotion and the 2H parry for the guard layer.
        /// </summary>
        static void BuildCrocOverride(AnimatorController ac)
        {
            var oc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(CrocControllerPath);
            if (oc == null)
            {
                oc = new AnimatorOverrideController(ac);
                AssetDatabase.CreateAsset(oc, CrocControllerPath);
            }
            oc.runtimeAnimatorController = ac;
            var swaps = new Dictionary<string, AnimationClip>
            {
                { "HumanM@CombatIdle1H01", K("Combat/2H/HumanM@CombatIdle2H01") },
                { "HumanM@Parry1H01_R - Loop", K("Combat/2H/HumanM@Parry2H01 - Loop") },
                { "HumanM@Parry1H01_R - Hit", K("Combat/2H/HumanM@Parry2H01 - Hit") },
            };
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            oc.GetOverrides(pairs);
            for (int i = 0; i < pairs.Count; i++)
                pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key,
                    swaps.TryGetValue(pairs[i].Key.name, out var to) ? to : null);
            oc.ApplyOverrides(pairs);
            EditorUtility.SetDirty(oc);
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
            ms.skill = new AttackData { id = "Skill", state = "Skill", damage = 110, poiseDamage = 70, staminaCost = 20,
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
