using System.Collections;
using ElderTree.Audio;
using ElderTree.Combat;
using UnityEngine;
using UnityEngine.Rendering;

namespace ElderTree.Enemies
{
    /// <summary>
    /// The Fairy's first-meeting cinematic: a short ballet in the grotto's moonbeam. She drifts down the beam onto her
    /// toes, sways into a few of the pack's dance steps (HumanF@Dance05/09/12/17, retargeted in FairyBoss.Dance.cs), does a port de bras (arms low, rounded in front, up over her head, open to the side), an arabesque that she
    /// turns slowly on one pointe, a développé to the side, then a preparation and pirouettes: a double, and a long
    /// accelerating spin that lifts her off the floor in a spiral of dust. She stops dead facing you, opens her arms, and a
    /// pale moon fades in on her back while her name comes up. Then the fight starts, and the moon fades away.
    /// The project has no ballet clips, so the ballet itself is choreographed on her own rig.
    /// </summary>
    public partial class FairyBoss
    {
        [Header("Intro cinematic (first meeting)")]
        public string introEpithet = "Moon Dancer of the Starlit Grotto";
        [Tooltip("Additive disc: the moon that fades in on her back (M_FX_FairyIntroMoon).")]
        public Material introMoonMaterial;
        [Tooltip("Additive soft glow round the moon (M_FX_FairyGlow).")]
        public Material introHaloMaterial;
        public AudioClip introMusic;
        [Range(0f, 1f)] public float introMusicVolume = 0.5f;
        [Tooltip("Played as the moon rises behind her.")]
        public AudioClip introRevealClip;
        public Color introMoonColor = new Color(0.6f, 0.64f, 0.76f, 1f);
        [Tooltip("Diameter of the moon disc (m).")]
        public float introMoonSize = 4.1f;

        public override bool HasIntroCinematic => true;

        float spinRate, legSharp = 4f;
        Renderer[] toeRenderers;

        // the moon on her back
        Transform moonRoot;
        Renderer moonDisc, moonHalo;
        Light moonLight;
        float moonAlpha, moonTarget;
        MaterialPropertyBlock moonMpb;
        static Mesh quadMesh;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // ================================================================== the ballet
        public override IEnumerator IntroCinematic(BossCinematic c)
        {
            StopAllCoroutines();
            ClearProjectiles();
            health = maxHealth;
            poise = maxPoise;
            phaseTwo = false;
            forcedMove = null;
            gameObject.SetActive(true);
            if (model) model.localScale = modelRestScale;
            ResetPose();
            State = Mode.Cinematic;
            currentMove = "Ballet";

            // the stage: the middle of the arena (the grotto's moonbeam), facing the player, who is the audience
            Vector3 S = Blast.Floor(arenaCenter);
            groundY = S.y;
            Vector3 P = c.Player != null ? c.Player.transform.position : S + Vector3.back * 11f;
            Vector3 F = Flat(P - S);
            if (F.sqrMagnitude < 0.01f) F = Vector3.back;
            F.Normalize();
            Vector3 R = Vector3.Cross(Vector3.up, F);          // her right as she faces you
            float yaw0 = Mathf.Atan2(F.x, F.z) * Mathf.Rad2Deg;
            Vector3 up = Vector3.up;
            Vector3 Orbit(float deg, float dist, float h) => S + Quaternion.AngleAxis(deg, up) * F * dist + up * h;
            c.PlayerWatch(S);
            AudioManager.PlayCinematicMusic(introMusic, introMusicVolume);

            // ---- 1. she drifts down the moonbeam: feet together and pointed, arms low, looking down at the floor
            yaw = yaw0;
            hover = 7.5f;
            Dance(3f);
            contact = 0f;
            tAir = 0.6f;
            BrasBas();
            Leg(0, 6f, 10f, 0f); Leg(1, 0f, 2f, 0f);
            Wings(-6f, 16f, 3.2f, 0f);
            tHeadPitch = 16f;
            SnapPose();
            Place(S);
            Dust(3f);
            Chime(0.5f, 1.3f);
            Vector3 behind = P + F * 2.4f + R * 1.1f;
            yield return Hold(4.4f, (k, dt) =>
            {
                hover = Mathf.Lerp(7.5f, -0.4f, Ease.InOutSine(k));   // the floor stops her (toes never go through)
                float e = Ease.InOutSine(k);
                Vector3 cam = Vector3.Lerp(behind + up * 1.5f, P + F * 1.3f + R * 0.85f + up * 1.45f, e);
                c.Shot(cam, S + up * (Mathf.Max(hover, 0f) + 1.6f), Mathf.Lerp(36f, 22f, e));
            });

            // ---- 2. she lands and sways into the pack's Dance09, a slow side-to-side step, while the wings settle
            Chime(0.45f, 1.5f);
            Dust(1.2f);
            contact = 1f;
            tAir = 0f;
            Leg(0, 0f, 0f, 0f); Leg(1, 0f, 0f, 0f);
            BrasBas(); tHeadPitch = 4f;
            Wings(-12f, 10f, 2.4f, 0f);
            yield return DancePhrase(DanceSway, 2.6f, 0.7f, (k, dt) =>
                c.Shot(Orbit(Mathf.Lerp(34f, 26f, k), Mathf.Lerp(6.8f, 6.4f, k), 1.25f), S + up * 1.65f, 38f));

            // ---- 3. on her toes: port de bras (bras bas -> first -> fifth en haut -> second), the wings open with the arms
            StopDance(0.6f);
            yield return Hold(4.2f, (k, dt) =>
            {
                float t = k * 4.8f;
                if (t < 0.6f) { BrasBas(); tHeadPitch = 10f; }
                else if (t < 1.6f) { FirstPos(); tHeadPitch = 16f; tHeadTilt = 8f; Leg(0, 0f, 0f, 0f); Leg(1, 0f, 0f, 0f); }
                else if (t < 2.8f) { FifthPos(); tHeadPitch = -16f; tHeadTilt = 0f; tTorso = -4f; Wings(8f, 14f, 2.6f, -4f); }
                else if (t < 4.0f) { SecondPos(); tHeadPitch = -4f; tHeadTilt = -9f; tTorso = -10f; tLean = -3f; Wings(18f, 12f, 2.2f, -6f); }
                else { SecondPos(); tHeadPitch = 2f; tHeadTilt = -4f; tTorso = -4f; tLean = 0f; }
                float e = Ease.InOutSine(k);
                c.Shot(Orbit(Mathf.Lerp(26f, -8f, e), Mathf.Lerp(6.4f, 5.7f, e), 1.3f), S + up * 1.75f, 40f);
            });

            // ---- 4. the arms open into the pack's Dance05: a light step with the arms flowing, the camera drifting round
            Chime(0.35f, 1.7f);
            yield return DancePhrase(DanceFlow, 2.4f, 0.6f, (k, dt) =>
                c.Shot(Orbit(Mathf.Lerp(-8f, -30f, Ease.InOutSine(k)), 5.9f, 1.2f), S + up * 1.7f, 40f));
            StopDance(0.6f);

            // ---- 5. arabesque, turned slowly on one pointe (a promenade) until her back and wings face you
            Chime(0.4f, 1.62f);
            sharp = 4f;
            yield return Hold(3.4f, (k, dt) =>
            {
                float e = Ease.InOutSine(Mathf.Clamp01(k / 0.25f));
                yaw = yaw0 + Mathf.Lerp(0f, 90f, e) + Mathf.Lerp(0f, 90f, Ease.InOutSine(Mathf.Clamp01((k - 0.25f) / 0.75f)));
                Leg(1, 0f, 0f, 0f);                          // standing leg
                Leg(0, -74f, 4f, 0f);                        // working leg stretched back and up
                Arm(1, 16f, 6f, 8f); tArmRoll[1] = 10f;      // front arm reaches forward
                Arm(0, 8f, 108f, 8f); tArmRoll[0] = 10f;     // back arm to the side
                tTorso = 16f; tLean = 6f; tHeadPitch = -10f; tHeadTilt = 0f;
                Wings(14f, 8f, 2f, -4f);
                spinRate = 0f;
                c.Shot(S + F * 6.4f + R * Mathf.Lerp(-0.9f, 0.7f, k) + up * 0.7f, S + up * 1.55f, 36f);
            });

            // ---- 6. a turn back to face you through passé, then a développé to the side
            yield return Hold(2.2f, (k, dt) =>
            {
                float t = k * 2.2f;
                yaw = yaw0 + 180f + 180f * Ease.InOutSine(Mathf.Clamp01(t / 0.8f));
                tTorso = 0f; tLean = 0f;
                if (t < 0.8f) { Leg(0, 70f, 115f, 55f); FirstPos(); tHeadPitch = 0f; Wings(-8f, 8f, 2.5f, 0f); }
                else
                {
                    float d = Ease.OutCubic(Mathf.Clamp01((t - 0.8f) / 0.9f));
                    Leg(0, Mathf.Lerp(70f, 84f, d), Mathf.Lerp(115f, 0f, d), Mathf.Lerp(55f, 86f, d));
                    SecondPos(); tHeadPitch = -6f; tHeadTilt = -12f; Wings(16f, 9f, 2f, -4f);
                }
                Leg(1, 0f, 0f, 0f);
                legSharp = 7f;
                c.Shot(Orbit(Mathf.Lerp(-16f, -26f, k), 5.8f, 1.35f), S + up * 1.65f, 38f);
            });

            // ---- 7. the leg comes down into the pack's Dance17, then Dance12 (deeper, quicker): the build-up to the spins
            Leg(0, 0f, 0f, 0f);
            SecondPos(); tHeadTilt = 0f; tHeadPitch = 0f;
            Wings(4f, 12f, 3f, 0f);
            yield return DancePhrase(DanceStep, 2.2f, 0.45f, (k, dt) =>
                c.Shot(Orbit(Mathf.Lerp(-30f, -12f, k), 5.4f, 0.9f), S + up * 1.6f, 40f));
            Chime(0.4f, 1.55f);
            yield return DancePhrase(DanceDrive, 2.2f, 0.35f, (k, dt) =>
                c.Shot(Orbit(Mathf.Lerp(30f, 18f, k), 5.0f, 0.75f), S + up * 1.55f, 42f));
            StopDance(0.35f);

            // ---- 8. preparation: fourth position, knees soft, one arm rounded in front, the other open; wings fold
            Leg(0, -20f, 18f, 0f); Leg(1, 8f, 20f, 0f);
            Arm(1, -16f, 30f, 56f); tArmRoll[1] = 85f;
            Arm(0, -6f, 80f, 18f); tArmRoll[0] = 90f;
            tTwist = -12f; tHeadPitch = 4f; tHeadTilt = 0f;
            Wings(-22f, 5f, 2f, 0f);
            sharp = 7f;
            legSharp = 8f;
            yield return Hold(0.75f, (k, dt) => c.Shot(Orbit(14f, 5.0f, 0.85f), S + up * 1.6f, 42f));

            // ---- 9a. a double pirouette en dehors in passé, spotting you
            AudioManager.Play(Sfx.WhooshLight, transform.position + up * 1.5f, 0.45f, 1.5f);
            tTwist = 0f;
            Leg(0, 78f, 118f, 62f); Leg(1, 0f, 0f, 0f);
            FirstPos();
            legSharp = 14f; sharp = 12f;
            float prevAngle = 0f;
            yield return Hold(0.95f, (k, dt) =>
            {
                float a = 720f * Ease.InOutSine(k);
                Turn(yaw0 + a, a - prevAngle, dt, yaw0);
                prevAngle = a;
                c.Shot(Orbit(Mathf.Lerp(14f, 4f, k), 5.0f, 0.85f), S + up * 1.6f, 42f);
            });
            if (Mathf.Abs(prevAngle) > 0f) AudioManager.Play(Sfx.WhooshLight, transform.position + up * 1.5f, 0.4f, 1.6f);
            spinRate = 0f; headSpot = float.NaN;
            // land in fourth, arms open for a breath
            Leg(0, -18f, 16f, 0f); Leg(1, 8f, 18f, 0f);
            SecondPos();
            sharp = 8f;
            yield return Hold(0.4f, (k, dt) => c.Shot(Orbit(4f, 5.0f, 0.85f), S + up * 1.6f, 42f));

            // ---- 9b. the long spin: arms pull in, she speeds up, rises off the floor and the dust spirals out
            Leg(0, 78f, 118f, 62f); Leg(1, 0f, 0f, 0f);
            FirstPos();
            Dust(14f);
            Chime(0.55f, 1.4f);
            const float spinTime = 2.7f, spinDeg = 1800f;
            prevAngle = 0f;
            int turnsRung = 0;
            yield return Hold(spinTime, (k, dt) =>
            {
                // a long acceleration and a sharp stop, exactly facing you again
                float f = k < 0.85f ? Mathf.Pow(k / 0.85f, 1.8f) * 0.93f : 0.93f + 0.07f * Ease.OutCubic((k - 0.85f) / 0.15f);
                float a = spinDeg * f;
                Turn(yaw0 + a, a - prevAngle, dt, k < 0.35f ? yaw0 : float.NaN);
                prevAngle = a;
                if (k > 0.35f) FifthPos();                     // arms up: she spins faster
                if (k > 0.3f) { contact = 0f; hover += (0.95f - hover) * Ease.Damp(2.2f, dt); }
                glowBoost = Mathf.Lerp(0f, 1.4f, k);
                int turns = Mathf.FloorToInt(a / 360f);
                if (turns > turnsRung)
                {
                    turnsRung = turns;
                    AudioManager.Play(Sfx.WhooshLight, transform.position + up * 1.6f, 0.35f + 0.05f * turns, 1.4f + 0.1f * turns);
                    Chime(0.3f + 0.05f * turns, 1.3f + 0.12f * turns);
                }
                float e = Ease.InOutSine(k);
                c.Shot(Orbit(Mathf.Lerp(24f, -46f, e), Mathf.Lerp(5.2f, 4.6f, e), Mathf.Lerp(0.8f, 1.4f, e)),
                       S + up * (1.65f + Mathf.Max(0f, hover) * 0.9f), Mathf.Lerp(44f, 40f, e), Mathf.Sin(k * Mathf.PI) * 3f);
            });

            // ---- 10. she stops dead, opens her arms and wings, and the moon fades in on her back
            spinRate = 0f; headSpot = float.NaN;
            yaw = yaw0;
            Dust(2f);
            sharp = 10f;
            Arms(52f, 64f, 12f, 52f, 64f, 12f); ArmRoll(90f, 90f);
            Leg(0, 6f, 10f, 0f); Leg(1, 0f, 2f, 0f);
            tTorso = -8f; tLean = -4f; tHeadPitch = -12f; tHeadTilt = 0f;
            Wings(26f, 9f, 2.2f, -6f);
            tAir = 0.35f;
            c.Hud?.Flash(0.18f);
            Chime(0.8f, 1.0f);
            AudioManager.Play(Sfx.OrbCharge, transform.position + up * 2f, 0.6f, 1.3f);
            AudioManager.PlayClip2D(introRevealClip, 0.75f);
            BuildIntroMoon();
            moonTarget = 1f;
            bool named = false;
            yield return Hold(4.8f, (k, dt) =>
            {
                hover += (0.9f - hover) * Ease.Damp(2f, dt);
                if (k > 0.15f) sharp = 4f;
                glowBoost = Mathf.Lerp(1.4f, 0.6f, k);
                if (!named && k > 0.26f)
                {
                    named = true;
                    c.Hud?.ShowBossIntro(displayName.ToUpperInvariant(), introEpithet);
                }
                float e = Ease.InOutSine(k);
                c.Shot(S + F * Mathf.Lerp(5.0f, 8.6f, e) + R * Mathf.Lerp(0.3f, 0f, e) + up * Mathf.Lerp(1.2f, 0.6f, e),
                       S + up * Mathf.Lerp(2.5f, 2.95f, e), Mathf.Lerp(40f, 38f, e));
            });
            glowBoost = 0f;
        }

        /// <summary>Skipped: she's simply there in the moonbeam, hovering and facing you.</summary>
        public override void SkipIntroCinematic()
        {
            ClearProjectiles();
            gameObject.SetActive(true);
            Vector3 S = Blast.Floor(arenaCenter);
            groundY = S.y;
            Vector3 F = Flat(PlayerPos - S);
            if (F.sqrMagnitude < 0.01f) F = Vector3.back;
            yaw = Mathf.Atan2(F.x, F.z) * Mathf.Rad2Deg;
            State = Mode.Cinematic;
            ResetPose();
            hover = hoverHeight;
            Place(S);
            moonAlpha = moonTarget = 0f;
            HideIntroMoon();
            StopDance(0.05f);
        }

        public override void BeginFightAfterIntro()
        {
            StopAllCoroutines();
            ClearProjectiles();
            health = maxHealth;
            poise = maxPoise;
            phaseTwo = false;
            forcedMove = null;
            swoopCd = 6f; spiralCd = 4f; moonCd = 3f; fleeCd = 0f; closeTimer = 0f;
            gameObject.SetActive(true);
            groundY = Blast.Floor(transform.position).y;
            // from the final pose she eases into her hover; the moon fades from her back as the fight begins
            contact = 0f; spinRate = 0f; legSharp = 4f; headSpot = float.NaN;
            StopDance(0.3f);
            RestPose(3f);
            moonTarget = 0f;
            glowBoost = 0f;
            Dust(1f);
            State = Mode.Hovering;
            currentMove = "-";
            StartCoroutine(Brain(false));
        }

        // indices into danceClips (the builder fills them in this order)
        const int DanceFlow = 0, DanceSway = 1, DanceDrive = 2, DanceStep = 3;   // HumanF@Dance05, 09, 12, 17

        /// <summary>Blend into pack dance clip <paramref name="clip"/> for <paramref name="time"/> s, facing you, toes on the floor.
        /// The next phrase blends her back out (StopDance).</summary>
        IEnumerator DancePhrase(int clip, float time, float fadeIn, System.Action<float, float> cam)
        {
            StartDance(clip, fadeIn);
            contact = 1f;
            tAir = 0f;
            spinRate = 0f;
            headSpot = float.NaN;
            yield return Hold(time, cam);
        }

        // ================================================================== pose helpers
        void Dance(float s)
        {
            sharp = s;
            tHeadTrack = 0f;
            tLean = 0f; tBank = 0f; tLift = 0f; tTorso = 0f; tTwist = 0f; tHeadTilt = 0f;
            legSharp = 6f;
        }

        void BrasBas() { Arms(-74f, 14f, 34f, -74f, 14f, 34f); ArmRoll(70f, 70f); }
        void FirstPos() { Arms(-20f, 32f, 58f, -20f, 32f, 58f); ArmRoll(85f, 85f); }
        void SecondPos() { Arms(-6f, 82f, 18f, -6f, 82f, 18f); ArmRoll(90f, 90f); }
        void FifthPos() { Arms(74f, 20f, 46f, 74f, 20f, 46f); ArmRoll(90f, 90f); }
        void ArmRoll(float l, float r) { tArmRoll[0] = l; tArmRoll[1] = r; }
        void Leg(int i, float hipDeg, float kneeDeg, float turnDeg) { tHip[i] = hipDeg; tKnee[i] = kneeDeg; tTurn[i] = turnDeg; }

        /// <summary>Jump every smoothed pose value straight to its target (a cut).</summary>
        void SnapPose()
        {
            lean = tLean; bank = tBank; lift = tLift; torsoPitch = tTorso; twist = tTwist; headPitch = tHeadPitch;
            headTrack = tHeadTrack; headTilt = tHeadTilt; air = tAir;
            headYawCur = headPitchCur = 0f;
            for (int i = 0; i < 2; i++)
            {
                elev[i] = tElev[i]; azim[i] = tAzim[i]; elbow[i] = tElbow[i]; armRoll[i] = tArmRoll[i];
                hip[i] = tHip[i]; knee[i] = tKnee[i]; turn[i] = tTurn[i];
            }
            wingOpen = tWingOpen; wingAmp = tWingAmp; wingFreq = tWingFreq; wingDroop = tWingDroop;
        }

        /// <summary>A step of a spin: face <paramref name="newYaw"/>; the head spots <paramref name="spotYaw"/> (NaN: no spotting).</summary>
        void Turn(float newYaw, float deltaDeg, float dt, float spotYaw)
        {
            yaw = newYaw;
            spinRate = deltaDeg / Mathf.Max(dt, 1e-4f);
            // spotting: the head holds on you while the body turns away, then whips round ahead of it
            headSpot = float.IsNaN(spotYaw) ? float.NaN : Mathf.Clamp(Mathf.DeltaAngle(yaw, spotYaw), -80f, 80f);
        }

        /// <summary>
        /// En pointe: lowers or raises her so the lowest toe rests on the floor (<see cref="contact"/> = 1), and in the
        /// cinematic never lets a toe sink through it.
        /// </summary>
        void KeepToesOnFloor()
        {
            if (toeRenderers == null)
            {
                var list = new System.Collections.Generic.List<Renderer>();
                for (int i = 0; i < 2; i++)
                {
                    if (foot[i]) list.AddRange(foot[i].GetComponentsInChildren<Renderer>());
                    if (list.Count == 0 && shin[i]) list.AddRange(shin[i].GetComponentsInChildren<Renderer>());
                }
                list.RemoveAll(r => r == null || r is ParticleSystemRenderer);
                toeRenderers = list.ToArray();
            }
            if (toeRenderers.Length == 0) return;
            float minY = float.MaxValue;
            foreach (var r in toeRenderers) if (r && r.enabled) minY = Mathf.Min(minY, r.bounds.min.y);
            if (minY == float.MaxValue) return;
            float err = minY - (groundY + 0.01f);
            float shift = err < 0f ? err : err * contact;
            if (Mathf.Abs(shift) < 1e-4f) return;
            transform.position -= Vector3.up * shift;
            hover -= shift;
        }

        // ================================================================== the moon on her back
        void BuildIntroMoon()
        {
            if (moonRoot != null || introMoonMaterial == null) return;
            moonRoot = new GameObject("Fairy Intro Moon").transform;
            moonDisc = MoonQuad("Disc", introMoonMaterial);
            if (introHaloMaterial) moonHalo = MoonQuad("Halo", introHaloMaterial);
            moonLight = new GameObject("MoonRim").AddComponent<Light>();
            moonLight.transform.SetParent(moonRoot, false);
            moonLight.type = LightType.Point;
            moonLight.color = new Color(0.72f, 0.82f, 1f);
            moonLight.range = 9f;
            moonLight.intensity = 0f;
            moonLight.shadows = LightShadows.None;
            moonMpb = new MaterialPropertyBlock();
            moonAlpha = 0f;
            moonRoot.gameObject.SetActive(false);
        }

        Renderer MoonQuad(string n, Material m)
        {
            var go = new GameObject(n);
            go.transform.SetParent(moonRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        static Mesh QuadMesh()
        {
            if (quadMesh != null) return quadMesh;
            quadMesh = new Mesh { name = "IntroMoonQuad" };
            quadMesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            quadMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            quadMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            quadMesh.RecalculateNormals();
            quadMesh.RecalculateBounds();
            return quadMesh;
        }

        void HideIntroMoon()
        {
            moonAlpha = moonTarget = 0f;
            if (moonRoot) moonRoot.gameObject.SetActive(false);
        }

        void UpdateIntroMoon(float dt)
        {
            if (moonRoot == null) return;
            moonAlpha = Mathf.MoveTowards(moonAlpha, moonTarget, dt / (moonTarget > moonAlpha ? 2.4f : 1.6f));
            bool on = moonAlpha > 0.001f;
            if (moonRoot.gameObject.activeSelf != on) moonRoot.gameObject.SetActive(on);
            if (!on) return;
            var cam = Camera.main;
            Vector3 back = cam != null ? Flat(cam.transform.forward) : -Heading;
            if (back.sqrMagnitude < 1e-4f) back = -Heading;
            back.Normalize();
            // centred on her upper back, a step behind her from where you're looking: it frames her head, arms and wings
            Vector3 chest = torso ? torso.position : transform.position + Vector3.up * 1.9f;
            moonRoot.position = chest + back * 1.6f + Vector3.up * 0.3f;
            moonRoot.rotation = cam != null ? cam.transform.rotation : Quaternion.LookRotation(back);
            float a = Ease.InOutSine(moonAlpha);
            float flicker = 0.96f + 0.04f * Mathf.Sin(Time.time * 2.3f);
            float size = introMoonSize * Mathf.Lerp(0.84f, 1f, Ease.OutCubic(moonAlpha));
            moonDisc.transform.localScale = Vector3.one * size;
            moonMpb.SetColor(BaseColorId, introMoonColor * (a * flicker));
            moonDisc.SetPropertyBlock(moonMpb);
            if (moonHalo)
            {
                moonHalo.transform.localScale = Vector3.one * size * 2.3f;
                moonMpb.SetColor(BaseColorId, new Color(0.45f, 0.6f, 1f, 1f) * (0.2f * a * flicker));
                moonHalo.SetPropertyBlock(moonMpb);
            }
            moonLight.intensity = 18f * a;
        }

        void OnDestroy()
        {
            if (moonRoot) Destroy(moonRoot.gameObject);
            ReleaseDanceGraph();
        }
    }
}
