using System.Collections;
using ElderTree.Audio;
using ElderTree.FX;
using ElderTree.World;
using UnityEngine;

namespace ElderTree.Enemies
{
    /// <summary>
    /// The Shark's first-meeting cinematic (~21 s) on the Bleached Atoll, along its <see cref="SharkSeaLane"/>:
    ///  1. From the dock over your shoulder: the sea is calm, a low drone, and far out a fin breaks the surface. The camera pushes in.
    ///  2. Low beside it: the fin cuts along the coast, spray off its leading edge, its wake spreading into a V behind it.
    ///  3. The fin slips under. It breaches, twisting, the atoll behind it, and crashes back in.
    ///  4. The fin comes round toward a camera on the water; it slips under again and bursts out in a big spinning leap that
    ///     flies right past the lens (a beat of slow motion at the top) and smashes down beside it.
    ///  5. Over your shoulder: the fin charges the dock, it bursts out of the water, over the rail, and crashes onto the planks.
    ///  6. It skids, whips round to face you, rears up and roars. Its name comes up. The fight starts from there.
    /// </summary>
    public partial class SharkBoss
    {
        [Header("Intro cinematic (first meeting)")]
        public string introEpithet = "Tide Reaver of the Bleached Atoll";
        public Color introNameColor = new Color(0.66f, 0.86f, 0.9f);
        [Tooltip("The cinematic's swell, from the second breach (it peaks about 7 s in, on the landing).")]
        public AudioClip introMusic;
        [Range(0f, 1f)] public float introMusicVolume = 0.6f;
        [Tooltip("A low drone under the approach.")]
        public AudioClip introDroneClip;
        [Range(0f, 1f)] public float introDroneVolume = 0.7f;
        [Tooltip("A deep sting under the name card.")]
        public AudioClip introRevealClip;
        [Tooltip("A reverse whoosh that builds into the leap onto the dock.")]
        public AudioClip introBuildClip;
        [Tooltip("A deep whoosh as it flies past the camera.")]
        public AudioClip introPassClip;
        [Tooltip("The foam its fin and its splashes leave on the sea (SeaFoam).")]
        public Material introFoamMaterial;

        public override bool HasIntroCinematic => SharkSeaLane.Find() != null && OceanSurface.Exists;

        /// <summary>Top of the dorsal fin over the body's centre (the pivot), in metres.</summary>
        const float FinTop = 1.88f;
        const float NoseZ = 3.75f;

        SharkSeaLane lane;
        SeaFoam foam;
        int wake = -1;
        float camShake;
        /// <summary>Seconds left in the current swim (an estimate at its current speed).</summary>
        float swimLeft;
        float swimSpeed;
        Vector3 camP, camL;
        bool camCut;

        // ================================================================== the run in
        public override IEnumerator IntroCinematic(BossCinematic c)
        {
            StopAllCoroutines();
            lane = SharkSeaLane.Find();
            if (lane == null || !lane.rise || !lane.breach1 || !lane.breach2 || !lane.launch || !lane.land) yield break;
            health = maxHealth;
            poise = maxPoise;
            phaseTwo = false;
            forcedMove = null;
            gameObject.SetActive(true);
            State = Mode.Cinematic;
            currentMove = "IntroRise";
            SetBodyColliders(false);
            if (digAudio) digAudio.Stop();
            groundY = ArenaFloorY;
            ResetPose();
            keepOffGround = false;
            groundWeight = 0f;
            camShake = 0f;
            foam = SeaFoam.Ensure(introFoamMaterial);
            foam?.Clear();
            wake = -1;

            Vector3 up = Vector3.up;
            Vector3 P = c.Player != null ? c.Player.transform.position : arenaCenter;
            AudioManager.SetMusic(MusicMood.None);
            AudioManager.PlayClip2D(introDroneClip, introDroneVolume);

            Vector3 rise = lane.rise.position, rDir = Flat(lane.rise.forward).normalized;
            Vector3 b1 = lane.breach1.position, b1Dir = Flat(lane.breach1.forward).normalized, b1Land = lane.Breach1Lands;
            Vector3 b2 = lane.breach2.position, b2Dir = Flat(lane.breach2.forward).normalized, b2Land = lane.Breach2Lands;
            Vector3 L0 = lane.launch.position, land = lane.land.position;
            Vector3 lDir = Flat(land - L0).normalized;

            // ---- 1-2. the fin rises far out and cuts along the coast; from the dock, then low beside it
            Vector3 F1 = Flat(rise - P).normalized, R1 = Vector3.Cross(up, F1);
            Vector3 shoulder = P - F1 * 3f - R1 * 1.4f + up * 3f;          // over your left shoulder, above the rail and pilings
            float t1 = 0f;
            bool rose = false, tracking = false;
            yield return Swim(rise, rDir, b1, b1Dir, lane.cruiseSpeed * 0.7f, lane.cruiseSpeed * 1.4f,
                k => k < 0.1f ? Mathf.Lerp(2.6f, 0f, Ease.OutCubic(k / 0.1f)) : k > 0.86f ? Mathf.Lerp(0f, 1.3f, Ease.InCubic((k - 0.86f) / 0.14f)) : 0f,
                (k, dt) =>
                {
                    t1 += dt;
                    if (!rose && k > 0.05f)
                    {
                        rose = true;
                        StartWake();
                        Vector3 f = FinPos;
                        foam?.AddRing(f, 0.4f, 2.2f, 0.5f, 2.4f);
                        FXManager.Spawn(FXType.Splash, OnSea(f, 0f), Quaternion.identity, 0.5f);
                        AudioManager.Play(Sfx.WaterSplash, f, 0.5f, 0.75f);
                    }
                    if (k > 0.86f) { currentMove = "IntroDive"; StopWake(); }
                    Vector3 fin = FinPos;
                    if (t1 < 3.0f)
                    {
                        // from the dock: wide on the sea, pushing in on the fin as it rises
                        currentMove = "IntroRise";
                        float e = Ease.InOutSine(Mathf.Clamp01(t1 / 2.7f));
                        Vector3 look = Vector3.Lerp(OnSea(rise, 3f), OnSea(fin, 0.5f), Ease.InOutSine(Mathf.Clamp01((t1 - 0.4f) / 2f)));
                        Cam(c, shoulder, look, Mathf.Lerp(38f, 11f, e));
                    }
                    else
                    {
                        // low beside it, on the sun's side, so the fin is lit and the atoll stands behind it
                        if (!tracking) { tracking = true; camCut = true; currentMove = "IntroFin"; }
                        Vector3 h = Heading, side = SunSide(h);
                        Vector3 want = fin + side * 6.5f - h * 3f;
                        want.y = Sea(want) + 1.0f;
                        Track(c, want, fin + h * 2.5f + up * 0.3f, 40f, 3f, 5f);
                    }
                    c.PlayerWatch(transform.position);
                });

            // ---- 3. the first breach, side-on from the sea with the atoll behind
            currentMove = "IntroBreach1";
            Vector3 mid1 = (b1 + b1Land) * 0.5f;
            Vector3 cam3 = mid1 + SunSide(b1Dir) * 15f - b1Dir * 1.5f;
            cam3.y = Sea(cam3) + 1.1f;
            yield return Leap(b1, b1Land, lane.breach1Apex, 1.35f, 0.5f, false, 1.5f, (s, dt) =>
                Cam(c, cam3, Vector3.Lerp(mid1 + up * 1.5f, transform.position + up * 1.2f, 0.5f), 44f));

            // ---- 4. round toward a camera on the water, under, and the big spinning leap right past it
            currentMove = "IntroFin";
            Vector3 b2Left = -Vector3.Cross(up, b2Dir);
            Vector3 cam4 = b2Land + b2Left * 5.5f + b2Dir * 2f;
            cam4.y = Sea(cam4) + 1.3f;
            float t2 = 0f;
            bool resurfaced = false;
            yield return Swim(b1Land, b1Dir, b2, b2Dir, lane.cruiseSpeed * 1.2f, lane.cruiseSpeed * 1.5f,
                k => k < 0.2f ? Mathf.Lerp(1.4f, 0f, Ease.OutCubic(k / 0.2f)) : k > 0.84f ? Mathf.Lerp(0f, 1.4f, Ease.InCubic((k - 0.84f) / 0.16f)) : 0f,
                (k, dt) =>
                {
                    t2 += dt;
                    if (!resurfaced && k > 0.12f) { resurfaced = true; StartWake(); }
                    if (k > 0.84f) { currentMove = "IntroDive"; StopWake(); }
                    if (t2 < 0.75f) Cam(c, cam3, b1Land + up * 0.8f, 44f);          // the splash settles, the fin comes back up
                    else
                    {
                        if (t2 - dt < 0.75f) camCut = true;           // the cut to the camera on the water
                        Track(c, cam4, FinPos + up * 0.4f, 46f, 99f, 6f);
                    }
                    c.PlayerWatch(transform.position);
                });
            currentMove = "IntroBreach2";
            AudioManager.PlayCinematicMusic(introMusic, introMusicVolume);
            bool passed = false, slowed = false;
            yield return Leap(b2, b2Land, lane.breach2Apex, 1.7f, lane.breach2Spins, true, 2f, (s, dt) =>
            {
                if (!slowed && s > 0.4f) { slowed = true; TimeDirector.SlowMo(0.55f, 0.35f); }
                Vector3 me = transform.position + Vector3.up * 1.25f;
                if (!passed && Vector3.Dot(Flat(cam4 - me), Flat(b2Dir)) < 2f)
                {
                    passed = true;
                    AudioManager.PlayClip2D(introPassClip, 0.9f);
                    Kick(0.12f);
                }
                Track(c, cam4, me, 54f, 99f, 7f);
            });

            // ---- 5. over your shoulder: it charges the dock, bursts out of the water, over the rail and onto the planks
            currentMove = "IntroCharge";
            Vector3 F5 = Flat(land - P).normalized, R5 = Vector3.Cross(up, F5);
            Vector3 cam5 = P - F5 * 1.5f - R5 * 3f + up * 2.2f;           // behind you and off to the side, so you don't hide the landing
            float t3 = 0f;
            bool built = false;
            yield return Swim(b2Land, b2Dir, L0, lDir, lane.cruiseSpeed * 1.2f, lane.chargeSpeed,
                k => k < 0.25f ? Mathf.Lerp(1.4f, -0.15f, Ease.OutCubic(k / 0.25f)) : k > 0.9f ? Mathf.Lerp(-0.15f, 0.9f, (k - 0.9f) / 0.1f) : -0.15f,
                (k, dt) =>
                {
                    t3 += dt;
                    if (wake < 0 && k > 0.15f) StartWake(2.6f);
                    if (k > 0.9f) StopWake();
                    if (!built && swimLeft < 0.55f) { built = true; AudioManager.PlayClip2D(introBuildClip, 0.9f); }
                    if (t3 < 0.55f) Track(c, cam4, b2Land + Vector3.up * 0.8f, 54f, 99f, 6f);
                    else
                    {
                        if (t3 - dt < 0.55f) camCut = true;
                        Track(c, cam5, FinPos + Vector3.up * 0.6f, 50f, 99f, 4f);
                    }
                    c.PlayerWatch(transform.position);
                });
            currentMove = "IntroLaunch";
            yield return Leap(L0, land, lane.launchApex, 1.25f, 0f, true, 1.6f, (s, dt) =>
            {
                Track(c, cam5, transform.position + Vector3.up * 1.2f, 50f, 99f, 6f);
                c.PlayerWatch(transform.position);
            }, toDeck: true);

            // ---- 6. it skids, whips round to face you, rears up and roars
            currentMove = "IntroSkid";
            Vector3 skidFrom = transform.position, h0 = Heading;
            float yaw0 = yaw;
            float chipT = 0f;
            yield return Act(0.55f, (k, dt) =>
            {
                float e = Ease.OutCubic(k);
                Place(skidFrom + h0 * 2.4f * e);
                Pose(5f * (1f - k), 0f, -0.28f, 30f, 10f);
                thrash = 0.8f;
                swim = 1.8f;
                if ((chipT -= dt) <= 0f)
                {
                    chipT = 0.09f;
                    FXManager.Spawn(FXType.WoodChips, transform.position + Heading * 1.5f, Quaternion.LookRotation(Heading + Vector3.up), 0.6f);
                    FXManager.Spawn(FXType.Splash, transform.position, Quaternion.identity, 0.25f);
                }
                Track(c, cam5, transform.position + Vector3.up * 1.4f, 50f, 99f, 6f);
            });
            AudioManager.Play(Sfx.Skid, transform.position, 0.8f, 0.8f);
            currentMove = "IntroTurn";
            Vector3 toYou = Flat(PlayerPos - transform.position);
            float yaw1 = Mathf.Atan2(toYou.x, toYou.z) * Mathf.Rad2Deg;
            float turn = Mathf.DeltaAngle(yaw0, yaw1);
            thrash = 0.3f;
            Vector3 S = transform.position;
            Vector3 cam6 = S + toYou.normalized * 9.6f + Vector3.Cross(up, toYou.normalized) * -2.8f + up * 1.1f;
            camCut = true;
            AudioManager.Play(Sfx.WhooshHeavy, TailTip, 0.7f, 0.7f);
            yield return Act(0.75f, (k, dt) =>
            {
                float e = Ease.InOutCubic(k);
                SetYaw(yaw0 + turn * e, dt);
                Place(S);
                Pose(0f, -Mathf.Sign(turn) * 12f * Mathf.Sin(k * Mathf.PI), -0.1f, 14f, 9f);
                tTailYaw = -Mathf.Sign(turn) * 40f * Mathf.Sin(k * Mathf.PI);
                Track(c, cam6, S + Vector3.up * 2.2f + toYou.normalized * 1.5f, 50f, 99f, 6f);
            });
            tTailYaw = 0f;
            currentMove = "IntroRoar";
            Roar(0.95f, 1f);
            Kick(0.2f);
            thrash = 0.5f;
            bool named = false;
            yield return Act(2.6f, (k, dt) =>
            {
                float upK = k < 0.8f ? Ease.OutCubic(Mathf.Clamp01(k / 0.2f)) : 1f - Ease.InOutCubic((k - 0.8f) / 0.2f);
                Pose(-34f * upK, Mathf.Sin(k * 22f) * 3f * upK, 0.5f * upK, 62f * upK, 7f);
                tTailPitch = -10f * upK;
                swim = 2.2f;
                Place(S);
                if (!named && k > 0.12f)
                {
                    named = true;
                    c.Hud?.ShowBossIntro(displayName.ToUpperInvariant(), introEpithet, introNameColor);
                    AudioManager.PlayClip2D(introRevealClip, 0.85f);
                }
                camShake = Mathf.Max(camShake, 0.05f * upK);
                Cam(c, cam6 - toYou.normalized * 0.8f * Ease.InOutSine(k), S + Vector3.up * (2.2f + 1.4f * upK) + toYou.normalized * 1.2f, 52f);
            });
            thrash = 0f;
            swim = 1f;
            tTailPitch = 0f;
            EndIntroDressing();
        }

        /// <summary>Skipped: it's on the dock where it would have landed, facing you.</summary>
        public override void SkipIntroCinematic()
        {
            if (lane == null) lane = SharkSeaLane.Find();
            gameObject.SetActive(true);
            State = Mode.Cinematic;
            groundY = ArenaFloorY;
            ResetPose();
            Vector3 at = lane != null && lane.land ? lane.land.position + Flat(lane.land.forward).normalized * 2.4f : arenaCenter;
            Vector3 to = Flat(PlayerPos - at);
            yaw = to.sqrMagnitude > 1e-4f ? Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg : 0f;
            yawRate = 0f;
            Place(at);
            EndIntroDressing();
            foam?.Clear();
        }

        public override void BeginFightAfterIntro()
        {
            StopAllCoroutines();
            EndIntroDressing();
            health = maxHealth;
            poise = maxPoise;
            phaseTwo = false;
            closePressure = spinCd = leapCd = tailCd = 0f;
            sprintCd = 2f;
            burrowCd = 9f;
            forcedMove = null;
            gameObject.SetActive(true);
            groundY = ArenaFloorY;
            // from where the cinematic left it: on the planks, facing you
            bank = Mathf.Repeat(bank + 180f, 360f) - 180f;
            tBank = 0f;
            airY = 0f;
            tSink = sink = 0f;
            keepOffGround = true;
            vel = Vector3.zero;
            swim = 1f;
            thrash = 0f;
            tTailYaw = tTailPitch = 0f;
            Pose(0f, 0f, 0f, 0f, 6f);
            Place(transform.position);
            State = Mode.Stalking;
            brain = StartCoroutine(Brain(false));
        }

        void EndIntroDressing()
        {
            StopWake();
            camShake = 0f;
        }

        // ================================================================== swimming and leaping
        /// <summary>
        /// Swim from <paramref name="a"/> to <paramref name="b"/> (leaving along <paramref name="d0"/>, arriving along
        /// <paramref name="d1"/>), speeding from <paramref name="v0"/> to <paramref name="v1"/>. <paramref name="depth"/> is how
        /// far under its cruising depth it is at each point of the way (0: the fin shows; negative: it rides higher).
        /// </summary>
        IEnumerator Swim(Vector3 a, Vector3 d0, Vector3 b, Vector3 d1, float v0, float v1, System.Func<float, float> depth,
            System.Action<float, float> each)
        {
            // arc length table, so the speed is the speed through the water
            const int N = 48;
            var len = new float[N + 1];
            Vector3 prev = SharkSeaLane.Swim(a, d0, b, d1, 0f, out _);
            for (int i = 1; i <= N; i++)
            {
                Vector3 p = SharkSeaLane.Swim(a, d0, b, d1, i / (float)N, out _);
                len[i] = len[i - 1] + Vector3.Distance(Flat(prev), Flat(p));
                prev = p;
            }
            float total = len[N], went = 0f, sprayT = 0f;
            int seg = 0;
            while (went < total)
            {
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                float k = went / total;
                swimSpeed = Mathf.Lerp(v0, v1, Ease.InOutSine(k));
                went = Mathf.Min(total, went + swimSpeed * dt);
                swimLeft = (total - went) / Mathf.Max(1f, swimSpeed);
                while (seg < N - 1 && len[seg + 1] < went) seg++;
                float u = (seg + Mathf.InverseLerp(len[seg], len[seg + 1], went)) / N;
                Vector3 p = SharkSeaLane.Swim(a, d0, b, d1, u, out var dir);
                float dpt = depth(went / total);
                p.y = Sea(p) - pivotRestLocal.y - (FinTop - lane.finShow) - dpt;
                if (dir.sqrMagnitude > 1e-6f) SetYaw(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, dt);
                vel = (p - transform.position) / dt;
                transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
                // nose down as it slips under, up as it comes back
                float dd = depth(Mathf.Min(1f, (went + swimSpeed * 0.2f) / total)) - dpt;
                Pose(Mathf.Clamp(dd * 30f, -18f, 22f), Mathf.Clamp(-yawRate * 0.12f, -14f, 14f), 0f, 5f, 6f);
                swim = 1.2f + swimSpeed / 8f;
                if (dpt < 0.5f && wake >= 0) foam?.Emit(wake, transform.position + Heading * 1.6f, Heading);
                if (dpt < 0.35f && (sprayT -= dt) <= 0f)
                {
                    // spray off the fin's leading edge, thrown back and out
                    sprayT = swimSpeed > 11f ? 0.05f : 0.09f;
                    Vector3 f = FinPos;
                    var r = Quaternion.FromToRotation(Vector3.up, (Vector3.up - Heading * 0.7f + Random.insideUnitSphere * 0.3f).normalized);
                    FXManager.Spawn(FXType.Splash, OnSea(f + Heading * 0.5f, 0f), r, Mathf.Lerp(0.16f, 0.32f, swimSpeed / 14f));
                }
                each?.Invoke(went / total, dt);
                yield return null;
            }
        }

        /// <summary>
        /// Leap out of the water at <paramref name="a"/> and come down at <paramref name="b"/> (into the sea, or onto the dock),
        /// <paramref name="apex"/> m over the water at the top, turning <paramref name="spins"/> times round its long axis.
        /// </summary>
        IEnumerator Leap(Vector3 a, Vector3 b, float apex, float time, float spins, bool jaws, float splash,
            System.Action<float, float> each, bool toDeck = false)
        {
            StopWake();
            float sea0 = Sea(a);
            float y0 = sea0 - pivotRestLocal.y - 2.0f;                       // from deep, so it's moving fast as it breaks out
            float y1 = toDeck ? groundY : Sea(b) - pivotRestLocal.y - 3.2f;
            float top = (toDeck ? groundY : sea0) + apex - pivotRestLocal.y;     // apex: its body's centre over the water (or deck)
            // a parabola through y0, top and y1 (the top where it falls: later when it lands lower than it left)
            float h = top - (y0 + y1) * 0.5f;
            float roll0 = bank, rollDir = Random.value < 0.5f ? 1f : -1f;
            bool outOfWater = false, noseIn = false, tailIn = false, deck = false;
            float t = 0f, dripT = 0f;
            Vector3 a0 = Flat(a) + Vector3.up * y0, b0 = Flat(b) + Vector3.up * y1;
            Vector3 dir = Flat(b - a).normalized;
            float hv = Vector3.Distance(Flat(a), Flat(b)) / time;
            yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            yawRate = 0f;
            swim = 2.4f;
            while (t < time)
            {
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                t = Mathf.Min(time, t + dt);
                float s = t / time;
                Vector3 p = Vector3.Lerp(a0, b0, s) + Vector3.up * (4f * h * s * (1f - s));
                float vy = (y1 - y0) / time + 4f * h * (1f - 2f * s) / time;
                vel = dir * hv + Vector3.up * vy;
                transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
                float pitchNow = -Mathf.Atan2(vy, hv) * Mathf.Rad2Deg * 0.9f;
                // the roll: half a twist for a plain breach, whole turns for the spinner
                float roll = spins > 0.01f ? rollDir * 360f * spins * Ease.InOutSine(s) : rollDir * 28f * Mathf.Sin(s * Mathf.PI);
                Pose(pitchNow, roll0 + roll, 0f, jaws ? (s > 0.2f && s < 0.6f ? 58f : 8f) : 10f, 22f);
                bank = Mathf.Lerp(bank, tBank, 0.6f);
                thrash = 0.35f;
                if (toDeck && s > 0.72f && !keepOffGround) { keepOffGround = true; groundWeight = 0.5f; }

                float nose = pivot.TransformPoint(0f, 0f, NoseZ).y, tail = TailTip.y, body = pivot.position.y;
                if (!outOfWater && nose > sea0)
                {
                    outOfWater = true;
                    Vector3 at = OnSea(pivot.TransformPoint(0f, 0f, NoseZ), 0f);
                    SplashBurst(at, 1.1f, splash * 0.75f, 3);
                    foam?.AddRing(at, 1.0f, 3.2f, 0.8f, 2.6f);
                    AudioManager.Play(Sfx.WaterSplash, at, 1f, 0.75f);
                    if (jaws) AudioManager.Play(Sfx.WhooshHeavy, at, 0.8f, 0.6f);
                }
                if (outOfWater && s < 0.5f && (dripT -= dt) <= 0f && tail < sea0 + 2f)
                {
                    dripT = 0.06f;                                          // water streaming off the tail as it clears
                    FXManager.Spawn(FXType.Splash, OnSea(TailTip, 0f), Quaternion.identity, 0.35f * splash);
                }
                if (!toDeck && s > 0.5f && !noseIn && nose < Sea(b))
                {
                    noseIn = true;
                    Vector3 at = OnSea(pivot.TransformPoint(0f, 0f, NoseZ * 0.5f), 0f);
                    SplashBurst(at, 1.5f, splash, 4);
                    foam?.AddRing(at, 1.4f, 4.2f, 1.1f, 3.2f);
                    AudioManager.Play(Sfx.WaterSplash, at, 1f, 0.6f);
                    AudioManager.Play(Sfx.Slam, at, 0.45f, 1.35f);
                    Kick(0.05f * splash);
                }
                if (!toDeck && noseIn && !tailIn && (tail < Sea(b) + 0.3f || body < Sea(b) - 1.5f))
                {
                    tailIn = true;                                          // the tail slaps down last
                    Vector3 at = OnSea(TailTip, 0f);
                    FXManager.Spawn(FXType.Splash, at, Quaternion.identity, 0.9f * splash);
                    foam?.AddRing(at, 0.6f, 2.6f, 0.6f, 2.4f);
                    AudioManager.Play(Sfx.WaterSplash, at, 0.6f, 0.9f);
                }
                each?.Invoke(s, dt);
                yield return null;
            }
            bank = tBank = Mathf.Repeat(roll0 + 180f, 360f) - 180f;
            if (toDeck && !deck)
            {
                // crashes onto the planks: splinters, the sea pouring off it, a heavy thud
                Vector3 at = transform.position;
                airY = 0f;
                keepOffGround = true;
                groundWeight = 1f;
                FXManager.Spawn(FXType.WoodChips, at + Heading, Quaternion.LookRotation(Vector3.up), 1.8f);
                FXManager.Spawn(FXType.WoodChips, at - Heading * 1.5f, Quaternion.LookRotation(Vector3.up), 1.3f);
                SplashBurst(at, 1.6f, 1.1f, 4);
                FXManager.Spawn(FXType.SlamRing, at + Vector3.up * 0.05f, Quaternion.identity, 2.8f);
                AudioManager.Play(Sfx.Slam, at, 1f, 0.7f);
                AudioManager.Play(Sfx.WaterSplash, at, 0.9f, 0.7f);
                AudioManager.Play(Sfx.JawSnap, at + Heading * 3f, 0.8f, 0.85f);
                Kick(0.4f);
                Pose(6f, 0f, -0.3f, 30f, 12f);
                Place(at);
            }
        }

        /// <summary>Run <paramref name="each"/> every frame for <paramref name="time"/> s (no movement of its own).</summary>
        IEnumerator Act(float time, System.Action<float, float> each)
        {
            float t = 0f;
            while (t < time)
            {
                float dt = Time.deltaTime;
                t += dt;
                each(Mathf.Clamp01(t / time), dt);
                yield return null;
            }
        }

        void StartWake(float width = 2f)
        {
            if (foam == null || wake >= 0) return;
            wake = foam.BeginTrail(width, 3f, 4.5f);
        }

        void StopWake()
        {
            if (foam != null && wake >= 0) foam.EndTrail(wake);
            wake = -1;
        }

        /// <summary>A burst of spray in a ring round <paramref name="at"/>.</summary>
        void SplashBurst(Vector3 at, float radius, float scale, int n)
        {
            FXManager.Spawn(FXType.Splash, at, Quaternion.identity, scale);
            float a0 = Random.value * 360f;
            for (int i = 0; i < n; i++)
            {
                var d = Quaternion.Euler(0f, a0 + i * 360f / n, 0f) * Vector3.forward;
                var r = Quaternion.FromToRotation(Vector3.up, (Vector3.up + d * 0.45f).normalized);
                FXManager.Spawn(FXType.Splash, at + d * radius, r, scale * 0.7f);
            }
        }

        // ================================================================== camera
        Vector3 FinPos => pivot ? pivot.TransformPoint(0f, FinTop - 0.4f, -0.85f) : transform.position;

        static float Sea(Vector3 p) => OceanSurface.Height(p.x, p.z);
        static Vector3 OnSea(Vector3 p, float h) { p.y = Sea(p) + h; return p; }

        /// <summary>The side of a heading that faces the sun (in the south): a camera there looks north with the sun behind it.</summary>
        static Vector3 SunSide(Vector3 h)
        {
            Vector3 r = Vector3.Cross(Vector3.up, Flat(h).normalized);
            var sun = RenderSettings.sun;
            Vector3 toSun = sun != null ? -Flat(sun.transform.forward) : Vector3.back;
            return Vector3.Dot(r, toSun) >= 0f ? r : -r;
        }

        void Kick(float amount) => camShake = Mathf.Max(camShake, amount);

        /// <summary>A camera that eases toward where it wants to be (a cut on the first frame of a shot).</summary>
        void Track(BossCinematic c, Vector3 pos, Vector3 look, float fov, float posRate, float lookRate)
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            if (camCut) { camCut = false; camP = pos; camL = look; }
            camP = Vector3.Lerp(camP, pos, Ease.Damp(posRate, dt));
            camL = Vector3.Lerp(camL, look, Ease.Damp(lookRate, dt));
            Cam(c, camP, camL, fov);
        }

        /// <summary>Move the camera, with the cinematic's own shake on top (Cinemachine is off).</summary>
        void Cam(BossCinematic c, Vector3 pos, Vector3 lookAt, float fov)
        {
            float dt = Time.deltaTime;
            pos.y = Mathf.Max(pos.y, Sea(pos) + 0.45f);                    // never under the swell
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
        }
    }
}
