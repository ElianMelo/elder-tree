using System.Collections;
using System.Collections.Generic;
using TungSahur.Audio;
using TungSahur.CameraSystem;
using TungSahur.Combat;
using TungSahur.FX;
using TungSahur.Player;
using UnityEngine;

namespace TungSahur.Enemies
{
    /// <summary>
    /// Tralalero Tralala: a shark that runs the arena on three sneakered legs. A ground boss, every
    /// attack announced by the body (no ground markers):
    ///  - Sprint: scuffs a sneaker, then sprints through the arena at you and lunges jaws first as it
    ///    passes; skids round and goes again (2 passes, 3 in phase 2), then stands panting.
    ///  - Bite: up close, rears its head and snaps forward (2 bites, 3 in phase 2).
    ///  - Tail whip (flank / behind): turns away with the tail cocked, then whips the body round.
    ///  - Tail slam (behind): the nose dips, the tail rises high, and it crashes down.
    ///  - Tralala tornado (you hug it): coils, then spins on its sneakers, drifting at you, and reels dizzy.
    ///  - Spin leap (mid range): crouches, leaps spinning and belly-slams where you stood.
    ///  - Burrow: dives into the dirt; only the dorsal fin cuts through the arena, circling you. Where the
    ///    dirt starts to boil, it erupts jaws first (twice in phase 2), then belly-flops, open to blows.
    /// Clubs and arrows wear its stance down: it flops onto its side, sneakers kicking, open to criticals.
    /// The legs are procedural: a three-beat gait drives two-bone IK so the sneakers land on the ground.
    /// </summary>
    public class TralaleroBoss : BossBase
    {
        public enum Mode { Dormant, Stalking, Attacking, Burrowed, Recovering, Staggered, Dead }

        public string displayName = "Tralalero Tralala";

        [Header("Rig")]
        [Tooltip("Body centre: pitch, roll, lift and the burrow sink happen here.")]
        public Transform pivot;
        public Transform jaw;
        public Transform tail0, tail1;
        [Tooltip("Front left, front right, back.")]
        public Transform[] thighs, shins, feet;
        public Transform lockPoint;
        public Renderer[] flashRenderers;
        public Collider[] bodyColliders;
        public AudioSource digAudio;

        [Header("Arena")]
        public Vector3 arenaCenter;
        public float arenaRadius = 15f;

        [Header("Stats")]
        public float maxHealth = 2600f;
        public float maxPoise = 130f;
        public float poiseRegenDelay = 4f;
        [Range(0f, 1f)] public float phaseTwoAt = 0.5f;
        public float staggerTime = 3.4f;

        [Header("Movement")]
        public float walkSpeed = 3.4f;
        public float runSpeed = 7.5f;
        public float sprintSpeed = 13f;
        public float accel = 20f;
        public float turnRate = 200f;

        [Header("Jaws")]
        public float biteDamage = 115f;
        public float lungeDamage = 150f;

        [Header("Tail")]
        public float tailDamage = 135f;
        public float tailSlamDamage = 165f;

        [Header("Spin")]
        public float spinDamage = 95f;
        public float leapDamage = 150f;

        [Header("Burrow")]
        public float eruptDamage = 175f;
        public float eruptRadius = 2.8f;
        public float finSpeed = 9f;
        [Tooltip("How far the body sinks: the dorsal fin tip should just break the surface.")]
        public float burrowDepth = 3.15f;

        public Mode State { get; private set; } = Mode.Dormant;
        public override bool IsAlive => State != Mode.Dead && State != Mode.Dormant;
        public override bool IsStaggered => State == Mode.Staggered;
        public override bool PhaseTwo => phaseTwo;
        public override float Health => health;
        public override float Poise => poise;
        public override string CurrentMove => currentMove;
        public override string DisplayName => displayName;
        public override float MaxHealth => maxHealth;
        public override string StateLabel => State.ToString();
        public override bool IsDead => State == Mode.Dead;
        public override Vector3 ArenaCenter => arenaCenter;
        public bool Burrowed => State == Mode.Burrowed;

        bool phaseTwo;
        float health, poise;
        string currentMove = "-";
        string forcedMove;
        Coroutine brain;
        float lastHitTime = -99f, flash;
        bool flashWasOn;
        MaterialPropertyBlock mpb;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        float SpeedMul => phaseTwo ? 1.2f : 1f;

        PlayerController Player => PlayerController.Instance;
        Vector3 PlayerPos => Player != null ? Player.transform.position : arenaCenter;
        bool PlayerAlive => Player != null && !Player.IsDead;

        // ------------------------------------------------------------------ kinematics + pose
        float yaw, yawRate, groundY, airY;
        Vector3 vel;
        // pose targets (set by moves) and smoothed values (applied in LateUpdate)
        float tPitch, tBank, tLift, tJaw, tTailYaw, tTailPitch, tSink, sharp = 6f;
        float pitch, bank, lift, jawAngle, tailYaw, tailPitch, sink;
        float swim = 1f;                 // tail swimming wiggle amount
        readonly float[] legAirTarget = new float[3], legAir = new float[3];
        float kickAmp;                   // 0 = legs hang, 1 = pedalling in the air
        bool tuckLegs;
        float closePressure, spinCd, leapCd, sprintCd, burrowCd, tailCd;

        // rig rest data
        Vector3 pivotRestLocal;
        Quaternion jawRel, tail0Rel, tail1Rel;
        readonly Quaternion[] thighRel = new Quaternion[3], shinRel = new Quaternion[3], footRel = new Quaternion[3];
        readonly Vector3[] hipRest = new Vector3[3], ankleRestRoot = new Vector3[3];
        readonly float[] len1 = new float[3], len2 = new float[3], phi1Rest = new float[3], phi2Rest = new float[3];
        readonly float[] legPhase = { 0f, 0.5f, 0.25f };
        readonly bool[] legSwing = new bool[3];
        Vector3 lockRest, prevPos;
        float tailLen = 1.1f;
        bool rigReady;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            health = maxHealth;
            poise = maxPoise;
            CaptureRig();
            SetBodyColliders(false);
        }

        void CaptureRig()
        {
            if (pivot == null) return;
            pivotRestLocal = pivot.localPosition;
            Quaternion inv = Quaternion.Inverse(pivot.rotation);
            if (jaw) jawRel = inv * jaw.rotation;
            if (tail0) tail0Rel = inv * tail0.rotation;
            if (tail1) tail1Rel = inv * tail1.rotation;
            if (tail0 && tail1) tailLen = Mathf.Max(0.6f, Vector3.Distance(tail0.position, tail1.position) * 0.9f);
            Quaternion rootInv = Quaternion.Inverse(transform.rotation);
            for (int i = 0; i < 3 && thighs != null && i < thighs.Length; i++)
            {
                if (!thighs[i] || !shins[i] || !feet[i]) continue;
                thighRel[i] = inv * thighs[i].rotation;
                shinRel[i] = inv * shins[i].rotation;
                footRel[i] = rootInv * feet[i].rotation;
                Vector3 h = pivot.InverseTransformPoint(thighs[i].position);
                Vector3 k = pivot.InverseTransformPoint(shins[i].position);
                Vector3 a = pivot.InverseTransformPoint(feet[i].position);
                hipRest[i] = h;
                ankleRestRoot[i] = transform.InverseTransformPoint(feet[i].position);
                len1[i] = Vector2.Distance(new Vector2(h.z, h.y), new Vector2(k.z, k.y));
                len2[i] = Vector2.Distance(new Vector2(k.z, k.y), new Vector2(a.z, a.y));
                phi1Rest[i] = Mathf.Atan2(k.z - h.z, -(k.y - h.y));
                phi2Rest[i] = Mathf.Atan2(a.z - k.z, -(a.y - k.y));
            }
            if (lockPoint) lockRest = lockPoint.localPosition;
            rigReady = true;
        }

        // ================================================================== encounter API
        public override void BeginFight()
        {
            StopAllCoroutines();
            health = maxHealth;
            poise = maxPoise;
            phaseTwo = false;
            closePressure = spinCd = leapCd = tailCd = 0f;
            sprintCd = 2f;
            burrowCd = 9f;
            forcedMove = null;
            gameObject.SetActive(true);
            groundY = Blast.Floor(arenaCenter).y;
            ResetPose();
            State = Mode.Stalking;
            brain = StartCoroutine(Brain(true));
        }

        public override void ResetBoss()
        {
            StopAllCoroutines();
            State = Mode.Dormant;
            health = maxHealth;
            poise = maxPoise;
            if (digAudio) digAudio.Stop();
            SetBodyColliders(false);
            gameObject.SetActive(false);
        }

        public override void ForceNextMove(string move) => forcedMove = move;

        void ResetPose()
        {
            Pose(0f, 0f, 0f, 0f, 6f);
            tTailYaw = tTailPitch = tSink = 0f;
            pitch = bank = lift = jawAngle = tailYaw = tailPitch = sink = 0f;
            airY = 0f;
            vel = Vector3.zero;
            swim = 1f;
            kickAmp = 0f;
            tuckLegs = false;
            for (int i = 0; i < 3; i++) legAir[i] = legAirTarget[i] = 0f;
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
                    yield return Stalk(Random.Range(phaseTwo ? 0.45f : 0.8f, phaseTwo ? 1.2f : 1.9f));

                string move = forcedMove ?? PickMove(last);
                forcedMove = null;
                last = move;
                currentMove = move;
                State = Mode.Attacking;
                switch (move)
                {
                    case "Sprint": yield return Sprint(); break;
                    case "Bite": yield return BiteCombo(); break;
                    case "TailWhip": yield return TailWhip(); break;
                    case "TailSlam": yield return TailSlam(); break;
                    case "Spin": yield return Tornado(); break;
                    case "Leap": yield return SpinLeap(); break;
                    case "Burrow": yield return Burrow(); break;
                    default: yield return BiteCombo(); break;
                }
                if (State != Mode.Dead) State = Mode.Stalking;
                currentMove = "-";
                Pose(0f, 0f, 0f, 0f, 6f);
                tTailYaw = tTailPitch = 0f;
                swim = 1f;
            }
        }

        string PickMove(string last)
        {
            if (!PlayerAlive) return "Sprint";
            Vector3 rel = Rel;
            float dist = FlatDist;
            bool behind = rel.z < -0.8f;
            bool flank = Mathf.Abs(rel.x) > 1.5f && rel.z < 2.8f;
            var opts = new List<(string, float)>
            {
                ("Bite", dist < 6.5f && rel.z > 0.8f && Mathf.Abs(rel.x) < 3f ? 3f : 0f),
                ("TailWhip", tailCd <= 0f && dist < 6.5f && (behind || flank) ? 4f : 0f),
                ("TailSlam", tailCd <= 0f && behind && Mathf.Abs(rel.x) < 2.6f && dist < 7.5f ? 3.5f : 0f),
                ("Spin", spinCd <= 0f && dist < 6.5f ? 1.2f + closePressure * 3f : 0f),
                ("Leap", leapCd <= 0f && dist > 5f && dist < 13f ? 2f : 0f),
                ("Sprint", sprintCd <= 0f && dist > 6f ? 2.4f + (dist > 11f ? 2f : 0f) : 0f),
                ("Burrow", burrowCd <= 0f ? (dist > 9f ? 2.6f : 1.1f) : 0f),
            };
            float total = 0f;
            for (int i = 0; i < opts.Count; i++)
            {
                if (opts[i].Item1 == last) opts[i] = (opts[i].Item1, opts[i].Item2 * 0.3f);
                total += opts[i].Item2;
            }
            if (total <= 0f) return dist > 6.5f ? "Sprint" : "Bite";
            float r = Random.value * total;
            foreach (var (n, w) in opts) { if ((r -= w) <= 0f) return n; }
            return "Bite";
        }

        // ================================================================== movement primitives
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        Vector3 Heading => Quaternion.Euler(0, yaw, 0) * Vector3.forward;
        Vector3 Rel => transform.InverseTransformPoint(PlayerPos);
        float FlatDist => Flat(PlayerPos - transform.position).magnitude;

        void Pose(float p, float b, float l, float j, float s)
        {
            tPitch = p; tBank = b; tLift = l; tJaw = j; sharp = s;
        }

        void Face(Vector3 dir, float rate, float dt)
        {
            dir = Flat(dir);
            float prev = yaw;
            if (dir.sqrMagnitude > 1e-4f)
                yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, rate * dt);
            yawRate = Mathf.DeltaAngle(prev, yaw) / Mathf.Max(dt, 1e-4f);
        }

        void SetYaw(float y, float dt)
        {
            yawRate = Mathf.DeltaAngle(yaw, y) / Mathf.Max(dt, 1e-4f);
            yaw = y;
        }

        /// <summary>Accelerate toward a planar velocity and move, staying inside the arena.</summary>
        void Locomote(Vector3 desired, float dt, float accelMul = 1f)
        {
            vel = Vector3.MoveTowards(vel, Flat(desired), accel * accelMul * dt);
            Place(transform.position + vel * dt);
        }

        /// <summary>
        /// How far from the arena centre it may go: the arena, stretched to follow a player who
        /// slipped out through a gap in the walls (it won't let you wait it out from outside).
        /// </summary>
        float Limit => Mathf.Clamp(Flat(PlayerPos - arenaCenter).magnitude + 2.5f, arenaRadius, arenaRadius * 2.5f);

        void Place(Vector3 p)
        {
            Vector3 rel = Flat(p - arenaCenter);
            float limit = Limit;
            if (rel.magnitude > limit)
            {
                p = arenaCenter + rel.normalized * limit;
                vel -= Vector3.Project(vel, rel.normalized) * (Vector3.Dot(vel, rel) > 0f ? 1f : 0f);
            }
            p.y = groundY + airY;
            transform.position = p;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        void SlideBy(Vector3 d) => Place(transform.position + Flat(d));

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

        /// <summary>Slow down to a stop (skid) while doing something else.</summary>
        void Brake(float dt, float k = 1f) => Locomote(Vector3.zero, dt, k);

        float DistToEdge(Vector3 dir)
        {
            Vector3 o = Flat(transform.position - arenaCenter);
            Vector2 p = new Vector2(o.x, o.z), d = new Vector2(dir.x, dir.z).normalized;
            float lim = Limit;
            float b = Vector2.Dot(p, d), c = p.sqrMagnitude - lim * lim;
            float disc = b * b - c;
            return disc < 0f ? 0f : Mathf.Max(0f, -b + Mathf.Sqrt(disc));
        }

        // ================================================================== hits
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

        /// <summary>Is the player in a box in front of the root (z from..to, half width)?</summary>
        bool InFront(float z0, float z1, float halfW, float maxY = 3f)
        {
            Vector3 r = Rel;
            return r.z > z0 && r.z < z1 && Mathf.Abs(r.x) < halfW && r.y < maxY && r.y > -1.5f;
        }

        Vector3 TailTip => tail1 ? tail1.position + (tail1.position - (tail0 ? tail0.position : transform.position)).normalized * tailLen
                                 : transform.position - transform.forward * 4.5f;

        /// <summary>The player's body touches the tail (tail root -> fin -> tip)?</summary>
        bool TailTouches(float radius)
        {
            if (!PlayerAlive || !tail0 || !tail1) return false;
            Vector3 p = PlayerPos + Vector3.up * 0.9f;
            Vector3 a = tail0.position, b = tail1.position, c = TailTip;
            return SegDist(p, a, b) < radius || SegDist(p, b, c) < radius * 1.15f;
        }

        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
            return Vector3.Distance(p, a + ab * t);
        }

        // ================================================================== sounds + fx
        void Roar(float pitchScale = 1f, float vol = 1f) =>
            AudioManager.Play(Sfx.SharkRoar, transform.position + transform.forward * 3f, vol, Random.Range(0.96f, 1.04f) * pitchScale);

        void Squeak(Vector3 at, float vol = 0.8f) => AudioManager.Play(Sfx.SneakerSqueak, at, vol, Random.Range(0.9f, 1.15f));

        Vector3 FootPos(int i) => feet != null && i < feet.Length && feet[i] ? feet[i].position : transform.position;

        void Thud(float k)
        {
            Vector3 p = transform.position;
            FXManager.Spawn(FXType.Dust, p, Quaternion.identity, 2f * k);
            FXManager.Spawn(FXType.SlamDebris, p + transform.forward, Quaternion.identity, k);
            AudioManager.Play(Sfx.Slam, p, 0.8f * Mathf.Min(1f, k), 0.75f);
            CameraDirector.Instance?.Shake(0.45f * k, Vector3.down);
            Player?.GetComponent<InputHandling.PlayerInputReader>()?.Rumble(0.4f * k, 0.25f * k, 0.25f);
        }

        // ================================================================== stalk
        IEnumerator Stalk(float duration)
        {
            State = Mode.Stalking;
            currentMove = "Stalk";
            float t = 0f, strafe = Random.value < 0.5f ? 1f : -1f;
            Pose(0f, 0f, 0f, 4f, 5f);
            while (t < duration)
            {
                float dt = Time.deltaTime;
                t += dt;
                Vector3 to = Flat(PlayerPos - transform.position);
                float d = to.magnitude;
                Vector3 dir = d > 0.01f ? to / d : Heading;
                Vector3 want;
                if (!PlayerAlive) want = Vector3.zero;
                else if (d > 11f) want = dir * runSpeed * SpeedMul;
                else if (d > 6.5f) want = dir * walkSpeed * 1.3f * SpeedMul;
                else if (d < 3.6f) want = -dir * walkSpeed * 0.8f;
                else want = Vector3.Cross(Vector3.up, dir) * strafe * walkSpeed * 0.75f + dir * 0.6f;
                // don't strafe into the wall
                if (Flat(transform.position + want - arenaCenter).magnitude > Limit - 1f) strafe = -strafe;
                Locomote(want, dt);
                Face(d > 0.01f ? to : Heading, turnRate * 0.6f * SpeedMul, dt);
                tJaw = 4f + 3f * Mathf.Sin(Time.time * 2.6f);
                if (d < 4.5f) closePressure += dt * 0.25f;
                SetBodyColliders(vel.magnitude < 1.5f);
                // someone sneaking around the back: react now
                Vector3 r = Rel;
                if (t > 0.35f && PlayerAlive && r.z < -1.2f && d < 6f) break;
                yield return null;
            }
            SetBodyColliders(false);
        }

        // ================================================================== intro / phase two
        IEnumerator Intro()
        {
            // arrives underground: the fin circles you once, then it bursts out and screams
            Vector3 dir = Flat(arenaCenter - PlayerPos);
            if (dir.sqrMagnitude < 1f) dir = Vector3.forward;
            dir.Normalize();
            Vector3 start = arenaCenter + dir * (arenaRadius - 1.5f);
            yaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;
            Place(start);
            EnterBurrowInstant();
            yield return FinTravel(2.6f, 7f, false);
            yield return Erupt(false, 0.75f);
            yield return BellyFlop(false);
            // rear up on the back sneaker and scream, front legs pedalling
            currentMove = "Roar";
            yield return RearRoar(1.7f);
        }

        IEnumerator RearRoar(float time)
        {
            Face(PlayerPos - transform.position, 999f, 1f);
            Roar();
            CameraDirector.Instance?.Shake(0.35f, Vector3.down);
            legAirTarget[0] = legAirTarget[1] = 1f;
            kickAmp = 1f;
            yield return Hold(time, (k, dt) =>
            {
                float up = k < 0.85f ? Ease.OutCubic(Mathf.Clamp01(k / 0.25f)) : 1f - (k - 0.85f) / 0.15f;
                Pose(-26f * up, Mathf.Sin(k * 18f) * 3f * up, 0.35f * up, 62f * up, 7f);
                tTailPitch = -14f * up;
                swim = 2.2f;
                Face(PlayerPos - transform.position, 120f, dt);
            });
            legAirTarget[0] = legAirTarget[1] = 0f;
            kickAmp = 0f;
            swim = 1f;
            tTailPitch = 0f;
        }

        IEnumerator PhaseShift()
        {
            phaseTwo = true;
            currentMove = "Rage";
            State = Mode.Attacking;
            yield return RearRoar(1.5f);
        }

        // ================================================================== sprint: run through the arena and bite
        IEnumerator Sprint()
        {
            sprintCd = phaseTwo ? 6f : 8.5f;
            int passes = phaseTwo ? 3 : 2;
            // scuff the back sneaker like a bull: the warning
            Roar(1.15f, 0.7f);
            yield return Hold(0.95f / SpeedMul, (k, dt) =>
            {
                Brake(dt, 2f);
                Face(PlayerPos - transform.position, 260f, dt);
                Pose(7f, 0f, -0.22f, 18f, 8f);
                tTailYaw = Mathf.Sin(k * 25f) * 18f;
                if (Mathf.Repeat(k * 2.2f, 1f) < dt * 2.2f / (0.95f / SpeedMul))
                {
                    Squeak(FootPos(2));
                    FXManager.Spawn(FXType.DustSmall, FootPos(2) - transform.forward * 0.5f, Quaternion.LookRotation(-transform.forward), 1.4f);
                }
            });
            tTailYaw = 0f;

            for (int pass = 0; pass < passes && PlayerAlive; pass++)
            {
                currentMove = "Sprint";
                swim = 2.4f;
                bool lunged = false, contact = false;
                float t = 0f, dustT = 0f;
                float speed = sprintSpeed * SpeedMul;
                float homing = (phaseTwo ? 110f : 92f) * SpeedMul;
                // run at you; jaws open as you get close, lunge as it reaches you
                while (t < 3.6f)
                {
                    float dt = Time.deltaTime;
                    t += dt;
                    Vector3 lead = Player != null ? Player.motor.PlanarVelocity * 0.25f : Vector3.zero;
                    Vector3 to = Flat(PlayerPos + lead - transform.position);
                    float d = Flat(PlayerPos - transform.position).magnitude;
                    float ang = Vector3.Angle(Heading, to);
                    Face(to, homing, dt);
                    Locomote(Heading * speed, dt, 1.4f);
                    float open = Mathf.InverseLerp(10f, 5f, d);
                    Pose(4f, Mathf.Clamp(-yawRate * 0.12f, -12f, 12f), -0.12f, 6f + 44f * open, 9f);
                    if (!contact && InFront(-2.4f, 3.2f, 1.2f, 2.6f))
                        contact = HitPlayer(90f, ImpactWeight.Heavy, transform.forward + transform.right * Mathf.Sign(Rel.x + 0.01f), 50f);
                    if ((dustT -= dt) <= 0f) { dustT = 0.1f; FXManager.Spawn(FXType.DustSmall, transform.position - transform.forward * 1.2f, Quaternion.LookRotation(-transform.forward), 1.1f); }
                    if (d < 4.8f && ang < 38f) { lunged = true; break; }
                    if (t > 0.6f && ang > 100f) break;                         // ran past: turn round
                    if (DistToEdge(Heading) < 2.2f && t > 0.4f) break;          // the wall
                    yield return null;
                }
                if (lunged)
                {
                    currentMove = "Lunge";
                    AudioManager.Play(Sfx.WhooshHeavy, transform.position + transform.forward * 3f, 1f, 0.8f);
                    bool hit = false;
                    yield return Hold(0.2f, (k, dt) =>
                    {
                        vel = Heading * speed * 1.25f;
                        SlideBy(vel * dt);
                        Pose(13f, 0f, 0.05f, k < 0.6f ? 70f : -4f, 24f);
                        if (!hit && InFront(0.6f, 5.4f, 1.9f))
                            hit = HitPlayer(lungeDamage, ImpactWeight.Heavy, transform.forward + transform.right * Mathf.Sign(Rel.x + 0.01f) * 0.5f, 75f);
                    });
                    AudioManager.Play(Sfx.JawSnap, transform.position + transform.forward * 3.5f, 1f, 0.85f);
                    CameraDirector.Instance?.Shake(0.3f, transform.forward);
                }
                // run through, then skid round to face you again
                currentMove = "Skid";
                float through = lunged ? 0.35f : 0.1f;
                yield return Hold(through, (k, dt) => Locomote(Heading * speed, dt));
                Squeak(transform.position, 1f);
                AudioManager.Play(Sfx.Skid, transform.position, 0.7f, 1.2f);
                float side = Mathf.Sign(Vector3.Dot(Vector3.Cross(Heading, Flat(PlayerPos - transform.position)), Vector3.up) + 0.01f);
                yield return Hold(0.6f / SpeedMul, (k, dt) =>
                {
                    Brake(dt, 1.6f);
                    Face(PlayerPos - transform.position, 330f, dt);
                    Pose(-4f, -side * 14f * (1f - k), 0f, 10f, 10f);
                    tTailYaw = side * 40f * (1f - k);
                    if (Random.value < dt * 22f)
                        FXManager.Spawn(FXType.DustSmall, FootPos(Random.Range(0, 3)), Quaternion.LookRotation(Vector3.up), 1.3f);
                });
                tTailYaw = 0f;
            }
            // winded: the punish window
            swim = 0.6f;
            yield return Recover(1.4f / Mathf.Sqrt(SpeedMul), "Winded");
        }

        IEnumerator Recover(float time, string label, bool dizzy = false)
        {
            State = Mode.Recovering;
            currentMove = label;
            SetBodyColliders(true);
            yield return Hold(time, (k, dt) =>
            {
                Brake(dt, 2f);
                float pant = Mathf.Sin(k * time * 9f);
                if (dizzy) Pose(5f, Mathf.Sin(k * time * 6f) * 11f * (1f - k * 0.6f), -0.12f, 30f, 5f);
                else Pose(6f + pant * 1.5f, 0f, -0.15f, 26f + pant * 10f, 6f);
                if (dizzy) { tTailYaw = Mathf.Sin(k * time * 5f) * 20f; SetYaw(yaw + Mathf.Sin(k * time * 3f) * 30f * dt, dt); }
            });
            SetBodyColliders(false);
            tTailYaw = 0f;
            State = Mode.Attacking;
        }

        // ================================================================== bite combo
        IEnumerator BiteCombo()
        {
            int bites = phaseTwo ? 3 : 2;
            for (int i = 0; i < bites && PlayerAlive; i++)
            {
                currentMove = "Bite";
                float windup = (i == 0 ? 0.58f : 0.36f) / SpeedMul;
                AudioManager.Play(Sfx.SharkRoar, transform.position + transform.forward * 3f, 0.4f, 1.6f);
                yield return Hold(windup, (k, dt) =>
                {
                    Brake(dt, 2f);
                    Face(PlayerPos - transform.position, 210f * SpeedMul, dt);
                    Pose(-14f * Ease.OutCubic(k), 0f, 0.22f * k, 62f, 8f);
                    tTailYaw = Mathf.Sin(k * 14f) * 12f;
                    SlideBy(-transform.forward * dt * 0.6f);
                });
                AudioManager.Play(Sfx.WhooshHeavy, transform.position + transform.forward * 3f, 0.9f, 0.85f);
                bool hit = false;
                float reach = Mathf.Clamp(FlatDist - 2.6f, 1.2f, 3.2f);
                yield return Hold(0.15f, (k, dt) =>
                {
                    SlideBy(transform.forward * dt * reach / 0.15f);
                    Pose(11f, 0f, -0.08f, k < 0.55f ? 66f : -3f, 26f);
                    if (!hit && k > 0.3f && InFront(0.8f, 5.2f, 1.8f))
                        hit = HitPlayer(biteDamage * (i == bites - 1 ? 1.15f : 1f), ImpactWeight.Heavy, transform.forward, 60f);
                });
                AudioManager.Play(Sfx.JawSnap, transform.position + transform.forward * 3.5f, 1f);
                CameraDirector.Instance?.Shake(0.22f, transform.forward);
                FXManager.Spawn(FXType.DustSmall, transform.position + transform.forward * 3.4f, Quaternion.identity, 1.3f);
                yield return Hold(i == bites - 1 ? 0.6f : 0.14f, (k, dt) => { Brake(dt, 3f); Pose(5f * (1f - k), 0f, 0f, 8f, 6f); });
            }
            tTailYaw = 0f;
        }

        // ================================================================== tail
        IEnumerator TailWhip()
        {
            currentMove = "TailWhip";
            tailCd = phaseTwo ? 1.5f : 2.5f;
            Vector3 r0 = Rel;
            float side = Mathf.Abs(r0.x) > 0.4f ? Mathf.Sign(r0.x) : (Random.value < 0.5f ? -1f : 1f);
            // turn away (tail swings to the far side) and cock the tail
            float startYaw = yaw;
            Squeak(FootPos(2), 0.7f);
            AudioManager.Play(Sfx.SharkRoar, transform.position, 0.45f, 1.45f);
            yield return Hold(0.62f / SpeedMul, (k, dt) =>
            {
                Brake(dt, 2f);
                float e = Ease.OutCubic(k);
                SetYaw(startYaw + side * 32f * e, dt);
                Pose(0f, -side * 8f * e, -0.1f * e, 20f, 8f);
                tTailYaw = side * 52f * e;
                swim = 0f;
            });
            AudioManager.Play(Sfx.WhooshHeavy, TailTip, 1f, 0.6f);
            bool hit = false;
            float from = yaw;
            yield return Hold(0.36f, (k, dt) =>
            {
                SetYaw(from - side * 215f * Ease.InOutCubic(k), dt);
                Pose(0f, side * 12f, 0f, 16f, 16f);
                tTailYaw = Mathf.Lerp(side * 52f, -side * 30f, Ease.InOutCubic(k));
                if (!hit && TailTouches(1.35f))
                    hit = HitPlayer(tailDamage, ImpactWeight.Heavy, Flat(PlayerPos - transform.position) + transform.forward * -0.3f, 70f);
            });
            CameraDirector.Instance?.Shake(0.25f, transform.right * side);
            FXManager.Spawn(FXType.Dust, Blast.Ground(TailTip), Quaternion.identity, 1.4f);
            yield return Hold(0.55f, (k, dt) => { Pose(0f, 0f, 0f, 10f, 5f); tTailYaw = -side * 30f * (1f - k); });
            swim = 1f;
        }

        IEnumerator TailSlam()
        {
            currentMove = "TailSlam";
            tailCd = phaseTwo ? 1.5f : 2.5f;
            AudioManager.Play(Sfx.SharkRoar, transform.position, 0.5f, 1.7f);
            Squeak(FootPos(0), 0.6f);
            legAirTarget[2] = 1f;
            yield return Hold(0.78f / SpeedMul, (k, dt) =>
            {
                Brake(dt, 2f);
                float e = Ease.OutCubic(k);
                Pose(16f * e, Mathf.Sin(k * 22f) * 2f * k, 0.12f * e, 0f, 7f);
                tTailPitch = 58f * e;
                swim = 0f;
                if (Random.value < dt * 8f) FXManager.Spawn(FXType.DustSmall, transform.position + transform.forward * 3.2f, Quaternion.identity, 0.9f);
            });
            bool hit = false;
            yield return Hold(0.14f, (k, dt) =>
            {
                Pose(-7f, 0f, -0.12f, 0f, 28f);
                tTailPitch = Mathf.Lerp(58f, -16f, k);
                if (!hit && k > 0.5f)
                {
                    Vector3 r = Rel;
                    if (r.z < -1.6f && r.z > -7.6f && Mathf.Abs(r.x) < 2.3f && r.y < 2.6f)
                        hit = HitPlayer(tailSlamDamage, ImpactWeight.Slam, -transform.forward, 90f);
                }
            });
            legAirTarget[2] = 0f;
            Vector3 tip = Blast.Ground(transform.position - transform.forward * 5.2f);
            FXManager.Spawn(FXType.SlamRing, tip + Vector3.up * 0.05f, Quaternion.identity, 2.2f);
            FXManager.Spawn(FXType.Dust, tip, Quaternion.identity, 1.8f);
            FXManager.Spawn(FXType.SlamDebris, tip, Quaternion.identity, 1.1f);
            AudioManager.Play(Sfx.Slam, tip, 1f, 0.85f);
            CameraDirector.Instance?.Shake(0.5f, Vector3.down);
            yield return Hold(0.7f, (k, dt) => { Pose(-2f * (1f - k), 0f, 0f, 6f, 4f); tTailPitch = -16f * (1f - k); });
            tTailPitch = 0f;
            swim = 1f;
        }

        // ================================================================== spins
        IEnumerator Tornado()
        {
            currentMove = "Spin";
            closePressure = 0f;
            spinCd = phaseTwo ? 8f : 11f;
            float dir = Random.value < 0.5f ? 1f : -1f;
            // coil: crouch, tail wound round, sneakers squealing
            Roar(1.25f, 0.8f);
            float y0 = yaw;
            yield return Hold(0.85f / SpeedMul, (k, dt) =>
            {
                Brake(dt, 2f);
                float e = Ease.OutCubic(k);
                SetYaw(y0 - dir * 28f * e, dt);
                Pose(-3f, Mathf.Sin(k * 40f) * 2f * k, -0.28f * e, 24f, 9f);
                tTailYaw = -dir * 62f * e;
                swim = 0f;
                if (Random.value < dt * 9f) Squeak(FootPos(Random.Range(0, 3)), 0.5f);
                if (Random.value < dt * 14f)
                {
                    float a = Random.value * Mathf.PI * 2f;
                    FXManager.Spawn(FXType.DustSmall, transform.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 3.5f, Quaternion.identity, 1.1f);
                }
            });
            float dur = phaseTwo ? 2.4f : 1.9f, turns = phaseTwo ? 5f : 3.5f;
            float from = yaw, nextHit = 0f, lastRev = 0f;
            float drift = (phaseTwo ? 4.6f : 3.6f);
            yield return Hold(dur, (k, dt) =>
            {
                float spun = turns * 360f * Ease.InOutCubic(k);
                SetYaw(from + dir * spun, dt);
                if (spun - lastRev > 360f) { lastRev += 360f; AudioManager.Play(Sfx.WhooshHeavy, transform.position, 0.8f, 0.7f); }
                Vector3 to = Flat(PlayerPos - transform.position);
                vel = to.sqrMagnitude > 1f ? to.normalized * drift : Vector3.zero;
                SlideBy(vel * dt);
                Pose(0f, dir * 9f, 0f, 30f, 12f);
                tTailYaw = dir * 14f;
                if (Random.value < dt * 18f) FXManager.Spawn(FXType.DustSmall, TailTip.WithY(transform.position.y), Quaternion.identity, 1.2f);
                if (Time.time > nextHit && PlayerAlive && (TailTouches(1.4f) || FlatDist < 2.3f))
                {
                    if (HitPlayer(spinDamage, ImpactWeight.Heavy, Flat(PlayerPos - transform.position), 45f)) nextHit = Time.time + 0.55f;
                }
            });
            CameraDirector.Instance?.Shake(0.3f, Vector3.down);
            vel = Vector3.zero;
            yield return Recover(1.5f, "Dizzy", true);
            swim = 1f;
        }

        IEnumerator SpinLeap()
        {
            currentMove = "Leap";
            leapCd = phaseTwo ? 4.5f : 6.5f;
            Roar(1.35f, 0.6f);
            yield return Hold(0.62f / SpeedMul, (k, dt) =>
            {
                Brake(dt, 2f);
                Face(PlayerPos - transform.position, 220f, dt);
                Pose(-6f * k, 0f, -0.36f * Ease.OutCubic(k), 34f, 9f);
                tTailYaw = Mathf.Sin(k * 20f) * 16f;
            });
            Vector3 start = transform.position;
            Vector3 land = PlayerPos + (Player != null ? Player.motor.PlanarVelocity * 0.35f : Vector3.zero);
            Vector3 lr = Flat(land - arenaCenter);
            if (lr.magnitude > Limit - 1f) land = arenaCenter + lr.normalized * (Limit - 1f);
            land.y = groundY;
            AudioManager.Play(Sfx.Jump, start, 1f, 0.5f);
            Squeak(start, 1f);
            FXManager.Spawn(FXType.Dust, start, Quaternion.identity, 1.8f);
            tuckLegs = true;
            for (int i = 0; i < 3; i++) legAirTarget[i] = 1f;
            float dir = Random.value < 0.5f ? 1f : -1f, y0 = yaw;
            const float air = 0.85f;
            yield return Hold(air, (k, dt) =>
            {
                airY = Mathf.Sin(k * Mathf.PI) * 3.6f;
                Vector3 p = Vector3.Lerp(start, land, k);
                SetYaw(y0 + dir * 360f * Ease.InOutCubic(k), dt);
                Pose(Mathf.Lerp(-12f, 8f, k), 0f, 0f, 40f, 10f);
                tTailYaw = dir * 10f;
                Place(p);
            });
            airY = 0f;
            Place(land);
            tuckLegs = false;
            for (int i = 0; i < 3; i++) legAirTarget[i] = 0f;
            Thud(1.2f);
            FXManager.Spawn(FXType.SlamRing, transform.position + Vector3.up * 0.05f, Quaternion.identity, 3.2f);
            if (PlayerAlive && FlatDist < 4.4f && Rel.y < 2.5f)
                HitPlayer(leapDamage, ImpactWeight.Slam, Flat(PlayerPos - transform.position), 90f);
            yield return Recover(0.85f, "Landed");
        }

        // ================================================================== burrow
        IEnumerator Burrow()
        {
            currentMove = "Dive";
            burrowCd = phaseTwo ? 10f : 14f;
            // rear up, hop forward and dive nose first
            Roar(1.1f, 0.6f);
            yield return Hold(0.5f / SpeedMul, (k, dt) =>
            {
                Brake(dt, 2f);
                Pose(-20f * Ease.OutCubic(k), 0f, 0.25f * k, 30f, 8f);
                legAirTarget[0] = legAirTarget[1] = k;
            });
            yield return Dive(3.2f);

            int eruptions = phaseTwo ? 2 : 1;
            for (int e = 0; e < eruptions; e++)
            {
                yield return FinTravel(e == 0 ? Random.Range(2f, 2.8f) : 1.1f, e == 0 ? 6.5f : 4.5f, true);
                bool last = e == eruptions - 1;
                yield return Erupt(true, phaseTwo ? 0.62f : 0.78f);
                if (!last) yield return Dive(2.6f);
            }
            yield return BellyFlop(true);
            yield return Recover(1.5f, "Stranded");
        }

        void EnterBurrowInstant()
        {
            State = Mode.Burrowed;
            SetBodyColliders(false);
            tSink = sink = 1f;
            tuckLegs = true;
            for (int i = 0; i < 3; i++) legAir[i] = legAirTarget[i] = 1f;
            Pose(0f, 0f, 0f, 0f, 6f);
            pitch = 0f;
            if (digAudio && !digAudio.isPlaying) digAudio.Play();
        }

        IEnumerator Dive(float distance)
        {
            currentMove = "Dive";
            tuckLegs = true;
            for (int i = 0; i < 3; i++) legAirTarget[i] = 1f;
            Vector3 start = transform.position;
            Vector3 end = start + Heading * distance;
            AudioManager.Play(Sfx.WhooshHeavy, start, 0.8f, 0.7f);
            bool splashed = false;
            yield return Hold(0.55f, (k, dt) =>
            {
                airY = Mathf.Sin(k * Mathf.PI) * 1.4f * (1f - k * 0.5f);
                Place(Vector3.Lerp(start, end, k));
                Pose(Mathf.Lerp(-20f, 72f, Ease.InOutCubic(k)), 0f, 0f, 20f, 18f);
                tSink = Mathf.Clamp01((k - 0.45f) / 0.55f) * 0.7f;
                tTailPitch = -10f;
                if (!splashed && k > 0.55f)
                {
                    splashed = true;
                    Vector3 hole = Blast.Ground(transform.position + Heading * 1.5f);
                    FXManager.Spawn(FXType.SlamDebris, hole, Quaternion.identity, 1.5f);
                    FXManager.Spawn(FXType.Dust, hole, Quaternion.identity, 2.2f);
                    AudioManager.Play(Sfx.Burrow, hole, 1f, 1f);
                    CameraDirector.Instance?.Shake(0.35f, Vector3.down);
                }
            });
            airY = 0f;
            State = Mode.Burrowed;
            SetBodyColliders(false);
            tSink = 1f;
            tTailPitch = 0f;
            Pose(0f, 0f, 0f, 0f, 5f);
            if (digAudio && !digAudio.isPlaying) digAudio.Play();
            yield return Hold(0.2f);
        }

        /// <summary>
        /// Underground: the fin circles the player (circleTime), then cuts straight in. Ends under
        /// where the player was when it committed.
        /// </summary>
        IEnumerator FinTravel(float circleTime, float radius, bool homeIn)
        {
            currentMove = "Fin";
            State = Mode.Burrowed;
            swim = 2.6f;
            float t = 0f, trailT = 0f, chunkT = 0f;
            float speed = finSpeed * SpeedMul;
            float dirSign = Random.value < 0.5f ? 1f : -1f;
            while (t < circleTime)
            {
                float dt = Time.deltaTime;
                t += dt;
                Vector3 rel = Flat(transform.position - PlayerPos);
                float a = Mathf.Atan2(rel.z, rel.x) + dirSign * 0.75f;
                Vector3 carrot = PlayerPos + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius;
                Face(carrot - transform.position, 240f, dt);
                Locomote(Heading * speed, dt, 2f);
                FinTrail(dt, ref trailT, ref chunkT);
                yield return null;
            }
            if (homeIn)
            {
                // cut in at you
                t = 0f;
                while (t < 1.8f && PlayerAlive)
                {
                    float dt = Time.deltaTime;
                    t += dt;
                    Vector3 to = Flat(PlayerPos - transform.position);
                    if (to.magnitude < 1.1f) break;
                    Face(to, 300f, dt);
                    Locomote(Heading * speed * 1.15f, dt, 2.5f);
                    FinTrail(dt, ref trailT, ref chunkT);
                    yield return null;
                }
            }
            vel = Vector3.zero;
        }

        void FinTrail(float dt, ref float trailT, ref float chunkT)
        {
            Vector3 fin = transform.position + Heading * 0.2f;
            if ((trailT -= dt) <= 0f)
            {
                trailT = 0.06f;
                FXManager.Spawn(FXType.DustSmall, fin + Vector3.up * 0.05f, Quaternion.LookRotation(-Heading + Vector3.up * 0.5f), 1.3f);
            }
            if ((chunkT -= dt) <= 0f)
            {
                chunkT = 0.22f;
                FXManager.Spawn(FXType.SlamDebris, fin, Quaternion.identity, 0.45f);
            }
        }

        /// <summary>The dirt boils for <paramref name="tell"/> seconds, then it bursts out jaws first.</summary>
        IEnumerator Erupt(bool damage, float tell)
        {
            currentMove = "Erupt";
            Vector3 spot = transform.position;
            // the fin slips under; the ground boils where it will come up
            tSink = 1.2f;
            AudioManager.Play(Sfx.Burrow, spot, 0.7f, 0.65f);
            yield return Hold(tell, (k, dt) =>
            {
                Brake(dt, 3f);
                if (PlayerAlive) Face(PlayerPos - transform.position, 90f, dt);
                if (Random.value < dt * 26f)
                {
                    Vector2 c = Random.insideUnitCircle * 1.6f;
                    FXManager.Spawn(FXType.DustSmall, spot + new Vector3(c.x, 0.05f, c.y), Quaternion.LookRotation(Vector3.up), 1f + k);
                }
                if (Random.value < dt * 5f) FXManager.Spawn(FXType.SlamDebris, spot, Quaternion.identity, 0.35f + k * 0.3f);
                CameraDirector.Instance?.Shake(0.05f + 0.1f * k, Vector3.down);
            });
            if (digAudio) digAudio.Stop();
            // burst out: nose to the sky, jaws wide, snap at the top
            State = Mode.Attacking;
            Roar(1.05f, 0.9f);
            AudioManager.Play(Sfx.Burrow, spot, 1f, 1.1f);
            FXManager.Spawn(FXType.SlamDebris, spot, Quaternion.identity, 1.8f);
            FXManager.Spawn(FXType.Dust, spot, Quaternion.identity, 2.6f);
            FXManager.Spawn(FXType.SlamRing, spot + Vector3.up * 0.05f, Quaternion.identity, eruptRadius * 0.9f);
            CameraDirector.Instance?.Shake(0.6f, Vector3.up);
            bool hit = false;
            pitch = -78f;
            yield return Hold(0.32f, (k, dt) =>
            {
                tSink = 0f;
                sink = Mathf.Lerp(1.2f, 0f, Ease.OutCubic(k));
                airY = Ease.OutCubic(k) * 1.3f;
                Pose(-80f, 0f, 0f, k < 0.7f ? 75f : -3f, 30f);
                pitch = -80f;
                if (damage && !hit && PlayerAlive)
                {
                    Vector3 d = PlayerPos - spot;
                    if (Flat(d).magnitude < eruptRadius && d.y < 4f)
                        hit = HitPlayer(eruptDamage, ImpactWeight.Slam, Flat(d), 100f);
                }
            });
            AudioManager.Play(Sfx.JawSnap, transform.position + Vector3.up * 4f, 1f, 0.8f);
            yield return Hold(0.18f, (k, dt) => { airY = 1.3f + k * 0.15f; Pose(-80f, 0f, 0f, -3f, 30f); });
        }

        /// <summary>Topple forward from the eruption and slap down on the belly.</summary>
        IEnumerator BellyFlop(bool damage)
        {
            currentMove = "BellyFlop";
            tuckLegs = false;
            for (int i = 0; i < 3; i++) legAirTarget[i] = 1f;
            kickAmp = 0.6f;
            Vector3 start = transform.position;
            Vector3 fwd = Heading;
            yield return Hold(0.42f, (k, dt) =>
            {
                float e = Ease.InCubic(k);
                airY = Mathf.Lerp(1.45f, 0f, e);
                Pose(Mathf.Lerp(-80f, 6f, e), 0f, 0f, 30f, 30f);
                pitch = tPitch;
                Place(start + fwd * 1.6f * e);
            });
            airY = 0f;
            for (int i = 0; i < 3; i++) legAirTarget[i] = 0f;
            kickAmp = 0f;
            Thud(1.1f);
            FXManager.Spawn(FXType.SlamRing, transform.position + transform.forward * 1.2f + Vector3.up * 0.05f, Quaternion.identity, 2.4f);
            if (damage && PlayerAlive && InFront(-2.5f, 4.6f, 2f, 2.5f))
                HitPlayer(85f, ImpactWeight.Heavy, transform.forward, 50f);
            Pose(4f, 0f, -0.2f, 20f, 6f);
            swim = 1f;
        }

        // ================================================================== stance break / death
        void OnStanceBreak()
        {
            if (brain != null) StopCoroutine(brain);
            brain = StartCoroutine(StaggerRoutine());
        }

        IEnumerator StaggerRoutine()
        {
            currentMove = "Stagger";
            State = Mode.Staggered;
            airY = 0f;
            tSink = 0f;
            tuckLegs = false;
            tTailYaw = tTailPitch = 0f;
            closePressure = 0f;
            vel = Vector3.zero;
            Place(transform.position);
            if (digAudio) digAudio.Stop();
            SetBodyColliders(true);
            AudioManager.Play(Sfx.Stagger, transform.position, 1f, 0.7f);
            AudioManager.Play(Sfx.SharkRoar, transform.position, 0.8f, 0.8f);
            float side = Random.value < 0.5f ? -1f : 1f;
            // flops onto its side, sneakers pedalling in the air
            for (int i = 0; i < 3; i++) legAirTarget[i] = 1f;
            kickAmp = 1f;
            swim = 1.6f;
            yield return Hold(0.4f, (k, dt) => Pose(0f, side * 96f * Ease.OutBack(k), -0.98f * k, 34f, 14f));
            Thud(0.8f);
            float t = 0f;
            while (t < staggerTime)
            {
                float dt = Time.deltaTime;
                t += dt;
                kickAmp = Mathf.Lerp(1f, 0.35f, t / staggerTime);
                Pose(0f, side * 96f, -0.98f, 30f + Mathf.Sin(t * 5f) * 8f, 8f);
                if (Random.value < dt * 3f)
                    FXManager.Spawn(FXType.HitSpark, transform.position + Vector3.up * 1.2f + transform.forward * Random.Range(-2f, 2f), Quaternion.LookRotation(Vector3.up), 0.6f);
                Place(transform.position);
                yield return null;
            }
            // scramble back up
            Squeak(transform.position, 1f);
            yield return Hold(0.5f, (k, dt) => Pose(0f, side * 96f * (1f - Ease.InOutCubic(k)), -0.98f * (1f - k), 20f, 12f));
            for (int i = 0; i < 3; i++) legAirTarget[i] = 0f;
            kickAmp = 0f;
            swim = 1f;
            poise = maxPoise;
            SetBodyColliders(false);
            State = Mode.Stalking;
            Pose(0f, 0f, 0f, 0f, 6f);
            brain = StartCoroutine(Brain(false));
        }

        void Die()
        {
            StopAllCoroutines();
            brain = null;
            State = Mode.Dead;
            currentMove = "Dead";
            if (digAudio) digAudio.Stop();
            SetBodyColliders(true);
            RaiseFelled();
            StartCoroutine(DeathRoutine());
        }

        IEnumerator DeathRoutine()
        {
            TimeDirector.SlowMo(1.1f, 0.3f);
            AudioManager.Play(Sfx.SharkRoar, transform.position, 1f, 0.65f);
            tSink = 0f;
            tuckLegs = false;
            tTailYaw = tTailPitch = 0f;
            float startAir = airY;
            float side = Random.value < 0.5f ? -1f : 1f;
            for (int i = 0; i < 3; i++) legAirTarget[i] = 1f;
            kickAmp = 1f;
            yield return Hold(0.6f, (k, dt) =>
            {
                airY = Mathf.Lerp(startAir, 0f, k);
                Pose(Mathf.Lerp(pitch, 4f, k), side * 98f * Ease.OutCubic(k), -0.98f * k, 50f, 10f);
            });
            airY = 0f;
            Thud(1f);
            float t = 0f;
            while (t < 2.6f)
            {
                float dt = Time.deltaTime;
                t += dt;
                kickAmp = Mathf.Max(0f, 1f - t / 2.2f) * (0.6f + 0.4f * Mathf.Sin(t * 13f));
                swim = Mathf.Max(0f, 1.5f - t);
                Place(transform.position);
                yield return null;
            }
            kickAmp = 0f;
            swim = 0f;
        }

        // ================================================================== damage
        public override bool ReceiveHit(in DamageInfo info)
        {
            if (!IsAlive || State == Mode.Burrowed) return false;
            lastHitTime = Time.time;
            closePressure += info.explosive ? 0.12f : 0.2f;
            health = Mathf.Max(0f, health - info.damage);
            bool broke = false;
            if (State != Mode.Staggered && !info.critical)
            {
                poise -= info.poiseDamage;
                if (poise <= 0f) { poise = maxPoise; broke = true; }
            }
            flash = 1f;
            FXManager.Spawn(FXType.HitSpark, info.point, Quaternion.LookRotation(-info.direction + Vector3.up * 0.3f), info.weight == ImpactWeight.Light ? 1f : 1.4f);
            AudioManager.Play(Sfx.DummyHit, info.point, 0.9f, info.weight == ImpactWeight.Light ? 0.7f : 0.55f);
            RaiseDamaged(info, broke);
            if (health <= 0f) { Die(); return true; }
            if (broke) OnStanceBreak();
            return true;
        }

        void SetBodyColliders(bool on)
        {
            if (bodyColliders == null) return;
            foreach (var c in bodyColliders) if (c) c.enabled = on;
        }

        // ================================================================== animation
        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || State == Mode.Dormant) return;
            spinCd -= dt; leapCd -= dt; sprintCd -= dt; burrowCd -= dt; tailCd -= dt;
            closePressure = Mathf.Max(0f, closePressure - dt * 0.12f);
            if (IsAlive && State != Mode.Staggered && Time.time - lastHitTime > poiseRegenDelay)
                poise = Mathf.MoveTowards(poise, maxPoise, maxPoise * 0.25f * dt);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || State == Mode.Dormant || !rigReady) return;

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            // measured motion drives the gait
            Vector3 moved = Flat(transform.position - prevPos) / dt;
            prevPos = transform.position;
            if (moved.magnitude > 40f) moved = Vector3.zero;     // teleports (spawn, intro)

            // smooth the pose
            float k = Ease.Damp(sharp, dt);
            pitch = Mathf.Lerp(pitch, tPitch, k);
            bank = Mathf.Lerp(bank, tBank, k);
            lift = Mathf.Lerp(lift, tLift, k);
            tailPitch = Mathf.Lerp(tailPitch, tTailPitch, Ease.Damp(14f, dt));
            tailYaw = Mathf.Lerp(tailYaw, tTailYaw, Ease.Damp(16f, dt));
            sink = Mathf.Lerp(sink, tSink, Ease.Damp(tSink > sink ? 7f : 12f, dt));
            jawAngle = Mathf.Lerp(jawAngle, tJaw, Ease.Damp(tJaw > jawAngle ? 14f : 30f, dt));
            for (int i = 0; i < 3; i++) legAir[i] = Mathf.MoveTowards(legAir[i], legAirTarget[i], dt * 5f);

            float speed = moved.magnitude;
            float turning = Mathf.Abs(yawRate) * Mathf.Deg2Rad * 1.3f;
            float gaitSpeed = State == Mode.Burrowed ? 0f : speed + Mathf.Min(turning, 6f);

            // body: bob with the stride, lean into turns, sink underground
            float bob = 0f;
            float amp = Mathf.Clamp01(gaitSpeed / 1.5f);
            if (amp > 0f) bob = -Mathf.Abs(Mathf.Sin(legPhase[0] * Mathf.PI * 2f)) * 0.07f * amp - (speed > 9f ? 0.12f : 0f);
            pivot.localPosition = pivotRestLocal + Vector3.up * (lift + bob - sink * burrowDepth);
            pivot.localRotation = Quaternion.Euler(pitch, 0f, bank);

            if (jaw) jaw.rotation = pivot.rotation * Quaternion.AngleAxis(jawAngle, Vector3.right) * jawRel;

            // tail: a swimming wave that grows with speed, plus whatever the move is doing
            float wave = swim * (0.35f + Mathf.Clamp01(speed / 8f)) * 16f;
            float w0 = Mathf.Sin(Time.time * (5f + speed * 0.6f)) * wave;
            float w1 = Mathf.Sin(Time.time * (5f + speed * 0.6f) - 1.1f) * wave * 1.4f;
            if (tail0) tail0.rotation = pivot.rotation * Quaternion.Euler(tailPitch, tailYaw * 0.6f + w0, 0f) * tail0Rel;
            if (tail1) tail1.rotation = pivot.rotation * Quaternion.Euler(tailPitch * 1.3f, tailYaw + w0 + w1, 0f) * tail1Rel;

            AnimateLegs(dt, moved, gaitSpeed);

            if (lockPoint) lockPoint.localPosition = Vector3.Lerp(lockRest, new Vector3(0f, 0.7f, -0.2f), Mathf.Clamp01(sink));

            if (digAudio && digAudio.isPlaying)
                digAudio.volume = Mathf.Clamp01(speed / 8f) * (AudioManager.Instance != null ? AudioManager.Instance.sfxVolume : 1f);

            UpdateFlash(dt);
        }

        void AnimateLegs(float dt, Vector3 moved, float gaitSpeed)
        {
            if (thighs == null || thighs.Length < 3) return;
            // a three-beat scurry: the stance sweeps back at body speed, the swing arcs forward
            const float duty = 0.42f;
            float amp = Mathf.Clamp01(gaitSpeed / 1.2f);
            float half = Mathf.Clamp(0.1f + gaitSpeed * 0.045f, 0.1f, 0.56f) * amp;
            float cycleDist = Mathf.Max(0.3f, 2f * half / duty);
            float rate = amp > 0.01f ? gaitSpeed / cycleDist : 1.5f;
            Vector3 moveLocal = transform.InverseTransformDirection(moved);
            moveLocal.y = 0f;
            Vector3 stepDir = moveLocal.sqrMagnitude > 0.04f ? moveLocal.normalized : Vector3.forward;
            Quaternion flatRot = Quaternion.Euler(0f, yaw, 0f);
            float t = Time.time;

            for (int i = 0; i < 3; i++)
            {
                if (!thighs[i] || !shins[i] || !feet[i]) continue;
                float prevPhase = legPhase[i];
                legPhase[i] = Mathf.Repeat(legPhase[i] + rate * dt, 1f);
                float ph = legPhase[i];
                float off, lift = 0f;
                bool swing = ph >= duty;
                if (!swing) off = half * (1f - 2f * ph / duty);
                else
                {
                    float s = (ph - duty) / (1f - duty);
                    off = -half + 2f * half * (s * s * (3f - 2f * s));
                    lift = Mathf.Sin(s * Mathf.PI) * (0.12f + half * 0.55f);
                }
                // footfall: a heavy rubber thud (and a squeak now and then)
                if (legSwing[i] && !swing && amp > 0.5f && State != Mode.Burrowed && legAir[i] < 0.2f)
                {
                    AudioManager.Play(Sfx.Footstep, FootPos(i), 0.55f, 0.55f);
                    if (gaitSpeed > 9f) FXManager.Spawn(FXType.DustSmall, FootPos(i), Quaternion.identity, 0.9f);
                    if (Random.value < 0.06f) Squeak(FootPos(i), 0.35f);
                }
                legSwing[i] = swing;

                Vector3 groundRoot = new Vector3(ankleRestRoot[i].x, ankleRestRoot[i].y + lift, ankleRestRoot[i].z) + stepDir * off;
                Vector3 groundWorld = transform.TransformPoint(groundRoot) - Vector3.up * airY;
                Vector3 groundLocal = pivot.InverseTransformPoint(groundWorld);

                // in the air: tucked, hanging, or pedalling
                Vector3 h = hipRest[i];
                float L = len1[i] + len2[i];
                Vector3 airLocal;
                if (tuckLegs) airLocal = h + new Vector3(0f, -L * 0.55f, 0.35f);
                else
                {
                    float w = t * 11f + i * 2.1f;
                    airLocal = h + new Vector3(0f, -L * (0.8f - 0.25f * kickAmp * (0.5f + 0.5f * Mathf.Sin(w))), 0.38f * kickAmp * Mathf.Cos(w));
                }
                Vector3 target = Vector3.Lerp(groundLocal, airLocal, legAir[i]);
                SolveLeg(i, target);

                // feet stay flat on the ground (or follow the body in the air), toe lifting through the swing
                float toe = swing ? -Mathf.Sin((ph - duty) / (1f - duty) * Mathf.PI) * 25f * amp : 0f;
                Quaternion ground = flatRot * Quaternion.AngleAxis(toe, Vector3.right) * footRel[i];
                Quaternion body = pivot.rotation * Quaternion.AngleAxis(20f * kickAmp * Mathf.Sin(t * 11f + i), Vector3.right) * footRel[i];
                feet[i].rotation = Quaternion.Slerp(ground, body, legAir[i]);
            }
        }

        /// <summary>Two-bone IK in the leg's sagittal plane (pivot space), knee bending forward.</summary>
        void SolveLeg(int i, Vector3 target)
        {
            Vector3 h = hipRest[i];
            float L1 = len1[i], L2 = len2[i];
            Vector2 d = new Vector2(target.z - h.z, target.y - h.y);   // (forward, up)
            float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(L1 - L2) + 0.02f, L1 + L2 - 0.002f);
            float alpha = Mathf.Atan2(d.x, -d.y);
            float a1 = Mathf.Acos(Mathf.Clamp((L1 * L1 + dist * dist - L2 * L2) / (2f * L1 * dist), -1f, 1f));
            float phi1 = alpha + a1;
            Vector2 knee = new Vector2(Mathf.Sin(phi1), -Mathf.Cos(phi1)) * L1;
            Vector2 foot = new Vector2(Mathf.Sin(alpha), -Mathf.Cos(alpha)) * dist;
            Vector2 shin = foot - knee;
            float phi2 = Mathf.Atan2(shin.x, -shin.y);
            thighs[i].rotation = pivot.rotation * Quaternion.AngleAxis(-(phi1 - phi1Rest[i]) * Mathf.Rad2Deg, Vector3.right) * thighRel[i];
            shins[i].rotation = pivot.rotation * Quaternion.AngleAxis(-(phi2 - phi2Rest[i]) * Mathf.Rad2Deg, Vector3.right) * shinRel[i];
        }

        void UpdateFlash(float dt)
        {
            flash = Mathf.MoveTowards(flash, 0f, dt * 6f);
            if (!(flash > 0f || flashWasOn) || flashRenderers == null) return;
            flashWasOn = flash > 0f;
            Color c = new Color(1f, 0.85f, 0.6f) * (flash * flash * 0.8f);
            foreach (var r in flashRenderers)
            {
                if (!r) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !mats[i].HasProperty(EmissionId) || mats[i].GetColor(EmissionId).maxColorComponent > 0.01f) continue;
                    r.GetPropertyBlock(mpb, i);
                    mpb.SetColor(EmissionId, c);
                    r.SetPropertyBlock(mpb, i);
                }
            }
        }
    }

    static class TralaleroVecExt
    {
        public static Vector3 WithY(this Vector3 v, float y) { v.y = y; return v; }
    }
}
