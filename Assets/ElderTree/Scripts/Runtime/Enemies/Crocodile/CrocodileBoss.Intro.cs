using System.Collections;
using ElderTree.Audio;
using ElderTree.FX;
using UnityEngine;

namespace ElderTree.Enemies
{
    /// <summary>
    /// The Crocodile's first-meeting cinematic (~27 s), played like an aircraft's departure from its airstrip
    /// (<see cref="CrocodileAirstrip"/>):
    ///  1. It sleeps on the hardstanding in the floodlight, nose sagging into the mud, snoring. (From beside you.)
    ///  2. It wakes: a snort, a huge yawn, a snap of the jaws, and it shakes the sleep off.
    ///  3. Engine start: the prop is cranked twice and coughs smoke; the third time it catches, and it runs the engine up.
    ///  4. It taxis out and turns onto the runway. The garrison's searchlights swing onto it.
    ///  5. Run-up against the brakes: prop screaming, nose down, a storm of dust behind.
    ///  6. Brakes off: the takeoff run, rotation, and it lifts off past the runway-side camera.
    ///  7. A high chase camera follows its climbing turn over the garrison. Its name comes up.
    ///  8. It dives back in and roars low over your head. The fight starts with it in the air.
    /// </summary>
    public partial class CrocodileBoss
    {
        [Header("Intro cinematic (first meeting)")]
        public string introEpithet = "Bomber Ace of the Drowned Garrison";
        public Color introNameColor = new Color(0.93f, 0.8f, 0.5f);
        public AudioClip introMusic;
        [Range(0f, 1f)] public float introMusicVolume = 0.55f;
        [Tooltip("A deep boom under the name card.")]
        public AudioClip introRevealClip;
        [Tooltip("Its snore while it sleeps (played low).")]
        public AudioClip introSnoreClip;
        [Range(0.3f, 1.2f)] public float introSnorePitch = 0.5f;
        [Range(0f, 1f)] public float introSnoreVolume = 0.55f;
        [Tooltip("A deep whoosh as it roars past the camera.")]
        public AudioClip introPassClip;

        public override bool HasIntroCinematic => CrocodileAirstrip.Find() != null;

        const float WaterY = -0.14f;
        CrocodileAirstrip strip;
        /// <summary>The engine drone's share of its volume (0 while it sleeps with the engine cold).</summary>
        float engineLevel = 1f;
        // the cinematic camera's own shake (Cinemachine is off while it plays)
        float camShake;
        Light cineFill;

        // ================================================================== the departure
        public override IEnumerator IntroCinematic(BossCinematic c)
        {
            StopAllCoroutines();
            ClearProjectiles();
            strip = CrocodileAirstrip.Find();
            if (strip == null || strip.park == null || strip.lineUp == null) yield break;
            health = maxHealth;
            poise = maxPoise;
            phaseTwo = false;
            forcedMove = null;
            closePressure = spinCd = hopCd = belchCd = 0f;
            gameObject.SetActive(true);
            State = Mode.Cinematic;
            SetBodyColliders(false);
            foreach (var s in engineSmoke) if (s) s.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            camShake = 0f;
            FillLight(true);

            Vector3 up = Vector3.up;
            Vector3 Pk = Blast.Floor(strip.park.position);
            Vector3 Fk = Flat(strip.park.forward).normalized;
            Vector3 Rk = Vector3.Cross(up, Fk);
            Vector3 L = Blast.Floor(strip.lineUp.position);
            Vector3 H = Flat(strip.lineUp.forward).normalized;          // down the runway
            Vector3 Rh = Vector3.Cross(up, H);
            Vector3 centre = arenaCenter;
            Vector3 P = c.Player != null ? c.Player.transform.position : arenaCenter;

            // asleep on the hardstanding: nose sagging into the mud, prop still, engine cold
            transform.position = Pk;
            yaw = YawOf(Fk);
            vel = Vector3.zero;
            yawRate = 0f;
            propSpin = propTarget = 0f;
            engineRev = 0f;
            engineLevel = 0f;
            if (engineAudio) { engineAudio.volume = 0f; engineAudio.pitch = 0.55f; engineAudio.Play(); }
            Pose(6f, 2.5f, -0.3f, 7f, 2f);
            pitch = 6f; bank = 2.5f; lift = -0.3f; jawAngle = 7f;
            ApplyAttitude(1f, false);
            c.PlayerWatch(Pk);
            AudioManager.PlayCinematicMusic(introMusic, introMusicVolume);

            // ---- 1. from beside you: the bomber asleep in the floodlight, snoring, pushing in
            currentMove = "IntroSleep";
            Vector3 Fp = Flat(Pk - P).normalized, Rp = Vector3.Cross(up, Fp);
            const float breath = 1.9f;
            float bt = 0f;
            int snores = 0;
            yield return Hold(3.6f, (k, dt) =>
            {
                bt += dt;
                float b = Mathf.Sin(bt / breath * Mathf.PI * 2f);
                Pose(6f - 0.8f * b, 2.5f, -0.3f + 0.05f * b, 7f + 6f * Mathf.Max(0f, b), 2f);
                if (bt / breath > snores + 0.05f) { snores++; Snore(); }
                float e = Ease.InOutSine(k);
                Cam(c, OnFloor(P - Fp * Mathf.Lerp(3.2f, 2.4f, e) + Rp * 2.3f, Mathf.Lerp(1.3f, 1.5f, e)), Pk + up * 1.7f, Mathf.Lerp(32f, 18f, e));
            });

            // ---- 2. it wakes: a snort, a yawn that shows every tooth, a snap of the jaws, then it shakes the sleep off
            currentMove = "IntroWake";
            Vector3 headCam = ClearOfLights(OnFloor(Pk + Fk * 12.5f + Rk * 3.4f, 2.0f));
            bool snorted = false, yawned = false, snapped = false;
            yield return Hold(3.0f, (k, dt) =>
            {
                if (!snorted && k > 0.06f) { snorted = true; Snort(); }
                if (k < 0.2f) Pose(6f, 2.5f * (1f - k / 0.2f), -0.3f, 7f, 4f);
                else if (k < 0.58f)
                {
                    if (!yawned) { yawned = true; AudioManager.Play(Sfx.CrocRoar, Snout, 0.45f, 0.6f); }
                    float e = Ease.OutCubic((k - 0.2f) / 0.38f);
                    Pose(Mathf.Lerp(6f, -11f, e), 0f, Mathf.Lerp(-0.3f, 0.3f, e), 62f * e, 3f);
                }
                else if (k < 0.66f)
                {
                    if (!snapped) { snapped = true; AudioManager.Play(Sfx.JawSnap, Snout, 0.8f, 0.85f); Kick(0.05f); }
                    Pose(-4f, 0f, 0.1f, 0f, 14f);
                }
                else
                {
                    float s = (k - 0.66f) / 0.34f;
                    Pose(0f, 8f * Mathf.Sin(s * 22f) * (1f - s), 0f, 4f, 10f);
                }
                float e2 = Ease.InOutSine(k);
                Cam(c, headCam - Fk * 1.2f * e2, HeadPos + up * 0.2f, Mathf.Lerp(40f, 36f, e2));
            });

            // ---- 3. engine start: two cranks that cough smoke and die, the third catches; then it runs the engine up
            currentMove = "IntroWarmUp";
            Vector3 warmCam = ClearOfLights(OnFloor(Pk - Fk * 13f + Rk * 3.5f, 4.2f));   // up behind the tail, over the prop
            int step = 0;
            float washT = 0f, wt = 0f;
            yield return Hold(5.0f, (k, dt) =>
            {
                wt += dt;
                if (step == 0 && wt > 0.4f) { step++; Crank(380f, 6, 0.55f); }
                if (step == 1 && wt > 1.35f) { step++; Crank(560f, 10, 0.75f); }
                if (step == 2 && wt > 2.3f) { step++; Catch(); }
                if (step == 3 && wt > 3.6f)
                {
                    step++;
                    propTarget = 1900f;
                    engineRev = 1f;
                    AudioManager.Play(Sfx.CrocRoar, Snout, 0.5f, 1.25f);
                }
                if (step == 4 && wt > 4.6f) { step++; propTarget = 1100f; }
                bool running = step >= 3;
                float shudder = !running ? 0f : step == 4 ? 1.6f : 1f;
                Pose(running ? 1.5f + Mathf.Sin(wt * 31f) * 0.6f * shudder : 0f, Mathf.Sin(wt * 23f) * 1.3f * shudder,
                    Mathf.Sin(wt * 37f) * 0.025f * shudder, step == 4 ? 26f : 4f, 14f);
                if (running && (washT -= dt) <= 0f) { washT = step == 4 ? 0.06f : 0.16f; PropWash(step == 4 ? 1.2f : 0.6f); }
                float e = Ease.InOutSine(k);
                Cam(c, warmCam + Fk * 1.6f * e, Vector3.Lerp(PropPos, HeadPos, 0.35f), Mathf.Lerp(42f, 38f, e));
            });

            // ---- 4. it taxis out and turns onto the runway; the garrison's searchlights swing onto it
            currentMove = "IntroTaxi";
            Searchlight.Spot = transform;
            Searchlight.SpotLift = 2f;
            propTarget = 1300f;
            Vector3 p0 = transform.position;
            float len = Flat(L - p0).magnitude;
            Vector3 m0 = Fk * len * 1.1f, m1 = H * len * 1.1f;
            Vector3 dMid = Flat(HermD(p0, m0, L, m1, 0.5f)).normalized;
            float turn = Mathf.Sign(Vector3.Cross(Fk, H).y);              // + turning right
            Vector3 taxiCam = ClearOfLights(OnFloor(Herm(p0, m0, L, m1, 0.5f) - Vector3.Cross(up, dMid) * turn * 12f, 2.6f));   // outside the turn
            float wakeT = 0f;
            bool smokeOff = false;
            yield return Hold(3.6f, (k, dt) =>
            {
                float s = Ease.InOutSine(k);                                // rolls off gently, eases to a stop on the line
                Vector3 p = Blast.Floor(Herm(p0, m0, L, m1, s));
                Vector3 d = Flat(HermD(p0, m0, L, m1, s));
                vel = (p - transform.position) / Mathf.Max(dt, 1e-4f);
                transform.position = p;
                if (d.sqrMagnitude > 1e-4f)
                {
                    float prev = yaw;
                    yaw = YawOf(d);
                    yawRate = Mathf.DeltaAngle(prev, yaw) / Mathf.Max(dt, 1e-4f);
                }
                float spd = Flat(vel).magnitude;
                Pose(1.2f * Mathf.Sin(k * 23f) * Mathf.Min(1f, spd / 3f), Mathf.Clamp(yawRate * 0.05f, -6f, 6f) + Mathf.Sin(k * 17f) * 0.8f,
                    0.03f * Mathf.Sin(k * 29f), 6f, 8f);
                if ((wakeT -= dt) <= 0f && spd > 0.8f) { wakeT = 0.14f; TaxiWake(0.7f); }
                if (!smokeOff && k > 0.7f)
                {
                    smokeOff = true;                                        // warm now: it burns clean
                    foreach (var sm in engineSmoke) if (sm) sm.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
                Cam(c, taxiCam, transform.position + up * 2f, 44f);
            });

            // ---- 5. run-up against the brakes: prop screaming, nose down, a storm of dust behind it
            currentMove = "IntroRunUp";
            transform.position = L;
            vel = Vector3.zero;
            yaw = YawOf(H);
            yawRate = 0f;
            // down the runway past where it lifts off, a little outside the turn: it charges the camera, lifts off and banks away past it
            Vector3 runCam = ClearOfLights(OnFloor(L + H * (strip.rollDistance + 7f) - Rh * (strip.turnRight ? 2.5f : -2.5f), 1.5f));
            AudioManager.Play(Sfx.CrocRoar, Snout, 0.9f, 1f);
            engineRev = 1f;
            float rt = 0f;
            yield return Hold(2.2f, (k, dt) =>
            {
                rt += dt;
                propTarget = 2400f;
                engineRev = Mathf.Max(engineRev, 0.7f);
                Pose(3.5f + Mathf.Sin(rt * 33f) * 0.7f, Mathf.Sin(rt * 27f) * 1.6f, -0.12f + Mathf.Sin(rt * 41f) * 0.03f, k < 0.6f ? 48f : 22f, 14f);
                if ((washT -= dt) <= 0f) { washT = 0.05f; PropWash(1.5f); }
                camShake = Mathf.Max(camShake, 0.04f);
                float e = Ease.InOutSine(k);
                Cam(c, runCam - H * 1.5f * e, L + up * 2f + H * 2f, Mathf.Lerp(30f, 26f, e));
            });

            // ---- 6. brakes off: the takeoff run, rotation, and it lifts off past the runway-side camera
            currentMove = "IntroTakeoff";
            AudioManager.Play(Sfx.Skid, transform.position, 0.8f, 0.7f);
            AudioManager.Play(Sfx.WhooshHeavy, PropPos, 0.6f, 0.5f);
            Pose(0f, 0f, 0f, 10f, 6f);
            float D = Mathf.Max(4f, strip.rollDistance), vLo = Mathf.Max(6f, strip.liftOffSpeed);
            float acc = vLo * vLo / (2f * D);
            float along = 0f, speed = 0f, air = 0f, height = 0f, vy = 0f, liftY = L.y;
            bool lifted = false, passed = false, rotated = false;
            wakeT = 0f;
            while (!lifted)
            {
                float dt = Time.deltaTime;
                speed = Mathf.Min(speed + acc * dt, vLo * 1.2f);
                along += speed * dt;
                Vector3 p = L + H * along;
                float g = Mathf.Max(Blast.Floor(p).y, WaterY - 0.3f);
                if (!rotated && along > D - speed * 0.35f)
                {
                    rotated = true;                                         // rotation: the nose comes up just before the wheels leave
                    pitchOffset = -9f;
                }
                if (!lifted && along >= D)
                {
                    lifted = true;
                    liftY = g;
                    Roar(1f);
                    FXManager.Spawn(FXType.Dust, transform.position, Quaternion.identity, 1.6f);
                }
                if (lifted)
                {
                    air += dt;
                    vy = Mathf.MoveTowards(vy, 6.5f, 9f * dt);
                    height += vy * dt;
                    pitchOffset = Mathf.MoveTowards(pitchOffset, -13f, 10f * dt);
                    p.y = Mathf.Max(g, liftY + height);
                }
                else p.y = g;
                vel = (p - transform.position) / Mathf.Max(dt, 1e-4f);
                transform.position = p;
                yawRate = 0f;
                poseSharp = 5f;
                ApplyAttitude(dt, false);
                if (!lifted && (wakeT -= dt) <= 0f) { wakeT = 0.07f; TaxiWake(1.3f); PropWash(1f); }
                engineRev = 1f;
                if (!passed && Vector3.Dot(Flat(transform.position - runCam), H) > -5f)
                {
                    passed = true;
                    AudioManager.PlayClip2D(introPassClip, 0.9f);
                    Kick(0.16f);
                }
                Vector3 me = transform.position + up * 2f;
                float fov = Mathf.Clamp(2f * Mathf.Atan(7.5f / Mathf.Max(0.5f, Vector3.Distance(runCam, me))) * Mathf.Rad2Deg, 26f, 72f);
                Cam(c, runCam, me, fov);
                yield return null;
            }

            // ---- 7-8. a scripted climbing spiral inside the clearing (clear of the tree line): the first turn's centre drifts
            //      onto the arena's over a lap while it climbs; then it dives round the far side, crosses the moon, and
            //      roars low over your head straight into the fight's orbit
            currentMove = "IntroClimb";
            orbitDir = strip.turnRight ? -1 : 1;
            float sign = orbitDir;                                          // + counter-clockwise (polar angle grows)
            Vector3 side = Rh * -sign;                                      // the turn's centre is on this side of the heading
            float r0 = Mathf.Max(5f, strip.firstTurnRadius), r1 = orbitRadius;
            Vector3 s0 = Flat(transform.position);
            Vector3 c0 = s0 + side * r0, c1 = Flat(centre);
            Vector3 rad0 = s0 - c0;
            float phi0 = Mathf.Atan2(rad0.z, rad0.x);
            Vector3 Spiral(float th)
            {
                float u = Mathf.Clamp01(th / (Mathf.PI * 2f));
                float e = u * u * (3f - 2f * u);
                float phi = phi0 + sign * th;
                return Vector3.Lerp(c0, c1, e) + new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi)) * Mathf.Lerp(r0, r1, e);
            }
            // where it passes over you: the point on the second lap nearest you
            float thPass = Mathf.PI * 2.5f, best = float.MaxValue;
            for (float th = Mathf.PI * 1.5f; th < Mathf.PI * 4f; th += 0.02f)
            {
                float d = Flat(Spiral(th) - P).sqrMagnitude;
                if (d < best) { best = d; thPass = th; }
            }
            float thDive = thPass - 3.4f;                                   // ~200 degrees before the pass it starts down
            float thEnd = thPass + 0.9f;
            const float flightSpeed = 16f;
            float theta = 0f, vyF = Mathf.Max(vel.y, 3f), yF = transform.position.y;    // off the ground and climbing hard
            Vector3 camP = transform.position - H * 16f + up * 7f, camL = transform.position + H * 6f;
            Vector3 diveCam = Vector3.zero;
            bool named = false, diving = false, over = false, chaseCut = false;
            float ct = 0f;
            while (theta < thEnd && ct < 16f)
            {
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                ct += dt;
                float rNow = Mathf.Lerp(r0, r1, Mathf.Clamp01(theta / (Mathf.PI * 2f)));
                theta += flightSpeed * dt / Mathf.Max(4f, rNow);
                // height: climb to the top, then down to pass over you, then up to the fight's cruise height
                float target = theta < thDive ? strip.climbAltitude : theta < thPass ? strip.passAltitude : cruiseAltitude;
                float vmax = theta < thDive ? 7f : 7.5f;
                vyF = Mathf.MoveTowards(vyF, Mathf.Clamp((target - yF) * 1.1f, -vmax, vmax), (theta < thDive ? 12f : 6f) * dt);
                yF += vyF * dt;
                Vector3 p = Spiral(theta);
                p.y = Mathf.Max(yF, Blast.Floor(p).y + 1f);
                Vector3 d = Flat(p - transform.position);
                vel = (p - transform.position) / dt;
                transform.position = p;
                if (d.sqrMagnitude > 1e-6f)
                {
                    float prev = yaw;
                    yaw = YawOf(d);
                    yawRate = Mathf.DeltaAngle(prev, yaw) / dt;
                }
                pitchOffset = Mathf.MoveTowards(pitchOffset, 0f, dt * 8f);   // the climb attitude now comes from the flight
                ApplyAttitude(dt, true);
                propTarget = 2400f;
                if (!named && ct > 1.4f)
                {
                    named = true;
                    c.Hud?.ShowBossIntro(displayName.ToUpperInvariant(), introEpithet, introNameColor);
                    AudioManager.PlayClip2D(introRevealClip, 0.8f);
                    Roar(0.95f);
                }
                Vector3 me = transform.position;
                if (ct < 1.1f)
                {
                    // still from the runway: it lifts off, banks away and roars past
                    if (!passed && Vector3.Dot(Flat(runCam - me), Flat(vel)) < 0f)
                    {
                        passed = true;
                        AudioManager.PlayClip2D(introPassClip, 0.9f);
                        Kick(0.16f);
                    }
                    Vector3 at = me + up * 2f;
                    Cam(c, runCam, at, Mathf.Clamp(2f * Mathf.Atan(7.5f / Mathf.Max(0.5f, Vector3.Distance(runCam, at))) * Mathf.Rad2Deg, 26f, 72f));
                    chaseCut = true;
                }
                else if (theta < thDive)
                {
                    // the high chase: above and behind it, looking down past it into the garrison
                    Vector3 hd = Heading;
                    Vector3 outward = Flat(me - Vector3.Lerp(c0, c1, Mathf.Clamp01(theta / (Mathf.PI * 2f)))).normalized;
                    // inside the turn and never out over the tree line
                    Vector3 wantP = me - hd * 19f + up * 8f - outward * 2f;
                    Vector3 fromC = Flat(wantP - c1);
                    if (fromC.magnitude > 15f) wantP += fromC.normalized * (15f - fromC.magnitude);
                    Vector3 wantL = me + hd * 6f - up * 2f;
                    if (chaseCut) { chaseCut = false; camP = wantP; camL = wantL; }     // a cut, not a swoop from the runway
                    camP = Vector3.Lerp(camP, wantP, Ease.Damp(2.4f, dt));
                    camL = Vector3.Lerp(camL, wantL, Ease.Damp(4.5f, dt));
                    Cam(c, camP, camL, 50f);
                }
                else
                {
                    // the dive, from where you stand: it comes round the far side, across the moon, and over your head
                    if (!diving)
                    {
                        diving = true;
                        currentMove = "IntroDive";
                        Vector3 Fd = Flat(Spiral(thPass - 1.2f) - P).normalized;
                        if (Fd.sqrMagnitude < 0.01f) Fd = Vector3.forward;
                        diveCam = ClearOfLights(OnFloor(P - Fd * 2.6f + Vector3.Cross(up, Fd) * 2.4f, 1.4f), 2.5f);
                    }
                    if (!over && theta >= thPass - 0.12f)
                    {
                        over = true;
                        Roar(1f);
                        AudioManager.PlayClip2D(introPassClip, 1f, 1.1f);
                        Kick(0.22f);
                    }
                    Cam(c, diveCam, me + up * 1.5f, 50f);
                }
                yield return null;
            }
            EndIntroDressing();
        }

        /// <summary>Skipped: it's already up, circling the garrison at its cruise height on the far side from you.</summary>
        public override void SkipIntroCinematic()
        {
            if (strip == null) strip = CrocodileAirstrip.Find();
            gameObject.SetActive(true);
            State = Mode.Cinematic;
            Vector3 dir = Flat(arenaCenter - PlayerPos);
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
            orbitDir = strip != null && strip.turnRight ? -1 : 1;
            Vector3 tangent = -Vector3.Cross(Vector3.up, dir) * orbitDir;
            transform.position = arenaCenter + dir * orbitRadius + Vector3.up * cruiseAltitude;
            yaw = YawOf(tangent);
            vel = tangent * cruiseSpeed;
            yawRate = 0f;
            Pose(0f, 0f, 0f, 0f, 3f);
            pitch = bank = pitchOffset = 0f;
            lift = 0f;
            jawAngle = 0f;
            propSpin = propTarget = 2400f;
            engineLevel = 1f;
            if (engineAudio && !engineAudio.isPlaying) engineAudio.Play();
            foreach (var s in engineSmoke) if (s) s.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            EndIntroDressing();
            ApplyAttitude(1f, true);
        }

        public override void BeginFightAfterIntro()
        {
            StopAllCoroutines();
            ClearProjectiles();
            EndIntroDressing();
            health = maxHealth;
            poise = maxPoise;
            phaseTwo = false;
            closePressure = spinCd = hopCd = belchCd = 0f;
            Pose(0f, 0f, 0f, 0f, 3f);
            gameObject.SetActive(true);
            SetBodyColliders(false);
            foreach (var s in engineSmoke) if (s) s.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (engineAudio && !engineAudio.isPlaying) engineAudio.Play();
            // from where the cinematic left it: in the air, at speed
            if (Flat(vel).sqrMagnitude < 1f) vel = Heading * cruiseSpeed;
            propTarget = 2400f;
            State = Mode.Flying;
            brain = StartCoroutine(Brain(false));
        }

        /// <summary>Hand the garrison's searchlights back, drop the cinematic's light and shake, and wake the engine drone.</summary>
        void EndIntroDressing()
        {
            FillLight(false);
            if (Searchlight.Spot == transform) Searchlight.Spot = null;
            camShake = 0f;
            engineLevel = 1f;
        }

        // ================================================================== helpers
        static float YawOf(Vector3 d) => Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;

        Vector3 PropPos => props != null && props.Length > 0 && props[0] ? props[0].position : transform.TransformPoint(0f, 2.3f, -4.9f);
        Vector3 HeadPos => pivot ? pivot.TransformPoint(0f, 0.3f, 4.3f) : transform.TransformPoint(0f, 2.1f, 4.3f);
        Vector3 Snout => pivot ? pivot.TransformPoint(0f, 0.45f, 5.8f) : transform.TransformPoint(0f, 2.2f, 5.8f);

        static Vector3 Herm(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float s)
        {
            float s2 = s * s, s3 = s2 * s;
            return (2f * s3 - 3f * s2 + 1f) * p0 + (s3 - 2f * s2 + s) * m0 + (-2f * s3 + 3f * s2) * p1 + (s3 - s2) * m1;
        }

        static Vector3 HermD(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float s)
        {
            float s2 = s * s;
            return (6f * s2 - 6f * s) * p0 + (3f * s2 - 4f * s + 1f) * m0 + (-6f * s2 + 6f * s) * p1 + (3f * s2 - 2f * s) * m1;
        }

        void Snore()
        {
            AudioManager.PlayClip2D(introSnoreClip, introSnoreVolume, introSnorePitch * Random.Range(0.95f, 1.05f));
            FXManager.Spawn(FXType.DustSmall, Snout, Quaternion.LookRotation(transform.forward + Vector3.down * 0.4f), 0.35f);
        }

        void Snort()
        {
            AudioManager.Play(Sfx.WhooshLight, Snout, 0.7f, 0.55f);
            FXManager.Spawn(FXType.DustSmall, Snout, Quaternion.LookRotation(transform.forward + Vector3.down * 0.5f), 0.7f);
            Kick(0.03f);
        }

        /// <summary>A crank that doesn't catch: the prop lurches round, coughs smoke and runs down.</summary>
        void Crank(float spin, int puffs, float k)
        {
            propSpin = spin;
            propTarget = 0f;
            engineRev = 0f;
            foreach (var s in engineSmoke) if (s) s.Emit(puffs);
            AudioManager.Play(Sfx.Knock, PropPos, 0.6f * k, 0.6f);
            AudioManager.Play(Sfx.BombPop, PropPos, 0.7f * k, 0.5f);
            lift -= 0.05f;
            Kick(0.03f * k);
        }

        /// <summary>The engine catches: a bang and a gout of smoke, and the prop spins up to idle.</summary>
        void Catch()
        {
            propSpin = 900f;
            propTarget = 1100f;
            engineRev = 1f;
            engineLevel = 1f;
            if (engineAudio) engineAudio.volume = EngineVolume * 0.45f;
            foreach (var s in engineSmoke) if (s) { s.Emit(24); s.Play(true); }
            FXManager.Spawn(FXType.Smoke, PropPos, Quaternion.identity, 0.8f);
            AudioManager.Play(Sfx.BombPop, PropPos, 1f, 0.42f);
            AudioManager.Play(Sfx.Slam, PropPos, 0.35f, 1.4f);
            Kick(0.1f);
        }

        /// <summary>The prop's wash: dust (or spray, over the pools) blown back off the ground behind it.</summary>
        void PropWash(float k)
        {
            Vector3 back = -Flat(transform.forward).normalized;
            Vector3 at = Flat(PropPos) + back * Random.Range(1.5f, 4.5f) + transform.right * Random.Range(-1.8f, 1.8f);
            Vector3 g = Blast.Floor(at);
            if (g.y < WaterY - 0.02f) { g.y = WaterY; FXManager.Spawn(FXType.Splash, g, Quaternion.identity, 0.5f * k); }
            else FXManager.Spawn(FXType.DustSmall, g + Vector3.up * 0.1f, Quaternion.LookRotation(back + Vector3.up * 0.2f), 1.2f * k);
        }

        /// <summary>Its wheels on the move: dust, or spray where they cut through the pools.</summary>
        void TaxiWake(float k)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 w = transform.position + transform.right * 1.3f * side + transform.forward * 0.5f;
                Vector3 g = Blast.Floor(w);
                if (g.y < WaterY - 0.02f) { g.y = WaterY; FXManager.Spawn(FXType.Splash, g, Quaternion.identity, 0.45f * k); }
                else FXManager.Spawn(FXType.DustSmall, g + Vector3.up * 0.05f, Quaternion.LookRotation(-transform.forward), k);
            }
        }

        void Kick(float amount) => camShake = Mathf.Max(camShake, amount);

        /// <summary>A soft moonlit fill on the plane from the camera's side while the cinematic plays (the swamp night would
        /// leave it a silhouette away from the floodlights); off for the fight.</summary>
        void FillLight(bool on)
        {
            if (on && cineFill == null)
            {
                cineFill = new GameObject("IntroFill").AddComponent<Light>();
                cineFill.transform.SetParent(transform, false);
                cineFill.type = LightType.Point;
                cineFill.color = new Color(0.82f, 0.9f, 1f);
                cineFill.range = 16f;
                cineFill.intensity = 14f;
                cineFill.shadows = LightShadows.None;
            }
            if (cineFill) cineFill.enabled = on;
        }

        void PlaceFill(Vector3 camPos)
        {
            if (!cineFill || !cineFill.enabled) return;
            Vector3 c = transform.position + Vector3.up * 2f;
            Vector3 to = camPos - c;
            to = to.sqrMagnitude > 0.01f ? to.normalized : Vector3.up;
            cineFill.transform.position = c + to * 5f + Vector3.up * 2f;
        }

        /// <summary>A camera spot <paramref name="h"/> m above the ground (or the water) under <paramref name="p"/>.</summary>
        static Vector3 OnFloor(Vector3 p, float h)
        {
            Vector3 f = Blast.Floor(p);
            f.y = Mathf.Max(f.y, WaterY);
            return f + Vector3.up * h;
        }

        /// <summary>Nudge a camera spot away from the garrison's torches and lanterns: up close, their halo in the fog washes the
        /// whole frame out.</summary>
        static Vector3 ClearOfLights(Vector3 p, float min = 4f)
        {
            var lights = FindObjectsByType<FogLight>(FindObjectsSortMode.None);
            for (int pass = 0; pass < 3; pass++)
                foreach (var f in lights)
                {
                    var l = f.GetComponent<Light>();
                    if (l == null || l.type != LightType.Point || !f.isActiveAndEnabled) continue;
                    Vector3 d = p - f.transform.position;
                    d.y = 0f;
                    float m = d.magnitude;
                    if (m >= min) continue;
                    p += (m > 0.01f ? d / m : Vector3.right) * (min - m);
                }
            return p;
        }

        /// <summary>Move the camera, with the cinematic's own shake on top (Cinemachine is off).</summary>
        void Cam(BossCinematic c, Vector3 pos, Vector3 lookAt, float fov)
        {
            float dt = Time.deltaTime;
            camShake = Mathf.MoveTowards(camShake, 0f, dt * 0.8f);
            float s = camShake * Settings.ShakeScale;
            float roll = 0f;
            if (s > 0f)
            {
                float t = Time.time * 26f;
                var n = new Vector3(Mathf.PerlinNoise(t, 0.31f) - 0.5f, Mathf.PerlinNoise(0.77f, t) - 0.5f, Mathf.PerlinNoise(t, t * 0.5f) - 0.5f) * (2f * s);
                pos += n;
                lookAt += n * 0.6f;
                roll = (Mathf.PerlinNoise(t * 0.7f, 3.1f) - 0.5f) * 8f * s;
            }
            c.Shot(pos, lookAt, fov, roll);
            PlaceFill(pos);
        }
    }
}
