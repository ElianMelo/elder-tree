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
    /// Bombardiro Crocodilo: a croc-headed bomber that spends most of the fight circling over the
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
    public class BombardiroBoss : MonoBehaviour, IDamageable, IStaggerable
    {
        public enum Mode { Dormant, Flying, Landing, Grounded, Charging, Staggered, Crashing, Dead }

        public string displayName = "Bombardiro Crocodilo";

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
        public float strikeFallTime = 1.6f;

        [Header("Living bombs")]
        public int livingBombs = 3;
        public int maxLivingBombs = 6;

        [Header("Ground charge")]
        public float chargeSpeed = 17f;
        public float chargeDamage = 190f;
        [Tooltip("Half width of the fuselage + inner propellers that runs you over.")]
        public float chargeHalfWidth = 2.6f;
        public float noseReach = 7.2f;
        public float groundedTime = 6.5f;
        public float biteDamage = 120f;
        public float staggerTime = 3.4f;

        public Mode State { get; private set; } = Mode.Dormant;
        public bool IsAlive => State != Mode.Dead && State != Mode.Dormant;
        public bool IsStaggered => State == Mode.Staggered;
        public bool PhaseTwo { get; private set; }
        public bool Airborne => transform.position.y > 0.5f;
        public Transform Root => transform;
        public float Health { get; private set; }
        public float Poise { get; private set; }
        public string CurrentMove { get; private set; } = "-";

        public event System.Action<BombardiroBoss, DamageInfo, bool> Damaged;
        public event System.Action<BombardiroBoss> Felled;

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

        PlayerController Player => PlayerController.Instance;
        Vector3 PlayerPos => Player != null ? Player.transform.position : arenaCenter;
        bool PlayerAlive => Player != null && !Player.IsDead;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            Health = maxHealth;
            Poise = maxPoise;
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
        public void BeginFight()
        {
            StopAllCoroutines();
            ClearProjectiles();
            Health = maxHealth;
            Poise = maxPoise;
            PhaseTwo = false;
            foreach (var s in engineSmoke) if (s) s.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            State = Mode.Flying;
            gameObject.SetActive(true);
            if (engineAudio) { engineAudio.Play(); engineAudio.volume = 0f; }
            brain = StartCoroutine(Brain(true));
        }

        public void ResetBoss()
        {
            StopAllCoroutines();
            ClearProjectiles();
            State = Mode.Dormant;
            Health = maxHealth;
            Poise = maxPoise;
            if (engineAudio) engineAudio.Stop();
            SetBodyColliders(false);
            gameObject.SetActive(false);
        }

        public void ForceNextMove(string move) => forcedMove = move;

        public void ClearProjectiles()
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
                yield return Orbit(Random.Range(PhaseTwo ? 1.4f : 2.2f, PhaseTwo ? 2.8f : 3.8f));

                string move = forcedMove ?? PickMove(last, sinceCharge);
                forcedMove = null;
                last = move;
                sinceCharge = move == "Charge" ? 0 : sinceCharge + 1;
                CurrentMove = move;
                switch (move)
                {
                    case "Carpet": yield return CarpetBombing(); break;
                    case "Strike": yield return PrecisionStrike(); break;
                    case "Living": yield return DeployLivingBombs(); break;
                    case "Charge": yield return GroundCharge(); break;
                }
                CurrentMove = "-";
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
            float targetBank = flying ? Mathf.Clamp(-yawRate * 0.55f, -maxBank, maxBank) : 0f;
            float speed = Mathf.Max(1f, Flat(vel).magnitude);
            float targetPitch = flying ? Mathf.Clamp(-Mathf.Atan2(vel.y, speed) * Mathf.Rad2Deg, -18f, 18f) : 0f;
            bank = Mathf.Lerp(bank, targetBank, Ease.Damp(3f, dt));
            pitch = Mathf.Lerp(pitch, targetPitch + pitchOffset, Ease.Damp(4f, dt));
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
            PhaseTwo = true;
            CurrentMove = "Rage";
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
                    Vector3 lead = Player.motor.PlanarVelocity * strikeFallTime * 0.55f;
                    Vector3 target = Blast.Ground(PlayerPos + lead);
                    var b = GetBomb();
                    b.Plan(target, strikeRadius, strikeDamage, Time.time + strikeFallTime, gameObject);
                    b.Release(SocketPos, vel);
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
            CurrentMove = "Charge";
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
                    if (!hit) hit = ContactHit(chargeDamage, ImpactWeight.Slam);
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
                if (contact && !rollHit) rollHit = ContactHit(chargeDamage * 0.6f, ImpactWeight.Heavy);
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

        // ================================================================== grounded window
        IEnumerator Grounded(float duration)
        {
            State = Mode.Grounded;
            CurrentMove = "Grounded";
            SetBodyColliders(true);
            propTarget = 600f;
            float t = 0f, biteCd = 1.2f;
            while (t < duration)
            {
                float dt = Time.deltaTime;
                t += dt;
                biteCd -= dt;
                Vector3 rel = transform.InverseTransformPoint(PlayerPos);
                // tracks you slowly while you are in front; flanks and the tail are safe
                if (PlayerAlive && rel.z > 0f && Mathf.Abs(Mathf.Atan2(rel.x, rel.z)) < 1.2f) TurnInPlace(PlayerPos, 28f, dt);
                jawTarget = 6f + 4f * Mathf.Sin(t * 2.3f);
                bool inFront = PlayerAlive && rel.z > 2.5f && rel.z < noseReach + 1.5f && Mathf.Abs(rel.x) < 2.4f;
                if (inFront && biteCd <= 0f)
                {
                    yield return Bite();
                    biteCd = PhaseTwo ? 1.0f : 1.6f;
                    t += 0.7f;
                }
                yield return null;
            }
            SetBodyColliders(false);
        }

        IEnumerator Bite()
        {
            CurrentMove = "Bite";
            AudioManager.Play(Sfx.CrocRoar, transform.position + transform.forward * 5f, 0.5f, 1.6f);
            jawTarget = 48f;
            pitchOffset = -5f;      // head rears up
            float t = 0f;
            while (t < 0.55f) { t += Time.deltaTime; TurnInPlace(PlayerPos, 40f, Time.deltaTime); ApplyAttitudeGround(); yield return null; }
            jawTarget = -2f;
            pitchOffset = 4f;       // lunges down
            yield return new WaitForSeconds(0.08f);
            AudioManager.Play(Sfx.JawSnap, transform.position + transform.forward * 5f, 1f);
            CameraDirector.Instance?.Shake(0.25f, transform.forward);
            if (PlayerAlive)
            {
                Vector3 rel = transform.InverseTransformPoint(PlayerPos);
                if (rel.z > 2.5f && rel.z < noseReach + 1.3f && Mathf.Abs(rel.x) < 2.0f && rel.y < 3f)
                    Player.TakeHit(new DamageInfo
                    {
                        damage = biteDamage, poiseDamage = 60f, point = PlayerPos + Vector3.up, direction = transform.forward,
                        weight = ImpactWeight.Heavy, source = gameObject
                    });
            }
            yield return new WaitForSeconds(0.45f);
            pitchOffset = 0f;
            CurrentMove = "Grounded";
        }

        void ApplyAttitudeGround() => ApplyAttitude(Time.deltaTime, false);

        IEnumerator Takeoff()
        {
            CurrentMove = "Takeoff";
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
                    if (!runHit) runHit = ContactHit(chargeDamage * 0.7f, ImpactWeight.Heavy);
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
            CurrentMove = "Stagger";
            pitchOffset = 0f;
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
            Poise = maxPoise;
            State = Mode.Grounded;
            yield return Grounded(groundedTime * 0.5f);
            yield return Takeoff();
            brain = StartCoroutine(Brain(false));
        }

        // ================================================================== damage
        public bool ReceiveHit(in DamageInfo info)
        {
            if (!IsAlive || State == Mode.Crashing) return false;
            lastHitTime = Time.time;
            Health = Mathf.Max(0f, Health - info.damage);
            bool broke = false;
            if (State != Mode.Staggered && !info.critical)
            {
                Poise -= info.poiseDamage;
                if (Poise <= 0f) { Poise = maxPoise; broke = true; }
            }
            flash = 1f;
            FXManager.Spawn(FXType.HitSpark, info.point, Quaternion.LookRotation(-info.direction + Vector3.up * 0.3f), info.weight == ImpactWeight.Light ? 1f : 1.4f);
            AudioManager.Play(Sfx.MetalHit, info.point, 0.9f, info.weight == ImpactWeight.Light ? 1.05f : 0.85f);
            Damaged?.Invoke(this, info, broke);
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
            CurrentMove = "Dead";
            SetBodyColliders(true);
            Felled?.Invoke(this);
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

        void EnsurePoolRoot()
        {
            if (poolRoot != null) return;
            poolRoot = new GameObject("Bombardiro_Ordnance").transform;   // world space: never moves with the plane
        }

        // ================================================================== presentation
        float EngineVolume => 0.85f * (AudioManager.Instance != null ? AudioManager.Instance.sfxVolume : 1f);

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || State == Mode.Dormant) return;

            if (IsAlive && State != Mode.Staggered && Time.time - lastHitTime > poiseRegenDelay)
                Poise = Mathf.MoveTowards(Poise, maxPoise, maxPoise * 0.25f * dt);

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
                engineAudio.volume = Mathf.MoveTowards(engineAudio.volume, EngineVolume * (0.55f + propSpin / 2400f * 0.45f), dt * 0.6f);
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
