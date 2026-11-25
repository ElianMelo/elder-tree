using System.Collections;
using ElderTree.Audio;
using ElderTree.FX;
using UnityEngine;

namespace ElderTree.Enemies
{
    /// <summary>
    /// The Druid's first-meeting cinematic (~22 s). It has been hiding in the forest past the standing stones, beside the
    /// great kapok (<see cref="DruidLair"/>):
    ///  1. Over your shoulder, the trees there shiver and leaves rain down; something heavy shifts. Two eyes open in the shade.
    ///  2. It steps out and lays its hands on two trees, then shoves them aside (they lean away and spring back).
    ///  3. It steps through, shedding leaves, to a third tree in its way. It wrenches it off its stump, tears it in two
    ///     and flings the halves into the undergrowth either side.
    ///  4. It strides into the clearing, plants its roots, rears up and roars. Its name comes up. The fight starts.
    /// On later visits the lair stays as it left it (trees leaning, a splintered stump, the halves on the ground).
    /// </summary>
    public partial class DruidBoss
    {
        [Header("Intro cinematic (first meeting)")]
        public string introEpithet = "Walking Grove of the Verdant Cathedral";
        public Color introNameColor = new Color(0.82f, 0.92f, 0.6f);
        public AudioClip introMusic;
        [Range(0f, 1f)] public float introMusicVolume = 0.5f;
        [Tooltip("A deep sting under the name card.")]
        public AudioClip introRevealClip;
        [Tooltip("Layered under its usual groan for the roar (played low).")]
        public AudioClip introRoarClip;
        [Range(0.3f, 1.2f)] public float introRoarPitch = 0.55f;
        [Range(0f, 1f)] public float introRoarVolume = 0.8f;

        public override bool HasIntroCinematic => DruidLair.Find() != null;

        /// <summary>Out in the forest for the cinematic: no arena limit, standing on the forest floor.</summary>
        bool freeRoam;
        DruidLair lair;

        // the torn halves of the tree, carried in its fists: kept at the grip, turned by the choreography (not by the wrist,
        // which swings a 6 m crown about wildly as the IK reorients the hand)
        readonly Transform[] held = new Transform[2];
        readonly Vector3[] heldOff = new Vector3[2];
        readonly Quaternion[] heldRot0 = new Quaternion[2], heldTilt = new Quaternion[2], heldTiltCur = new Quaternion[2];

        // the cinematic camera's own shake (Cinemachine is off while it plays)
        float camShake;
        Camera cineCam;
        float treeSettleT = -1f;

        // ================================================================== the entrance
        public override IEnumerator IntroCinematic(BossCinematic c)
        {
            StopAllCoroutines();
            ReleaseHeld(false);
            lair = DruidLair.Find();
            if (lair == null || lair.hide == null || lair.entry == null) yield break;
            health = maxHealth;
            footing = 0f;
            phaseTwo = false;
            forcedMove = null;
            sinkDepth = 0f;
            gameObject.SetActive(true);
            State = Mode.Cinematic;
            freeRoam = true;
            lair.Intact();
            DropHeldProps();
            cineCam = c.Cam;
            camShake = 0f;
            FillLight(true);
            treeSettleT = -1f;

            Vector3 up = Vector3.up;
            Vector3 O = lair.hide.position;
            Vector3 D = lair.Out;                                   // out of the forest, into the clearing
            Vector3 R = Vector3.Cross(up, D);                       // its right as it comes out
            Vector3 E = lair.entry.position;
            Vector3 P = c.Player != null ? c.Player.transform.position : E + D * 20f;
            Vector3 Fp = Flat(O - P).normalized, Rp = Vector3.Cross(up, Fp);
            Vector3 trees = (lair.pushTrees[0].position + lair.pushTrees[1].position) * 0.5f;
            Vector3 stand1 = O + D * Mathf.Max(1.5f, Vector3.Dot(trees - O, D) - 2.6f);    // trunks 2.6 m ahead of it
            Vector3 stump = lair.ripStump ? lair.ripStump.position : O + D * 8f;
            float ripSide = Mathf.Sign(Vector3.Dot(stump - O, R));               // the torn tree stands off to its right
            Vector3 stand2 = O + D * (Vector3.Dot(stump - O, D) - 2.3f) + R * (Vector3.Dot(stump - O, R) - ripSide * 1.2f);

            yaw = Mathf.Atan2(D.x, D.z) * Mathf.Rad2Deg;
            groundY = Blast.Floor(O).y;
            Place(O);
            ResetPose();
            ResetPlants();
            gait = true;
            look = 0f;
            eyeGlow = 0f;
            // crouched in the shade, head bowed, arms hanging: a tree among the trees
            tCrouch = crouch = 0.22f;
            tHeadPitch = headPitch = 16f;
            tTorsoPitch = torsoPitch = 8f;
            for (int i = 0; i < 2; i++) { tHand[i] = handOff[i] = Arm(i, 0.3f, 0.5f, 0.3f); tCurl[i] = curl[i] = 10f; }
            c.PlayerWatch(O);
            AudioManager.PlayCinematicMusic(introMusic, introMusicVolume);

            // ---- 1. over your shoulder: the forest past the stones stirs. Leaves rain down, the trees shiver, something
            //         heavy shifts its weight. Two eyes open in the shade.
            lair.Rain(10f);
            currentMove = "IntroStir";
            int beat = 0;
            bool eyes = false;
            yield return Hold(4.2f, (k, dt) =>
            {
                lair.Rain(Mathf.Lerp(10f, 46f, k));
                float shiver = Mathf.Lerp(0.3f, 1.5f, k);
                lair.Lean(0, 0f, shiver);
                lair.Lean(1, 0f, shiver * 0.8f);
                if (beat == 0 && k > 0.16f) { beat++; Stir(O, 0.6f); }
                if (beat == 1 && k > 0.46f) { beat++; Stir(O, 0.85f); }
                eyeGlow = Mathf.Clamp01((k - 0.64f) / 0.22f);
                if (!eyes && eyeGlow > 0f)
                {
                    eyes = true;
                    AudioManager.Play(Sfx.WoodGroan, head.position, 0.55f, 0.42f);
                }
                agitation = Mathf.Max(agitation, 0.2f + 0.35f * k);
                float e = Ease.InOutSine(k);
                // beside you rather than over your shoulder (your crown would fill the frame), pushing in on the trees
                Cam(c, OnFloor(P - Fp * Mathf.Lerp(3.4f, 2.6f, e) + Rp * 2.4f, Mathf.Lerp(1.3f, 1.5f, e)), O + up * Mathf.Lerp(5f, 7.6f, e), Mathf.Lerp(32f, 16f, e));
            });

            // ---- 2. it steps out of the shade and lays its hands on two trees
            currentMove = "IntroReach";
            look = 0f;
            tHeadPitch = 4f; tTorsoPitch = 12f; tCrouch = 0.26f;
            handSharp = 3.2f;
            Vector3 camA = O + D * 16f - R * 2.8f;                    // in the clearing, looking up the lane it cleared, above the ferns
            float shedT = 0f;
            Groan(0.8f, 0.7f);
            yield return Hold(2.3f, (k, dt) =>
            {
                lair.Rain(Mathf.Lerp(46f, 24f, k));
                WalkTo(stand1, 1.7f, dt);
                Face(D, 40f, dt);
                for (int i = 0; i < 2; i++) { Reach(i, lair.TrunkPoint(i, 5f)); tCurl[i] = Mathf.Lerp(10f, 40f, k); }
                lair.Lean(0, 0f, 0.5f);
                lair.Lean(1, 0f, 0.4f);
                if ((shedT -= dt) <= 0f) { shedT = 0.22f; Shed(1, 1.2f); }
                float e = Ease.InOutSine(k);
                Cam(c, OnFloor(camA - D * 1f * e, 3.2f), transform.position + up * Mathf.Lerp(6.2f, 5.6f, e), 46f);
            });

            // ---- 3. the grip, then the shove: both trees lean away from the path, groaning, their crowns thrashing
            currentMove = "IntroShove";
            AudioManager.Play(Sfx.WoodGroan, lair.TrunkPoint(0, 5f), 0.9f, 0.52f);
            AudioManager.Play(Sfx.Knock, lair.TrunkPoint(1, 5f), 0.8f, 0.45f);
            Kick(0.12f);
            handSharp = 9f;
            bool creak2 = false;
            yield return Hold(2.2f, (k, dt) =>
            {
                Brake(dt);
                float e = Ease.InOutCubic(k);
                float ang = lair.pushAngle * e;
                lair.Lean(0, ang, (1f - k) * 0.7f);
                lair.Lean(1, ang * 0.94f, (1f - k) * 0.6f);
                // its hands stay on the trunks until they're past its reach, then sweep on out wide
                for (int i = 0; i < 2; i++)
                {
                    if (k < 0.48f) Reach(i, lair.TrunkPoint(i, 5f));
                    else tHand[i] = Arm(i, 2.6f, 1.2f, 1.1f);
                    tCurl[i] = k < 0.48f ? 45f : -10f;
                }
                tTorsoPitch = Mathf.Lerp(12f, 20f, e);
                tCrouch = 0.26f + 0.1f * Mathf.Sin(k * Mathf.PI);
                tHeadPitch = Mathf.Lerp(4f, -6f, e);
                agitation = 0.8f;
                if (!creak2 && k > 0.42f)
                {
                    creak2 = true;
                    AudioManager.Play(Sfx.WoodGroan, lair.TrunkPoint(1, 5f), 1f, 0.45f);
                    AudioManager.Play(Sfx.VineRustle, lair.TrunkPoint(0, 8f), 0.9f, 0.7f);
                    for (int i = 0; i < 2; i++) FXManager.Spawn(FXType.LeafBurst, lair.TrunkPoint(i, 9f), Quaternion.LookRotation(up), 3.2f);
                    Kick(0.08f);
                }
                if ((shedT -= dt) <= 0f)
                {
                    shedT = 0.16f;
                    FXManager.Spawn(FXType.LeafFall, lair.TrunkPoint(Random.Range(0, 2), Random.Range(7f, 10.5f)), Quaternion.identity, 3f);
                }
                float e2 = Ease.InOutSine(k);
                Cam(c, OnFloor(camA - D * (1f + 0.8f * e2), 3.2f - 0.3f * e2), transform.position + up * 5.6f, Mathf.Lerp(46f, 50f, e2));
            });
            treeSettleT = 0f;

            // ---- 4. it steps through between them, shedding leaves, to the third tree
            currentMove = "IntroStep";
            lair.Rain(0f);
            tTorsoPitch = 8f; tCrouch = 0.12f; tHeadPitch = 0f;
            handSharp = 6f;
            Vector3 camB = O + D * 14.5f - R * 0.6f;
            yield return Hold(2.4f, (k, dt) =>
            {
                SettleTrees(dt);
                WalkTo(stand2, 2.4f, dt);
                Face(k < 0.6f ? D : stump - transform.position, 40f, dt);
                for (int i = 0; i < 2; i++) { tHand[i] = Arm(i, Mathf.Lerp(2.0f, 0.6f, k), Mathf.Lerp(1f, 0.6f, k), 0.6f); tCurl[i] = 0f; }
                if ((shedT -= dt) <= 0f) { shedT = 0.14f; Shed(1, 1.3f); }
                float e = Ease.InOutSine(k);
                Cam(c, OnFloor(camB + D * 1f * e, 2f), transform.position + up * 5.4f, 48f);
            });

            // ---- 5. it takes hold of the trunk: one fist low, one high
            currentMove = "IntroGrip";
            // one fist on the middle, the other on the crown's trunk just above the upper break
            Vector3 lowGrip = stump + up * Mathf.Lerp(lair.ripBreaks.x, lair.ripBreaks.y, 0.62f), highGrip = stump + up * (lair.ripBreaks.y + 0.9f);
            tCrouch = 0.42f; tTorsoPitch = 24f; tHeadPitch = 10f;
            handSharp = 4f;
            Vector3 Fh = Flat(stump - stand2).normalized, Rh = Vector3.Cross(up, Fh);   // facing the tree
            // facing the Druid from beyond the tree, a little to one side: the halves fly out left and right across the frame
            Vector3 camC = stump + Fh * 7.5f + Rh * ripSide * 2.4f;
            yield return Hold(1.0f, (k, dt) =>
            {
                SettleTrees(dt);
                Brake(dt);
                Face(Fh, 50f, dt);
                Reach(0, lowGrip);
                Reach(1, highGrip);
                tCurl[0] = tCurl[1] = Mathf.Lerp(0f, 55f, k);
                Cam(c, OnFloor(camC, 3f), Vector3.Lerp(stump + up * 5f, transform.position + up * 6f, 0.4f), 56f);
            });
            Grip(0, lair.ripMid);
            Grip(1, lair.ripTop);
            AudioManager.Play(Sfx.Knock, lowGrip, 0.8f, 0.42f);
            Groan(0.7f, 0.8f);

            // ---- 6. it wrenches the tree off its stump (crack), rearing back with it
            currentMove = "IntroWrench";
            bool snapped = false;
            handSharp = 7f;
            yield return Hold(1.15f, (k, dt) =>
            {
                SettleTrees(dt);
                float e = Ease.OutCubic(k);
                tCrouch = Mathf.Lerp(0.42f, 0.16f, e);
                tTorsoPitch = Mathf.Lerp(24f, -2f, e);
                tHeadPitch = Mathf.Lerp(10f, -8f, e);
                Reach(0, lowGrip - Fh * 1.1f * e + up * 1.3f * e);
                Reach(1, highGrip - Fh * 1.1f * e + up * 1.3f * e);
                heldTilt[0] = heldTilt[1] = Quaternion.AngleAxis(-12f * e, Vector3.Cross(up, Fh));   // its crown tips back over the Druid
                if (!snapped && k > 0.06f)
                {
                    snapped = true;
                    Splinter(stump + up * lair.ripBreaks.x, 1.2f);
                    AudioManager.Play(Sfx.RootSnap, stump + up * lair.ripBreaks.x, 1f, 0.5f);
                    AudioManager.Play(Sfx.WoodGroan, stump + up * 3f, 1f, 0.38f);
                    FXManager.Spawn(FXType.Dust, stump, Quaternion.identity, 2f);
                    FXManager.Spawn(FXType.LeafBurst, lair.ripTop ? lair.ripTop.position : highGrip, Quaternion.LookRotation(up), 3.5f);
                    Kick(0.3f);
                }
                agitation = 0.9f;
                float e2 = Ease.InOutSine(k);
                Cam(c, OnFloor(camC + Fh * 0.8f * e2, 3f + 0.3f * e2), Vector3.Lerp(stump + up * 5.4f, transform.position + up * 6.4f, 0.4f), Mathf.Lerp(56f, 60f, e2));
            });

            // ---- 7. and tears it in two: the low fist drags the middle down and out, the high one heaves the crown up and away
            currentMove = "IntroTear";
            bool torn = false;
            handSharp = 5f;
            yield return Hold(1.1f, (k, dt) =>
            {
                SettleTrees(dt);
                float e = Ease.InOutCubic(k);
                tHand[0] = Arm(0, 2.4f, 0.2f, 1.3f);
                tHand[1] = Arm(1, 1.9f, 3.0f, 0.7f);
                heldTilt[0] = Quaternion.AngleAxis(55f * e, Vector3.Cross(up, -Rh)) * Quaternion.AngleAxis(-12f * (1f - e), Vector3.Cross(up, Fh));
                heldTilt[1] = Quaternion.AngleAxis(30f * e, Vector3.Cross(up, Rh)) * Quaternion.AngleAxis(-12f - 10f * e, Vector3.Cross(up, Fh));
                tTorsoRoll = 6f * e; tTorsoPitch = -4f; tHeadPitch = -10f;
                if (!torn && k > 0.22f)
                {
                    torn = true;
                    Vector3 seam = lair.ripTop && lair.ripMid ? Vector3.Lerp(lair.ripMid.position, lair.ripTop.position, 0.5f) : highGrip;
                    Splinter(seam, 1.4f);
                    AudioManager.Play(Sfx.RootSnap, seam, 1f, 0.6f);
                    AudioManager.Play(Sfx.WoodGroan, seam, 0.8f, 0.6f);
                    AudioManager.Play(Sfx.VineRustle, seam, 1f, 0.6f);
                    FXManager.Spawn(FXType.LeafExplosion, seam, Quaternion.LookRotation(up), 1.5f);
                    Kick(0.25f);
                }
                Cam(c, OnFloor(camC + Fh * (0.8f + 1.4f * k), 3.3f), Vector3.Lerp(stump + up * 5.4f, transform.position + up * 6.2f, 0.5f), Mathf.Lerp(60f, 64f, k));
            });

            // ---- 8. it flings the halves aside, one each way, into the undergrowth
            currentMove = "IntroThrow";
            bool threwL = false, threwR = false;
            handSharp = 9f;
            yield return Hold(1.5f, (k, dt) =>
            {
                SettleTrees(dt);
                // left arm swings out first, the right a beat later; each lets go at the end of its swing
                float l = Mathf.Clamp01(k / 0.4f), r = Mathf.Clamp01((k - 0.25f) / 0.4f);
                tHand[0] = Vector3.Lerp(Arm(0, 1.4f, 1.8f, -0.4f), Arm(0, 3.2f, 0.6f, 1.6f), Ease.InCubic(l));
                tHand[1] = Vector3.Lerp(Arm(1, 1.2f, 3.0f, -0.4f), Arm(1, 3.2f, 1.4f, 1.6f), Ease.InCubic(r));
                tTorsoYaw = Mathf.Lerp(-14f, 16f, Ease.InOutSine(k));
                tTorsoRoll = Mathf.Lerp(6f, -4f, k);
                if (!threwL && l >= 0.95f) { threwL = true; Throw(c, 0); }
                if (!threwR && r >= 0.95f) { threwR = true; Throw(c, 1); }
                tCurl[0] = threwL ? -20f : 55f;
                tCurl[1] = threwR ? -20f : 55f;
                float e = Ease.InOutSine(k);
                Cam(c, OnFloor(camC + Fh * Mathf.Lerp(2.2f, 5f, e), Mathf.Lerp(3.3f, 3.6f, e)),
                    Vector3.Lerp(stump, transform.position, 0.5f) + up * 4.5f, Mathf.Lerp(64f, 70f, e));
            });
            if (!threwL) Throw(c, 0);
            if (!threwR) Throw(c, 1);

            // ---- 9. it strides out into the clearing, toward you
            currentMove = "IntroStride";
            RestPose();
            look = 0.3f;
            tTorsoPitch = 6f;
            Vector3 toward = Flat(P - E).normalized;
            if (toward.sqrMagnitude < 0.01f) toward = D;
            yield return Hold(4.3f, (k, dt) =>
            {
                SettleTrees(dt);
                WalkTo(E, 3.0f, dt);
                Face(Vector3.Slerp(D, toward, Ease.InOutSine(k)), 30f, dt);
                if ((shedT -= dt) <= 0f) { shedT = 0.3f; Shed(1, 1.1f); }
                float e = Ease.InOutSine(k);
                Vector3 me = transform.position;
                Vector3 fwd = Heading;
                Vector3 side = Vector3.Cross(up, fwd);
                Cam(c, OnFloor(me + fwd * Mathf.Lerp(12.5f, 10.5f, e) - side * Mathf.Lerp(3.2f, 2f, e), 1f),
                    me + up * Mathf.Lerp(5.6f, 6.4f, e), 54f);
            });

            // ---- 10. it plants its roots, gathers itself, rears up and roars; its name comes up
            currentMove = "IntroRoar";
            freeRoam = false;
            groundY = Blast.Floor(transform.position).y;
            treeSettleT = 99f;
            lair.Aftermath();
            Vector3 S = transform.position;
            Vector3 Fs = Flat(P - S).normalized;
            if (Fs.sqrMagnitude < 0.01f) Fs = Heading;
            Vector3 Rs = Vector3.Cross(up, Fs);
            tCrouch = 0.34f; tTorsoPitch = 18f; tHeadPitch = 22f;
            for (int i = 0; i < 2; i++) { tHand[i] = Arm(i, 0.2f, 0.4f, 0.9f); tCurl[i] = 40f; }
            look = 0f;
            yield return Hold(0.75f, (k, dt) =>
            {
                Brake(dt, 3f);
                Face(Fs, 40f, dt);
                Cam(c, OnFloor(S + Fs * 10.5f - Rs * 3.6f, 0.9f), S + up * 5.8f, 52f);
            });

            Groan(0.62f, 1f);
            AudioManager.Play(Sfx.DeepRumble, S, 0.9f, 0.9f);
            AudioManager.PlayClip2D(introRoarClip, introRoarVolume, introRoarPitch);
            FXManager.Spawn(FXType.LeafExplosion, head ? head.position : S + up * 8.5f, Quaternion.LookRotation(up), 2.2f);
            FXManager.Spawn(FXType.Dust, S, Quaternion.identity, 3f);
            FXManager.Spawn(FXType.SlamRing, S + up * 0.05f, Quaternion.identity, 3f);
            Shed(12, 1.6f);
            Kick(0.5f);
            c.Hud?.Flash(0.12f);
            sharp = 8f;
            bool named = false;
            yield return Hold(4.6f, (k, dt) =>
            {
                float rear = k < 0.72f ? Ease.OutCubic(Mathf.Clamp01(k / 0.14f)) : 1f - Ease.InOutSine((k - 0.72f) / 0.28f) * 0.85f;
                tHeadPitch = -34f * rear;
                tTorsoPitch = -14f * rear;
                tCrouch = 0.1f + 0.1f * rear;
                for (int i = 0; i < 2; i++) { tHand[i] = Vector3.Lerp(handRest[i], Arm(i, 2.8f, 3.6f, 0.6f), rear); tCurl[i] = -40f * rear; }
                agitation = Mathf.Max(agitation, rear);
                if ((shedT -= dt) <= 0f && rear > 0.5f) { shedT = 0.1f; Shed(1, 1.5f); }
                if (k < 0.6f) camShake = Mathf.Max(camShake, 0.16f * rear);
                if (!named && k > 0.3f)
                {
                    named = true;
                    c.Hud?.ShowBossIntro(displayName.ToUpperInvariant(), introEpithet, introNameColor);
                    AudioManager.PlayClip2D(introRevealClip, 0.75f);
                }
                if (k < 0.45f)
                {
                    float e = Ease.OutCubic(k / 0.45f);
                    Cam(c, OnFloor(S + Fs * Mathf.Lerp(10.5f, 9.6f, e) - Rs * 3.6f, Mathf.Lerp(0.9f, 0.7f, e)), S + up * Mathf.Lerp(5.8f, 7.4f, e), Mathf.Lerp(52f, 55f, e));
                }
                else
                {
                    // the last beat from where you stand: the size of the thing
                    float e = Ease.InOutSine((k - 0.45f) / 0.55f);
                    Cam(c, OnFloor(P - Fs * Mathf.Lerp(2.4f, 3.4f, e) + Vector3.Cross(up, Fs) * 2.3f, Mathf.Lerp(1.4f, 1.8f, e)),
                        S + up * Mathf.Lerp(6f, 5f, e), Mathf.Lerp(40f, 46f, e));
                }
            });
            RestPose();
            look = 1f;
            camShake = 0f;
            FillLight(false);
        }

        /// <summary>Skipped: it's simply there in the clearing, facing you, the lair torn open behind it.</summary>
        public override void SkipIntroCinematic()
        {
            DropHeldProps();
            if (lair == null) lair = DruidLair.Find();
            if (lair != null) { lair.Rain(0f); lair.Aftermath(); }
            gameObject.SetActive(true);
            freeRoam = false;
            groundY = Blast.Floor(arenaCenter).y;
            Vector3 E = lair != null && lair.entry ? lair.entry.position : arenaCenter + Vector3.forward * (arenaRadius - 3.5f);
            Vector3 to = Flat(PlayerPos - E);
            if (to.sqrMagnitude < 0.01f) to = Vector3.back;
            yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            State = Mode.Cinematic;
            Place(E);
            ResetPose();
            ResetPlants();
            eyeGlow = 1f;
            camShake = 0f;
            treeSettleT = -1f;
            FillLight(false);
        }

        public override void BeginFightAfterIntro()
        {
            StopAllCoroutines();
            ReleaseHeld(false);
            DropHeldProps();
            FillLight(false);
            if (lair != null) lair.Rain(0f);
            freeRoam = false;
            health = maxHealth;
            footing = 0f;
            phaseTwo = false;
            stompCd = 0f; kickCd = 1.5f; grabCd = 5f;
            forcedMove = null;
            gameObject.SetActive(true);
            groundY = Blast.Floor(arenaCenter).y;
            sinkDepth = 0f;
            // from where the cinematic left it: feet back on the clearing's floor, standing, looking at you
            for (int i = 0; i < 2; i++) if (!stepping[i]) plant[i] = Ground(plant[i]);
            eyeGlow = 1f;
            treeSettleT = -1f;
            EndMove();
            State = Mode.Stalking;
            brain = StartCoroutine(Brain(false));
        }

        // ================================================================== helpers
        /// <summary>Walk toward <paramref name="target"/> at up to <paramref name="speed"/> m/s (its gait does the stepping).</summary>
        void WalkTo(Vector3 target, float speed, float dt)
        {
            Vector3 to = Flat(target - transform.position);
            float d = to.magnitude;
            Locomote(d > 0.12f ? to / d * Mathf.Min(speed, d * 1.6f) : Vector3.zero, dt, 0.8f);
        }

        /// <summary>Its fist <paramref name="i"/> to a point in the world (the palm against it, not the wrist through it).</summary>
        void Reach(int i, Vector3 world)
        {
            if (!upperArm[i]) return;
            Vector3 fromShoulder = upperArm[i].position - world;
            float m = fromShoulder.magnitude;
            Vector3 wrist = world + (m > 0.01f ? fromShoulder / m : Vector3.up) * 0.55f;
            tHand[i] = HandLocal(i, wrist);
        }

        /// <summary>Fist <paramref name="i"/> closes on <paramref name="piece"/>: it rides along from here.</summary>
        void Grip(int i, Transform piece)
        {
            if (!piece || !hand[i]) return;
            held[i] = piece;
            heldOff[i] = piece.position - hand[i].position;
            heldRot0[i] = piece.rotation;
            heldTilt[i] = heldTiltCur[i] = Quaternion.identity;
        }

        void DropHeldProps()
        {
            for (int i = 0; i < 2; i++) held[i] = null;
        }

        /// <summary>LateUpdate, after the arms are posed: carried pieces sit in the fists.</summary>
        void UpdateHeldProps()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < 2; i++)
            {
                if (!held[i] || !hand[i]) continue;
                heldTiltCur[i] = Quaternion.Slerp(heldTiltCur[i], heldTilt[i], Ease.Damp(7f, dt));
                held[i].SetPositionAndRotation(hand[i].position + heldTiltCur[i] * heldOff[i], heldTiltCur[i] * heldRot0[i]);
            }
        }

        /// <summary>Let go of half <paramref name="i"/>: it tumbles through the air and crashes down where the lair says.</summary>
        void Throw(BossCinematic c, int i)
        {
            var piece = held[i];
            held[i] = null;
            if (!piece || lair == null) return;
            AudioManager.Play(Sfx.WhooshHeavy, piece.position, 1f, 0.55f);
            c.StartCoroutine(Fly(piece, i));
        }

        IEnumerator Fly(Transform piece, int i)
        {
            lair.LandingPose(i, out var endPos, out var endRot);
            Vector3 p0 = piece.position;
            Quaternion r0 = piece.rotation;
            float dist = Flat(endPos - p0).magnitude;
            float time = Mathf.Clamp(0.55f + dist * 0.05f, 0.6f, 1.1f);
            float arc = 1.5f + dist * 0.15f;
            float t = 0f;
            while (t < time)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / time);
                Vector3 p = Vector3.Lerp(p0, endPos, k) + Vector3.up * (arc * 4f * k * (1f - k));
                piece.SetPositionAndRotation(p, Quaternion.Slerp(r0, endRot, Ease.InOutSine(k)));
                yield return null;
            }
            piece.SetPositionAndRotation(endPos, endRot);
            Thud(endPos, 1.1f);
            FXManager.Spawn(FXType.LeafBurst, endPos + Vector3.up * 1.5f, Quaternion.LookRotation(Vector3.up), 4f);
            AudioManager.Play(Sfx.VineRustle, endPos, 0.9f, 0.55f);
            Kick(0.22f);
        }

        /// <summary>A trunk splitting: chips and splinters burst out round the break.</summary>
        void Splinter(Vector3 at, float k)
        {
            FXManager.Spawn(FXType.WoodChips, at, Quaternion.LookRotation(Vector3.up), 3.2f * k);
            FXManager.Spawn(FXType.WoodChips, at, Quaternion.LookRotation(Random.onUnitSphere + Vector3.up), 2.6f * k);
            FXManager.Spawn(FXType.SlamDebris, at, Quaternion.identity, 0.9f * k);
        }

        /// <summary>Something heavy shifting in the trees: a low rumble and a far footfall.</summary>
        void Stir(Vector3 at, float k)
        {
            AudioManager.Play(Sfx.DeepRumble, at, 0.7f * k, 0.8f);
            AudioManager.Play(Sfx.Footstep, at, 1f, 0.42f);
            AudioManager.Play(Sfx.Slam, at, 0.35f * k, 0.55f);
            AudioManager.Play(Sfx.VineRustle, at + Vector3.up * 6f, 0.8f * k, 0.6f);
            Kick(0.05f * k);
        }

        /// <summary>The shoved trees spring back from their lean and come to rest leaning a little less.</summary>
        void SettleTrees(float dt)
        {
            if (lair == null || treeSettleT < 0f || treeSettleT > 3f) return;
            treeSettleT += dt;
            float a = lair.settleAngle + (lair.pushAngle - lair.settleAngle) * Mathf.Exp(-2.6f * treeSettleT) * Mathf.Cos(7.5f * treeSettleT);
            lair.Lean(0, a);
            lair.Lean(1, a * 0.97f);
        }

        void Kick(float amount) => camShake = Mathf.Max(camShake, amount);

        Light cineFill;

        /// <summary>A soft light on the Druid's front from the camera's side while the cinematic plays (the forest's shade
        /// would leave it a silhouette); off for the fight.</summary>
        void FillLight(bool on)
        {
            if (on && cineFill == null)
            {
                cineFill = new GameObject("IntroFill").AddComponent<Light>();
                cineFill.transform.SetParent(transform, false);
                cineFill.type = LightType.Point;
                cineFill.color = new Color(0.95f, 1f, 0.82f);
                cineFill.range = 15f;
                cineFill.intensity = 26f;
                cineFill.shadows = LightShadows.None;
            }
            if (cineFill) cineFill.enabled = on;
        }

        void PlaceFill(Vector3 camPos)
        {
            if (!cineFill || !cineFill.enabled) return;
            Vector3 to = Flat(camPos - transform.position);
            to = to.sqrMagnitude > 0.01f ? to.normalized : Heading;
            cineFill.transform.position = transform.position + to * 4.5f + Vector3.up * 6.5f;
        }

        /// <summary>A camera spot <paramref name="h"/> m above the ground under <paramref name="p"/>.</summary>
        static Vector3 OnFloor(Vector3 p, float h) => Blast.Floor(p) + Vector3.up * h;

        /// <summary>A footfall during the cinematic shakes its camera, harder the closer it lands.</summary>
        void CinematicFootfall(Vector3 at)
        {
            if (cineCam == null) return;
            float d = Vector3.Distance(cineCam.transform.position, at);
            Kick(0.14f * Mathf.Clamp01(1.25f - d / 22f));
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
