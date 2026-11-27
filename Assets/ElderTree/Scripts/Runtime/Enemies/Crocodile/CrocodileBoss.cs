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
    /// Crocodile: a croc-headed bomber that spends most of the fight circling over the
    /// arena. Moves:
    ///  - Carpet bombing: lines up on you and lays a chain of bombs. Their impact rings appear first,
    ///    and each fills as its bomb falls.
    ///  - Precision strike: a few single bombs dropped on where you are going.
    ///  - Living bombs: a low pass that drops walking bombs that chase you and blink twice before they blow.
    ///  - Ground charge: lands at the far side, roars, runs you over in a straight line, then stays on the
    ///    field for a while (snapping its jaws at whatever is in front) before it takes off again. This is
    ///    the melee window.
    /// Arrows wear its stance down: break it in the air and it crash-lands, open to a critical blow.
    /// Below half health it flies faster, drops more and charges twice.
    /// </summary>
    public partial class CrocodileBoss : BossBase
    {
        public enum Mode { Dormant, Flying, Landing, Grounded, Charging, Staggered, Crashing, Dead, Cinematic }

        public string displayName = "Crocodile";

        [Header("Rig")]
        [Tooltip("Pivot at fuselage height: pitch and bank happen here.")]
        public Transform pivot;
        public Transform jaw;
        public Transform[] props;
        public Transform bombSocket;
        public Renderer[] flashRenderers;
        public AudioSource engineAudio;
        public ParticleSystem[] engineSmoke;
        public Collider[] bodyColliders;
        [Tooltip("Blinking belly beacon (light + glow mesh) so the plane can be tracked in the night sky.")]
        public Light beacon;
        public Renderer beaconGlow;
        public AerialBomb bombPrefab;
        public LivingBomb livingBombPrefab;

        [Header("Arena")]
        public Vector3 arenaCenter;
        public float arenaRadius = 13f;
        public float orbitRadius = 12.5f;
        public float cruiseAltitude = 11f;

        [Header("Stats")]
        public float maxHealth = 3200f;
        public float maxPoise = 150f;
        public float poiseRegenDelay = 4f;
        [Range(0f, 1f)] public float phaseTwoAt = 0.5f;

        [Header("Flight")]
        public float cruiseSpeed = 13f;
        public float turnRate = 70f;
        public float maxBank = 38f;

        [Header("Carpet bombing")]
        public int carpetBombs = 7;
        public float carpetSpacing = 2.7f;
        public float carpetRadius = 2.5f;
        public float carpetDamage = 140f;
        public float bombFallTime = 1.25f;

        [Header("Precision strike")]
        public int strikeBombs = 3;
        public float strikeRadius = 3.1f;
        public float strikeDamage = 165f;
        public float strikeFallTime = 1.35f;

        [Header("Living bombs")]
        public int livingBombs = 3;
        public int maxLivingBombs = 6;

        [Header("Ground charge")]
        public float chargeSpeed = 17f;
        public float chargeDamage = 190f;
        [Tooltip("Half width of the fuselage + inner propellers that runs you over.")]
        public float chargeHalfWidth = 2.6f;
        public float noseReach = 7.2f;
        public float groundedTime = 9f;
        public float biteDamage = 120f;
        public float staggerTime = 3.4f;

        public Mode State { get; private set; } = Mode.Dormant;
        protected override bool LocalIsAlive => State != Mode.Dead && State != Mode.Dormant;
        protected override bool LocalIsStaggered => State == Mode.Staggered;
        protected override bool LocalPhaseTwo => phaseTwo;
        public bool Airborne => transform.position.y > 0.5f;
        protected override float LocalHealth => health;
        protected override float LocalPoise => poise;
        protected override string LocalCurrentMove => currentMove;
        public override string DisplayName => displayName;
        protected override float LocalMaxHealth => maxHealth;
        protected override float MaxHealthField { get => maxHealth; set => maxHealth = value; }
        protected override string LocalStateLabel => State.ToString();
        protected override bool LocalIsDead => State == Mode.Dead;
        public override Vector3 ArenaCenter => arenaCenter;

        bool phaseTwo;
        float health, poise;
        string currentMove = "-";

        // kinematics
        Vector3 vel;
        float yaw, bank, pitch, pitchOffset, yawRate;
        float propSpin = 0f, propTarget = 0f, jawAngle, jawTarget, engineRev, flash, lastHitTime = -99f;
        Vector3 jawAxisLocal = Vector3.right;
        float jawSign = 1f;
        Coroutine brain;
        int orbitDir = 1;
        string forcedMove;
        readonly List<AerialBomb> bombPool = new();
        readonly List<LivingBomb> livingPool = new();
        Transform poolRoot;
        MaterialPropertyBlock mpb;
        bool flashWasOn;
        float SpeedMul => PhaseTwo ? 1.18f : 1f;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        PlayerController Player => Aggro;
        Vector3 PlayerPos => Player != null ? Player.transform.position : arenaCenter;
        bool PlayerAlive => Player != null && !Player.IsDead;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            health = maxHealth;
            poise = maxPoise;
            CalibrateJaw();
            SetBodyColliders(false);
        }

        void OnDestroy()
        {
            if (poolRoot != null) Destroy(poolRoot.gameObject);
        }

        /// <summary>Find the jaw hinge axis that swings the jaw tip downward.</summary>
        void CalibrateJaw()
        {
            if (jaw == null) return;
            var r = jaw.GetComponentInChildren<Renderer>();
            Vector3 tip = r != null ? r.bounds.center : jaw.position + transform.forward;
            Vector3 arm = tip - jaw.position;
            Vector3 axisWorld = Vector3.Cross(arm, Vector3.down).normalized;
            if (axisWorld.sqrMagnitude < 1e-4f) axisWorld = transform.right;
            jawAxisLocal = jaw.InverseTransformDirection(axisWorld);
            jawSign = 1f;
            jawRest = jaw.localRotation;
        }

        Quaternion jawRest;

        // ================================================================== encounter API
        public override void BeginFight()
        {
            StopAllCoroutines();
            ClearProjectiles();
            health = maxHealth;
            poise = maxPoise;
            phaseTwo = false;
            closePressure = spinCd = hopCd = belchCd = 0f;
            Pose(0f, 0f, 0f, 0f, 3f);
            foreach (var s in engineSmoke) if (s) s.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            State = Mode.Flying;
            gameObject.SetActive(true);
            if (engineAudio) { engineAudio.Play(); engineAudio.volume = 0f; }
            brain = StartCoroutine(Brain(true));
        }

        public override void ResetBoss()
        {
            StopAllCoroutines();
            ClearProjectiles();
            EndIntroDressing();
            State = Mode.Dormant;
            health = maxHealth;
            poise = maxPoise;
            if (engineAudio) engineAudio.Stop();
            SetBodyColliders(false);
            gameObject.SetActive(false);
        }

        public override void ForceNextMove(string move) => forcedMove = move;

        public override void ClearProjectiles()
        {
            foreach (var b in bombPool) if (b) b.Cancel();
            foreach (var l in livingPool) if (l && l.IsAlive) l.Despawn();
        }

        // ================================================================== brain
        IEnumerator Brain(bool intro)
        {
            if (intro) yield return Intro();
            string last = "";
            int sinceCharge = 0;
            while (true)
            {
                if (!PhaseTwo && Health <= maxHealth * phaseTwoAt) yield return PhaseShift();
                yield return Orbit(Random.Range(PhaseTwo ? 1.4f : 2.2f, PhaseTwo ? 2.8f : 3.8f) / Aggression);

                string move = forcedMove ?? PickMove(last, sinceCharge);
                forcedMove = null;
                last = move;
                sinceCharge = move == "Charge" ? 0 : sinceCharge + 1;
                currentMove = move;
                switch (move)
                {
                    case "Carpet": yield return CarpetBombing(); break;
                    case "Strike": yield return PrecisionStrike(); break;
                    case "Living": yield return DeployLivingBombs(); break;
                    case "Charge": yield return GroundCharge(); break;
                }
                currentMove = "-";
            }
        }

        string PickMove(string last, int sinceCharge)
        {
            if (!PlayerAlive) return "Carpet";
            int alive = 0;
            foreach (var l in livingPool) if (l && l.IsAlive) alive++;
            var opts = new List<(string, float)>
            {
                ("Carpet", 3f), ("Strike", 2.4f),
                ("Living", alive + livingBombs <= maxLivingBombs ? 2f : 0f),
                ("Charge", sinceCharge >= 1 ? 2.6f + sinceCharge * 0.8f : 0f),
            };
            float total = 0f;
            for (int i = 0; i < opts.Count; i++)
            {
                if (opts[i].Item1 == last) opts[i] = (opts[i].Item1, opts[i].Item2 * 0.25f);
                total += opts[i].Item2;
            }
            float r = Random.value * total;
            foreach (var (n, w) in opts) { if ((r -= w) <= 0f) return n; }
            return "Carpet";
        }

        // ================================================================== flight primitives
        Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        Vector3 Heading => Quaternion.Euler(0, yaw, 0) * Vector3.forward;

        /// <summary>Banked turn toward a flat direction while holding an altitude and speed.</summary>
        void Steer(Vector3 dir, float altitude, float speed, float dt, float turnMul = 1f)
        {
            dir = Flat(dir);
            float prevYaw = yaw;
            if (dir.sqrMagnitude > 1e-4f)
            {
                float target = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                yaw = Mathf.MoveTowardsAngle(yaw, target, turnRate * turnMul * SpeedMul * dt);
            }
            yawRate = Mathf.DeltaAngle(prevYaw, yaw) / Mathf.Max(dt, 1e-4f);
            float vy = Mathf.Clamp((altitude - transform.position.y) * 1.3f, -7f, 7f);
            Vector3 h = Heading * Mathf.Lerp(Flat(vel).magnitude, speed, Ease.Damp(1.5f, dt));
            vel = new Vector3(h.x, Mathf.Lerp(vel.y, vy, Ease.Damp(3f, dt)), h.z);
            transform.position += vel * dt;
            ApplyAttitude(dt, true);
        }

        void ApplyAttitude(float dt, bool flying)
        {
            float targetBank = flying ? Mathf.Clamp(-yawRate * 0.55f, -maxBank, maxBank) : groundBank;
            float speed = Mathf.Max(1f, Flat(vel).magnitude);
            float targetPitch = flying ? Mathf.Clamp(-Mathf.Atan2(vel.y, speed) * Mathf.Rad2Deg, -18f, 18f) : 0f;
            bank = Mathf.Lerp(bank, targetBank, Ease.Damp(flying ? 3f : poseSharp, dt));
            pitch = Mathf.Lerp(pitch, targetPitch + pitchOffset, Ease.Damp(flying ? 4f : poseSharp + 1f, dt));
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (pivot) pivot.localRotation = Quaternion.Euler(pitch, 0f, bank);
        }

        /// <summary>Direction toward a point a little ahead on the orbit circle (pulls back in from far away).</summary>
        Vector3 OrbitDir()
        {
            Vector3 rel = Flat(transform.position - arenaCenter);
            float ang = Mathf.Atan2(rel.z, rel.x) + 0.65f * orbitDir;
            Vector3 carrot = arenaCenter + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * orbitRadius;
            return carrot - transform.position;
        }

        IEnumerator Orbit(float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                float dt = Time.deltaTime;
                t += dt;
                Steer(OrbitDir(), cruiseAltitude, cruiseSpeed * SpeedMul, dt);
                yield return null;
            }
        }

        /// <summary>Cubic Hermite flight from the current state to a point arriving along a tangent.</summary>
        IEnumerator FlyPath(Vector3 to, Vector3 endDir, float speedEnd, float tangentScale = 1f, System.Action<float> onStep = null)
        {
            Vector3 p0 = transform.position;
            Vector3 v0 = vel.sqrMagnitude > 0.1f ? vel : Heading * cruiseSpeed;
            float len = Vector3.Distance(p0, to);
            Vector3 m0 = v0.normalized * len * tangentScale, m1 = endDir.normalized * len * tangentScale;
            float avgSpeed = (v0.magnitude + speedEnd) * 0.5f;
            float T = Mathf.Max(0.6f, len * 1.15f / Mathf.Max(3f, avgSpeed));
            float t = 0f;
            while (t < T)
            {
                float dt = Time.deltaTime;
                t = Mathf.Min(T, t + dt);
                float s = t / T;
                float s2 = s * s, s3 = s2 * s;
                Vector3 p = (2 * s3 - 3 * s2 + 1) * p0 + (s3 - 2 * s2 + s) * m0 + (-2 * s3 + 3 * s2) * to + (s3 - s2) * m1;
                Vector3 d = (6 * s2 - 6 * s) * p0 + (3 * s2 - 4 * s + 1) * m0 + (-6 * s2 + 6 * s) * to + (3 * s2 - 2 * s) * m1;
                vel = (p - transform.position) / Mathf.Max(dt, 1e-4f);
                transform.position = p;
                Vector3 df = Flat(d);
                float prev = yaw;
                if (df.sqrMagnitude > 1e-3f) yaw = Mathf.Atan2(df.x, df.z) * Mathf.Rad2Deg;
                yawRate = Mathf.DeltaAngle(prev, yaw) / Mathf.Max(dt, 1e-4f);
                ApplyAttitude(dt, true);
                onStep?.Invoke(s);
                yield return null;
            }
            vel = endDir.normalized * speedEnd;
        }

        // ================================================================== intro / phase shift
        IEnumerator Intro()
        {
            // appear far out in the mist, roar over the arena low, climb to the orbit
            Vector3 dir = Flat(PlayerPos - arenaCenter);
            if (dir.sqrMagnitude < 1f) dir = Vector3.forward;
            dir = Quaternion.Euler(0, Random.Range(-50f, 50f), 0) * dir.normalized;
            transform.position = arenaCenter + dir * 70f + Vector3.up * 16f;
            yaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;
            vel = Heading * 20f;
            propSpin = propTarget = 2400f;
            ApplyAttitude(1f, true);
            float t = 0f;
            bool roared = false;
            orbitDir = Random.value < 0.5f ? 1 : -1;
            while (t < 6.5f)
            {
                float dt = Time.deltaTime;
                t += dt;
                Vector3 to = Flat(PlayerPos - transform.position);
                float alt = to.magnitude > 18f ? 7f : 8f;
                bool passed = roared && Vector3.Dot(to, Heading) < 0f;
                Steer(passed ? OrbitDir() : to, passed ? cruiseAltitude : alt, passed ? cruiseSpeed : 20f, dt, passed ? 1f : 0.6f);
                if (!roared && to.magnitude < 14f)
                {
                    roared = true;
                    Roar(1f);
                }
                yield return null;
            }
        }

        IEnumerator PhaseShift()
        {
            phaseTwo = true;
            currentMove = "Rage";
            foreach (var s in engineSmoke) if (s) s.Play(true);
            Roar(1.15f);
            CameraDirector.Instance?.Shake(0.5f, Vector3.down);
            yield return Orbit(1.6f);
        }

        void Roar(float pitchScale)
        {
            AudioManager.Play(Sfx.CrocRoar, transform.position + transform.forward * 4f, 1f, Random.Range(0.95f, 1.05f) / pitchScale);
            StartCoroutine(JawOpen(40f, 1.3f));
            engineRev = 1f;
        }

        IEnumerator JawOpen(float degrees, float hold)
        {
            jawTarget = degrees;
            yield return new WaitForSeconds(hold);
            jawTarget = 0f;
        }

        // ================================================================== carpet bombing
        IEnumerator CarpetBombing()
        {
            int n = PhaseTwo ? carpetBombs + 3 : carpetBombs;
            float L = (n - 1) * carpetSpacing;
            float speed = cruiseSpeed * SpeedMul;
            float alt = cruiseAltitude - 1f;

            float lead = speed * bombFallTime * 0.5f;    // the bomb trails forward under the plane
            float need = L * 0.5f + lead + 4f;

            // plan the run: of 16 headings across the player, take the one whose entry point lies ahead
            // of the plane and lines up with its current flight, then curve onto it
            Vector3 C0 = Flat(PlayerPos);
            Vector3 X = Flat(transform.position);
            Vector3 D = Heading;
            float best = float.MaxValue;
            for (int k = 0; k < 16; k++)
            {
                Vector3 cand = Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward;
                Vector3 toP = (C0 - cand * need) - X;
                float d = toP.magnitude;
                float score = Vector3.Angle(Heading, toP) + Vector3.Angle(toP, cand) * 0.7f + d * 0.6f + (d < 12f ? 300f : 0f)
                              + (Flat(C0 - cand * need - arenaCenter).magnitude > 40f ? 200f : 0f);
                if (score < best) { best = score; D = cand; }
            }
            Vector3 P0 = C0 - D * need;
            P0.y = alt;
            yield return FlyPath(P0, D, speed, 0.85f);

            // commit: the line runs along D over where the player is now (within reach of the flight line)
            Vector3 C = C0;
            Vector3 off = Flat(PlayerPos) - C0;
            Vector3 side = Vector3.Cross(Vector3.up, D);
            C += D * Mathf.Clamp(Vector3.Dot(off, D), -4f, 4f) + side * Mathf.Clamp(Vector3.Dot(off, side), -5f, 5f);
            C = Blast.Ground(C);
            var bombs = new AerialBomb[n];
            var targets = new Vector3[n];
            var dropAt = new float[n];
            float along0 = Vector3.Dot(Flat(transform.position), D);
            for (int i = 0; i < n; i++)
            {
                targets[i] = Blast.Ground(C + D * (-L * 0.5f + i * carpetSpacing));
                float alongT = Vector3.Dot(Flat(targets[i]), D) - lead;
                dropAt[i] = Time.time + Mathf.Max(0.3f, (alongT - along0) / speed);
                bombs[i] = GetBomb();
                bombs[i].Plan(targets[i], carpetRadius, carpetDamage, dropAt[i] + bombFallTime, gameObject);
            }
            AudioManager.Play(Sfx.CrocRoar, transform.position, 0.6f, 1.25f);
            // bomb bay run: straight and level
            int next = 0;
            float end = dropAt[n - 1] + 0.8f;
            while (Time.time < end)
            {
                float dt = Time.deltaTime;
                Steer(D, alt, speed, dt, 0f);
                while (next < n && Time.time >= dropAt[next])
                {
                    bombs[next].Release(SocketPos, vel);
                    next++;
                }
                yield return null;
            }
        }

        Vector3 SocketPos => bombSocket != null ? bombSocket.position : transform.position + Vector3.up * 0.8f;

        // ================================================================== precision strike
        IEnumerator PrecisionStrike()
        {
            int n = PhaseTwo ? strikeBombs + 1 : strikeBombs;
            float speed = cruiseSpeed * SpeedMul * 0.95f;
            int dropped = 0;
            float nextDrop = Time.time + 0.6f;
            float t = 0f;
            while (dropped < n && t < 12f)
            {
                float dt = Time.deltaTime;
                t += dt;
                Vector3 to = Flat(PlayerPos - transform.position);
                Steer(to.magnitude > 9f ? to : Heading, cruiseAltitude, speed, dt, 1.1f);
                if (Time.time >= nextDrop && to.magnitude < 17f && PlayerAlive)
                {
                    Vector3 lead = Player.motor.PlanarVelocity * strikeFallTime * 0.9f;
                    Vector3 target = Blast.Ground(PlayerPos + lead);
                    var b = GetBomb();
                    b.Plan(target, strikeRadius, strikeDamage, Time.time + strikeFallTime, gameObject);
                    b.Release(SocketPos, vel);
                    if (PhaseTwo)
                    {
                        // a second bomb where you are now: running in a straight line isn't enough
                        var b2 = GetBomb();
                        b2.Plan(Blast.Ground(PlayerPos), strikeRadius * 0.85f, strikeDamage * 0.8f, Time.time + strikeFallTime + 0.15f, gameObject);
                        b2.Release(SocketPos, vel);
                    }
                    dropped++;
                    nextDrop = Time.time + (PhaseTwo ? 0.8f : 1.05f);
                }
                yield return null;
            }
            yield return Orbit(0.8f);
        }

        // ================================================================== living bombs
        IEnumerator DeployLivingBombs()
        {
            int n = PhaseTwo ? livingBombs + 1 : livingBombs;
            float speed = cruiseSpeed * SpeedMul * 0.85f;
            const float lowAlt = 5.5f;
            float t = 0f;
            // swoop: dive toward the player, drop a stick of walkers as we pass
            while (t < 8f)
            {
                float dt = Time.deltaTime;
                t += dt;
                Vector3 to = Flat(PlayerPos - transform.position);
                Steer(to.magnitude > 6f ? to : Heading, lowAlt, speed, dt, 1.2f);
                if (to.magnitude < 11f && Vector3.Angle(Heading, to) < 30f && transform.position.y < lowAlt + 2.5f) break;
                yield return null;
            }
            AudioManager.Play(Sfx.CrocRoar, transform.position, 0.55f, 1.4f);
            for (int i = 0; i < n; i++)
            {
                var lb = GetLivingBomb();
                Vector3 side = Quaternion.Euler(0, 90, 0) * Heading * ((i % 2 == 0 ? 1 : -1) * (0.6f + 0.5f * i));
                lb.Launch(SocketPos, vel * 0.55f + side + Vector3.down * 1f, this, PhaseTwo ? 1.15f : 1f);
                float w = 0.32f;
                while ((w -= Time.deltaTime) > 0f) { Steer(Heading, lowAlt, speed, Time.deltaTime); yield return null; }
            }
            // climb out
            t = 0f;
            while (t < 2.5f) { t += Time.deltaTime; Steer(OrbitDir(), cruiseAltitude, cruiseSpeed * SpeedMul, Time.deltaTime, 0.8f); yield return null; }
        }

        // ================================================================== ground charge
        IEnumerator GroundCharge()
        {
            // touchdown point across the arena from the player
            Vector3 pRel = Flat(PlayerPos - arenaCenter);
            float pAng = pRel.magnitude > 2.5f ? Mathf.Atan2(pRel.z, pRel.x) : Random.Range(0f, Mathf.PI * 2f);
            float sAng = pAng + Mathf.PI + Random.Range(-0.55f, 0.55f);
            Vector3 S = Blast.Floor(arenaCenter + new Vector3(Mathf.Cos(sAng), 0, Mathf.Sin(sAng)) * (arenaRadius - 1f));
            Vector3 H = Flat(PlayerPos - S).normalized;
            if (H.sqrMagnitude < 0.5f) H = Flat(arenaCenter - S).normalized;

            // approach from outside the arena, gliding down onto the line
            Vector3 A = S - H * 16f + Vector3.up * 9f;
            float t = 0f;
            while (t < 9f && Flat(A - transform.position).magnitude > 6f)
            {
                float dt = Time.deltaTime;
                t += dt;
                Steer(A - transform.position, 9f, cruiseSpeed * SpeedMul, dt, 1.2f);
                yield return null;
            }
            State = Mode.Landing;
            currentMove = "Charge";
            propTarget = 1500f;
            yield return FlyPath(S, H, 9f, 0.8f);

            // touchdown + rollout
            State = Mode.Charging;
            transform.position = S;
            vel = H * 9f;
            yaw = Mathf.Atan2(H.x, H.z) * Mathf.Rad2Deg;
            Touchdown(0.8f);
            yield return Roll(0f, 2.4f, false, 14f);

            int charges = PhaseTwo ? 2 : 1;
            for (int c = 0; c < charges; c++)
            {
                // roar and re-aim, turning on the spot like a croc
                engineRev = 1f;
                Roar(1f);
                float aimT = 0f;
                float windup = c == 0 ? 1.35f : 0.9f;
                while (aimT < windup)
                {
                    float dt = Time.deltaTime;
                    aimT += dt;
                    TurnInPlace(PlayerPos, c == 0 ? 120f : 200f, dt);
                    if (Random.value < dt * 10f) FXManager.Spawn(FXType.DustSmall, transform.position + transform.forward * 2.5f + transform.right * Random.Range(-4f, 4f), Quaternion.identity, 1.2f);
                    yield return null;
                }
                // full throttle across the arena
                Vector3 dir = transform.forward;
                // stop with the snout at the wall line
                float run = DistanceToEdge(transform.position - arenaCenter, dir, arenaRadius + 2.5f) - noseReach + 0.5f;
                run = Mathf.Max(run, 6f);
                bool hit = false;
                Vector3 start = transform.position;
                float spd = 0f, travelled = 0f, dustT = 0f;
                AudioManager.Play(Sfx.Skid, transform.position, 0.6f, 0.8f);
                while (travelled < run)
                {
                    float dt = Time.deltaTime;
                    float remaining = run - travelled;
                    float target = remaining < 5f ? Mathf.Lerp(3f, chargeSpeed * SpeedMul, remaining / 5f) : chargeSpeed * SpeedMul;
                    spd = Mathf.MoveTowards(spd, target, (remaining < 5f ? 40f : 32f) * dt);
                    travelled += spd * dt;
                    transform.position = start + dir * travelled;
                    vel = dir * spd;
                    pitchOffset = -3f;
                    ApplyAttitude(dt, false);
                    if (HitEach(hit, () => ContactHit(chargeDamage, ImpactWeight.Slam))) hit = true;
                    if ((dustT -= dt) <= 0f) { dustT = 0.09f; WheelDust(1f); }
                    yield return null;
                }
                pitchOffset = 0f;
                vel = Vector3.zero;
                FXManager.Spawn(FXType.Dust, transform.position + transform.forward * 3f, Quaternion.identity, 2f);
                AudioManager.Play(Sfx.Skid, transform.position, 0.9f, 1.1f);
                CameraDirector.Instance?.Shake(0.35f, transform.forward);
            }

            yield return Grounded(PhaseTwo ? groundedTime * 0.8f : groundedTime);
            yield return Takeoff();
        }

        static float DistanceToEdge(Vector3 p, Vector3 dir, float r)
        {
            // ray vs circle in XZ around the arena centre (assumed at origin offset applied by caller)
            Vector2 o = new Vector2(p.x, p.z), d = new Vector2(dir.x, dir.z).normalized;
            float b = Vector2.Dot(o, d), c = o.sqrMagnitude - r * r;
            float disc = b * b - c;
            if (disc < 0f) return 0f;
            return Mathf.Max(0f, -b + Mathf.Sqrt(disc));
        }

        void TurnInPlace(Vector3 at, float rate, float dt)
        {
            Vector3 to = Flat(at - transform.position);
            if (to.sqrMagnitude < 0.5f) return;
            float target = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float prev = yaw;
            yaw = Mathf.MoveTowardsAngle(yaw, target, rate * dt);
            yawRate = Mathf.DeltaAngle(prev, yaw) / Mathf.Max(dt, 1e-4f);
            bank = Mathf.Lerp(bank, Mathf.Clamp(-yawRate * 0.04f, -5f, 5f), Ease.Damp(6f, dt));
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (pivot) pivot.localRotation = Quaternion.Euler(pitch, 0f, bank);
        }

        /// <summary>Decelerating ground roll along the heading.</summary>
        IEnumerator Roll(float endSpeed, float maxTime, bool contact, float decel = 7f)
        {
            bool rollHit = false;
            float t = 0f;
            float dustT = 0f;
            while (t < maxTime && Flat(vel).magnitude > endSpeed + 0.2f)
            {
                float dt = Time.deltaTime;
                t += dt;
                vel = Vector3.MoveTowards(vel, Heading * endSpeed, decel * dt);
                Vector3 np = transform.position + vel * dt;
                // never roll out of the arena
                if (Flat(np - arenaCenter).magnitude < arenaRadius + 1f) transform.position = np;
                ApplyAttitude(dt, false);
                if (contact && HitEach(rollHit, () => ContactHit(chargeDamage * 0.6f, ImpactWeight.Heavy))) rollHit = true;
                if ((dustT -= dt) <= 0f) { dustT = 0.12f; WheelDust(0.7f); }
                yield return null;
            }
            vel = Vector3.zero;
        }

        void Touchdown(float k)
        {
            Vector3 p = transform.position;
            FXManager.Spawn(FXType.Dust, p, Quaternion.identity, 2.4f * k);
            FXManager.Spawn(FXType.SlamDebris, p + transform.forward * 1.5f, Quaternion.identity, 1.2f * k);
            AudioManager.Play(Sfx.Skid, p, 1f);
            AudioManager.Play(Sfx.Slam, p, 0.7f * k, 0.7f);
            CameraDirector.Instance?.Shake(0.6f * k, Vector3.down);
            Player?.GetComponent<InputHandling.PlayerInputReader>()?.Rumble(0.5f * k, 0.3f * k, 0.3f);
        }

        void WheelDust(float scale)
        {
            Vector3 p = transform.position;
            Vector3 r = transform.right;
            FXManager.Spawn(FXType.DustSmall, p + r * 2.1f, Quaternion.LookRotation(-transform.forward), scale * 1.4f);
            FXManager.Spawn(FXType.DustSmall, p - r * 2.1f, Quaternion.LookRotation(-transform.forward), scale * 1.4f);
        }

        /// <summary>Run-over check against the fuselage, head and inner propellers (the outer wing passes overhead).</summary>
        bool ContactHit(float damage, ImpactWeight weight)
        {
            if (!PlayerAlive) return false;
            Vector3 rel = transform.InverseTransformPoint(PlayerPos);
            if (rel.y > 2.6f || rel.z < -1.2f || rel.z > noseReach || Mathf.Abs(rel.x) > chargeHalfWidth) return false;
            Vector3 push = (transform.right * Mathf.Sign(rel.x == 0f ? 1f : rel.x) * 0.8f + transform.forward).normalized;
            return Player.TakeHit(new DamageInfo
            {
                damage = damage, poiseDamage = damage * 0.6f, point = PlayerPos + Vector3.up, direction = push,
                weight = weight, source = gameObject
            });
        }

        // ================================================================== grounded melee
        // On the field the croc fights back. Every move is announced by the body, not by a ground marker:
        //  - Bite combo (in front): rears its head up, jaws gaping, then lunges and snaps; 2 bites (3 in phase 2).
        //  - Wing sweep (at a flank): turns away and cocks the near wing high, revs, then whips round.
        //  - Tail slam (behind): nose dips into the dirt so the tail lifts high, then it crashes down.
        //  - Propeller spin (you've been hugging / hitting it): crouches, the props scream up, then a full
        //    whirl that clips everything around it; it staggers dizzy afterwards (punish window).
        //  - Hop slam (mid range): crouches low and bounces onto you; the landing shakes three bombs loose.
        //  - Bomb belch (far): head rears back, mouth wide, and lobs a spread of bombs (their shadows show where).
        float closePressure, spinCd, hopCd, belchCd;
        float liftTarget, lift, groundBank, poseSharp = 3f;
        Vector3 pivotBase;
        bool pivotBaseSet;

        Vector3 Rel => transform.InverseTransformPoint(PlayerPos);
        float FlatDist => Flat(PlayerPos - transform.position).magnitude;

        void Pose(float pitchT, float bankT, float liftT, float jawT, float sharp)
        {
            pitchOffset = pitchT;
            groundBank = bankT;
            liftTarget = liftT;
            jawTarget = jawT;
            poseSharp = sharp;
        }

        void YawToward(Vector3 at, float rate, float dt)
        {
            Vector3 to = Flat(at - transform.position);
            if (to.sqrMagnitude < 0.5f) return;
            float prev = yaw;
            yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, rate * dt);
            yawRate = Mathf.DeltaAngle(prev, yaw) / Mathf.Max(dt, 1e-4f);
        }

        void GroundStep(float dt) => ApplyAttitude(dt, false);

        void SlideBy(Vector3 delta)
        {
            Vector3 np = transform.position + delta;
            Vector3 rel = Flat(np - arenaCenter);
            if (rel.magnitude > arenaRadius) np = arenaCenter + rel.normalized * arenaRadius + Vector3.up * np.y;
            transform.position = np;
        }

        IEnumerator Hold(float time, System.Action<float, float> each = null)
        {
            float t = 0f;
            while (t < time)
            {
                float dt = Time.deltaTime;
                t += dt;
                each?.Invoke(t / time, dt);
                GroundStep(dt);
                yield return null;
            }
        }

        bool HitPlayer(float damage, ImpactWeight weight, Vector3 push, float poise)
        {
            if (!PlayerAlive) return false;
            push.y = 0f;
            return Player.TakeHit(new DamageInfo
            {
                damage = damage, poiseDamage = poise, point = PlayerPos + Vector3.up, direction = push.sqrMagnitude > 1e-3f ? push.normalized : transform.forward,
                weight = weight, source = gameObject
            });
        }

        IEnumerator Grounded(float duration)
        {
            State = Mode.Grounded;
            currentMove = "Grounded";
            SetBodyColliders(true);
            propTarget = 600f;
            float t = 0f, cd = 0.9f;
            while (t < duration)
            {
                float dt = Time.deltaTime;
                t += dt;
                cd -= dt;
                spinCd -= dt; hopCd -= dt; belchCd -= dt;
                Vector3 rel = Rel;
                float dist = FlatDist;
                if (dist < 6.5f) closePressure += dt * 0.22f;
                else closePressure = Mathf.Max(0f, closePressure - dt * 0.15f);

                // idle: breathing jaw, slowly squaring up to you
                Pose(0f, 0f, 0f, 6f + 4f * Mathf.Sin(t * 2.3f), 4f);
                if (PlayerAlive && rel.z > -1f) YawToward(PlayerPos, 45f * SpeedMul, dt);
                GroundStep(dt);

                if (cd <= 0f && PlayerAlive)
                {
                    float t0 = Time.time;
                    IEnumerator move = PickGroundMove(rel, dist);
                    if (move != null)
                    {
                        yield return move;
                        t += Time.time - t0;
                        cd = Random.Range(PhaseTwo ? 0.25f : 0.45f, PhaseTwo ? 0.6f : 0.95f) / Aggression;
                        currentMove = "Grounded";
                        continue;
                    }
                }
                yield return null;
            }
            Pose(0f, 0f, 0f, 0f, 3f);
            SetBodyColliders(false);
        }

        IEnumerator PickGroundMove(Vector3 rel, float dist)
        {
            float side = Mathf.Sign(rel.x == 0f ? 1f : rel.x);
            if (closePressure >= 1f && spinCd <= 0f && dist < 9f) return PropSpin();
            if (rel.z < -1.8f && Mathf.Abs(rel.x) < 3.6f && dist < 10f) return TailSlam();
            if (Mathf.Abs(rel.x) > 1.5f && rel.z > -3.5f && rel.z < 4f && dist < 7.5f) return WingSweep(side);
            if (rel.z > 1.2f && Mathf.Abs(rel.x) < 2.8f && rel.z < noseReach + 3.5f) return BiteCombo();
            if (dist > 7f && dist < 15f && hopCd <= 0f && (belchCd > 0f || Random.value < 0.55f)) return HopSlam();
            if (dist > 8f && belchCd <= 0f) return BombBelch();
            return null;
        }

        IEnumerator BiteCombo()
        {
            int bites = PhaseTwo ? 3 : 2;
            for (int i = 0; i < bites; i++)
            {
                currentMove = "Bite";
                // rear up: head high, jaws wide, weight shifted back; tracks you while it winds up
                float windup = (i == 0 ? 0.62f : 0.4f) / SpeedMul;
                AudioManager.Play(Sfx.CrocRoar, transform.position + transform.forward * 5f, 0.45f, 1.7f);
                yield return Hold(windup, (k, dt) =>
                {
                    Pose(-13f * Ease.OutCubic(k), 0f, 0.45f * k, 58f, 7f);
                    YawToward(PlayerPos, 70f * SpeedMul, dt);
                    SlideBy(-transform.forward * dt * 0.5f);
                });
                // lunge + snap
                AudioManager.Play(Sfx.WhooshHeavy, transform.position + transform.forward * 5f, 1f, 0.7f);
                bool hit = false;
                yield return Hold(0.13f, (k, dt) =>
                {
                    Pose(9f, 0f, -0.1f, -2f, 22f);
                    SlideBy(transform.forward * dt * 2.4f / 0.13f);
                    if (HitEach(hit, () =>
                    {
                        Vector3 r = Rel;
                        return r.z > 2f && r.z < noseReach + 2.4f && Mathf.Abs(r.x) < 2.2f && r.y < 3f &&
                               HitPlayer(biteDamage * (i == bites - 1 ? 1.15f : 1f), ImpactWeight.Heavy, transform.forward, 60f);
                    })) hit = true;
                });
                AudioManager.Play(Sfx.JawSnap, transform.position + transform.forward * 5f, 1f);
                CameraDirector.Instance?.Shake(0.28f, transform.forward);
                FXManager.Spawn(FXType.DustSmall, transform.position + transform.forward * 6f, Quaternion.identity, 1.6f);
                yield return Hold(i == bites - 1 ? 0.55f : 0.16f, (k, dt) => Pose(4f * (1f - k), 0f, 0f, 4f, 6f));
            }
        }

        IEnumerator WingSweep(float side)
        {
            currentMove = "WingSweep";
            // turn away from you and cock the near wing up (positive bank lifts the right wing)
            float startYaw = yaw;
            engineRev = 1f;
            propTarget = 2400f;
            AudioManager.Play(Sfx.Skid, transform.position, 0.5f, 0.75f);
            yield return Hold(0.8f / SpeedMul, (k, dt) =>
            {
                float e = Ease.OutCubic(k);
                yaw = startYaw - side * 32f * e;
                Pose(0f, side * 30f * e, 0.45f * e, 20f, 8f);
                if (Random.value < dt * 10f) FXManager.Spawn(FXType.DustSmall, transform.position + transform.right * side * 4.5f, Quaternion.identity, 1.2f);
            });
            // whip round through the flank, the wing slamming down
            AudioManager.Play(Sfx.WhooshHeavy, transform.position + transform.right * side * 4f, 1f, 0.6f);
            bool hit = false;
            float from = yaw;
            yield return Hold(0.3f, (k, dt) =>
            {
                float prev = yaw;
                yaw = from + side * 115f * Ease.InOutCubic(k);
                yawRate = Mathf.DeltaAngle(prev, yaw) / Mathf.Max(dt, 1e-4f);
                Pose(0f, -side * 14f, 0f, 20f, 18f);
                if (HitEach(hit, () =>
                {
                    Vector3 r = Rel;
                    return r.y < 2.8f && r.z > -5.6f && r.z < 0.6f && r.x * side > 1.2f && r.x * side < 6.6f &&   // box-cell wings sit aft (z -2.5..-4.6)
                           HitPlayer(150f, ImpactWeight.Heavy, transform.right * side + transform.forward * 0.4f, 70f);
                })) hit = true;
            });
            CameraDirector.Instance?.Shake(0.35f, transform.right * side);
            FXManager.Spawn(FXType.Dust, transform.position + transform.right * side * 4.5f - transform.forward * 3.5f, Quaternion.identity, 1.6f);
            AudioManager.Play(Sfx.Slam, transform.position + transform.right * side * 4f, 0.6f, 1.2f);
            yield return Hold(0.65f, (k, dt) => Pose(0f, 0f, 0f, 8f, 5f));
        }

        IEnumerator TailSlam()
        {
            currentMove = "TailSlam";
            // nose into the dirt, tail up high, a rattle of warning
            AudioManager.Play(Sfx.Skid, transform.position, 0.6f, 0.6f);
            AudioManager.Play(Sfx.CrocRoar, transform.position, 0.4f, 1.9f);
            yield return Hold(0.85f / SpeedMul, (k, dt) =>
            {
                Pose(15f * Ease.OutCubic(k), Mathf.Sin(k * 20f) * 2f * k, 0.25f * k, 0f, 6f);
                if (Random.value < dt * 8f) FXManager.Spawn(FXType.DustSmall, transform.position + transform.forward * 5.5f, Quaternion.identity, 1f);
            });
            bool hit = false;
            yield return Hold(0.14f, (k, dt) =>
            {
                Pose(-7f, 0f, -0.15f, 0f, 26f);
                if (k > 0.6f && HitEach(hit, () =>
                {
                    Vector3 r = Rel;
                    return r.z < -2.2f && r.z > -9.5f && Mathf.Abs(r.x) < 3.2f && r.y < 2.5f &&
                           HitPlayer(170f, ImpactWeight.Slam, -transform.forward, 90f);
                })) hit = true;
            });
            Vector3 tail = transform.position - transform.forward * 5.2f;   // the pusher prop
            FXManager.Spawn(FXType.SlamRing, Blast.Ground(tail) + Vector3.up * 0.05f, Quaternion.identity, 2.4f);
            FXManager.Spawn(FXType.Dust, tail, Quaternion.identity, 2f);
            FXManager.Spawn(FXType.SlamDebris, tail, Quaternion.identity, 1.2f);
            AudioManager.Play(Sfx.Slam, tail, 1f, 0.8f);
            CameraDirector.Instance?.Shake(0.55f, Vector3.down);
            yield return Hold(0.75f, (k, dt) => Pose(-2f * (1f - k), 0f, 0f, 6f, 4f));
        }

        IEnumerator PropSpin()
        {
            currentMove = "Spin";
            closePressure = 0f;
            spinCd = PhaseTwo ? 7f : 10f;
            // crouch, props wind up to a scream: get out
            propTarget = 2400f;
            engineRev = 1f;
            AudioManager.Play(Sfx.CrocRoar, transform.position, 0.8f, 1.2f);
            yield return Hold(0.95f / SpeedMul, (k, dt) =>
            {
                engineRev = 1f;
                Pose(-3f, Mathf.Sin(k * 40f) * 3f * k, -0.3f * k, 12f, 8f);
                if (Random.value < dt * 16f)
                {
                    float a = Random.value * Mathf.PI * 2f;
                    FXManager.Spawn(FXType.DustSmall, transform.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 4.5f, Quaternion.identity, 1.3f);
                }
            });
            bool hit = false;
            float from = yaw;
            float dir = Random.value < 0.5f ? 1f : -1f;
            AudioManager.Play(Sfx.WhooshHeavy, transform.position, 1f, 0.55f);
            yield return Hold(0.7f, (k, dt) =>
            {
                float prev = yaw;
                yaw = from + dir * 540f * Ease.InOutCubic(k);
                yawRate = Mathf.DeltaAngle(prev, yaw) / Mathf.Max(dt, 1e-4f);
                Pose(0f, dir * 16f, 0.2f, 30f, 12f);
                if (Random.value < dt * 20f) WheelDust(1.2f);
                if (HitEach(hit, () => PlayerAlive && FlatDist < 7f && Rel.y < 2.6f &&
                                       HitPlayer(130f, ImpactWeight.Heavy, Flat(PlayerPos - transform.position), 80f))) hit = true;
            });
            CameraDirector.Instance?.Shake(0.4f, Vector3.down);
            // dizzy: the punish window
            currentMove = "Dizzy";
            propTarget = 300f;
            yield return Hold(1.15f, (k, dt) => Pose(5f, Mathf.Sin(k * 14f) * 9f * (1f - k), -0.1f, 32f, 5f));
        }

        IEnumerator HopSlam()
        {
            currentMove = "HopSlam";
            hopCd = PhaseTwo ? 4f : 6f;
            propTarget = 2400f;
            engineRev = 1f;
            AudioManager.Play(Sfx.CrocRoar, transform.position, 0.6f, 1.4f);
            yield return Hold(0.6f / SpeedMul, (k, dt) =>
            {
                YawToward(PlayerPos, 160f, dt);
                Pose(-6f * k, 0f, -0.4f * Ease.OutCubic(k), 30f, 9f);
            });
            // hop onto where you are (clamped inside the arena)
            Vector3 start = transform.position;
            Vector3 land = PlayerPos;
            Vector3 lr = Flat(land - arenaCenter);
            if (lr.magnitude > arenaRadius - 1f) land = arenaCenter + lr.normalized * (arenaRadius - 1f);
            land = Blast.Floor(land) - transform.forward * 1.5f;   // the belly lands on you, not the nose
            AudioManager.Play(Sfx.Jump, transform.position, 1f, 0.5f);
            FXManager.Spawn(FXType.Dust, transform.position, Quaternion.identity, 2f);
            const float air = 0.8f;
            float t = 0f;
            while (t < air)
            {
                float dt = Time.deltaTime;
                t += dt;
                float s = Mathf.Clamp01(t / air);
                Vector3 p = Vector3.Lerp(start, land, s);
                p.y = Mathf.Lerp(start.y, land.y, s) + Mathf.Sin(s * Mathf.PI) * 4f;
                transform.position = p;
                Pose(Mathf.Lerp(-10f, 6f, s), 0f, 0f, 30f, 8f);
                GroundStep(dt);
                yield return null;
            }
            transform.position = land;
            Touchdown(1.1f);
            FXManager.Spawn(FXType.SlamRing, land + Vector3.up * 0.05f, Quaternion.identity, 3f);
            HitEach(false, () => PlayerAlive && FlatDist < 5.2f && Rel.y < 2.5f &&
                                 HitPlayer(160f, ImpactWeight.Slam, Flat(PlayerPos - transform.position), 90f));
            // the landing shakes bombs loose: their shadows show where
            int n = PhaseTwo ? 4 : 3;
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector3 p = Blast.Ground(transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(3.5f, 6.5f));
                var b = GetBomb();
                b.Plan(p, 1.9f, 75f, Time.time + 1.0f + i * 0.12f, gameObject);
                b.Release(p + Vector3.up * 7f, Vector3.zero);
            }
            yield return Hold(0.6f, (k, dt) => Pose(4f * (1f - k), 0f, 0f, 10f, 5f));
        }

        IEnumerator BombBelch()
        {
            currentMove = "Belch";
            belchCd = PhaseTwo ? 4.5f : 6.5f;
            AudioManager.Play(Sfx.CrocRoar, transform.position + transform.forward * 5f, 0.9f, 0.9f);
            yield return Hold(0.75f / SpeedMul, (k, dt) =>
            {
                YawToward(PlayerPos, 120f, dt);
                Pose(-17f * Ease.OutCubic(k), 0f, 0.3f * k, 64f, 7f);
            });
            int n = PhaseTwo ? 5 : 3;
            Vector3 mouth = transform.position + transform.forward * 6.5f + Vector3.up * 2.4f;
            Vector3 lead = Player != null ? Player.motor.PlanarVelocity * 0.8f : Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Vector3 jitter = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * (i == 0 ? 0.3f : 2.6f);
                Vector3 p = Blast.Ground(PlayerPos + lead * (i == 0 ? 1f : 0.5f) + jitter);
                var b = GetBomb();
                b.Plan(p, 2.3f, strikeDamage * 0.75f, Time.time + 1.15f + i * 0.14f, gameObject);
                b.Release(mouth, (p - mouth) * 0.6f + Vector3.up * 6f);
                AudioManager.Play(Sfx.BombPop, mouth, 0.5f, 1.4f);
                pitchOffset = 4f;
                yield return Hold(0.12f);
                pitchOffset = -14f;
            }
            yield return Hold(0.6f, (k, dt) => Pose(0f, 0f, 0f, 10f, 5f));
        }

        void ApplyAttitudeGround() => ApplyAttitude(Time.deltaTime, false);

        IEnumerator Takeoff()
        {
            currentMove = "Takeoff";
            Pose(0f, 0f, 0f, 0f, 3f);
            State = Mode.Charging;
            SetBodyColliders(false);
            // face across the arena if the wall is close ahead
            if (DistanceToEdge(transform.position - arenaCenter, transform.forward, arenaRadius) < 14f)
            {
                float t0 = 0f;
                while (t0 < 1.6f) { t0 += Time.deltaTime; TurnInPlace(arenaCenter, 110f, Time.deltaTime); yield return null; }
            }
            engineRev = 1f;
            propTarget = 2400f;
            AudioManager.Play(Sfx.CrocRoar, transform.position, 0.7f, 1.2f);
            float spd = 0f, t = 0f, dustT = 0f;
            bool runHit = false;
            while (transform.position.y < cruiseAltitude - 1f && t < 6f)
            {
                float dt = Time.deltaTime;
                t += dt;
                spd = Mathf.MoveTowards(spd, cruiseSpeed * SpeedMul * 1.1f, 9f * dt);
                float climb = t > 1.1f ? Mathf.Min(6f, (t - 1.1f) * 5f) : 0f;
                vel = Heading * spd + Vector3.up * climb;
                transform.position += vel * dt;
                pitchOffset = t > 1.1f ? -6f : 0f;
                ApplyAttitude(dt, false);
                if (transform.position.y < 0.5f)
                {
                    if (HitEach(runHit, () => ContactHit(chargeDamage * 0.7f, ImpactWeight.Heavy))) runHit = true;
                    if ((dustT -= dt) <= 0f) { dustT = 0.1f; WheelDust(0.8f); }
                }
                yield return null;
            }
            pitchOffset = 0f;
            State = Mode.Flying;
        }

        // ================================================================== stance break / crash
        void OnStanceBreak()
        {
            if (brain != null) StopCoroutine(brain);
            brain = StartCoroutine(StaggerRoutine());
        }

        IEnumerator StaggerRoutine()
        {
            currentMove = "Stagger";
            pitchOffset = 0f;
            groundBank = 0f; liftTarget = 0f; poseSharp = 3f; closePressure = 0f;
            SetBodyColliders(false);
            AudioManager.Play(Sfx.Stagger, transform.position, 1f, 0.6f);
            AudioManager.Play(Sfx.CrocRoar, transform.position, 0.8f, 0.75f);
            if (Airborne)
            {
                // engines choke; it drops out of the sky and belly-lands
                State = Mode.Crashing;
                propTarget = 300f;
                foreach (var s in engineSmoke) if (s) s.Play(true);
                Vector3 g = transform.position + Heading * 11f;
                Vector3 gRel = Flat(g - arenaCenter);
                if (gRel.magnitude > arenaRadius - 2f) g = arenaCenter + gRel.normalized * (arenaRadius - 2f);
                g = Blast.Floor(g);
                Vector3 endDir = Flat(g - transform.position);
                if (endDir.sqrMagnitude < 1f) endDir = Heading;
                yield return FlyPath(g, endDir.normalized + Vector3.down * 0.3f, 8f, 0.7f);
                transform.position = g;
                vel = Flat(vel).normalized * 7f;
                yaw = Mathf.Atan2(vel.x, vel.z) * Mathf.Rad2Deg;
                Touchdown(1.4f);
                Blast.Detonate(g + transform.forward * 2f, 3.2f, 90f, true, gameObject, 0.7f);
                yield return Roll(0f, 1.5f, false);
                if (!PhaseTwo) foreach (var s in engineSmoke) if (s) s.Stop();
            }
            State = Mode.Staggered;
            SetBodyColliders(true);
            propTarget = 120f;
            jawTarget = 30f;
            pitchOffset = 5f;          // nose in the dirt
            float t = 0f;
            while (t < staggerTime)
            {
                t += Time.deltaTime;
                ApplyAttitude(Time.deltaTime, false);
                if (Random.value < Time.deltaTime * 3f)
                    FXManager.Spawn(FXType.HitSpark, transform.position + Vector3.up * 2.5f + transform.right * Random.Range(-4f, 4f), Quaternion.LookRotation(Vector3.up), 0.6f);
                yield return null;
            }
            pitchOffset = 0f;
            jawTarget = 0f;
            poise = maxPoise;
            State = Mode.Grounded;
            yield return Grounded(groundedTime * 0.5f);
            yield return Takeoff();
            brain = StartCoroutine(Brain(false));
        }

        // ================================================================== damage
        protected override bool LocalReceiveHit(in DamageInfo info)
        {
            if (!IsAlive || State == Mode.Crashing || State == Mode.Cinematic) return false;
            lastHitTime = Time.time;
            if (State == Mode.Grounded) closePressure += info.explosive ? 0.12f : 0.2f;
            health = Mathf.Max(0f, Health - info.damage);
            bool broke = false;
            if (State != Mode.Staggered && !info.critical)
            {
                poise -= info.poiseDamage;
                if (Poise <= 0f) { poise = maxPoise; broke = true; }
            }
            flash = 1f;
            FXManager.Spawn(FXType.HitSpark, info.point, Quaternion.LookRotation(-info.direction + Vector3.up * 0.3f), info.weight == ImpactWeight.Light ? 1f : 1.4f);
            AudioManager.Play(Sfx.MetalHit, info.point, 0.9f, info.weight == ImpactWeight.Light ? 1.05f : 0.85f);
            RaiseDamaged(info, broke);
            if (Health <= 0f) { Die(); return true; }
            if (broke) OnStanceBreak();
            return true;
        }

        void Die()
        {
            StopAllCoroutines();
            brain = null;
            ClearProjectiles();
            State = Mode.Dead;
            currentMove = "Dead";
            groundBank = 0f; liftTarget = 0f; poseSharp = 3f;
            SetBodyColliders(true);
            RaiseFelled();
            StartCoroutine(DeathRoutine());
        }

        IEnumerator DeathRoutine()
        {
            TimeDirector.SlowMo(1.1f, 0.3f);
            AudioManager.Play(Sfx.CrocRoar, transform.position, 1f, 0.6f);
            foreach (var s in engineSmoke) if (s) s.Play(true);
            propTarget = 0f;
            jawTarget = 35f;
            if (Airborne)
            {
                Vector3 g = transform.position + Heading * 12f;
                Vector3 gRel = Flat(g - arenaCenter);
                if (gRel.magnitude > arenaRadius - 2f) g = arenaCenter + gRel.normalized * (arenaRadius - 2f);
                g = Blast.Floor(g);
                Vector3 endDir = Flat(g - transform.position);
                if (endDir.sqrMagnitude < 1f) endDir = Heading;
                yield return FlyPath(g, endDir.normalized + Vector3.down * 0.4f, 9f, 0.7f, s => bank = Mathf.Lerp(bank, 25f, s * 0.1f));
                transform.position = g;
                vel = Flat(vel).normalized * 8f;
                Touchdown(1.8f);
                yield return Roll(0f, 1.6f, false);
            }
            // the bomb bay goes up, piece by piece
            for (int i = 0; i < 5; i++)
            {
                Vector3 p = transform.position + transform.forward * Random.Range(-4f, 4f) + transform.right * Random.Range(-4f, 4f) + Vector3.up * Random.Range(1f, 2.5f);
                Blast.Detonate(p, 2f, 0f, false, gameObject, i == 4 ? 1.6f : 0.9f);
                flash = 1f;
                yield return new WaitForSeconds(i == 3 ? 0.6f : 0.28f);
            }
            pitchOffset = 7f;
            bank = 9f;
            float t = 0f;
            while (t < 1.5f) { t += Time.deltaTime; ApplyAttitude(Time.deltaTime, false); if (engineAudio) engineAudio.volume = Mathf.Lerp(engineAudio.volume, 0f, Time.deltaTime * 2f); yield return null; }
            if (engineAudio) engineAudio.Stop();
        }

        /// <summary>Nearest point on the fuselage line (used for splash damage from popped living bombs).</summary>
        public Vector3 ClosestBodyPoint(Vector3 p)
        {
            Vector3 a = transform.position + transform.TransformVector(new Vector3(0, 1.8f, -4.5f));
            Vector3 b = transform.position + transform.TransformVector(new Vector3(0, 1.8f, 6.5f));
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return a + ab * t;
        }

        void SetBodyColliders(bool on)
        {
            foreach (var c in bodyColliders) if (c) c.enabled = on;
        }

        // ================================================================== pools
        AerialBomb GetBomb()
        {
            EnsurePoolRoot();
            foreach (var b in bombPool) if (b && !b.InUse) return b;
            var nb = Instantiate(bombPrefab, poolRoot);
            bombPool.Add(nb);
            return nb;
        }

        LivingBomb GetLivingBomb()
        {
            EnsurePoolRoot();
            foreach (var l in livingPool) if (l && !l.IsAlive) return l;
            var nl = Instantiate(livingBombPrefab, poolRoot);
            livingPool.Add(nl);
            return nl;
        }

        // co-op: clients mirror the bombs and walkers under the same root
        public override Transform NetPoolRoot(bool create)
        {
            if (create) EnsurePoolRoot();
            return poolRoot;
        }
        public override GameObject[] NetPoolPrefabs => new[] { bombPrefab ? bombPrefab.gameObject : null, livingBombPrefab ? livingBombPrefab.gameObject : null };

        void EnsurePoolRoot()
        {
            if (poolRoot != null) return;
            poolRoot = new GameObject("Crocodile_Ordnance").transform;   // world space: never moves with the plane
        }

        // ================================================================== presentation
        float EngineVolume => 1f * (AudioManager.Instance != null ? AudioManager.Instance.SfxLevel : 1f);

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || State == Mode.Dormant) return;

            if (IsAlive && State != Mode.Staggered && Time.time - lastHitTime > poiseRegenDelay)
                poise = Mathf.MoveTowards(Poise, maxPoise, maxPoise * 0.25f * dt);

            // ground poses raise / lower the body on its gear
            if (pivot)
            {
                if (!pivotBaseSet) { pivotBase = pivot.localPosition; pivotBaseSet = true; }
                lift = Mathf.Lerp(lift, liftTarget, Ease.Damp(10f, dt));
                pivot.localPosition = pivotBase + Vector3.up * lift;
            }

            // propellers: spin around the nose axis, each a little out of phase
            propSpin = Mathf.Lerp(propSpin, propTarget, Ease.Damp(engineRev > 0.1f ? 4f : 1.2f, dt));
            if (State == Mode.Flying && propTarget < 2000f) propTarget = 2400f;
            Vector3 axis = transform.forward;
            if (pivot) axis = pivot.forward;
            for (int i = 0; i < props.Length; i++)
                if (props[i]) props[i].Rotate(axis, propSpin * (1f + i * 0.03f) * dt * (i % 2 == 0 ? 1f : -1f), Space.World);

            // jaw
            jawAngle = Mathf.Lerp(jawAngle, jawTarget, Ease.Damp(jawTarget > jawAngle ? 9f : 22f, dt));
            if (jaw) jaw.localRotation = Quaternion.AngleAxis(jawAngle * jawSign, jawAxisLocal) * jawRest;

            // engine drone follows prop speed; revs spike on roars and throttle-ups
            engineRev = Mathf.MoveTowards(engineRev, 0f, dt * 0.8f);
            if (engineAudio && State != Mode.Dead)
            {
                engineAudio.pitch = 0.55f + propSpin / 2400f * 0.5f + engineRev * 0.25f;
                engineAudio.volume = Mathf.MoveTowards(engineAudio.volume, EngineVolume * (0.55f + propSpin / 2400f * 0.45f) * engineLevel, dt * 0.6f);
            }

            if (beacon)
            {
                bool on = State != Mode.Dead && Mathf.Repeat(Time.time, 1.1f) < 0.16f;
                beacon.enabled = on;
                if (beaconGlow) beaconGlow.enabled = on;
            }

            flash = Mathf.MoveTowards(flash, 0f, dt * 6f);
            if (flash > 0f || flashWasOn)
            {
                flashWasOn = flash > 0f;
                Color c = new Color(1f, 0.85f, 0.6f) * (flash * flash * 0.8f);
                foreach (var r in flashRenderers)
                {
                    if (!r) continue;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        // glowing parts (eyes, lit cockpit) keep their own emission
                        if (mats[i] == null || mats[i].GetColor(EmissionId).maxColorComponent > 0.01f) continue;
                        r.GetPropertyBlock(mpb, i);
                        mpb.SetColor(EmissionId, c);
                        r.SetPropertyBlock(mpb, i);
                    }
                }
            }
        }
    }
}
