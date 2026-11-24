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
    /// Shark: a great shark that swims through the air just above the dirt. A ground boss, every
    /// attack announced by the body (no ground markers):
    ///  - Sprint: lashes the dirt with its tail, then charges through the arena at you and lunges jaws first
    ///    as it passes; skids round and goes again (2 passes, 3 in phase 2), then hangs there panting.
    ///  - Bite: up close, rears its head and snaps forward (2 bites, 3 in phase 2).
    ///  - Tail whip (flank / behind): turns away with the tail cocked, then whips the body round.
    ///  - Tail slam (behind): the nose dips, the tail rises high, and it crashes down.
    ///  - Shark tornado (you hug it): coils, then spins on the spot, drifting at you, and reels dizzy.
    ///  - Spin leap (mid range): crouches, leaps spinning and belly-slams where you stood.
    ///  - Burrow: dives into the dirt; only the dorsal fin cuts through the arena, circling you. Where the
    ///    dirt starts to boil, it erupts jaws first (twice in phase 2), then belly-flops, open to blows.
    /// Clubs and arrows wear its stance down: it flops onto its side, tail thrashing, open to criticals.
    /// Animation is procedural: the tail swims, the body sways and swells, and a hull of extreme points keeps
    /// it out of the ground (a raised head or a dropped tail props the body up instead of clipping).
    /// </summary>
    public class SharkBoss : BossBase
    {
        public enum Mode { Dormant, Stalking, Attacking, Burrowed, Recovering, Staggered, Dead }

        public string displayName = "Shark";

        [Header("Rig")]
        [Tooltip("Body centre: pitch, roll, lift and the burrow sink happen here.")]
        public Transform pivot;
        public Transform jaw;
        public Transform tail0, tail1;
        [Tooltip("Extreme points of the rigid parts (built by the editor): the lowest stays above the ground.")]
        public HullPoint[] hull;
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
        public float burrowDepth = 2.3f;

        [System.Serializable]
        public struct HullPoint { public Transform bone; public Vector3 local; }

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
        float thrash;                    // 0 = calm, 1 = tail lashing hard (stagger, death, warnings)
        bool keepOffGround = true;       // off while it dives, erupts and flops (it goes into the dirt on purpose)
        float groundWeight = 1f;
        float closePressure, spinCd, leapCd, sprintCd, burrowCd, tailCd;

        // rig rest data
        Vector3 pivotRestLocal;
        Quaternion jawRel, tail0Rel, tail1Rel;
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
            thrash = 0f;
            keepOffGround = true;
            groundWeight = 1f;
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

        /// <summary>The tail slaps the dirt: a low thump and a puff of dust where the tail tip is.</summary>
        void TailSlap(float vol = 0.6f)
        {
            Vector3 at = Blast.Ground(TailTip);
            AudioManager.Play(Sfx.Slam, at, vol * 0.45f, Random.Range(1.25f, 1.45f));
            FXManager.Spawn(FXType.DustSmall, at, Quaternion.LookRotation(Vector3.up), 1.2f);
        }

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
            // rear up on its tail and scream
            currentMove = "Roar";
            yield return RearRoar(1.7f);
        }

        IEnumerator RearRoar(float time)
        {
            Face(PlayerPos - transform.position, 999f, 1f);
            Roar();
            CameraDirector.Instance?.Shake(0.35f, Vector3.down);
            thrash = 0.5f;
            yield return Hold(time, (k, dt) =>
            {
                float up = k < 0.85f ? Ease.OutCubic(Mathf.Clamp01(k / 0.25f)) : 1f - (k - 0.85f) / 0.15f;
                Pose(-34f * up, Mathf.Sin(k * 18f) * 3f * up, 0.5f * up, 62f * up, 7f);
                tTailPitch = -10f * up;
                swim = 2.2f;
                Face(PlayerPos - transform.position, 120f, dt);
            });
            thrash = 0f;
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
            // drops low and lashes the dirt with its tail like a bull pawing: the warning
            Roar(1.15f, 0.7f);
            yield return Hold(0.95f / SpeedMul, (k, dt) =>
            {
                Brake(dt, 2f);
                Face(PlayerPos - transform.position, 260f, dt);
                Pose(7f, 0f, -0.22f, 18f, 8f);
                tTailYaw = Mathf.Sin(k * 25f) * 26f;
                tTailPitch = -12f;
                swim = 0.3f;
                if (Mathf.Repeat(k * 2.2f, 1f) < dt * 2.2f / (0.95f / SpeedMul)) TailSlap(0.8f);
            });
            tTailYaw = tTailPitch = 0f;

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
                AudioManager.Play(Sfx.Skid, transform.position, 0.7f, 1.05f);
                float side = Mathf.Sign(Vector3.Dot(Vector3.Cross(Heading, Flat(PlayerPos - transform.position)), Vector3.up) + 0.01f);
                yield return Hold(0.6f / SpeedMul, (k, dt) =>
                {
                    Brake(dt, 1.6f);
                    Face(PlayerPos - transform.position, 330f, dt);
                    Pose(-4f, -side * 14f * (1f - k), 0f, 10f, 10f);
                    tTailYaw = side * 40f * (1f - k);
                    if (Random.value < dt * 22f)
                        FXManager.Spawn(FXType.DustSmall, Blast.Ground(TailTip), Quaternion.LookRotation(Vector3.up), 1.3f);
                });
                tTailYaw = 0f;
            }
            // winded: the punish window
            swim = 0.6f;
            yield return Recover(1.4f / Mathf.Sqrt(SpeedMul), "Winded");
        }

        IEnumerator Recover(float time, string label, bool dizzy = false, bool grounded = false)
        {
            State = Mode.Recovering;
            currentMove = label;
            SetBodyColliders(true);
            yield return Hold(time, (k, dt) =>
            {
                Brake(dt, 2f);
                float pant = Mathf.Sin(k * time * 9f);
                if (dizzy) Pose(5f, Mathf.Sin(k * time * 6f) * 11f * (1f - k * 0.6f), -0.12f, 30f, 5f);
                else if (grounded) Pose(3f + pant * 1.5f, pant * 2f, -0.6f, 26f + pant * 10f, 6f);   // beached: lies on the dirt
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
            // coil: drops low, tail wound round, belly grinding the dirt
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
                if (Random.value < dt * 4f) AudioManager.Play(Sfx.Skid, transform.position, 0.35f, Random.Range(0.8f, 1f));
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
            TailSlap(1f);
            FXManager.Spawn(FXType.Dust, start, Quaternion.identity, 1.8f);
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
            Thud(1.2f);
            FXManager.Spawn(FXType.SlamRing, transform.position + Vector3.up * 0.05f, Quaternion.identity, 3.2f);
            if (PlayerAlive && FlatDist < 4.4f && Rel.y < 2.5f)
                HitPlayer(leapDamage, ImpactWeight.Slam, Flat(PlayerPos - transform.position), 90f);
            yield return Recover(0.85f, "Landed", false, true);
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
                swim = 1f + k;
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
            yield return Recover(1.5f, "Stranded", false, true);
        }

        /// <summary>Burrow effects follow the ground: through a dock it tears up splinters and sea spray, not dirt.</summary>
        static void BurrowFX(FXType t, Vector3 p, Quaternion r, float s)
        {
            if (World.GroundSurface.At(p) == World.SurfaceKind.Wood)
            {
                if (t == FXType.SlamDebris) { FXManager.Spawn(FXType.WoodChips, p, r, s * 1.4f); return; }
                if (t == FXType.Dust || t == FXType.DustSmall) { FXManager.Spawn(FXType.Splash, p, Quaternion.identity, t == FXType.Dust ? s * 0.6f : s * 0.32f); return; }
            }
            FXManager.Spawn(t, p, r, s);
        }

        void EnterBurrowInstant()
        {
            State = Mode.Burrowed;
            SetBodyColliders(false);
            tSink = sink = 1f;
            keepOffGround = false;
            groundWeight = 0f;
            Pose(0f, 0f, 0f, 0f, 6f);
            pitch = 0f;
            if (digAudio && !digAudio.isPlaying) digAudio.Play();
        }

        IEnumerator Dive(float distance)
        {
            currentMove = "Dive";
            keepOffGround = false;
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
                    BurrowFX(FXType.SlamDebris, hole, Quaternion.identity, 1.5f);
                    BurrowFX(FXType.Dust, hole, Quaternion.identity, 2.2f);
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
                BurrowFX(FXType.DustSmall, fin + Vector3.up * 0.05f, Quaternion.LookRotation(-Heading + Vector3.up * 0.5f), 1.3f);
            }
            if ((chunkT -= dt) <= 0f)
            {
                chunkT = 0.22f;
                BurrowFX(FXType.SlamDebris, fin, Quaternion.identity, 0.45f);
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
                    BurrowFX(FXType.DustSmall, spot + new Vector3(c.x, 0.05f, c.y), Quaternion.LookRotation(Vector3.up), 1f + k);
                }
                if (Random.value < dt * 5f) BurrowFX(FXType.SlamDebris, spot, Quaternion.identity, 0.35f + k * 0.3f);
                CameraDirector.Instance?.Shake(0.05f + 0.1f * k, Vector3.down);
            });
            if (digAudio) digAudio.Stop();
            // burst out: nose to the sky, jaws wide, snap at the top
            State = Mode.Attacking;
            Roar(1.05f, 0.9f);
            AudioManager.Play(Sfx.Burrow, spot, 1f, 1.1f);
            BurrowFX(FXType.SlamDebris, spot, Quaternion.identity, 1.8f);
            BurrowFX(FXType.Dust, spot, Quaternion.identity, 2.6f);
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
            keepOffGround = false;
            thrash = 0.6f;
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
            thrash = 0f;
            keepOffGround = true;
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
            keepOffGround = true;
            tTailYaw = tTailPitch = 0f;
            closePressure = 0f;
            vel = Vector3.zero;
            Place(transform.position);
            if (digAudio) digAudio.Stop();
            SetBodyColliders(true);
            AudioManager.Play(Sfx.Stagger, transform.position, 1f, 0.7f);
            AudioManager.Play(Sfx.SharkRoar, transform.position, 0.8f, 0.8f);
            float side = Random.value < 0.5f ? -1f : 1f;
            // flops onto its side, tail thrashing
            thrash = 1f;
            swim = 1.6f;
            yield return Hold(0.4f, (k, dt) => Pose(0f, side * 96f * Ease.OutBack(k), -0.98f * k, 34f, 14f));
            Thud(0.8f);
            float t = 0f;
            while (t < staggerTime)
            {
                float dt = Time.deltaTime;
                t += dt;
                thrash = Mathf.Lerp(1f, 0.35f, t / staggerTime);
                Pose(0f, side * 96f, -0.98f, 30f + Mathf.Sin(t * 5f) * 8f, 8f);
                if (Random.value < dt * 3f)
                    FXManager.Spawn(FXType.HitSpark, transform.position + Vector3.up * 1.2f + transform.forward * Random.Range(-2f, 2f), Quaternion.LookRotation(Vector3.up), 0.6f);
                Place(transform.position);
                yield return null;
            }
            // rolls upright and rises off the dirt
            AudioManager.Play(Sfx.WhooshHeavy, transform.position, 0.7f, 0.75f);
            yield return Hold(0.5f, (k, dt) => Pose(0f, side * 96f * (1f - Ease.InOutCubic(k)), -0.98f * (1f - k), 20f, 12f));
            thrash = 0f;
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
            keepOffGround = true;
            tTailYaw = tTailPitch = 0f;
            float startAir = airY;
            float side = Random.value < 0.5f ? -1f : 1f;
            thrash = 1f;
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
                thrash = Mathf.Max(0f, 1f - t / 2.2f) * (0.6f + 0.4f * Mathf.Sin(t * 13f));
                swim = Mathf.Max(0f, 1.5f - t);
                Place(transform.position);
                yield return null;
            }
            thrash = 0f;
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
            // measured motion drives the tail
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

            float speed = moved.magnitude;
            float t = Time.time;

            // tail: a swimming wave that grows with speed, a fast lash when thrashing, plus whatever the move is doing
            float freq = 5f + speed * 0.6f;
            float wave = swim * (0.35f + Mathf.Clamp01(speed / 8f)) * 16f;
            float w0 = Mathf.Sin(t * freq) * wave + Mathf.Sin(t * 14f) * thrash * 22f;
            float w1 = Mathf.Sin(t * freq - 1.1f) * wave * 1.4f + Mathf.Sin(t * 14f - 1.3f) * thrash * 30f;

            // body: swims with a slow swell, the head sways against the tail, sinks underground
            float swell = State == Mode.Burrowed ? 0f : Mathf.Sin(t * 1.7f) * 0.07f * (1f - Mathf.Clamp01(speed / 10f));
            pivot.localPosition = pivotRestLocal + Vector3.up * (lift + swell - sink * burrowDepth);
            pivot.localRotation = Quaternion.Euler(pitch + Mathf.Sin(t * 1.7f - 0.9f) * 1.5f, -w0 * 0.14f,
                                                   bank + Mathf.Sin(t * 14f + 0.5f) * thrash * 5f);

            if (jaw) jaw.rotation = pivot.rotation * Quaternion.AngleAxis(jawAngle, Vector3.right) * jawRel;
            if (tail0) tail0.rotation = pivot.rotation * Quaternion.Euler(tailPitch, tailYaw * 0.6f + w0, 0f) * tail0Rel;
            if (tail1) tail1.rotation = pivot.rotation * Quaternion.Euler(tailPitch * 1.3f, tailYaw + w0 + w1, 0f) * tail1Rel;

            KeepOffGround(dt);

            if (lockPoint) lockPoint.localPosition = Vector3.Lerp(lockRest, new Vector3(0f, 0.7f, -0.2f), Mathf.Clamp01(sink));

            if (digAudio && digAudio.isPlaying)
                digAudio.volume = Mathf.Clamp01(speed / 8f) * (AudioManager.Instance != null ? AudioManager.Instance.sfxVolume : 1f);

            UpdateFlash(dt);
        }

        /// <summary>
        /// Lift the body so its lowest hull point stays on or above the ground: a raised head plants the tail,
        /// a tail slam props the body up, a flop on the side rests on the flank. Off while it burrows on purpose.
        /// </summary>
        void KeepOffGround(float dt)
        {
            groundWeight = Mathf.MoveTowards(groundWeight, keepOffGround && sink < 0.02f ? 1f : 0f, dt * 4f);
            if (groundWeight <= 0f || hull == null || hull.Length == 0) return;
            float minY = float.MaxValue;
            foreach (var h in hull)
                if (h.bone) minY = Mathf.Min(minY, h.bone.TransformPoint(h.local).y);
            float deficit = groundY + 0.03f - minY;
            if (deficit > 0f) pivot.position += Vector3.up * deficit * groundWeight;
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

    static class SharkBossVecExt
    {
        public static Vector3 WithY(this Vector3 v, float y) { v.y = y; return v; }
    }
}
