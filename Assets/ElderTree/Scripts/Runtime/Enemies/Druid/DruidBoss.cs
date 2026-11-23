using System.Collections;
using System.Collections.Generic;
using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.Combat;
using ElderTree.FX;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree.Enemies
{
    /// <summary>
    /// Druid: a giant dryad (Brr Brr Patapim's nose in a bark face, trunk legs, feet of splayed roots), its body
    /// shrouded in leaves that never stop moving. A slow ground giant; every attack is told by the body:
    ///  - Stomp: a root foot rises high over you (it tracks you for most of the tell), then slams down: a crushing
    ///    hit under the foot and a shockwave along the ground around it (jump it or be out of range). 1-2 stomps,
    ///    three alternating in phase 2.
    ///  - Kick: it draws a leg back, then punts through you and launches you across the arena (two in phase 2).
    ///  - Crouch and grab: it sinks low and spreads its arms, then sweeps both hands in. Roll through it. Caught,
    ///    it lifts you to its face, crushes you in its fist and hurls you into the dirt. A miss leaves its hands in
    ///    the dirt and its head low.
    /// Its weakness is its footing: blows to the root feet wear it down (the roots crack and glow, the knees buckle,
    /// it starts to sway). Enough of them and it topples onto its back, face to the sky, open to criticals on the face.
    /// Animation is procedural on rigid segments: stepping feet with 3D two-bone IK (legs and arms), and every leaf
    /// clump sways on its own, harder when it moves or is struck.
    /// </summary>
    public class DruidBoss : BossBase
    {
        public enum Mode { Dormant, Stalking, Attacking, Holding, Recovering, Toppled, Dead }
        public enum Part { Body, FootL, FootR, Head }

        public string displayName = "Druid";

        [Header("Rig (arrays: 0 = left, 1 = right)")]
        [Tooltip("The hips. Crouch, pitch, bank and the fall happen here.")]
        public Transform pivot;
        public Transform torso, head;
        public Transform[] upperArm = new Transform[2], forearm = new Transform[2], hand = new Transform[2];
        [Tooltip("Where a grabbed player sits in each hand.")]
        public Transform[] palm = new Transform[2];
        public Transform[] thigh = new Transform[2], shin = new Transform[2], foot = new Transform[2];
        [Tooltip("Leaf clumps that sway (each parented to the part it grows on).")]
        public Transform[] leaves;
        [Tooltip("Extreme points of the body (built by the editor): the lowest stays above the ground when it lies down.")]
        public HullPoint[] hull;
        public Transform lockPoint;
        public Renderer[] flashRenderers;
        public Renderer[] footRenderers = new Renderer[2];
        [Tooltip("Solid (non-trigger) colliders round the shins, so you can't walk through its legs.")]
        public Collider[] legColliders = new Collider[2];

        [Header("Arena")]
        public Vector3 arenaCenter;
        public float arenaRadius = 15f;

        [Header("Stats")]
        public float maxHealth = 3200f;
        [Range(0f, 1f)] public float phaseTwoAt = 0.5f;

        [Header("Footing (blows to the root feet topple it)")]
        public float footingMax = 100f;
        [Tooltip("Phase two needs this much more.")]
        public float footingPhaseTwoBonus = 20f;
        public float footingRegenDelay = 4f;
        public float footingRegen = 14f;
        [Tooltip("HP damage scale for blows to the roots (they're tough, but they're how you win).")]
        public float footDamageScale = 0.85f;
        [Tooltip("Extra multiplier on face hits while toppled (the weapon's critical multiplier applies too).")]
        public float faceMultiplier = 1.6f;
        public float toppleTime = 5.5f;

        [Header("Movement")]
        public float walkSpeed = 2.4f;
        public float accel = 6f;
        public float turnRate = 75f;

        [Header("Attacks")]
        public float stompDamage = 170f;
        public float stompRadius = 2.4f;
        public float shockDamage = 70f;
        public float shockRadius = 5.4f;
        public float kickDamage = 140f;
        public float squeezeDamage = 40f;
        public float throwDamage = 110f;
        public float grabRadius = 1.7f;

        [Header("Body")]
        [Tooltip("How far the hips drop at full crouch.")]
        public float crouchDrop = 2.6f;

        [System.Serializable]
        public struct HullPoint { public Transform bone; public Vector3 local; }

        public Mode State { get; private set; } = Mode.Dormant;
        public override bool IsAlive => State != Mode.Dead && State != Mode.Dormant;
        public override bool IsStaggered => false;               // only the face goes critical (DruidPart)
        public override bool PhaseTwo => phaseTwo;
        public override float Health => health;
        /// <summary>Footing left before it topples (shown where other bosses show poise).</summary>
        public override float Poise => Mathf.Max(0f, FootingCap - footing);
        public override string CurrentMove => currentMove;
        public override string DisplayName => displayName;
        public override float MaxHealth => maxHealth;
        public override string StateLabel => State.ToString();
        public override bool IsDead => State == Mode.Dead;
        public override Vector3 ArenaCenter => arenaCenter;
        public bool Toppled => State == Mode.Toppled;
        public float Footing01 => Mathf.Clamp01(footing / FootingCap);
        public bool PartIsCritical(Part p) => p == Part.Head && State == Mode.Toppled;

        bool phaseTwo;
        float health, footing, lastFootHit = -99f;
        float FootingCap => footingMax + (phaseTwo ? footingPhaseTwoBonus : 0f);
        string currentMove = "-";
        string forcedMove;
        Coroutine brain;
        float SpeedMul => phaseTwo ? 1.2f : 1f;
        float stompCd, kickCd, grabCd;
        GameObject lastHitSource;
        float lastHitTime = -99f;
        int lastHitFrame = -1;

        PlayerController Player => PlayerController.Instance;
        Vector3 PlayerPos => Player != null ? Player.transform.position : arenaCenter;
        bool PlayerAlive => Player != null && !Player.IsDead;

        // ------------------------------------------------------------------ kinematics
        float yaw, groundY;
        /// <summary>How far the felled body has sunk into the earth (0 while alive).</summary>
        float sinkDepth;
        Vector3 vel;

        // pose targets (set by moves) and smoothed values (applied in LateUpdate)
        float tCrouch, tPitch, tBank, tLift, tShiftX, tShiftZ, tTorsoPitch, tTorsoYaw, tTorsoRoll, tHeadPitch, tHeadYaw, sharp = 5f;
        float crouch, pitch, bank, lift, shiftX, shiftZ, torsoPitch, torsoYaw, torsoRoll, headPitch, headYaw;
        float look = 1f, lookYaw, lookPitch;
        readonly Vector3[] tHand = new Vector3[2], handOff = new Vector3[2];
        readonly float[] tCurl = new float[2], curl = new float[2];
        float handSharp = 7f;

        // legs: planted feet in the world, one step at a time; moves can take a foot over
        readonly Vector3[] plant = new Vector3[2], stepFrom = new Vector3[2], stepTo = new Vector3[2], legTarget = new Vector3[2];
        readonly float[] plantYaw = new float[2], stepYawFrom = new float[2], stepT = new float[2], stepDur = new float[2], stepHeight = new float[2];
        readonly bool[] stepping = new bool[2], legOverride = new bool[2];
        readonly float[] footPitch = new float[2], footFollow = new float[2], tFootFollow = new float[2];
        bool gait = true;
        float settleT, stepGate;

        // feel
        float agitation;                         // leaves: 0 calm, 1 thrashing
        readonly float[] buckle = new float[2];  // a foot was struck: that knee gives a little
        readonly float[] footGlow = new float[2];
        float flash, eyeGlow = 1f, sway;
        bool keepOffGround;
        int holdHand = -1;

        // rig rest data (root-relative)
        bool rigReady;
        Vector3 pivotRest;
        Quaternion torsoRel, headRel;
        readonly Quaternion[] upRel = new Quaternion[2], foRel = new Quaternion[2], thRel = new Quaternion[2], shRel = new Quaternion[2], ftRel = new Quaternion[2];
        readonly Vector3[] upDir = new Vector3[2], foDir = new Vector3[2], thDir = new Vector3[2], shDir = new Vector3[2];
        readonly float[] armL1 = new float[2], armL2 = new float[2], legL1 = new float[2], legL2 = new float[2];
        readonly Vector3[] handRest = new Vector3[2];    // wrist offset from the shoulder, root frame
        readonly Vector3[] ankleRest = new Vector3[2];   // ankle in root space
        readonly Quaternion[] handLocalRest = new Quaternion[2];
        readonly Vector3[] ankleNow = new Vector3[2];
        float ankleH = 0.95f;
        Vector3 lockRest;
        Quaternion[] leafRest;
        Vector3[] leafAxis;
        float[] leafPhase, leafFreq;

        // materials
        MaterialPropertyBlock mpb;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        Material[][] rendererMats;
        Color eyeBase;
        static readonly Color Ember = new Color(0.78f, 0.38f, 0.1f);

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            health = maxHealth;
            CaptureRig();
            CaptureMaterials();
        }

        void CaptureRig()
        {
            if (pivot == null || torso == null || head == null) return;
            Quaternion inv = Quaternion.Inverse(transform.rotation);
            pivotRest = pivot.localPosition;
            headRenderer = head.GetComponent<Renderer>();
            torsoRel = inv * torso.rotation;
            headRel = inv * head.rotation;
            for (int i = 0; i < 2; i++)
            {
                if (!upperArm[i] || !forearm[i] || !hand[i] || !thigh[i] || !shin[i] || !foot[i]) return;
                upRel[i] = inv * upperArm[i].rotation;
                foRel[i] = inv * forearm[i].rotation;
                thRel[i] = inv * thigh[i].rotation;
                shRel[i] = inv * shin[i].rotation;
                ftRel[i] = inv * foot[i].rotation;
                upDir[i] = inv * (forearm[i].position - upperArm[i].position);
                foDir[i] = inv * (hand[i].position - forearm[i].position);
                thDir[i] = inv * (shin[i].position - thigh[i].position);
                shDir[i] = inv * (foot[i].position - shin[i].position);
                armL1[i] = upDir[i].magnitude; armL2[i] = foDir[i].magnitude;
                legL1[i] = thDir[i].magnitude; legL2[i] = shDir[i].magnitude;
                upDir[i].Normalize(); foDir[i].Normalize(); thDir[i].Normalize(); shDir[i].Normalize();
                handRest[i] = inv * (hand[i].position - upperArm[i].position);
                handLocalRest[i] = hand[i].localRotation;
                ankleRest[i] = transform.InverseTransformPoint(foot[i].position);
            }
            ankleH = (ankleRest[0].y + ankleRest[1].y) * 0.5f;
            if (lockPoint) lockRest = lockPoint.localPosition;
            int n = leaves != null ? leaves.Length : 0;
            leafRest = new Quaternion[n]; leafAxis = new Vector3[n]; leafPhase = new float[n]; leafFreq = new float[n];
            var rnd = new System.Random(7);
            for (int i = 0; i < n; i++)
            {
                if (!leaves[i]) continue;
                leafRest[i] = leaves[i].localRotation;
                leafAxis[i] = new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f).normalized;
                leafPhase[i] = (float)rnd.NextDouble() * 6.28f;
                leafFreq[i] = 1.6f + (float)rnd.NextDouble() * 1.4f;
            }
            rigReady = true;
        }

        void CaptureMaterials()
        {
            if (flashRenderers == null) return;
            rendererMats = new Material[flashRenderers.Length][];
            for (int r = 0; r < flashRenderers.Length; r++)
            {
                rendererMats[r] = flashRenderers[r] ? flashRenderers[r].sharedMaterials : new Material[0];
                foreach (var m in rendererMats[r])
                    if (m != null && m.name.StartsWith("M_DruidEye") && m.HasProperty(EmissionId)) eyeBase = m.GetColor(EmissionId);
            }
        }

        // ================================================================== encounter API
        public override void BeginFight()
        {
            StopAllCoroutines();
            ReleaseHeld(false);
            health = maxHealth;
            footing = 0f;
            phaseTwo = false;
            stompCd = 0f; kickCd = 1.5f; grabCd = 5f;
            forcedMove = null;
            gameObject.SetActive(true);
            groundY = Blast.Floor(arenaCenter).y;
            sinkDepth = 0f;
            State = Mode.Stalking;
            brain = StartCoroutine(Brain(true));
        }

        public override void ResetBoss()
        {
            StopAllCoroutines();
            ReleaseHeld(false);
            State = Mode.Dormant;
            health = maxHealth;
            footing = 0f;
            sinkDepth = 0f;
            gameObject.SetActive(false);
        }

        public override void ForceNextMove(string move)
        {
            if (move == "Topple") { if (IsAlive && State != Mode.Toppled) Topple(); return; }
            forcedMove = move;
        }

        void ResetPose()
        {
            tCrouch = crouch = 0.08f;
            tPitch = pitch = tBank = bank = tLift = lift = tShiftX = shiftX = tShiftZ = shiftZ = 0f;
            tTorsoPitch = torsoPitch = tTorsoYaw = torsoYaw = tTorsoRoll = torsoRoll = 0f;
            tHeadPitch = headPitch = tHeadYaw = headYaw = 0f;
            sharp = 5f; handSharp = 7f; look = 1f;
            for (int i = 0; i < 2; i++)
            {
                tHand[i] = handOff[i] = handRest[i];
                tCurl[i] = curl[i] = 0f;
                legOverride[i] = stepping[i] = false;
                footPitch[i] = footFollow[i] = tFootFollow[i] = 0f;
                buckle[i] = footGlow[i] = 0f;
            }
            vel = Vector3.zero;
            gait = true;
            keepOffGround = false;
            agitation = 0f;
            eyeGlow = 1f;
            holdHand = -1;
        }

        void ResetPlants()
        {
            for (int i = 0; i < 2; i++)
            {
                plant[i] = Ground(transform.TransformPoint(new Vector3(ankleRest[i].x, 0f, ankleRest[i].z)));
                plantYaw[i] = yaw;
                stepping[i] = legOverride[i] = false;
            }
        }

        // ================================================================== brain
        IEnumerator Brain(bool intro)
        {
            if (intro) yield return Intro();
            string last = "";
            while (true)
            {
                if (!phaseTwo && health <= maxHealth * phaseTwoAt) yield return PhaseShift();
                if (forcedMove == null)
                    yield return Stalk(Random.Range(phaseTwo ? 0.4f : 0.7f, phaseTwo ? 1.1f : 1.7f));

                string move = forcedMove ?? PickMove(last);
                forcedMove = null;
                last = move;
                currentMove = move;
                State = Mode.Attacking;
                switch (move)
                {
                    case "Stomp": yield return Stomp(); break;
                    case "Kick": yield return Kick(); break;
                    case "Grab": yield return Grab(); break;
                    case "Roar": yield return Roar(1.6f); break;
                    default: yield return Stomp(); break;
                }
                EndMove();
            }
        }

        void EndMove()
        {
            if (State != Mode.Dead) State = Mode.Stalking;
            currentMove = "-";
            for (int i = 0; i < 2; i++)
            {
                if (legOverride[i]) { plant[i] = Ground(legTarget[i]); plantYaw[i] = yaw; legOverride[i] = false; }
                tFootFollow[i] = 0f;
                footPitch[i] = 0f;
            }
            gait = true;
            RestPose();
        }

        /// <summary>Standing, arms hanging, looking at you.</summary>
        void RestPose()
        {
            tCrouch = 0.08f; tPitch = tBank = tLift = tShiftX = tShiftZ = 0f;
            tTorsoPitch = tTorsoYaw = tTorsoRoll = tHeadPitch = tHeadYaw = 0f;
            for (int i = 0; i < 2; i++) { tHand[i] = handRest[i]; tCurl[i] = 0f; }
            sharp = 5f; handSharp = 7f; look = 1f;
        }

        string PickMove(string last)
        {
            if (!PlayerAlive) return "Stomp";
            Vector3 r = Rel;
            float d = FlatDist;
            bool front = r.z > 0.8f && Mathf.Abs(r.x) < r.z * 1.3f + 1.2f;
            var opts = new List<(string, float)>
            {
                ("Stomp", stompCd <= 0f && d < 7f ? 3f + (d < 3.5f ? 1.5f : 0f) + Footing01 * 2f : 0f),   // someone at its roots gets stamped on
                ("Kick", kickCd <= 0f && front && d > 1.2f && d < 6f ? 3.2f : 0f),
                ("Grab", grabCd <= 0f && front && d > 2.4f && d < 8.5f ? 2.6f + (d > 5f ? 1f : 0f) : 0f),
            };
            float total = 0f;
            for (int i = 0; i < opts.Count; i++)
            {
                if (opts[i].Item1 == last) opts[i] = (opts[i].Item1, opts[i].Item2 * 0.35f);
                total += opts[i].Item2;
            }
            if (total <= 0f) return d < 7f ? "Stomp" : "Grab";
            float pick = Random.value * total;
            foreach (var (n, w) in opts) { if ((pick -= w) <= 0f) return n; }
            return "Stomp";
        }

        // ================================================================== movement primitives
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        Vector3 Heading => Quaternion.Euler(0, yaw, 0) * Vector3.forward;
        Vector3 Rel => transform.InverseTransformPoint(PlayerPos);
        float FlatDist => Flat(PlayerPos - transform.position).magnitude;
        Vector3 Ground(Vector3 p) { p.y = groundY; return p; }
        float Side(int leg) => leg == 0 ? -1f : 1f;

        void Face(Vector3 dir, float rate, float dt)
        {
            dir = Flat(dir);
            if (dir.sqrMagnitude > 1e-4f)
                yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, rate * dt);
        }

        void Locomote(Vector3 desired, float dt, float accelMul = 1f)
        {
            vel = Vector3.MoveTowards(vel, Flat(desired), accel * accelMul * dt);
            Place(transform.position + vel * dt);
        }

        void Brake(float dt, float k = 1.5f) => Locomote(Vector3.zero, dt, k);

        /// <summary>The arena, stretched to follow a player who slipped out through a gap in the walls.</summary>
        float Limit => Mathf.Clamp(Flat(PlayerPos - arenaCenter).magnitude + 3f, arenaRadius, arenaRadius * 2.5f);

        void Place(Vector3 p)
        {
            Vector3 rel = Flat(p - arenaCenter);
            float limit = Limit;
            if (rel.magnitude > limit)
            {
                p = arenaCenter + rel.normalized * limit;
                vel -= Vector3.Project(vel, rel.normalized) * (Vector3.Dot(vel, rel) > 0f ? 1f : 0f);
            }
            p.y = groundY - sinkDepth;
            transform.position = p;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        IEnumerator Hold(float time, System.Action<float, float> each = null)
        {
            float t = 0f;
            while (t < time)
            {
                float dt = Time.deltaTime;
                t += dt;
                each?.Invoke(Mathf.Clamp01(t / time), dt);
                Place(transform.position);
                yield return null;
            }
        }

        /// <summary>Ground point under hip <paramref name="leg"/> (where a straight leg would stand).</summary>
        Vector3 HipGround(int leg) => Ground(transform.TransformPoint(new Vector3(ankleRest[leg].x, 0f, ankleRest[leg].z)));
        Vector3 AnklePos(int leg) => foot[leg] ? foot[leg].position : HipGround(leg) + Vector3.up * ankleH;
        int LegToward(Vector3 p) => transform.InverseTransformPoint(p).x < 0f ? 0 : 1;

        /// <summary>A wrist target given in the world, as an offset from the shoulder in the torso frame.</summary>
        Vector3 HandLocal(int i, Vector3 world) => Quaternion.Inverse(TorsoFrame) * (world - upperArm[i].position);
        Quaternion TorsoFrame => pivot.rotation * Quaternion.Euler(torsoPitch, torsoYaw, torsoRoll);

        /// <summary>Arm offset from rest in the torso frame (x out to its side, y up, z forward), mirrored per side.</summary>
        Vector3 Arm(int i, float outward, float up, float fwd) => handRest[i] + new Vector3(Side(i) * outward, up, fwd);

        void Look(float w) => look = w;

        // ================================================================== hits on the player
        bool HitPlayer(float damage, ImpactWeight weight, Vector3 push, float poiseDmg)
        {
            if (!PlayerAlive) return false;
            push.y = 0f;
            return Player.TakeHit(new DamageInfo
            {
                damage = damage, poiseDamage = poiseDmg, point = PlayerPos + Vector3.up,
                direction = push.sqrMagnitude > 1e-3f ? push.normalized : transform.forward,
                weight = weight, source = gameObject
            });
        }

        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
            return Vector3.Distance(p, a + ab * t);
        }

        // ================================================================== sounds + fx
        void Groan(float pitchScale = 1f, float vol = 1f) =>
            AudioManager.Play(Sfx.WoodGroan, head ? head.position : transform.position + Vector3.up * 8f, vol, pitchScale * Random.Range(0.95f, 1.05f));

        void Thud(Vector3 at, float k)
        {
            FXManager.Spawn(FXType.Dust, at, Quaternion.identity, 2.2f * k);
            FXManager.Spawn(FXType.SlamDebris, at, Quaternion.identity, 1.1f * k);
            AudioManager.Play(Sfx.Slam, at, Mathf.Min(1f, 0.85f * k), 0.62f);
            float d = Flat(PlayerPos - at).magnitude;
            float near = Mathf.Clamp01(1.2f - d / 18f);
            CameraDirector.Instance?.Shake(0.55f * k * near, Vector3.down);
            Player?.GetComponent<InputHandling.PlayerInputReader>()?.Rumble(0.45f * k * near, 0.3f * k * near, 0.25f);
        }

        void Footfall(int leg)
        {
            Vector3 at = plant[leg];
            AudioManager.Play(Sfx.Footstep, at, 1f, 0.5f);
            AudioManager.Play(Sfx.Slam, at, 0.28f, Random.Range(0.55f, 0.65f));
            FXManager.Spawn(FXType.DustSmall, at + Heading * 0.6f, Quaternion.LookRotation(Vector3.up), 1.8f);
            float d = Flat(PlayerPos - at).magnitude;
            if (d < 14f) CameraDirector.Instance?.Shake(0.13f * (1f - d / 14f), Vector3.down);
            agitation = Mathf.Max(agitation, 0.3f);
        }

        /// <summary>Leaves torn loose from <paramref name="n"/> random clumps.</summary>
        void Shed(int n, float scale = 1.3f)
        {
            if (leaves == null || leaves.Length == 0) return;
            for (int k = 0; k < n; k++)
            {
                var l = leaves[Random.Range(0, leaves.Length)];
                if (l) FXManager.Spawn(FXType.LeafBurst, l.position, Quaternion.LookRotation(Vector3.up), scale);
            }
        }

        // ================================================================== stalk
        IEnumerator Stalk(float duration)
        {
            State = Mode.Stalking;
            currentMove = "Stalk";
            RestPose();
            float t = 0f;
            while (t < duration)
            {
                float dt = Time.deltaTime;
                t += dt;
                Vector3 to = Flat(PlayerPos - transform.position);
                float d = to.magnitude;
                Vector3 want = Vector3.zero;
                if (PlayerAlive && d > 7.5f) want = to / d * walkSpeed * SpeedMul;
                else if (PlayerAlive && d > 5f) want = to / d * walkSpeed * 0.45f * SpeedMul;
                Locomote(want, dt);
                Face(d > 0.01f ? to : Heading, turnRate * SpeedMul, dt);
                // close and in reach, facing you: don't wait out the whole stalk
                if (t > 0.35f && PlayerAlive && d < 6.5f && Mathf.Abs(Vector3.SignedAngle(Heading, to, Vector3.up)) < 35f && Random.value < dt * 1.5f) break;
                yield return null;
            }
        }

        // ================================================================== intro / roar / phase two
        IEnumerator Intro()
        {
            // a still tree at the edge of the arena, arms raised like boughs: it tears its roots out and roars
            Vector3 dir = Flat(arenaCenter - PlayerPos);
            if (dir.sqrMagnitude < 1f) dir = Vector3.forward;
            dir.Normalize();
            Vector3 start = arenaCenter + dir * (arenaRadius - 3.5f);
            yaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;
            Place(start);
            ResetPose();
            ResetPlants();
            for (int i = 0; i < 2; i++)
            {
                legOverride[i] = true;
                legTarget[i] = plant[i] + Vector3.up * (ankleH - 0.75f);     // sunk in the dirt
                tHand[i] = handOff[i] = Arm(i, 1.0f, 3.6f, 0.4f);             // boughs raised
                tCurl[i] = curl[i] = -20f;
            }
            tCrouch = crouch = 0.24f;
            tHeadPitch = headPitch = 18f;
            look = 0f;
            eyeGlow = 0f;
            gait = false;
            currentMove = "Dormant";
            yield return Hold(1.1f, (k, dt) => { eyeGlow = k > 0.5f ? Mathf.Clamp01((k - 0.5f) * 2f) : 0f; });
            Groan(0.85f, 0.9f);
            look = 1f;
            tHeadPitch = 0f;
            currentMove = "Uproot";
            for (int i = 0; i < 2; i++)
            {
                int leg = i;
                Vector3 sunk = legTarget[leg];
                bool torn = false;
                yield return Hold(0.75f, (k, dt) =>
                {
                    float up = k < 0.55f ? Ease.OutCubic(k / 0.55f) : 1f - Ease.InCubic((k - 0.55f) / 0.45f);
                    legTarget[leg] = Vector3.Lerp(sunk, plant[leg] + Vector3.up * ankleH, Mathf.Clamp01(k / 0.55f)) + Vector3.up * up * 1.1f;
                    tBank = -Side(leg) * 4f * up;
                    agitation = 0.6f;
                    if (!torn && k > 0.12f)
                    {
                        torn = true;
                        FXManager.Spawn(FXType.SlamDebris, plant[leg], Quaternion.identity, 1.3f);
                        FXManager.Spawn(FXType.Dust, plant[leg], Quaternion.identity, 2f);
                        AudioManager.Play(Sfx.RootSnap, plant[leg], 1f, 0.7f);
                        AudioManager.Play(Sfx.Burrow, plant[leg], 0.7f, 0.7f);
                    }
                });
                legOverride[leg] = false;
                Footfall(leg);
            }
            gait = true;
            yield return Roar(1.9f);
        }

        IEnumerator Roar(float time)
        {
            currentMove = "Roar";
            Groan(0.75f, 1f);
            AudioManager.Play(Sfx.DeepRumble, transform.position, 0.8f, 1f);
            CameraDirector.Instance?.Shake(0.4f, Vector3.down);
            float shedT = 0f;
            sharp = 7f;
            yield return Hold(time, (k, dt) =>
            {
                Brake(dt);
                float up = k < 0.85f ? Ease.OutCubic(Mathf.Clamp01(k / 0.25f)) : 1f - (k - 0.85f) / 0.15f;
                tHeadPitch = -32f * up;
                tTorsoPitch = -12f * up;
                tCrouch = 0.08f + 0.12f * up;
                for (int i = 0; i < 2; i++) { tHand[i] = Vector3.Lerp(handRest[i], Arm(i, 2.6f, 3.4f, 0.6f), up); tCurl[i] = -35f * up; }
                look = 1f - up;
                agitation = Mathf.Max(agitation, up);
                if ((shedT -= dt) <= 0f && up > 0.5f) { shedT = 0.12f; Shed(1); }
            });
            RestPose();
        }

        IEnumerator PhaseShift()
        {
            phaseTwo = true;
            State = Mode.Attacking;
            Shed(10, 1.7f);
            yield return Roar(1.6f);
        }

        // ================================================================== stomp
        Vector3 StompAim(int leg)
        {
            Vector3 lead = Player != null ? Player.motor.PlanarVelocity * 0.18f : Vector3.zero;
            Vector3 hip = HipGround(leg);
            Vector3 d = Flat(PlayerPos + lead - hip);
            float m = d.magnitude;
            if (m > 3.1f) d = d / m * 3.1f;
            return Ground(hip + d);
        }

        IEnumerator Stomp()
        {
            stompCd = phaseTwo ? 1.5f : 2.5f;
            int count = phaseTwo ? 3 : (Random.value < 0.35f ? 2 : 1);
            int leg = LegToward(PlayerPos);
            gait = false;
            for (int n = 0; n < count && PlayerAlive; n++)
            {
                int l = leg;
                currentMove = "Stomp";
                legOverride[l] = true;
                Vector3 from = AnklePos(l);
                legTarget[l] = from;
                Vector3 aim = StompAim(l);
                float tell = (n == 0 ? 0.95f : 0.6f) / SpeedMul;
                Groan(1.15f, 0.55f);
                sharp = 6f;
                yield return Hold(tell, (k, dt) =>
                {
                    Brake(dt);
                    if (k < 0.72f)
                    {
                        aim = Vector3.Lerp(aim, StompAim(l), Ease.Damp(6f, dt));
                        Face(PlayerPos - transform.position, turnRate * 0.6f, dt);
                    }
                    float up = Ease.OutCubic(Mathf.Clamp01(k / 0.55f));
                    Vector3 raised = aim + Vector3.up * (ankleH + 2.7f) - Flat(aim - HipGround(l)) * 0.3f;
                    legTarget[l] = Vector3.Lerp(from, raised, up) + Vector3.up * Mathf.Sin(up * Mathf.PI) * 0.4f;
                    footPitch[l] = 22f * up;
                    tCrouch = 0.18f; tPitch = -6f * up; tBank = -Side(l) * 5f * up;
                    tShiftX = -Side(l) * 0.35f * up;                                  // weight onto the standing leg
                    for (int i = 0; i < 2; i++) { tHand[i] = Arm(i, 1.7f * up, 1.2f * up, 0.3f); tCurl[i] = -20f * up; }
                    agitation = Mathf.Max(agitation, 0.35f);
                });
                Vector3 top = legTarget[l];
                Vector3 land = aim;
                AudioManager.Play(Sfx.WhooshHeavy, top, 0.9f, 0.55f);
                sharp = 18f;
                yield return Hold(0.14f, (k, dt) =>
                {
                    legTarget[l] = Vector3.Lerp(top, land + Vector3.up * ankleH, Ease.InCubic(k));
                    footPitch[l] = 22f * (1f - k);
                    tCrouch = 0.42f; tPitch = 5f; tBank = 0f;
                });
                legOverride[l] = false;
                plant[l] = land;
                plantYaw[l] = yaw;
                footPitch[l] = 0f;
                StompImpact(land);
                sharp = 5f;
                yield return Hold(n == count - 1 ? 0.55f : 0.22f, (k, dt) =>
                {
                    tCrouch = Mathf.Lerp(0.42f, 0.2f, k); tPitch = 3f * (1f - k); tShiftX = 0f;
                    for (int i = 0; i < 2; i++) { tHand[i] = Arm(i, 0.8f, 0.4f, 0.4f); tCurl[i] = 0f; }
                });
                leg = 1 - leg;
            }
            gait = true;
            yield return Recover(0.6f / SpeedMul, "Planted");
        }

        void StompImpact(Vector3 p)
        {
            FXManager.Spawn(FXType.SlamRing, p + Vector3.up * 0.05f, Quaternion.identity, shockRadius * 0.62f);
            FXManager.Spawn(FXType.SlamRing, p + Vector3.up * 0.07f, Quaternion.identity, stompRadius * 0.7f);
            FXManager.Spawn(FXType.Dust, p, Quaternion.identity, 3f);
            FXManager.Spawn(FXType.SlamDebris, p, Quaternion.identity, 1.6f);
            AudioManager.Play(Sfx.Slam, p, 1f, 0.6f);
            AudioManager.Play(Sfx.DeepRumble, p, 0.45f, 1.2f);
            CameraDirector.Instance?.Shake(0.75f, Vector3.down);
            Player?.GetComponent<InputHandling.PlayerInputReader>()?.Rumble(0.7f, 0.5f, 0.3f);
            agitation = 1f;
            Shed(3);
            if (!PlayerAlive) return;
            Vector3 d = Flat(PlayerPos - p);
            float dist = d.magnitude;
            if (dist < stompRadius && PlayerPos.y < p.y + 2.6f)
                HitPlayer(stompDamage, ImpactWeight.Slam, d, 100f);
            else if (dist < shockRadius && Player.motor.Grounded)   // the shockwave runs along the ground: jump it
                HitPlayer(shockDamage, ImpactWeight.Heavy, d, 40f);
        }

        // ================================================================== kick
        bool KickTouches(int leg)
        {
            if (!PlayerAlive || !shin[leg] || !foot[leg]) return false;
            Vector3 p = PlayerPos + Vector3.up * 0.9f;
            Vector3 knee = shin[leg].position, ankle = foot[leg].position;
            Vector3 toe = ankle + (ankle - knee).normalized * 1.3f + Heading * 0.6f;
            return SegDist(p, knee, ankle) < 1.4f || SegDist(p, ankle, toe) < 1.5f;
        }

        IEnumerator Kick()
        {
            kickCd = phaseTwo ? 2.5f : 3.5f;
            int kicks = phaseTwo ? 2 : 1;
            gait = false;
            for (int n = 0; n < kicks && PlayerAlive; n++)
            {
                int l = LegToward(PlayerPos);
                currentMove = "Kick";
                legOverride[l] = true;
                Vector3 from = AnklePos(l);
                legTarget[l] = from;
                Groan(1.3f, 0.5f);
                sharp = 6f;
                // draw the leg back, leaning in over it
                yield return Hold((n == 0 ? 0.8f : 0.55f) / SpeedMul, (k, dt) =>
                {
                    Brake(dt);
                    Face(PlayerPos - transform.position, turnRate * 0.9f, dt);
                    float e = Ease.OutCubic(k);
                    Vector3 back = HipGround(l) - Heading * 2.3f + Vector3.up * (ankleH + 1.4f);
                    legTarget[l] = Vector3.Lerp(from, back, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * 0.3f;
                    tFootFollow[l] = 1f;
                    tCrouch = 0.2f; tPitch = 8f * e; tTorsoPitch = 6f * e; tShiftX = -Side(l) * 0.3f * e;
                    tHand[l] = Arm(l, 0.7f, 0.6f, 1.6f * e);              // counterbalance
                    tHand[1 - l] = Arm(1 - l, 0.9f, 0.3f, -0.8f * e);
                });
                AudioManager.Play(Sfx.WhooshHeavy, AnklePos(l), 1f, 0.5f);
                bool hit = false;
                Vector3 back0 = legTarget[l];
                sharp = 14f;
                yield return Hold(0.24f, (k, dt) =>
                {
                    float e = Ease.InOutCubic(k);
                    Vector3 hip = HipGround(l);
                    Vector3 mid = hip + Heading * 0.9f + Vector3.up * (ankleH + 0.35f);
                    Vector3 front = hip + Heading * 3.5f + Vector3.up * (ankleH + 2.6f);
                    Vector3 a = Vector3.Lerp(back0, mid, e), b = Vector3.Lerp(mid, front, e);
                    legTarget[l] = Vector3.Lerp(a, b, e);
                    tPitch = Mathf.Lerp(8f, -12f, e); tTorsoPitch = Mathf.Lerp(6f, -6f, e);
                    tHand[l] = Arm(l, 0.9f, 0.4f, Mathf.Lerp(1.6f, -0.8f, e));
                    tHand[1 - l] = Arm(1 - l, 0.9f, 0.6f, Mathf.Lerp(-0.8f, 1.2f, e));
                    if (!hit && k > 0.2f && KickTouches(l))
                    {
                        hit = HitPlayer(kickDamage, ImpactWeight.Slam, Heading, 90f);
                        if (hit)
                        {
                            // punted: flies across the arena
                            Vector3 away = Flat(PlayerPos - transform.position).normalized * 0.5f + Heading;
                            Player.motor.SetPlanarVelocityImmediate(away.normalized * 13f);
                            Player.motor.SetVerticalVelocity(8f);
                            AudioManager.Play(Sfx.ImpactHeavy, PlayerPos + Vector3.up, 1f, 0.6f);
                            CameraDirector.Instance?.Shake(0.6f, Heading);
                            TimeDirector.HitStop(0.08f);
                        }
                    }
                });
                // follow-through, then the foot comes down in front
                Vector3 top = legTarget[l];
                Vector3 down = HipGround(l) + Heading * 1.5f;
                sharp = 6f;
                yield return Hold(0.5f / SpeedMul, (k, dt) =>
                {
                    float e = Ease.InOutCubic(k);
                    legTarget[l] = Vector3.Lerp(top, down + Vector3.up * ankleH, e) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.3f;
                    tFootFollow[l] = 1f - e;
                    tPitch = -12f * (1f - e); tTorsoPitch = -6f * (1f - e); tShiftX = 0f;
                });
                legOverride[l] = false;
                plant[l] = down;
                plantYaw[l] = yaw;
                Footfall(l);
            }
            gait = true;
            yield return Recover(0.7f / SpeedMul, "Off balance");
        }

        // ================================================================== crouch and grab
        Vector3 GrabPoint()
        {
            Vector3 lead = Player != null ? Player.motor.PlanarVelocity * 0.25f : Vector3.zero;
            Vector3 rel = transform.InverseTransformPoint(Ground(PlayerPos + lead));
            rel.z = Mathf.Clamp(rel.z, 2.4f, 7f);
            rel.x = Mathf.Clamp(rel.x, -2.5f, 2.5f);
            rel.y = 0f;
            return Ground(transform.TransformPoint(rel));
        }

        int TryCatch()
        {
            if (!PlayerAlive || Player.Invulnerable) return -1;
            Vector3 p = PlayerPos + Vector3.up * 1f;
            int best = -1; float bestD = grabRadius;
            for (int i = 0; i < 2; i++)
            {
                if (!palm[i]) continue;
                Vector3 tip = palm[i].position + (palm[i].position - forearm[i].position).normalized * 0.9f;
                float d = Mathf.Min(Vector3.Distance(p, palm[i].position), SegDist(p, palm[i].position, tip));
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best >= 0 && Player.TryGrab()) return best;
            return -1;
        }

        IEnumerator Grab()
        {
            grabCd = phaseTwo ? 6f : 8.5f;
            currentMove = "Crouch";
            Groan(0.95f, 0.8f);
            sharp = 5f;
            handSharp = 6f;
            // sinks low, leans in, both arms rise wide with the fingers spread
            yield return Hold(0.9f / SpeedMul, (k, dt) =>
            {
                Brake(dt);
                Face(PlayerPos - transform.position, turnRate * 1.1f, dt);
                float e = Ease.OutCubic(k);
                tCrouch = 0.1f + 0.5f * e; tTorsoPitch = 26f * e; tPitch = 6f * e;
                for (int i = 0; i < 2; i++) { tHand[i] = Arm(i, 2.4f * e, 2.6f * e, 2f * e); tCurl[i] = -30f * e; }
                agitation = Mathf.Max(agitation, 0.4f);
            });
            currentMove = "Grab";
            Vector3 g = GrabPoint();
            float lunge = Mathf.Clamp(Flat(g - transform.position).magnitude - 4.4f, 0f, 2.4f);
            Vector3 start = transform.position;
            Vector3 fwd = Heading;
            int caught = -1;
            AudioManager.Play(Sfx.WhooshHeavy, transform.position + Heading * 3f + Vector3.up * 3f, 1f, 0.55f);
            Groan(1.2f, 0.6f);
            sharp = 12f;
            handSharp = 16f;
            yield return Hold(0.34f / SpeedMul, (k, dt) =>
            {
                float e = Ease.InOutCubic(k);
                Place(start + fwd * lunge * e);
                tCrouch = 0.6f + 0.3f * e; tTorsoPitch = 26f + 32f * e; tPitch = 6f + 6f * e;
                for (int i = 0; i < 2; i++)
                {
                    Vector3 wide = g + transform.right * Side(i) * 3f + Vector3.up * 2.6f;
                    Vector3 close = g + transform.right * Side(i) * 0.55f + Vector3.up * 1.35f;
                    tHand[i] = HandLocal(i, Vector3.Lerp(wide, close, e));
                    tCurl[i] = Mathf.Lerp(-30f, 55f, Mathf.Clamp01((k - 0.55f) / 0.45f));
                }
                if (caught < 0 && k > 0.4f) caught = TryCatch();
            });
            if (caught < 0)
            {
                // missed: the hands thump into the dirt and it's stuck down there, head low
                currentMove = "Stuck";
                for (int i = 0; i < 2; i++) if (palm[i]) Thud(Ground(palm[i].position), 0.8f);
                Shed(2);
                handSharp = 8f;
                yield return Recover(1.35f / Mathf.Sqrt(SpeedMul), "Stuck", true);
                yield break;
            }
            yield return HoldPlayer(caught);
        }

        IEnumerator HoldPlayer(int i)
        {
            State = Mode.Holding;
            holdHand = i;
            int other = 1 - i;
            currentMove = "Lift";
            sharp = 4f;
            handSharp = 6f;
            // lifts you up to its face
            yield return Hold(0.8f, (k, dt) =>
            {
                tCrouch = 0.1f; tTorsoPitch = 4f; tPitch = 0f;
                tHand[i] = Arm(i, -0.9f, 3.4f, 2.6f);
                tCurl[i] = 62f;
                tHand[other] = handRest[other]; tCurl[other] = 0f;
            });
            int squeezes = phaseTwo ? 3 : 2;
            for (int s = 0; s < squeezes && Player != null && Player.IsHeld; s++)
            {
                currentMove = "Squeeze";
                Groan(1.4f, 0.45f);
                yield return Hold(0.38f, (k, dt) =>
                {
                    tCurl[i] = 62f + 26f * Ease.OutCubic(k);
                    tHand[i] = Arm(i, -0.9f, 3.4f + Mathf.Sin(k * 40f) * 0.05f, 2.6f);
                    agitation = Mathf.Max(agitation, 0.5f);
                });
                Player.HeldSqueeze(squeezeDamage, palm[i].position);
                AudioManager.Play(Sfx.RootSnap, palm[i].position, 0.8f, 0.9f);
                yield return Hold(0.3f, (k, dt) => tCurl[i] = Mathf.Lerp(88f, 66f, k));
            }
            if (Player == null || !Player.IsHeld) { holdHand = -1; yield return Recover(0.6f, "Recover"); yield break; }
            // the throw: winds up high, then hurls you down into the dirt in front of it
            currentMove = "Throw";
            Groan(1f, 0.9f);
            yield return Hold(0.42f, (k, dt) =>
            {
                tHand[i] = Arm(i, 0.3f, 3.6f, -0.2f);
                tTorsoPitch = -10f; tCrouch = 0.05f;
            });
            Vector3 slamAt = Ground(transform.position + Heading * 4.6f);
            handSharp = 22f;
            sharp = 12f;
            AudioManager.Play(Sfx.WhooshHeavy, palm[i].position, 1f, 0.6f);
            yield return Hold(0.2f, (k, dt) =>
            {
                tHand[i] = HandLocal(i, slamAt + Vector3.up * 2.2f);
                tTorsoPitch = 28f; tCrouch = 0.35f;
            });
            holdHand = -1;
            Player.ReleaseHeld(new DamageInfo
            {
                damage = throwDamage, poiseDamage = 100f, point = PlayerPos + Vector3.up,
                direction = Heading, weight = ImpactWeight.Slam, source = gameObject
            }, Heading * 5f + Vector3.down * 6f);
            Thud(slamAt, 1.2f);
            FXManager.Spawn(FXType.SlamRing, slamAt + Vector3.up * 0.05f, Quaternion.identity, 2.2f);
            handSharp = 7f;
            yield return Recover(0.9f, "Recover");
        }

        /// <summary>Let go of the player (toppled, sent away, killed): a drop, or nothing if not holding.</summary>
        void ReleaseHeld(bool _)
        {
            holdHand = -1;
            if (Player != null && Player.IsHeld) Player.DropHeld();
        }

        // ================================================================== recover (punish windows)
        IEnumerator Recover(float time, string label, bool stuck = false)
        {
            State = Mode.Recovering;
            currentMove = label;
            float t0 = Time.time;
            yield return Hold(time, (k, dt) =>
            {
                Brake(dt);
                float pant = Mathf.Sin((Time.time - t0) * 5f);
                if (stuck)
                {
                    // hands in the dirt, head low: the face is in reach
                    tCrouch = 0.9f; tTorsoPitch = 56f + pant * 2f; tPitch = 10f; tHeadPitch = -20f;
                    look = 0.3f;
                }
                else
                {
                    tCrouch = 0.25f + pant * 0.03f; tTorsoPitch = 10f + pant * 2f; tHeadPitch = 6f;
                    for (int i = 0; i < 2; i++) { tHand[i] = Arm(i, 0.2f, 0.2f, 0.5f); tCurl[i] = 10f; }
                }
            });
            if (stuck)
            {
                // pushes itself back up
                Groan(1.1f, 0.6f);
                yield return Hold(0.6f, (k, dt) => { tCrouch = Mathf.Lerp(0.9f, 0.15f, k); tTorsoPitch = Mathf.Lerp(56f, 0f, k); tHeadPitch = 0f; look = 1f; });
            }
            State = Mode.Attacking;
        }

        // ================================================================== footing: the topple
        void AddFooting(in DamageInfo info, int leg)
        {
            float pts = Mathf.Clamp(8f + info.poiseDamage * 0.3f, 8f, 30f) * (info.charged ? 1.3f : 1f) * (info.critical ? 0.5f : 1f);
            footing += pts;
            lastFootHit = Time.time;
            buckle[leg] = 1f;
            FXManager.Spawn(FXType.WoodChips, info.point, Quaternion.LookRotation(-info.direction + Vector3.up * 0.5f), 1.8f);
            AudioManager.Play(Sfx.RootSnap, info.point, 0.9f, Random.Range(0.9f, 1.1f));
            if (Footing01 > 0.55f && Random.value < 0.5f) Groan(1.25f, 0.45f);
            if (footing >= FootingCap && State != Mode.Toppled && State != Mode.Dead) Topple();
        }

        void Topple()
        {
            StopAllCoroutines();
            brain = null;
            ReleaseHeld(false);
            brain = StartCoroutine(ToppleRoutine());
        }

        IEnumerator ToppleRoutine()
        {
            State = Mode.Toppled;
            currentMove = "Wobble";
            vel = Vector3.zero;
            gait = false;
            for (int i = 0; i < 2; i++)
            {
                stepping[i] = false;
                if (legOverride[i]) { plant[i] = Ground(legTarget[i]); legOverride[i] = false; }
                else plant[i] = Ground(AnklePos(i));
                footPitch[i] = 0f;
            }
            Groan(0.9f, 1f);
            AudioManager.Play(Sfx.RootSnap, plant[0], 1f, 0.7f);
            AudioManager.Play(Sfx.RootSnap, plant[1], 1f, 0.6f);
            sharp = 9f;
            handSharp = 9f;
            // the knees buckle, it sways, arms flailing for balance
            yield return Hold(0.75f, (k, dt) =>
            {
                tCrouch = 0.35f;
                tBank = Mathf.Sin(k * 15f) * 8f * (1f - k * 0.4f);
                tPitch = -7f * k;
                tHeadPitch = -15f * k;
                for (int i = 0; i < 2; i++) { tHand[i] = Arm(i, 2.2f, 2f + Mathf.Sin(k * 18f + i * 2f) * 1.2f, 0.4f); tCurl[i] = -30f; }
                agitation = 1f;
                look = 0.4f;
            });
            yield return Fall(false);
            currentMove = "Toppled";
            float t = 0f, shedT = 0f;
            while (t < toppleTime)
            {
                float dt = Time.deltaTime;
                t += dt;
                tTorsoPitch = Mathf.Sin(t * 1.5f) * 2f;                     // slow breath through the bark
                tHeadYaw = Mathf.Sin(t * 0.8f) * 10f;
                agitation = Mathf.Max(agitation, 0.25f);
                if ((shedT -= dt) <= 0f) { shedT = Random.Range(0.4f, 0.9f); Shed(1, 1.1f); }
                Place(transform.position);
                yield return null;
            }
            // pushes itself back up onto its roots
            yield return Rise();
            footing = 0f;
            State = Mode.Stalking;
            stompCd = 0.5f;
            brain = StartCoroutine(Brain(false));
        }

        /// <summary>Timber: it tips back and crashes down, face to the sky, feet sliding out from under it.</summary>
        IEnumerator Fall(bool dying)
        {
            currentMove = dying ? "Dying" : "Fall";
            for (int i = 0; i < 2; i++) { legOverride[i] = true; legTarget[i] = AnklePos(i); }
            Vector3[] from = { legTarget[0], legTarget[1] };
            Vector3[] to = { Ground(plant[0] + Heading * 2.6f) + Vector3.up * 0.6f, Ground(plant[1] + Heading * 2.6f) + Vector3.up * 0.6f };
            float p0 = pitch;
            look = 0f;
            keepOffGround = true;
            sharp = 30f;
            handSharp = 6f;
            AudioManager.Play(Sfx.WoodGroan, head.position, 1f, 0.55f);
            yield return Hold(0.75f, (k, dt) =>
            {
                float e = Ease.InCubic(k);
                tPitch = Mathf.Lerp(p0, -84f, e);
                tLift = Mathf.Lerp(0f, -4.15f, e);
                tShiftZ = -1.6f * e;
                tCrouch = Mathf.Lerp(0.35f, 0f, e);
                tBank = Mathf.Lerp(tBank, 0f, k);
                tTorsoPitch = 0f;
                tHeadPitch = 28f * e;
                for (int i = 0; i < 2; i++)
                {
                    legTarget[i] = Vector3.Lerp(from[i], to[i], Ease.InOutCubic(k));
                    tFootFollow[i] = e;
                    tHand[i] = Arm(i, 3f * e + 1f, 1.5f - 1f * e, -0.6f * e);
                    tCurl[i] = -10f;
                }
            });
            // the crash: dust along the whole body, leaves everywhere
            Vector3 c = transform.position;
            AudioManager.Play(Sfx.DeepRumble, c, 1f, 0.85f);
            AudioManager.Play(Sfx.Felled, c, 0.8f, 0.7f);
            for (int s = 0; s < 4; s++) Thud(Ground(c - Heading * (s * 2.2f - 1f)), s == 1 ? 1.6f : 0.9f);
            CameraDirector.Instance?.Shake(0.9f, Vector3.down);
            Shed(12, 1.7f);
            agitation = 1f;
            sharp = 6f;
        }

        IEnumerator Rise()
        {
            currentMove = "Rise";
            Groan(0.9f, 0.9f);
            Vector3[] from = { legTarget[0], legTarget[1] };
            ResetPlants();
            for (int i = 0; i < 2; i++) legOverride[i] = true;
            float p0 = pitch, l0 = lift, z0 = shiftZ;
            sharp = 30f;
            yield return Hold(1.7f, (k, dt) =>
            {
                float e = Ease.InOutCubic(k);
                tPitch = Mathf.Lerp(p0, 0f, e);
                tLift = Mathf.Lerp(l0, 0f, e);
                tShiftZ = Mathf.Lerp(z0, 0f, e);
                tCrouch = Mathf.Sin(e * Mathf.PI) * 0.55f;
                tHeadPitch = Mathf.Lerp(28f, 0f, e);
                tTorsoPitch = Mathf.Sin(e * Mathf.PI) * 30f;            // folds forward to get its weight over its roots
                for (int i = 0; i < 2; i++)
                {
                    legTarget[i] = Vector3.Lerp(from[i], plant[i] + Vector3.up * ankleH, Ease.InOutCubic(Mathf.Clamp01(k * 1.4f)));
                    tFootFollow[i] = 1f - e;
                    // pushes off the ground beside it, then the arms hang again
                    tHand[i] = k < 0.6f ? HandLocal(i, Ground(transform.position + transform.right * Side(i) * 3f - Heading * 1.5f) + Vector3.up * 1.6f)
                                        : Vector3.Lerp(tHand[i], handRest[i], Ease.Damp(6f, dt));
                }
                look = e;
            });
            keepOffGround = false;
            for (int i = 0; i < 2; i++) { legOverride[i] = false; tFootFollow[i] = 0f; Footfall(i); }
            gait = true;
            RestPose();
            sharp = 5f;
        }

        // ================================================================== death
        void Die()
        {
            StopAllCoroutines();
            brain = null;
            ReleaseHeld(false);
            State = Mode.Dead;
            currentMove = "Dead";
            gait = false;
            RaiseFelled();
            StartCoroutine(DeathRoutine());
        }

        IEnumerator DeathRoutine()
        {
            TimeDirector.SlowMo(1.1f, 0.3f);
            Groan(0.6f, 1f);
            bool alreadyDown = pitch < -60f;
            if (!alreadyDown)
            {
                for (int i = 0; i < 2; i++) { stepping[i] = false; if (!legOverride[i]) plant[i] = Ground(AnklePos(i)); }
                yield return Hold(0.6f, (k, dt) =>
                {
                    tCrouch = 0.4f; tBank = Mathf.Sin(k * 10f) * 6f; tHeadPitch = -25f;
                    for (int i = 0; i < 2; i++) { tHand[i] = Arm(i, 1.8f, 1.6f, 0.4f); tCurl[i] = -20f; }
                    agitation = 1f;
                });
                yield return Fall(true);
            }
            float t = 0f;
            while (t < 3f)
            {
                float dt = Time.deltaTime;
                t += dt;
                eyeGlow = Mathf.Clamp01(1f - t / 2.2f);
                agitation = Mathf.Max(0.15f, 1f - t / 2f);
                if (Random.value < dt * 5f) Shed(1, 1.2f);
                Place(transform.position);
                yield return null;
            }

            // the giant returns to the earth: it sinks out of sight in a fall of leaves, then leaves the arena
            AudioManager.Play(Sfx.DeepRumble, transform.position, 0.8f, 0.8f);
            Groan(0.5f, 0.7f);
            const float SinkTime = 3.2f, SinkTo = 9f;
            t = 0f;
            float dustAt = 0f;
            while (t < SinkTime)
            {
                float dt = Time.deltaTime;
                t += dt;
                sinkDepth = SinkTo * Ease.InOutCubic(Mathf.Clamp01(t / SinkTime));
                agitation = 0.5f;
                if (Random.value < dt * 14f) Shed(1, 1.4f);
                if (t >= dustAt)
                {
                    dustAt = t + 0.45f;
                    Vector3 at = Ground(transform.position + Random.insideUnitSphere * 4f);
                    FXManager.Spawn(FXType.Dust, at, Quaternion.identity, 2f);
                }
                Place(transform.position);
                yield return null;
            }
            ResetBoss();
        }

        // ================================================================== damage
        public override bool ReceiveHit(in DamageInfo info) => ReceivePartHit(info, Part.Body);

        /// <summary>Matches the critical multiplier the player's weapons apply on staggered targets.</summary>
        const float PlayerCriticalMultiplier = 2.6f;
        Renderer headRenderer;

        public bool ReceivePartHit(in DamageInfo info, Part part)
        {
            if (!IsAlive) return false;
            // one blow, one hit: a club sweeping through the roots and the shin in one swing counts once
            if (info.source != null && info.source == lastHitSource &&
                (info.explosive ? Time.frameCount == lastHitFrame : Time.time - lastHitTime < 0.12f)) return false;
            lastHitSource = info.source;
            lastHitTime = Time.time;
            lastHitFrame = Time.frameCount;

            float dmg = info.damage;
            // lying down, a blow that lands on the head is a blow to the face, even when the shoulder's hurtbox caught
            // the swing first (the weapon then didn't know it was a critical: give it the player's critical multiplier)
            if (part == Part.Body && State == Mode.Toppled && headRenderer &&
                Vector3.Distance(info.point, headRenderer.bounds.center) < 1.9f)
            {
                part = Part.Head;
                if (!info.critical) dmg *= PlayerCriticalMultiplier;
            }
            bool face = part == Part.Head && State == Mode.Toppled;
            int leg = part == Part.FootL ? 0 : part == Part.FootR ? 1 : -1;
            if (face) dmg *= faceMultiplier;
            if (leg >= 0) dmg *= footDamageScale;
            dmg = Mathf.Round(dmg);
            health = Mathf.Max(0f, health - dmg);
            flash = 1f;
            agitation = Mathf.Max(agitation, face ? 1f : 0.55f);
            FXManager.Spawn(FXType.HitSpark, info.point, Quaternion.LookRotation(-info.direction + Vector3.up * 0.3f), info.weight == ImpactWeight.Light ? 1.1f : 1.5f);
            AudioManager.Play(Sfx.Knock, info.point, 0.9f, info.weight == ImpactWeight.Light ? 0.6f : 0.5f);
            if (face)
            {
                FXManager.Spawn(FXType.LeafBurst, info.point, Quaternion.LookRotation(Vector3.up), 1f);
                FXManager.Spawn(FXType.WoodChips, info.point, Quaternion.LookRotation(-info.direction + Vector3.up), 2f);
                AudioManager.Play(Sfx.Stagger, info.point, 0.9f, 0.7f);
                if (Random.value < 0.4f) Groan(1.3f, 0.6f);
            }
            else if (leg < 0 && Random.value < 0.5f) FXManager.Spawn(FXType.LeafBurst, info.point, Quaternion.LookRotation(Vector3.up), 1.2f);
            var shown = info;
            shown.damage = dmg;
            RaiseDamaged(shown, false);
            if (health <= 0f) { Die(); return true; }
            if (leg >= 0 && State != Mode.Toppled && State != Mode.Dormant) AddFooting(info, leg);
            return true;
        }

        // ================================================================== update
        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || State == Mode.Dormant) return;
            stompCd -= dt; kickCd -= dt; grabCd -= dt;
            if (State != Mode.Toppled && Time.time - lastFootHit > footingRegenDelay)
                footing = Mathf.MoveTowards(footing, 0f, footingRegen * dt);
            for (int i = 0; i < 2; i++)
            {
                buckle[i] = Mathf.MoveTowards(buckle[i], 0f, dt * 2.5f);
                float glowTarget = State == Mode.Dead ? 0f : Footing01 * Footing01;
                footGlow[i] = Mathf.MoveTowards(footGlow[i], glowTarget + buckle[i] * 0.5f, dt * 3f);
            }
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || State == Mode.Dormant || !rigReady) return;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            float t = Time.time;

            // ---- smooth the pose
            float k = Ease.Damp(sharp, dt);
            crouch = Mathf.Lerp(crouch, tCrouch, k);
            pitch = Mathf.Lerp(pitch, tPitch, k);
            bank = Mathf.Lerp(bank, tBank, k);
            lift = Mathf.Lerp(lift, tLift, k);
            shiftX = Mathf.Lerp(shiftX, tShiftX, k);
            shiftZ = Mathf.Lerp(shiftZ, tShiftZ, k);
            torsoPitch = Mathf.Lerp(torsoPitch, tTorsoPitch, k);
            torsoYaw = Mathf.Lerp(torsoYaw, tTorsoYaw, k);
            torsoRoll = Mathf.Lerp(torsoRoll, tTorsoRoll, k);
            headPitch = Mathf.Lerp(headPitch, tHeadPitch, k);
            headYaw = Mathf.Lerp(headYaw, tHeadYaw, k);
            float hk = Ease.Damp(handSharp, dt);
            for (int i = 0; i < 2; i++)
            {
                handOff[i] = Vector3.Lerp(handOff[i], tHand[i], hk);
                curl[i] = Mathf.Lerp(curl[i], tCurl[i], hk);
                footFollow[i] = Mathf.MoveTowards(footFollow[i], tFootFollow[i], dt * 4f);
            }

            // ---- feet: steps, then where each ankle wants to be
            float speed = Flat(vel).magnitude;
            UpdateGait(dt, speed);
            var ankle = ankleNow;
            float stepS = 0f; int stepLeg = -1;
            for (int i = 0; i < 2; i++)
            {
                if (legOverride[i]) { ankle[i] = legTarget[i]; continue; }
                if (stepping[i])
                {
                    float s = Mathf.Clamp01(stepT[i]);
                    float e = s * s * (3f - 2f * s);
                    ankle[i] = Vector3.Lerp(stepFrom[i], stepTo[i], e) + Vector3.up * (ankleH + Mathf.Sin(s * Mathf.PI) * stepHeight[i]);
                    stepS = Mathf.Sin(s * Mathf.PI); stepLeg = i;
                }
                else ankle[i] = plant[i] + Vector3.up * ankleH;
            }

            // ---- hips: crouch, weight over the standing foot, a sway once its footing goes, the fall
            float wobble = State == Mode.Toppled || State == Mode.Dead ? 0f : Mathf.Max(0f, Footing01 - 0.5f) * 2f;
            sway = Mathf.Sin(t * 2.3f) * wobble;
            float buckleDrop = (buckle[0] + buckle[1]) * 0.22f;
            float weightShift = stepLeg >= 0 ? -Side(stepLeg) * 0.22f * stepS : 0f;
            float bob = stepLeg >= 0 ? 0.1f * stepS - 0.05f : 0f;
            pivot.localPosition = pivotRest + new Vector3(shiftX + weightShift, lift - crouch * crouchDrop + bob - buckleDrop, shiftZ);
            pivot.localRotation = Quaternion.Euler(pitch + Mathf.Sin(t * 1.1f) * 0.6f,
                                                   0f,
                                                   bank + sway * 6f + (buckle[1] - buckle[0]) * 5f - weightShift * 8f);

            // ---- torso + head (the head tracks you)
            Quaternion tf = TorsoFrame;
            torso.rotation = tf * torsoRel;
            UpdateLook(dt, tf);
            Quaternion hf = tf * Quaternion.Euler(headPitch + lookPitch * look, headYaw + lookYaw * look, sway * 4f);
            head.rotation = hf * headRel;

            // ---- lying down: the lowest point of the body stays on the dirt
            KeepOffGround();

            // ---- legs (3D two-bone IK, knees forward)
            for (int i = 0; i < 2; i++)
            {
                Vector3 target = ankle[i];
                Vector3 pole = pivot.rotation * new Vector3(Side(i) * 0.25f, 0f, 1f);
                TwoBone(thigh[i], shin[i], pivot.rotation, thRel[i], shRel[i], thDir[i], shDir[i], legL1[i], legL2[i], target, pole);
                // feet: flat on the dirt at their own heading, or following the shin when kicked up / lying down
                float fy = stepping[i] ? Mathf.LerpAngle(stepYawFrom[i], plantYaw[i], Mathf.Clamp01(stepT[i])) : legOverride[i] ? yaw : plantYaw[i];
                float toe = stepping[i] ? -Mathf.Sin(Mathf.Clamp01(stepT[i]) * Mathf.PI) * 12f : footPitch[i];
                Quaternion flat = Quaternion.Euler(0f, fy, 0f) * Quaternion.Euler(toe, 0f, 0f) * ftRel[i];
                Quaternion follow = shin[i].rotation * Quaternion.Inverse(shRel[i]) * ftRel[i];
                foot[i].rotation = Quaternion.Slerp(flat, follow, footFollow[i]);
                if (legColliders != null && i < legColliders.Length && legColliders[i])
                    legColliders[i].enabled = !legOverride[i] && !stepping[i] && State != Mode.Toppled && State != Mode.Dead;
            }

            // ---- arms (3D two-bone IK, elbows back and out), walking swing, finger curl
            for (int i = 0; i < 2; i++)
            {
                Vector3 off = handOff[i];
                if (stepLeg >= 0 && holdHand != i) off.z += (stepLeg == i ? -1f : 1f) * 0.45f * stepS;
                Vector3 target = upperArm[i].position + tf * off;
                if (target.y < groundY + 0.6f) target.y = groundY + 0.6f;
                Vector3 pole = tf * new Vector3(Side(i) * 0.7f, -0.1f, -1f);
                TwoBone(upperArm[i], forearm[i], tf, upRel[i], foRel[i], upDir[i], foDir[i], armL1[i], armL2[i], target, pole);
                hand[i].localRotation = handLocalRest[i];
                Vector3 along = (hand[i].position - forearm[i].position).normalized;
                Vector3 inward = tf * new Vector3(-Side(i), 0f, 0f);
                Vector3 axis = Vector3.Cross(along, inward);
                if (axis.sqrMagnitude > 1e-4f) hand[i].rotation = Quaternion.AngleAxis(curl[i] * 0.6f, axis.normalized) * hand[i].rotation;
            }

            // ---- the grabbed player rides in the fist, facing the face
            if (holdHand >= 0 && Player != null && Player.IsHeld && palm[holdHand])
            {
                Vector3 p = palm[holdHand].position - Vector3.up * 1f;
                Vector3 toFace = Flat(head.position - p);
                Player.SetHeldPose(p, Quaternion.LookRotation(toFace.sqrMagnitude > 0.01f ? toFace : -Heading));
            }

            UpdateLeaves(dt, speed, t);

            if (lockPoint)
            {
                float down = Mathf.Clamp01(-pitch / 60f);
                Vector3 stand = transform.TransformPoint(lockRest);
                lockPoint.position = Vector3.Lerp(stand, head.position + Vector3.up * 0.4f, down);
            }
            UpdateMaterials(dt);
        }

        /// <summary>
        /// Two-bone IK in 3D. Each bone keeps its rest pose in <paramref name="frame"/> and is turned by the shortest arc so
        /// its rest direction points along the solved segment (rigid tubes, so the twist doesn't show).
        /// </summary>
        static void TwoBone(Transform upper, Transform lower, Quaternion frame, Quaternion upperRel, Quaternion lowerRel,
                            Vector3 upperDir, Vector3 lowerDir, float l1, float l2, Vector3 target, Vector3 pole)
        {
            if (!upper || !lower) return;
            Vector3 h = upper.position;
            Vector3 d = target - h;
            float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(l1 - l2) + 0.02f, l1 + l2 - 0.002f);
            Vector3 u = d.sqrMagnitude > 1e-6f ? d.normalized : frame * Vector3.down;
            float a = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
            float hgt = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
            Vector3 p = pole - u * Vector3.Dot(pole, u);
            if (p.sqrMagnitude < 1e-6f) p = frame * Vector3.forward - u * Vector3.Dot(frame * Vector3.forward, u);
            p.Normalize();
            Vector3 knee = h + u * a + p * hgt;
            Vector3 end = h + u * dist;
            upper.rotation = Quaternion.FromToRotation(frame * upperDir, knee - h) * (frame * upperRel);
            lower.rotation = Quaternion.FromToRotation(frame * lowerDir, end - knee) * (frame * lowerRel);
        }

        void UpdateLook(float dt, Quaternion tf)
        {
            float ty = 0f, tp = 0f;
            if (PlayerAlive && head)
            {
                Vector3 local = Quaternion.Inverse(tf) * (PlayerPos + Vector3.up * 1.2f - head.position);
                ty = Mathf.Clamp(Mathf.Atan2(local.x, Mathf.Max(0.3f, local.z)) * Mathf.Rad2Deg, -55f, 55f);
                tp = Mathf.Clamp(Mathf.Atan2(-local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -20f, 50f);
            }
            float k = Ease.Damp(4f, dt);
            lookYaw = Mathf.Lerp(lookYaw, ty, k);
            lookPitch = Mathf.Lerp(lookPitch, tp, k);
        }

        void UpdateGait(float dt, float speed)
        {
            for (int i = 0; i < 2; i++)
            {
                if (!stepping[i]) continue;
                if (legOverride[i]) { stepping[i] = false; continue; }
                stepT[i] += dt / stepDur[i];
                if (stepT[i] >= 1f)
                {
                    stepping[i] = false;
                    plant[i] = stepTo[i];
                    Footfall(i);
                }
            }
            stepGate -= dt;
            if (!gait || stepping[0] || stepping[1] || legOverride[0] || legOverride[1] || stepGate > 0f) return;
            int leg = -1; float worst = 0f;
            for (int i = 0; i < 2; i++)
            {
                float err = Flat(HipGround(i) - plant[i]).magnitude + Mathf.Abs(Mathf.DeltaAngle(plantYaw[i], yaw)) * 0.015f;
                if (err > worst) { worst = err; leg = i; }
            }
            bool moving = speed > 0.3f;
            if (leg >= 0 && worst > 0.4f && !moving) settleT += dt; else settleT = 0f;
            if (leg < 0 || !(moving ? worst > 0.5f : settleT > 0.3f)) return;
            // step: land a little ahead of where the hip will be
            float dur = Mathf.Lerp(0.72f, 0.5f, Mathf.Clamp01(speed / 3f)) / SpeedMul;
            stepFrom[leg] = plant[leg];
            stepTo[leg] = Ground(HipGround(leg) + vel * dur * 0.9f);
            stepYawFrom[leg] = plantYaw[leg];
            plantYaw[leg] = yaw;
            stepHeight[leg] = Mathf.Clamp(0.3f + Flat(stepTo[leg] - stepFrom[leg]).magnitude * 0.25f, 0.3f, 0.85f);
            stepDur[leg] = dur;
            stepT[leg] = 0f;
            stepping[leg] = true;
            stepGate = 0.05f;
            settleT = 0f;
        }

        void KeepOffGround()
        {
            if (!keepOffGround || hull == null || hull.Length == 0) return;
            float minY = float.MaxValue;
            foreach (var h in hull)
                if (h.bone) minY = Mathf.Min(minY, h.bone.TransformPoint(h.local).y);
            float deficit = groundY - sinkDepth + 0.05f - minY;
            if (deficit > 0f) pivot.position += Vector3.up * deficit;
        }

        void UpdateLeaves(float dt, float speed, float t)
        {
            float moving = Mathf.Clamp01(speed / walkSpeed) * 0.3f;
            agitation = Mathf.Max(Mathf.MoveTowards(agitation, 0f, dt * 0.9f), moving);
            if (leaves == null || leafRest == null) return;
            float wind = 0.7f + 0.3f * Mathf.Sin(t * 0.6f);
            float amp = 4f * wind + agitation * 26f;
            float flutter = 1.5f + agitation * 9f;
            for (int i = 0; i < leaves.Length; i++)
            {
                var l = leaves[i];
                if (!l) continue;
                float w = t * leafFreq[i] * (1f + agitation * 1.6f) + leafPhase[i];
                float a = amp * Mathf.Sin(w) + flutter * Mathf.Sin(w * 3.7f + leafPhase[i]);
                l.localRotation = leafRest[i] * Quaternion.AngleAxis(a, leafAxis[i]);
            }
        }

        void UpdateMaterials(float dt)
        {
            flash = Mathf.MoveTowards(flash, 0f, dt * 6f);
            if (flashRenderers == null || rendererMats == null) return;
            Color fl = new Color(1f, 0.85f, 0.6f) * (flash * flash * 0.6f);
            for (int r = 0; r < flashRenderers.Length; r++)
            {
                var ren = flashRenderers[r];
                if (!ren) continue;
                int footIdx = footRenderers != null && footRenderers.Length > 1 ? (ren == footRenderers[0] ? 0 : ren == footRenderers[1] ? 1 : -1) : -1;
                var mats = rendererMats[r];
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null || !mat.HasProperty(EmissionId)) continue;
                    Color c = fl;
                    if (mat.name.StartsWith("M_DruidEye")) c += eyeBase * eyeGlow * (phaseTwo ? 1.4f : 1f);
                    if (footIdx >= 0) c += Ember * (footGlow[footIdx] * 0.55f);
                    ren.GetPropertyBlock(mpb, m);
                    mpb.SetColor(EmissionId, c);
                    ren.SetPropertyBlock(mpb, m);
                }
            }
        }
    }
}
