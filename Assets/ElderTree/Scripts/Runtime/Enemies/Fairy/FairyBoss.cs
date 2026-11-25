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
    /// Fairy: a tall pale fae in a blue dress with white hair and four white wings. She doesn't fly about; she
    /// <b>hovers</b> a little off the ground, toes pointed, wings fluttering, and keeps you at a distance:
    ///  - Volley: a hand rises and gathers light, then flicks a fan of homing motes at you (2 volleys, 3 in phase 2).
    ///  - Halo: motes bloom one by one in a ring behind her head, then fire at you one after another.
    ///  - Moonfall: she rises with her arms up; rings of light open under you and columns of light slam down.
    ///  - Swoop (far away): draws back, then skims at you low and fast with her arms out.
    ///  - Spiral (phase 2): spins with her arms out, shedding a spiral of motes across the arena.
    ///  - Flee: stay close to her for a brief moment and she takes off and flies to the far side of the arena.
    ///    In phase 2 she shoves you away with her wings first and leaves motes hanging in the air behind her.
    /// After Moonfall, a Swoop or a Spiral she is spent for a moment and sinks low: that's when to hit her (she
    /// won't flee until it's over). Stance break: her wings fold and she drops to her knees, open to criticals.
    /// Animation is procedural: rigid parts posed from pose targets, wings fluttering, the skirt and hair dragging.
    /// </summary>
    public class FairyBoss : BossBase
    {
        public enum Mode { Dormant, Hovering, Attacking, Fleeing, Spent, Staggered, Dead }

        public string displayName = "Fairy";

        [Header("Rig (index 0 = left, 1 = right)")]
        [Tooltip("Body centre at the hips: lean, bank and lift happen here.")]
        public Transform pivot;
        public Transform model;
        public Transform pelvis, skirt, torso, head, hair;
        public Transform[] upperArm = new Transform[2], forearm = new Transform[2], hand = new Transform[2];
        public Transform[] thigh = new Transform[2], shin = new Transform[2], foot = new Transform[2];
        public Transform[] wingUp = new Transform[2], wingDown = new Transform[2];
        [Tooltip("Where motes gather and leave from (palm of each hand).")]
        public Transform[] palm = new Transform[2];
        public Transform lockPoint;
        public Renderer[] flashRenderers;
        public Light bodyLight;
        public ParticleSystem wingDust;
        public ParticleSystem[] gatherFx = new ParticleSystem[2];

        [Header("Projectiles")]
        public FairyMote motePrefab;
        public FairyPillar pillarPrefab;

        [Header("Arena")]
        public Vector3 arenaCenter;
        public float arenaRadius = 15f;

        [Header("Stats")]
        public float maxHealth = 2200f;
        public float maxPoise = 90f;
        public float poiseRegenDelay = 4f;
        [Range(0f, 1f)] public float phaseTwoAt = 0.5f;
        public float staggerTime = 3.6f;

        [Header("Hover")]
        [Tooltip("How far her toes hang above the ground while hovering.")]
        public float hoverHeight = 0.6f;
        public float driftSpeed = 3f;
        public float accel = 9f;
        public float turnRate = 240f;

        [Header("Flee")]
        [Tooltip("Closer than this (flat metres) counts as 'too close'.")]
        public float closeRange = 3.8f;
        [Tooltip("How long you can stay close before she flies away (phase 1 / phase 2).")]
        public Vector2 closeDelay = new Vector2(1.5f, 1f);
        public float fleeDistance = 11f;
        public float gustDamage = 50f;

        [Header("Attacks")]
        public float volleyDamage = 55f;
        public float haloDamage = 60f;
        public float pillarDamage = 120f;
        public float pillarRadius = 1.7f;
        public float swoopDamage = 110f;
        public float spiralDamage = 45f;
        public float mineDamage = 70f;

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

        bool phaseTwo;
        float health, poise;
        string currentMove = "-";
        string forcedMove;
        float lastHitTime = -99f, flash;
        bool flashWasOn;
        MaterialPropertyBlock mpb;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        float SpeedMul => phaseTwo ? 1.2f : 1f;

        PlayerController Player => PlayerController.Instance;
        Vector3 PlayerPos => Player != null ? Player.transform.position : arenaCenter;
        Vector3 PlayerChest => PlayerPos + Vector3.up * 1.1f;
        bool PlayerAlive => Player != null && !Player.IsDead;

        // ------------------------------------------------------------------ kinematics
        float yaw, yawRate, groundY, hover, swoopCd, spiralCd, moonCd, fleeCd, closeTimer;
        Vector3 vel, prevPos, localVel;
        bool interruptible, moveDone;
        Coroutine moveRoutine;

        // ------------------------------------------------------------------ pose: targets (t*) and smoothed values
        float tLean, tBank, tLift, tHover, tTorso, tTwist, tHeadPitch, tHeadTrack = 1f, sharp = 6f;
        float lean, bank, lift, torsoPitch, twist, headPitch, headTrack = 1f, headYawCur, headPitchCur;
        readonly float[] tElev = new float[2], tAzim = new float[2], tElbow = new float[2];
        readonly float[] elev = new float[2], azim = new float[2], elbow = new float[2];
        readonly float[] tHip = new float[2], tKnee = new float[2], hip = new float[2], knee = new float[2];
        float tWingOpen, tWingAmp = 26f, tWingFreq = 7f, tWingDroop;
        float wingOpen, wingAmp = 26f, wingFreq = 7f, wingDroop, wingPhase;
        float skirtPitch, skirtRoll, hairPitch, hairRoll, bob;
        float glowBase = 1f, glowBoost, dustBase = 10f;

        // rest rotations relative to the pivot
        readonly Dictionary<Transform, Quaternion> rest = new();
        Vector3 pivotRestLocal, modelRestScale = Vector3.one;
        bool rigReady;

        // pools
        readonly List<FairyMote> motes = new();
        readonly List<FairyPillar> pillars = new();
        readonly List<FairyMote> held = new();
        Transform fxRoot;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            health = maxHealth;
            poise = maxPoise;
            CaptureRig();
            if (bodyLight) glowBase = bodyLight.intensity;
            if (wingDust) dustBase = wingDust.emission.rateOverTime.constant;
        }

        void CaptureRig()
        {
            if (pivot == null) return;
            pivotRestLocal = pivot.localPosition;
            if (model) modelRestScale = model.localScale;
            Quaternion inv = Quaternion.Inverse(pivot.rotation);
            void Cap(Transform t) { if (t && !rest.ContainsKey(t)) rest[t] = inv * t.rotation; }
            Cap(pelvis); Cap(skirt); Cap(torso); Cap(head); Cap(hair);
            for (int i = 0; i < 2; i++)
            {
                Cap(upperArm[i]); Cap(forearm[i]); Cap(hand[i]); Cap(thigh[i]); Cap(shin[i]); Cap(foot[i]);
                Cap(wingUp[i]); Cap(wingDown[i]);
            }
            rigReady = true;
        }

        // ================================================================== encounter API
        public override void BeginFight()
        {
            StopAllCoroutines();
            ClearProjectiles();
            health = maxHealth;
            poise = maxPoise;
            phaseTwo = false;
            forcedMove = null;
            swoopCd = 6f; spiralCd = 4f; moonCd = 3f; fleeCd = 0f; closeTimer = 0f;
            gameObject.SetActive(true);
            if (model) model.localScale = modelRestScale;
            groundY = Blast.Floor(arenaCenter).y;
            ResetPose();
            State = Mode.Hovering;
            StartCoroutine(Brain(true));
        }

        public override void ResetBoss()
        {
            StopAllCoroutines();
            ClearProjectiles();
            State = Mode.Dormant;
            health = maxHealth;
            poise = maxPoise;
            if (model) model.localScale = modelRestScale;
            gameObject.SetActive(false);
        }

        public override void ForceNextMove(string move) => forcedMove = move;

        public override void ClearProjectiles()
        {
            held.Clear();
            foreach (var m in motes) if (m) m.Cancel();
            foreach (var p in pillars) if (p) p.Cancel();
            for (int i = 0; i < 2; i++) if (gatherFx[i]) gatherFx[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void ResetPose()
        {
            RestPose();
            tHover = hover = hoverHeight;
            lean = bank = lift = torsoPitch = twist = headPitch = 0f;
            for (int i = 0; i < 2; i++) { elev[i] = tElev[i]; azim[i] = tAzim[i]; elbow[i] = tElbow[i]; hip[i] = tHip[i]; knee[i] = tKnee[i]; }
            vel = Vector3.zero;
            glowBoost = 0f;
        }

        /// <summary>The hovering stance: arms loose and a little out, one knee bent more than the other, toes pointed.</summary>
        void RestPose(float s = 5f)
        {
            tLean = 4f; tBank = 0f; tLift = 0f; tTorso = 0f; tTwist = 0f; tHeadPitch = 0f; tHeadTrack = 1f; sharp = s;
            Arms(-78f, 14f, 18f, -78f, 14f, 18f);
            tHip[0] = 16f; tKnee[0] = 36f; tHip[1] = 6f; tKnee[1] = 14f;
            Wings(0f, 26f, 7f, 0f);
            tHover = hoverHeight;
        }

        void Arms(float le, float la, float lb, float re, float ra, float rb)
        {
            tElev[0] = le; tAzim[0] = la; tElbow[0] = lb;
            tElev[1] = re; tAzim[1] = ra; tElbow[1] = rb;
        }

        void Arm(int i, float e, float a, float b) { tElev[i] = e; tAzim[i] = a; tElbow[i] = b; }

        void Wings(float open, float amp, float freq, float droop)
        {
            tWingOpen = open; tWingAmp = amp; tWingFreq = freq; tWingDroop = droop;
        }

        // ================================================================== brain
        bool FleeDue => fleeCd <= 0f && PlayerAlive && closeTimer >= (phaseTwo ? closeDelay.y : closeDelay.x);

        IEnumerator Brain(bool intro)
        {
            if (intro) yield return Intro();
            string last = "";
            while (true)
            {
                if (!phaseTwo && health <= maxHealth * phaseTwoAt) yield return PhaseShift();
                if (FleeDue) { yield return Flee(); continue; }
                if (forcedMove == null)
                {
                    yield return Drift(Random.Range(phaseTwo ? 0.6f : 0.9f, phaseTwo ? 1.3f : 1.9f));
                    if (FleeDue) continue;
                }

                string move = forcedMove ?? PickMove(last);
                forcedMove = null;
                last = move;
                if (move == "Flee") { yield return Flee(); continue; }
                currentMove = move;
                State = Mode.Attacking;
                interruptible = true;
                moveDone = false;
                moveRoutine = StartCoroutine(RunMove(Move(move)));
                while (!moveDone)
                {
                    if (interruptible && FleeDue)
                    {
                        StopCoroutine(moveRoutine);
                        Interrupted();
                        break;
                    }
                    yield return null;
                }
                moveRoutine = null;
                if (State != Mode.Dead) State = Mode.Hovering;
                currentMove = "-";
                RestPose();
            }
        }

        IEnumerator RunMove(IEnumerator m)
        {
            yield return m;
            moveDone = true;
        }

        IEnumerator Move(string move)
        {
            switch (move)
            {
                case "Volley": return Volley();
                case "Halo": return Halo();
                case "Moonfall": return Moonfall();
                case "Swoop": return Swoop();
                case "Spiral": return Spiral();
                default: return Volley();
            }
        }

        /// <summary>A move was cut short so she can flee: anything still gathering fizzles out.</summary>
        void Interrupted()
        {
            foreach (var m in held) if (m) m.Cancel();
            held.Clear();
            for (int i = 0; i < 2; i++) if (gatherFx[i]) gatherFx[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
            AudioManager.Play(Sfx.OrbImpact, transform.position + Vector3.up * 2f, 0.25f, 2f);
        }

        string PickMove(string last)
        {
            if (!PlayerAlive) return "Halo";
            float d = FlatDist;
            var opts = new List<(string, float)>
            {
                ("Volley", d < 13f ? 3f : 1.5f),
                ("Halo", d > 3f ? 2.4f : 0.8f),
                ("Moonfall", moonCd <= 0f ? (d > 9f ? 3f : 1.3f) : 0f),
                ("Swoop", swoopCd <= 0f && d > 8f ? (phaseTwo ? 2.4f : 1.4f) : 0f),
                ("Spiral", phaseTwo && spiralCd <= 0f && d < 11f ? 2.2f : 0f),
            };
            float total = 0f;
            for (int i = 0; i < opts.Count; i++)
            {
                if (opts[i].Item1 == last) opts[i] = (opts[i].Item1, opts[i].Item2 * 0.3f);
                total += opts[i].Item2;
            }
            if (total <= 0f) return "Volley";
            float r = Random.value * total;
            foreach (var (n, w) in opts) { if ((r -= w) <= 0f) return n; }
            return "Volley";
        }

        // ================================================================== movement primitives
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        Vector3 Heading => Quaternion.Euler(0, yaw, 0) * Vector3.forward;
        float FlatDist => Flat(PlayerPos - transform.position).magnitude;
        Vector3 Side(int i) => transform.right * (i == 0 ? -1f : 1f);

        void Face(Vector3 dir, float rate, float dt)
        {
            dir = Flat(dir);
            float prev = yaw;
            if (dir.sqrMagnitude > 1e-4f)
                yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, rate * dt);
            yawRate = Mathf.DeltaAngle(prev, yaw) / Mathf.Max(dt, 1e-4f);
        }

        void FacePlayer(float dt, float mul = 1f) { if (PlayerAlive) Face(PlayerPos - transform.position, turnRate * mul, dt); }

        void Locomote(Vector3 desired, float dt, float accelMul = 1f)
        {
            vel = Vector3.MoveTowards(vel, Flat(desired), accel * accelMul * dt);
            Place(transform.position + vel * dt);
        }

        /// <summary>The arena, stretched to follow a player who slipped out through a gap in the walls.</summary>
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
            float floor = Blast.Floor(p).y;
            groundY = Mathf.Abs(floor - groundY) > 4f ? floor : Mathf.Lerp(groundY, floor, 0.2f);
            p.y = groundY + hover;
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

        void Brake(float dt, float k = 1f) => Locomote(Vector3.zero, dt, k);

        /// <summary>A spot inside the arena about <paramref name="dist"/> from the player, away from them if possible.</summary>
        Vector3 FarSpot(float dist)
        {
            Vector3 away = Flat(transform.position - PlayerPos);
            if (away.sqrMagnitude < 0.01f) away = -Heading;
            away.Normalize();
            float lim = Limit - 1.8f;
            float sign = Random.value < 0.5f ? 1f : -1f;
            foreach (float a in new[] { 0f, 35f, -35f, 70f, -70f, 110f, -110f })
            {
                Vector3 c = PlayerPos + Quaternion.Euler(0, a * sign, 0) * away * dist;
                if (Flat(c - arenaCenter).magnitude < lim) return c;
            }
            Vector3 opp = Flat(arenaCenter - PlayerPos);
            if (opp.sqrMagnitude < 0.01f) opp = away;
            return arenaCenter + opp.normalized * lim;
        }

        // ================================================================== sounds + fx
        void Chime(float vol = 0.6f, float pitch = 1.7f) =>
            AudioManager.Play(Sfx.SpellGather, transform.position + Vector3.up * 2f, vol, pitch * Random.Range(0.97f, 1.03f));

        void WingBeat(float vol = 0.6f) =>
            AudioManager.Play(Sfx.WhooshLight, transform.position + Vector3.up * 2f, vol, Random.Range(1.25f, 1.45f));

        void Dust(float mul)
        {
            if (!wingDust) return;
            var em = wingDust.emission;
            em.rateOverTime = dustBase * mul;
        }

        FairyMote GetMote()
        {
            foreach (var m in motes) if (m && m.Free) return m;
            if (motePrefab == null) return null;
            if (fxRoot == null) fxRoot = new GameObject("Fairy FX").transform;
            var n = Instantiate(motePrefab, fxRoot);
            motes.Add(n);
            return n;
        }

        FairyPillar GetPillar()
        {
            foreach (var p in pillars) if (p && p.Free) return p;
            if (pillarPrefab == null) return null;
            if (fxRoot == null) fxRoot = new GameObject("Fairy FX").transform;
            var n = Instantiate(pillarPrefab, fxRoot);
            pillars.Add(n);
            return n;
        }

        Vector3 Palm(int i) => palm[i] ? palm[i].position : transform.position + Vector3.up * 2f + Side(i) * 0.4f;

        void Gather(int i, bool on)
        {
            var g = gatherFx[i];
            if (!g) return;
            if (on && !g.isEmitting) g.Play(true);
            else if (!on) g.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        Vector3 Lead(float flightTime) => PlayerChest + (Player != null ? Player.motor.PlanarVelocity * flightTime : Vector3.zero);

        // ================================================================== drift (between moves)
        IEnumerator Drift(float duration)
        {
            State = Mode.Hovering;
            currentMove = "Drift";
            RestPose();
            float t = 0f, strafe = Random.value < 0.5f ? 1f : -1f;
            while (t < duration)
            {
                float dt = Time.deltaTime;
                t += dt;
                Vector3 to = Flat(PlayerPos - transform.position);
                float d = to.magnitude;
                Vector3 dir = d > 0.01f ? to / d : Heading;
                Vector3 want;
                if (!PlayerAlive) want = Vector3.zero;
                // you're in her face: she startles and holds almost still (the brief opening), then flees
                else if (d < closeRange) want = -dir * 0.5f;
                else if (d > 12f) want = dir * driftSpeed * SpeedMul;
                else if (d < 6f) want = -dir * driftSpeed * 0.8f + Vector3.Cross(Vector3.up, dir) * strafe * driftSpeed * 0.5f;
                else want = Vector3.Cross(Vector3.up, dir) * strafe * driftSpeed * 0.7f;
                if (Flat(transform.position + want - arenaCenter).magnitude > Limit - 1.5f) strafe = -strafe;
                Locomote(want, dt);
                FacePlayer(dt, 0.8f);
                // lean into the drift a little
                Vector3 lv = transform.InverseTransformDirection(vel);
                tLean = 4f + lv.z * 3f;
                tBank = -lv.x * 4f;
                if (FleeDue) break;
                yield return null;
            }
        }

        // ================================================================== intro / phase two
        IEnumerator Intro()
        {
            currentMove = "Descend";
            // drifts down out of the sky some way in front of you, then opens her arms and wings
            Vector3 dir = Flat(arenaCenter - PlayerPos);
            if (dir.sqrMagnitude < 1f) dir = Vector3.forward;
            dir.Normalize();
            Vector3 start = PlayerPos + dir * 10f;
            Vector3 rel = Flat(start - arenaCenter);
            if (rel.magnitude > arenaRadius - 2f) start = arenaCenter + rel.normalized * (arenaRadius - 2f);
            yaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;
            hover = hoverHeight + 9f;
            Place(start);
            Dust(4f);
            Chime(0.5f, 1.4f);
            tHeadTrack = 0.4f;
            yield return Hold(2.4f, (k, dt) =>
            {
                hover = Mathf.Lerp(hoverHeight + 9f, hoverHeight, Ease.OutCubic(k));
                tHover = hover;
                FacePlayer(dt, 0.4f);
                Arms(-70f, 30f, 10f, -70f, 30f, 10f);
                Wings(-10f, 18f, 5f, 0f);
                tLean = 0f;
            });
            currentMove = "Bloom";
            Chime(0.8f, 1.25f);
            AudioManager.Play(Sfx.OrbCharge, transform.position + Vector3.up * 2f, 0.6f, 1.5f);
            tHeadTrack = 1f;
            yield return Hold(1.3f, (k, dt) =>
            {
                float e = Ease.OutCubic(Mathf.Clamp01(k * 2f));
                Arms(-10f * e - 70f * (1f - e), 85f, 15f, -10f * e - 70f * (1f - e), 85f, 15f);
                Wings(15f * e, 34f, 10f, -8f * e);
                tLean = -8f * e;
                tHeadPitch = -12f * e;
                glowBoost = Mathf.Sin(k * Mathf.PI) * 2.5f;
                FacePlayer(dt);
            });
            glowBoost = 0f;
            Dust(1f);
        }

        IEnumerator PhaseShift()
        {
            phaseTwo = true;
            currentMove = "Nova";
            State = Mode.Attacking;
            interruptible = false;
            Chime(0.9f, 1.1f);
            AudioManager.Play(Sfx.OrbCharge, transform.position + Vector3.up * 2f, 0.9f, 1.2f);
            // curls up, rising, then flings her arms and wings wide: a ring of motes bursts out all round her
            yield return Hold(0.9f, (k, dt) =>
            {
                Brake(dt, 2f);
                Arms(-40f, -20f, 120f, -40f, -20f, 120f);
                tLean = 18f; tHeadPitch = 25f;
                tLift = 0.6f * k;
                Wings(-40f, 10f, 14f, 10f);
                glowBoost = k * 2f;
                tKnee[0] = tKnee[1] = 70f; tHip[0] = tHip[1] = 40f;
            });
            int n = 14;
            float h = groundY + 1.15f;
            for (int i = 0; i < n; i++)
            {
                var m = GetMote();
                if (!m) break;
                Vector3 d = Quaternion.Euler(0, i * 360f / n + yaw, 0) * Vector3.forward;
                m.Fire(transform.position.WithY(h) + d * 0.8f, d, 9f, 0f, 0f, spiralDamage, 15f, gameObject, 3f);
            }
            AudioManager.Play(Sfx.OrbFire, transform.position + Vector3.up * 2f, 1f, 1.5f);
            CameraDirector.Instance?.Shake(0.3f, Vector3.up);
            Dust(5f);
            yield return Hold(1f, (k, dt) =>
            {
                RestPose(9f);
                Arms(10f, 95f, 5f, 10f, 95f, 5f);
                Wings(20f, 36f, 12f, -10f);
                tLean = -10f; tLift = 0.6f * (1f - k);
                glowBoost = 2f * (1f - k);
            });
            Dust(1.5f);
            glowBoost = 0f;
        }

        // ================================================================== flee
        IEnumerator Flee()
        {
            State = Mode.Fleeing;
            currentMove = "Flee";
            interruptible = false;
            closeTimer = 0f;
            foreach (var m in held) if (m) m.Cancel();
            held.Clear();
            Gather(0, false); Gather(1, false);

            if (phaseTwo)
            {
                // gust: wings snap back, then forward in a blast that shoves you away
                currentMove = "Gust";
                yield return Hold(0.32f, (k, dt) =>
                {
                    Brake(dt, 3f);
                    FacePlayer(dt, 2f);
                    Wings(-55f * Ease.OutCubic(k), 8f, 6f, -5f);
                    tLean = -6f; sharp = 12f;
                    Arms(-60f, 40f, 40f, -60f, 40f, 40f);
                });
                WingBeat(1f);
                AudioManager.Play(Sfx.WhooshHeavy, transform.position + Vector3.up * 1.5f, 0.8f, 1.3f);
                Vector3 g = Blast.Ground(transform.position);
                FXManager.Spawn(FXType.SlamRing, g + Vector3.up * 0.05f, Quaternion.identity, 2.6f);
                FXManager.Spawn(FXType.Dust, g, Quaternion.identity, 1.6f);
                if (PlayerAlive && FlatDist < closeRange + 0.4f)
                    Player.TakeHit(new DamageInfo
                    {
                        damage = gustDamage, poiseDamage = 40f, point = PlayerChest,
                        direction = Flat(PlayerPos - transform.position).normalized, weight = ImpactWeight.Heavy, source = gameObject
                    });
                tWingOpen = 35f; wingOpen = 35f;
            }
            else
            {
                // a quick crouch in the air, wings cocked
                yield return Hold(0.16f, (k, dt) =>
                {
                    Brake(dt, 3f);
                    FacePlayer(dt, 2f);
                    Wings(-35f, 6f, 6f, -4f);
                    tLift = -0.2f; tKnee[0] = tKnee[1] = 60f; sharp = 14f;
                });
                WingBeat(0.9f);
            }

            currentMove = "Flee";
            Vector3 from = transform.position, to = FarSpot(fleeDistance);
            float dur = (phaseTwo ? 0.85f : 1f) * Mathf.Clamp(Flat(to - from).magnitude / fleeDistance, 0.7f, 1.3f);
            float rise = phaseTwo ? 1.8f : 2.4f;
            int mines = phaseTwo ? 3 : 0, dropped = 0;
            Dust(6f);
            Vector3 travel = Flat(to - from).normalized;
            float beat = 0f;
            yield return Hold(dur, (k, dt) =>
            {
                float e = Ease.InOutCubic(k);
                Vector3 p = Vector3.Lerp(from, to, e);
                tHover = hover = hoverHeight + Mathf.Sin(k * Mathf.PI) * rise;
                vel = travel * Flat(to - from).magnitude / dur;
                Place(p);
                FacePlayer(dt, 1.2f);
                // flying away backwards: leaning back, legs trailing, arms swept back, wings beating hard
                Vector3 lv = transform.InverseTransformDirection(travel);
                tLean = lv.z * 22f;
                tBank = -lv.x * 22f;
                Arms(-45f, 150f, 20f, -45f, 150f, 20f);
                tHip[0] = tHip[1] = -lv.z * 25f;
                tKnee[0] = 40f; tKnee[1] = 25f;
                Wings(10f, 42f, 13f, 0f);
                sharp = 8f;
                if ((beat -= dt) <= 0f) { beat = 0.33f; WingBeat(0.45f); }
                if (dropped < mines && k > 0.25f + dropped * 0.25f)
                {
                    dropped++;
                    var m = GetMote();
                    if (m) m.Mine(transform.position + Vector3.up * 0.9f, 0.6f, mineDamage, gameObject);
                }
            });
            vel = travel * 2f;
            Dust(1.5f);
            yield return Hold(0.35f, (k, dt) => { Brake(dt, 2f); FacePlayer(dt); RestPose(6f); });
            Dust(1f);
            fleeCd = phaseTwo ? 0.8f : 1.2f;
            closeTimer = 0f;
            State = Mode.Hovering;
            currentMove = "-";
        }

        // ================================================================== volley
        IEnumerator Volley()
        {
            int volleys = phaseTwo ? 3 : 2;
            int count = phaseTwo ? 5 : 3;
            for (int v = 0; v < volleys && PlayerAlive; v++)
            {
                int i = v % 2 == 0 ? 1 : 0;
                currentMove = "Volley";
                float tell = (v == 0 ? 0.7f : 0.5f) / SpeedMul;
                var core = GetMote();
                if (core) held.Add(core);
                Gather(i, true);
                Chime(0.4f, 1.8f);
                yield return Hold(tell, (k, dt) =>
                {
                    Brake(dt, 2f);
                    FacePlayer(dt, 1.2f);
                    sharp = 7f;
                    Arm(i, 35f, 35f, 70f);
                    Arm(1 - i, -60f, 40f, 30f);
                    tTwist = (i == 1 ? 1f : -1f) * 18f;
                    tLean = -4f;
                    if (core) core.Hold(Palm(i), k);
                });
                Gather(i, false);
                // flick: the hand sweeps forward and the motes fan out
                AudioManager.Play(Sfx.OrbFire, Palm(i), 0.55f, Random.Range(1.6f, 1.8f));
                Vector3 aim = (Lead(0.3f) - Palm(i)).normalized;
                for (int m = 0; m < count; m++)
                {
                    float a = count == 1 ? 0f : Mathf.Lerp(-22f, 22f, m / (float)(count - 1));
                    var mote = m == count / 2 && core ? core : GetMote();
                    if (!mote) continue;
                    Vector3 d = Quaternion.AngleAxis(a, Vector3.up) * aim;
                    mote.Fire(Palm(i), d, 11f * SpeedMul, 65f, 0.75f, volleyDamage, 14f, gameObject);
                }
                held.Remove(core);
                yield return Hold(0.32f, (k, dt) =>
                {
                    Brake(dt, 2f);
                    sharp = 18f;
                    Arm(i, -5f, 5f, 5f);
                    tTwist = (i == 1 ? -1f : 1f) * 12f;
                    tLean = 8f;
                });
            }
            yield return Hold(0.45f, (k, dt) => { Brake(dt, 2f); FacePlayer(dt); RestPose(6f); });
        }

        // ================================================================== halo
        IEnumerator Halo()
        {
            int n = phaseTwo ? 8 : 6;
            currentMove = "Halo";
            var ring = new List<FairyMote>();
            float spin = 0f;
            Vector3 RingPos(int j)
            {
                Vector3 c = (head ? head.position : transform.position + Vector3.up * 2.4f) - transform.forward * 0.45f + Vector3.up * 0.2f;
                float a = spin + j * Mathf.PI * 2f / n;
                return c + (transform.right * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)) * 0.95f;
            }
            AudioManager.Play(Sfx.OrbCharge, transform.position + Vector3.up * 2.4f, 0.55f, 1.6f);
            // the ring blooms one mote at a time while she spreads her arms
            float bloom = 1.0f / SpeedMul;
            yield return Hold(bloom, (k, dt) =>
            {
                Brake(dt, 2f);
                FacePlayer(dt);
                spin += dt * 1.4f;
                Arms(15f, 80f, 25f, 15f, 80f, 25f);
                Wings(12f, 30f, 9f, -6f);
                tLean = -6f; tHeadPitch = -6f;
                while (ring.Count < Mathf.Min(n, Mathf.FloorToInt(k * n * 1.15f) + 1))
                {
                    var m = GetMote();
                    if (!m) break;
                    ring.Add(m); held.Add(m);
                    AudioManager.Play(Sfx.SpellGather, transform.position + Vector3.up * 2.4f, 0.25f, 1.7f + ring.Count * 0.06f);
                }
                for (int j = 0; j < ring.Count; j++) ring[j].Hold(RingPos(j), Mathf.Clamp01(k * 3f - j * 0.2f) + 0.2f);
            });
            // fire them one by one, each at where you are going
            float gap = phaseTwo ? 0.15f : 0.22f;
            for (int j = 0; j < ring.Count && PlayerAlive; j++)
            {
                int hnd = j % 2;
                yield return Hold(gap, (k, dt) =>
                {
                    Brake(dt, 2f);
                    FacePlayer(dt);
                    spin += dt * 1.4f;
                    sharp = 14f;
                    Arm(hnd, 10f, 15f, 5f);
                    Arm(1 - hnd, 20f, 80f, 25f);
                    for (int q = j; q < ring.Count; q++) ring[q].Hold(RingPos(q), 1f);
                });
                var m = ring[j];
                held.Remove(m);
                Vector3 from = RingPos(j);
                float flight = Vector3.Distance(from, PlayerChest) / 16f;
                m.Fire(from, Lead(flight * 0.7f) - from, 16f, 0f, 0f, haloDamage, 16f, gameObject);
                AudioManager.Play(Sfx.OrbFire, from, 0.35f, Random.Range(1.8f, 2f));
            }
            held.Clear();
            yield return Hold(0.5f, (k, dt) => { Brake(dt, 2f); FacePlayer(dt); RestPose(6f); });
        }

        // ================================================================== moonfall
        IEnumerator Moonfall()
        {
            currentMove = "Moonfall";
            moonCd = phaseTwo ? 6f : 9f;
            int count = phaseTwo ? 5 : 3;
            Chime(0.8f, 1.2f);
            yield return Hold(0.65f / SpeedMul, (k, dt) =>
            {
                Brake(dt, 2f);
                FacePlayer(dt);
                Arms(80f, 25f, 15f, 80f, 25f, 15f);
                tLift = 0.9f * Ease.OutCubic(k);
                tHeadPitch = -28f; tLean = -8f;
                Wings(15f, 34f, 10f, -8f);
                glowBoost = k * 1.5f;
                Gather(0, true); Gather(1, true);
            });
            float gap = phaseTwo ? 0.3f : 0.38f, tell = phaseTwo ? 0.85f : 1f;
            for (int i = 0; i < count && PlayerAlive; i++)
            {
                Vector3 at = PlayerPos + (Player != null ? Player.motor.PlanarVelocity * 0.45f : Vector3.zero);
                if (i > 0) { Vector2 r = Random.insideUnitCircle * 1.3f; at += new Vector3(r.x, 0f, r.y); }
                Vector3 rel = Flat(at - arenaCenter);
                if (rel.magnitude > Limit) at = arenaCenter + rel.normalized * Limit;
                GetPillar()?.Strike(Blast.Ground(at), pillarRadius, tell, pillarDamage, gameObject);
                yield return Hold(gap, (k, dt) =>
                {
                    Brake(dt, 2f);
                    FacePlayer(dt);
                    Arms(80f + Mathf.Sin(k * Mathf.PI) * 6f, 25f, 15f, 80f - Mathf.Sin(k * Mathf.PI) * 6f, 25f, 15f);
                });
            }
            Gather(0, false); Gather(1, false);
            glowBoost = 0f;
            yield return Hold(tell * 0.6f, (k, dt) => { Brake(dt, 2f); FacePlayer(dt); });
            yield return Spent(1.5f);
        }

        /// <summary>Worn out after a big spell: sinks low, arms limp, slow wings. She won't flee until it's over.</summary>
        IEnumerator Spent(float time, bool dizzy = false)
        {
            interruptible = false;
            State = Mode.Spent;
            currentMove = dizzy ? "Dizzy" : "Spent";
            Dust(0.4f);
            yield return Hold(time, (k, dt) =>
            {
                Brake(dt, 2f);
                if (!dizzy) FacePlayer(dt, 0.3f);
                float breath = Mathf.Sin(k * time * 5f);
                sharp = 4f;
                tLift = -0.5f + breath * 0.04f;
                tLean = 14f + breath * 2f;
                tHeadPitch = 18f; tHeadTrack = 0.3f;
                Arms(-85f, 8f, 10f, -85f, 8f, 10f);
                tKnee[0] = 50f; tKnee[1] = 35f; tHip[0] = 20f; tHip[1] = 12f;
                Wings(-15f, 10f, 3.5f, 14f);
                if (dizzy) { tBank = Mathf.Sin(k * time * 4f) * 12f; yaw += Mathf.Sin(k * time * 2.5f) * 40f * dt; }
            });
            tHeadTrack = 1f;
            tBank = 0f;
            Dust(1f);
            State = Mode.Attacking;
        }

        // ================================================================== swoop
        IEnumerator Swoop()
        {
            currentMove = "Swoop";
            swoopCd = phaseTwo ? 7f : 10f;
            WingBeat(0.7f);
            yield return Hold(0.6f / SpeedMul, (k, dt) =>
            {
                Brake(dt, 2f);
                FacePlayer(dt, 1.5f);
                Arms(-40f, 160f, 10f, -40f, 160f, 10f);
                tLean = -22f * Ease.OutCubic(k);
                tLift = 0.5f * k;
                Wings(-30f, 14f, 6f, 0f);
                tKnee[0] = tKnee[1] = 70f;
            });
            AudioManager.Play(Sfx.WhooshHeavy, transform.position + Vector3.up * 1.5f, 0.9f, 1.15f);
            interruptible = false;
            float speed = (phaseTwo ? 19f : 17f);
            Vector3 dir = Flat(Lead(0.25f) - transform.position).normalized;
            if (dir.sqrMagnitude < 0.01f) dir = Heading;
            bool hit = false, passed = false;
            float t = 0f, after = 0f, startD = FlatDist;
            Dust(4f);
            while (t < 1.6f)
            {
                float dt = Time.deltaTime;
                t += dt;
                vel = dir * speed;
                Place(transform.position + vel * dt);
                Face(dir, 900f, dt);
                tHover = hover = Mathf.MoveTowards(hover, hoverHeight - 0.45f, dt * 3f);
                tLift = 0f;
                tLean = 34f; tBank = 0f; sharp = 10f;
                Arms(-5f, 25f, 0f, -5f, 25f, 0f);
                tHip[0] = tHip[1] = -20f; tKnee[0] = tKnee[1] = 15f;
                Wings(-20f, 20f, 14f, 0f);
                Vector3 rel = transform.InverseTransformPoint(PlayerPos);
                if (!hit && PlayerAlive && Mathf.Abs(rel.x) < 1.4f && Mathf.Abs(rel.z) < 1.3f && rel.y > -2.5f && rel.y < 1.2f)
                    hit = Player.TakeHit(new DamageInfo
                    {
                        damage = swoopDamage, poiseDamage = 70f, point = PlayerChest, direction = dir + transform.right * Mathf.Sign(rel.x + 0.01f) * 0.5f,
                        weight = ImpactWeight.Heavy, source = gameObject
                    });
                if (!passed && rel.z < -0.5f) passed = true;
                if (passed && (after += dt) > 0.22f) break;
                if (Flat(transform.position - arenaCenter).magnitude > Limit - 0.6f && t > 0.2f) break;
                if (t * speed > startD + 6f) break;
                yield return null;
            }
            Dust(1f);
            yield return Hold(0.45f, (k, dt) =>
            {
                Brake(dt, 3f);
                FacePlayer(dt, 1.5f);
                tHover = hover = Mathf.Lerp(hover, hoverHeight, k);
                RestPose(6f);
                tLean = -10f * (1f - k);
            });
            yield return Spent(0.9f);
        }

        // ================================================================== spiral (phase 2)
        IEnumerator Spiral()
        {
            currentMove = "Spiral";
            spiralCd = 9f;
            AudioManager.Play(Sfx.OrbCharge, transform.position + Vector3.up * 2f, 0.7f, 1.35f);
            yield return Hold(0.65f, (k, dt) =>
            {
                Brake(dt, 2f);
                FacePlayer(dt);
                Arms(0f, 92f, 4f, 0f, 92f, 4f);
                tLift = -0.3f * k;
                Wings(20f, 30f, 11f, -8f);
                Gather(0, true); Gather(1, true);
            });
            interruptible = false;
            float dur = 2.4f, rate = 0.085f, next = 0f, dirSign = Random.value < 0.5f ? 1f : -1f;
            int side = 0;
            float beat = 0f;
            yield return Hold(dur, (k, dt) =>
            {
                yaw += dirSign * 290f * dt;
                Locomote(Flat(PlayerPos - transform.position).normalized * 1f, dt, 0.5f);
                Arms(0f, 92f, 4f, 0f, 92f, 4f);
                tLean = 0f; tHeadTrack = 0f;
                if ((beat -= dt) <= 0f) { beat = 0.4f; WingBeat(0.35f); }
                if ((next -= dt) <= 0f)
                {
                    next = rate;
                    var m = GetMote();
                    if (m)
                    {
                        Vector3 outDir = Side(side);
                        Vector3 d = (Quaternion.AngleAxis(dirSign * 25f, Vector3.up) * outDir + Vector3.down * 0.17f).normalized;
                        m.Fire(Palm(side), d, 7.5f, 0f, 0f, spiralDamage, 10f, gameObject, 3.5f, 0.3f);
                        if (side == 0) AudioManager.Play(Sfx.OrbFire, Palm(side), 0.22f, Random.Range(1.9f, 2.1f));
                    }
                    side = 1 - side;
                }
            });
            Gather(0, false); Gather(1, false);
            tHeadTrack = 1f;
            yield return Spent(1.6f, true);
        }

        // ================================================================== stance break / death
        void OnStanceBreak()
        {
            StopAllCoroutines();
            moveRoutine = null;
            foreach (var m in held) if (m) m.Cancel();
            held.Clear();
            StartCoroutine(StaggerRoutine());
        }

        /// <summary>Her wings fold and she drops out of the air onto her knees.</summary>
        void KneelPose()
        {
            tHover = 0f;
            tLift = -0.98f;
            tLean = 22f; tBank = 0f; tTwist = 0f; tTorso = 8f;
            tHeadPitch = 26f; tHeadTrack = 0.2f;
            Arms(-55f, 18f, 35f, -50f, 22f, 30f);
            tHip[0] = 80f; tHip[1] = 70f; tKnee[0] = 150f; tKnee[1] = 140f;
            Wings(-60f, 4f, 2f, 22f);
        }

        IEnumerator StaggerRoutine()
        {
            currentMove = "Fallen";
            State = Mode.Staggered;
            interruptible = false;
            closeTimer = 0f;
            Gather(0, false); Gather(1, false);
            glowBoost = 0f;
            Dust(0.2f);
            AudioManager.Play(Sfx.Stagger, transform.position, 1f, 1.1f);
            Chime(0.6f, 0.9f);
            // she drops: the hover collapses fast, then a thump onto her knees
            float h0 = hover;
            yield return Hold(0.35f, (k, dt) =>
            {
                hover = Mathf.Lerp(h0, 0f, Ease.InCubic(k));
                KneelPose();
                sharp = 12f;
                vel = Vector3.zero;
            });
            hover = 0f;
            FXManager.Spawn(FXType.Dust, Blast.Ground(transform.position), Quaternion.identity, 1.2f);
            AudioManager.Play(Sfx.Land, transform.position, 0.9f, 0.9f);
            CameraDirector.Instance?.Shake(0.15f, Vector3.down);
            yield return Hold(staggerTime, (k, dt) =>
            {
                KneelPose();
                sharp = 5f;
                tLean = 22f + Mathf.Sin(k * staggerTime * 3f) * 3f;
            });
            // rises with a beat of her wings
            WingBeat(0.9f);
            Dust(3f);
            yield return Hold(0.6f, (k, dt) =>
            {
                hover = Mathf.Lerp(0f, hoverHeight, Ease.OutCubic(k));
                RestPose(7f);
                Wings(10f, 36f, 11f, 0f);
                FacePlayer(dt);
            });
            Dust(1f);
            poise = maxPoise;
            closeTimer = 0f;
            fleeCd = 0f;
            State = Mode.Hovering;
            StartCoroutine(Brain(false));
        }

        void Die()
        {
            StopAllCoroutines();
            moveRoutine = null;
            State = Mode.Dead;
            currentMove = "Dead";
            foreach (var m in held) if (m) m.Cancel();
            held.Clear();
            Gather(0, false); Gather(1, false);
            RaiseFelled();
            StartCoroutine(DeathRoutine());
        }

        IEnumerator DeathRoutine()
        {
            TimeDirector.SlowMo(1.1f, 0.3f);
            Chime(1f, 0.8f);
            float h0 = hover;
            // falls out of the air and lies on her back, wings crumpled
            yield return Hold(0.6f, (k, dt) =>
            {
                hover = Mathf.Lerp(h0, 0f, Ease.InCubic(k));
                tLean = -60f * k; tLift = -1.1f * k; tHeadPitch = -10f; tHeadTrack = 0f;
                Arms(-20f, 100f, 20f, -30f, 90f, 30f);
                tHip[0] = 20f; tHip[1] = 5f; tKnee[0] = 40f; tKnee[1] = 10f;
                Wings(-70f, 2f, 1f, 30f);
                sharp = 6f;
            });
            hover = 0f;
            FXManager.Spawn(FXType.Dust, Blast.Ground(transform.position), Quaternion.identity, 1.4f);
            AudioManager.Play(Sfx.Land, transform.position, 1f, 0.8f);
            yield return Hold(0.6f, (k, dt) => { tLean = -82f; tLift = -1.22f; sharp = 5f; });
            yield return Hold(2.2f, (k, dt) => { glowBoost = -k; Dust(0.3f); });
            // she comes apart into motes of light that drift up and wink out
            Dust(12f);
            Chime(0.7f, 1.4f);
            float t = 0f;
            const float Fade = 2.4f;
            while (t < Fade)
            {
                float dt = Time.deltaTime;
                t += dt;
                float k = Mathf.Clamp01(t / Fade);
                if (model) model.localScale = modelRestScale * Mathf.Lerp(1f, 0.02f, Ease.InCubic(k));
                glowBoost = -1f;
                Place(transform.position);
                yield return null;
            }
            Dust(0f);
            yield return new WaitForSeconds(1.2f);
            ResetBoss();
        }

        // ================================================================== damage
        public override bool ReceiveHit(in DamageInfo info)
        {
            if (!IsAlive) return false;
            lastHitTime = Time.time;
            closeTimer += 0.25f;
            health = Mathf.Max(0f, health - info.damage);
            bool broke = false;
            if (State != Mode.Staggered && !info.critical)
            {
                poise -= info.poiseDamage;
                if (poise <= 0f) { poise = maxPoise; broke = true; }
            }
            flash = 1f;
            FXManager.Spawn(FXType.HitSpark, info.point, Quaternion.LookRotation(-info.direction + Vector3.up * 0.3f), info.weight == ImpactWeight.Light ? 0.8f : 1.1f);
            AudioManager.Play(Sfx.DummyHit, info.point, 0.8f, info.weight == ImpactWeight.Light ? 1f : 0.85f);
            // a flinch: recoils away from the blow
            Vector3 lv = transform.InverseTransformDirection(info.direction);
            lean -= lv.z * 10f;
            bank += lv.x * 10f;
            RaiseDamaged(info, broke);
            if (health <= 0f) { Die(); return true; }
            if (broke) OnStanceBreak();
            return true;
        }

        // ================================================================== animation
        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || State == Mode.Dormant) return;
            swoopCd -= dt; spiralCd -= dt; moonCd -= dt; fleeCd -= dt;
            if (IsAlive && PlayerAlive && State != Mode.Staggered && State != Mode.Fleeing && FlatDist < closeRange &&
                Mathf.Abs(PlayerPos.y - groundY) < 3f)
                closeTimer += dt;
            else
                closeTimer = Mathf.Max(0f, closeTimer - dt * 1.5f);
            if (IsAlive && State != Mode.Staggered && Time.time - lastHitTime > poiseRegenDelay)
                poise = Mathf.MoveTowards(poise, maxPoise, maxPoise * 0.25f * dt);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || State == Mode.Dormant || !rigReady) return;

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 moved = (transform.position - prevPos) / dt;
            prevPos = transform.position;
            if (moved.magnitude > 60f) moved = Vector3.zero;
            localVel = Vector3.Lerp(localVel, transform.InverseTransformDirection(moved), Ease.Damp(6f, dt));

            float k = Ease.Damp(sharp, dt);
            lean = Mathf.Lerp(lean, tLean, k);
            bank = Mathf.Lerp(bank, tBank, k);
            lift = Mathf.Lerp(lift, tLift, Ease.Damp(5f, dt));
            torsoPitch = Mathf.Lerp(torsoPitch, tTorso, k);
            twist = Mathf.Lerp(twist, tTwist, k);
            headPitch = Mathf.Lerp(headPitch, tHeadPitch, k);
            headTrack = Mathf.Lerp(headTrack, tHeadTrack, Ease.Damp(4f, dt));
            for (int i = 0; i < 2; i++)
            {
                elev[i] = Mathf.Lerp(elev[i], tElev[i], k);
                azim[i] = Mathf.LerpAngle(azim[i], tAzim[i], k);
                elbow[i] = Mathf.Lerp(elbow[i], tElbow[i], k);
                hip[i] = Mathf.Lerp(hip[i], tHip[i], Ease.Damp(4f, dt));
                knee[i] = Mathf.Lerp(knee[i], tKnee[i], Ease.Damp(4f, dt));
            }
            if (State != Mode.Fleeing && State != Mode.Staggered && State != Mode.Dead && currentMove != "Swoop")
                hover = Mathf.Lerp(hover, tHover, Ease.Damp(3f, dt));
            float wk = Ease.Damp(5f, dt);
            wingOpen = Mathf.Lerp(wingOpen, tWingOpen, Ease.Damp(sharp, dt));
            wingAmp = Mathf.Lerp(wingAmp, tWingAmp, wk);
            wingFreq = Mathf.Lerp(wingFreq, tWingFreq, wk);
            wingDroop = Mathf.Lerp(wingDroop, tWingDroop, wk);
            wingPhase += wingFreq * dt * Mathf.PI * 2f;

            float t = Time.time;
            bool aloft = State != Mode.Staggered && State != Mode.Dead;
            bob = aloft ? Mathf.Sin(t * 1.5f) * 0.09f + Mathf.Sin(wingPhase) * 0.012f * wingAmp / 26f : 0f;

            // body: hover height, a slow bob, lean/bank (+ a gentle sway)
            pivot.localPosition = pivotRestLocal + Vector3.up * (lift + bob);
            float sway = aloft ? Mathf.Sin(t * 1.1f) * 2.5f : 0f;
            pivot.localRotation = Quaternion.Euler(lean + sway * 0.4f, 0f, bank + sway);

            Quaternion P = pivot.rotation;
            Quaternion Ax(float a, Vector3 axis) => Quaternion.AngleAxis(a, axis);
            void Pose(Transform tr, Quaternion o) { if (tr && rest.TryGetValue(tr, out var r)) tr.rotation = P * o * r; }

            // torso + head (the head tracks you)
            Quaternion oT = Ax(twist, Vector3.up) * Ax(torsoPitch, Vector3.right);
            Pose(pelvis, Quaternion.identity);
            Pose(torso, oT);
            float wantYaw = 0f, wantPitch = 0f;
            if (PlayerAlive && head)
            {
                Vector3 to = Quaternion.Inverse(P) * (PlayerPos + Vector3.up * 1.4f - head.position);
                wantYaw = Mathf.Clamp(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg - twist, -70f, 70f);
                wantPitch = Mathf.Clamp(-Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg - torsoPitch, -30f, 35f);
            }
            headYawCur = Mathf.Lerp(headYawCur, wantYaw * headTrack, Ease.Damp(6f, dt));
            headPitchCur = Mathf.Lerp(headPitchCur, wantPitch * headTrack, Ease.Damp(6f, dt));
            Quaternion oH = oT * Ax(headYawCur, Vector3.up) * Ax(headPitchCur + headPitch, Vector3.right);
            Pose(head, oH);

            // hair and skirt drag behind the motion and swing with the body
            float tp = Mathf.Clamp(localVel.z * 3.2f + lean * 0.3f, -10f, 40f), tr = Mathf.Clamp(-localVel.x * 3.2f - bank * 0.3f, -30f, 30f);
            skirtPitch = Mathf.Lerp(skirtPitch, tp + (aloft ? Mathf.Sin(t * 2.3f) * 2f : 0f), Ease.Damp(5f, dt));
            skirtRoll = Mathf.Lerp(skirtRoll, tr + (aloft ? Mathf.Sin(t * 1.7f) * 2f : 0f), Ease.Damp(5f, dt));
            hairPitch = Mathf.Lerp(hairPitch, Mathf.Clamp(tp * 0.6f - headPitchCur * 0.4f, -8f, 30f), Ease.Damp(4f, dt));
            hairRoll = Mathf.Lerp(hairRoll, Mathf.Clamp(tr * 0.5f, -15f, 15f), Ease.Damp(4f, dt));
            Pose(skirt, Ax(skirtPitch, Vector3.right) * Ax(skirtRoll, Vector3.forward));
            Pose(hair, oH * Ax(hairPitch, Vector3.right) * Ax(hairRoll, Vector3.forward));

            // arms: elevation (-90 down .. 90 up) and azimuth (0 forward, + outward), then the elbow bend
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? -1f : 1f;
                Quaternion oU = oT * Ax(s * azim[i], Vector3.up) * Ax(-(elev[i] + 90f), Vector3.right);
                Pose(upperArm[i], oU);
                Quaternion oF = oU * Ax(-elbow[i], Vector3.right);
                Pose(forearm[i], oF);
                Pose(hand[i], oF * Ax(-elbow[i] * 0.25f, Vector3.right));
            }

            // legs dangle: hips forward/back, knees bend back, toes pointed; a slow paddling sway while aloft
            for (int i = 0; i < 2; i++)
            {
                float paddle = aloft ? Mathf.Sin(t * 1.5f + i * 1.9f) * 5f : 0f;
                Quaternion oTh = Ax(-(hip[i] + paddle), Vector3.right);
                Pose(thigh[i], oTh);
                Quaternion oSh = oTh * Ax(knee[i] + paddle * 0.8f, Vector3.right);
                Pose(shin[i], oSh);
                Pose(foot[i], oSh);
            }

            // wings: sweep forward/back round the root (flutter), tips dip and rise a little out of phase
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? -1f : 1f;
                float fu = wingOpen + Mathf.Sin(wingPhase) * wingAmp;
                float fd = wingOpen * 0.8f + Mathf.Sin(wingPhase - 0.7f) * wingAmp * 0.85f;
                float du = wingDroop + Mathf.Sin(wingPhase + 1.2f) * wingAmp * 0.3f;
                float dd = wingDroop * 0.6f + Mathf.Sin(wingPhase + 0.5f) * wingAmp * 0.25f;
                Pose(wingUp[i], oT * Ax(-s * fu, Vector3.up) * Ax(-s * du, Vector3.forward));
                Pose(wingDown[i], oT * Ax(-s * fd, Vector3.up) * Ax(-s * dd, Vector3.forward));
            }

            if (bodyLight)
            {
                float target = glowBase * Mathf.Max(0f, 1f + glowBoost + (phaseTwo ? 0.35f : 0f)) * (0.92f + 0.08f * Mathf.Sin(t * 3.1f));
                bodyLight.intensity = Mathf.Lerp(bodyLight.intensity, target, Ease.Damp(8f, dt));
            }
            UpdateFlash(dt);
        }

        void UpdateFlash(float dt)
        {
            flash = Mathf.MoveTowards(flash, 0f, dt * 6f);
            if (!(flash > 0f || flashWasOn) || flashRenderers == null) return;
            flashWasOn = flash > 0f;
            Color c = new Color(0.75f, 0.85f, 1f) * (flash * flash * 0.8f);
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
}
