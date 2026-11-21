using System.Collections.Generic;
using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.Combat;
using ElderTree.Equipment;
using ElderTree.FX;
using UnityEngine;

namespace ElderTree.Player
{
    /// <summary>
    /// Shark Mask (Shark reward) actions:
    ///  * Long sword (Monster Hunter style): every swing leaves a crescent slash and cuts vines. Hits fill the
    ///    spirit gauge; heavy (spirit) attacks spend it for extra damage, and a spirited roundslash that lands
    ///    raises the gauge level (white, yellow, red: more damage, coloured slashes) for a while.
    ///  * Crossbow: raise with LT / RMB, the loaded bolt fires at once on RT / LMB, then the string is cranked
    ///    back (slower than the bow) and the next bolt drops into the groove. Bolts hit much harder.
    ///  * Shark Tornado (light + heavy): coil, then whirl several turns drifting with the stick; each turn cuts
    ///    everything in reach once (vines too) and the last one ends in a wide finishing cut.
    /// </summary>
    public partial class PlayerController
    {
        [Header("Shark Mask")]
        public ArrowProjectile boltPrefab;
        [Tooltip("Root yaw relative to the aim while holding the crossbow (the Rifle clips aim slightly off the root).")]
        public float crossbowStanceYaw = 0f;
        [Tooltip("Rifle_Reload01 length (filled by the builder): the reload clip is sped up to fit the crossbow's reload time.")]
        public float crossbowReloadClipLength = 2f;
        [Tooltip("Sword_Spin (Attack2H03) length, filled by the builder.")]
        public float spinClipLength = 1.5f;
        [Tooltip("Normalized time of Attack2H03 held through the spin: blade level (~3 deg), tip 0.8 m up, out to the side " +
                 "(measured on the shark rig). The coil plays the sweep from 0 up to this frame.")]
        public float spinPoseNorm = 0.3f;
        [Tooltip("+1 turns clockwise seen from above, -1 counter-clockwise (follow the clip's sweep).")]
        public float spinDirection = -1f;
        [Tooltip("Spin special, one entry per turn (the last entry repeats): held pose, normalized time, body lift (m), " +
                 "lean (deg). The tornado climbs low, mid, airborne and high, then crashes down into the finisher.")]
        public SpinTurn[] spinTurns =
        {
            new SpinTurn { state = "Sword_Spin", norm = 0.30f, lift = 0f, lean = 9f },        // low sweep, blade at the knees
            new SpinTurn { state = "Sword_SpinMid", norm = 0.65f, lift = 0.14f, lean = 4f },   // rising, blade at the waist
            new SpinTurn { state = "Sword_SpinHigh", norm = 0.45f, lift = 0.5f, lean = -7f },  // off the ground, blade overhead
            new SpinTurn { state = "Sword_Spin", norm = 0.30f, lift = 0f, lean = 13f },        // crash down: wide low finisher
        };

        [System.Serializable]
        public struct SpinTurn
        {
            public string state;
            public float norm;
            public float lift;
            public float lean;
        }

        SlashArc slashArc;
        Vector3 slashPrevTip;

        // ================================================================== blade roll
        // Single-edged blades turn about their own axis per swing (AttackData.bladeRoll) so the edge leads the cut.
        Transform rollWeapon, rollParent;
        Quaternion rollRest;
        float bladeRollNow;

        void CaptureWeaponRest()
        {
            if (rollWeapon != null && rollWeapon.parent == rollParent) rollWeapon.localRotation = rollRest;
            rollWeapon = weapon != null ? weapon.transform : null;
            rollParent = rollWeapon != null ? rollWeapon.parent : null;
            if (rollWeapon != null) rollRest = rollWeapon.localRotation;
            bladeRollNow = 0f;
        }

        void UpdateBladeRoll(float dt)
        {
            if (rollWeapon == null || HammerThrown || rollWeapon.parent != rollParent) return;
            float target = State == PlayerState.Attack && attack != null && !inStance ? attack.bladeRoll : 0f;
            if (Mathf.Approximately(target, bladeRollNow) && Mathf.Approximately(target, 0f)) return;
            bladeRollNow = Mathf.MoveTowards(bladeRollNow, target, dt * 1100f);
            rollWeapon.localRotation = rollRest * Quaternion.AngleAxis(bladeRollNow, Vector3.up);
        }

        // ================================================================== slash
        void BeginSlash()
        {
            var m = Melee;
            if (m == null) return;
            Color core = m.slashColor, edge = m.slashEdgeColor;
            if (SpiritLevel > 0 && m.spiritLevelColors != null && m.spiritLevelColors.Length > 0)
            {
                var lc = m.spiritLevelColors[Mathf.Min(SpiritLevel, m.spiritLevelColors.Length) - 1];
                edge = new Color(lc.r, lc.g, lc.b, Mathf.Max(edge.a, 0.75f));
            }
            if (spiritedSwing) core = Color.Lerp(core, new Color(1f, 0.4f, 0.32f, core.a), 0.55f);
            if (slashArc) slashArc.Begin(core, edge);
            if (trail)
            {
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(core, 0f), new GradientColorKey(edge, 1f) },
                          new[] { new GradientAlphaKey(core.a, 0f), new GradientAlphaKey(0f, 1f) });
                trail.colour = g;
            }
        }

        // ================================================================== spirit gauge (long sword)
        /// <summary>0..100.</summary>
        public float Spirit { get; private set; }
        /// <summary>0 = none, 1 white, 2 yellow, 3 red.</summary>
        public int SpiritLevel { get; private set; }
        public bool HasSpiritGauge => Melee != null && Melee.spiritGauge;
        public float SpiritLevelTime01 => Melee != null && SpiritLevel > 0 ? Mathf.Clamp01(spiritLevelLeft / Mathf.Max(1f, Melee.spiritLevelDuration)) : 0f;
        public event System.Action<int> SpiritLevelChanged;
        public event System.Action SpiritSpent;

        float spiritLevelLeft, lastSpiritGainAt = -99f;
        bool spiritedSwing, spiritLevelledThisSwing;

        float SpiritDamageMultiplier
        {
            get
            {
                var m = Melee;
                if (m == null || !m.spiritGauge) return 1f;
                float k = 1f;
                if (SpiritLevel > 0 && m.spiritLevelDamage != null && m.spiritLevelDamage.Length > 0)
                    k = m.spiritLevelDamage[Mathf.Min(SpiritLevel, m.spiritLevelDamage.Length) - 1];
                if (spiritedSwing) k *= m.spiritDamageBonus;
                return k;
            }
        }

        bool TrySpendSpirit()
        {
            spiritLevelledThisSwing = false;
            var m = Melee;
            if (m == null || !m.spiritGauge || Spirit < m.spiritAttackCost) return false;
            Spirit -= m.spiritAttackCost;
            SpiritSpent?.Invoke();
            // the blade flares as the spirit is drawn into it
            AudioManager.Play(Sfx.BladeRing, transform.position + Vector3.up * 1.2f, 0.8f, 1.15f);
            return true;
        }

        void GainSpirit(AttackData a, bool heavy)
        {
            var m = Melee;
            if (m == null || !m.spiritGauge) return;
            Spirit = Mathf.Min(100f, Spirit + (heavy ? m.spiritPerHeavyHit : m.spiritPerHit));
            lastSpiritGainAt = Time.time;
            if (a != null && a.levelsSpirit && spiritedSwing && !spiritLevelledThisSwing)
            {
                spiritLevelledThisSwing = true;
                LevelUpSpirit();
            }
        }

        void LevelUpSpirit()
        {
            var m = Melee;
            int max = m.spiritLevelColors != null && m.spiritLevelColors.Length > 0 ? m.spiritLevelColors.Length : 3;
            SpiritLevel = Mathf.Min(max, SpiritLevel + 1);
            spiritLevelLeft = m.spiritLevelDuration;
            Vector3 p = clubTip != null ? clubTip.position : transform.position + Vector3.up * 1.4f;
            FXManager.Spawn(FXType.ChargeFlash, p, Quaternion.identity, 1.3f);
            AudioManager.Play(Sfx.BladeRing, p, 1f, 0.85f);
            AudioManager.Play(Sfx.BladeWhoosh, p, 0.7f, 1.1f);
            CameraDirector.Instance?.FovKick(-2.5f);
            input.Rumble(0.4f, 0.7f, 0.15f);
            SpiritLevelChanged?.Invoke(SpiritLevel);
        }

        void UpdateSpirit(float dt)
        {
            var m = Melee;
            if (m == null || !m.spiritGauge) return;
            if (Time.time - lastSpiritGainAt > 3f && State != PlayerState.Attack)
                Spirit = Mathf.MoveTowards(Spirit, 0f, m.spiritDrainPerSec * dt);
            if (SpiritLevel > 0)
            {
                spiritLevelLeft -= dt;
                if (spiritLevelLeft <= 0f)
                {
                    SpiritLevel--;
                    spiritLevelLeft = SpiritLevel > 0 ? m.spiritLevelDuration : 0f;
                    SpiritLevelChanged?.Invoke(SpiritLevel);
                }
            }
        }

        void ResetSpirit()
        {
            Spirit = 0f;
            spiritLevelLeft = 0f;
            spiritedSwing = false;
            if (SpiritLevel != 0) { SpiritLevel = 0; SpiritLevelChanged?.Invoke(0); }
        }

        /// <summary>Debug / autopilot: fill the gauge.</summary>
        public void DevFillSpirit(int level = -1)
        {
            Spirit = 100f;
            lastSpiritGainAt = Time.time;
            if (level >= 0 && Melee != null) { SpiritLevel = level; spiritLevelLeft = Melee.spiritLevelDuration; SpiritLevelChanged?.Invoke(level); }
        }

        // ================================================================== crossbow
        float CrossbowReloadSpeed() =>
            Mathf.Clamp(crossbowReloadClipLength / Mathf.Max(0.3f, Crossbow.reloadTime - CrossbowReloadDelay - 0.1f), 0.5f, 4f);

        void FireCrossbow()
        {
            var cb = Crossbow;
            fireQueued = false;
            fireQueuedAt = -9f;
            if (!stats.TrySpendMana(cb.manaPerShot))
            {
                AudioManager.Play2D(Sfx.ManaEmpty, 0.8f);
                input.Rumble(0.02f, 0.1f, 0.05f);
                return;
            }
            cannonReadyAt = Time.time + cb.reloadTime;
            cannonShootAt = Time.time;
            crossbowReloadPlaying = false;
            Vector3 muzzle = cannonRig.MuzzleWorld;
            Vector3 target = ProjectileTarget(muzzle, cb.boltSpeed, cb.leadFactor);
            Vector3 vel = SolveLaunch(muzzle, target, cb.boltSpeed, cb.boltGravity);
            var bolt = Instantiate(boltPrefab);
            Vector3 start = muzzle - vel.normalized * bolt.length;
            bolt.Launch(start, vel, cb.boltGravity, cb.damage, cb.poise, 1f, true, transform);
            ArrowsLoosed++;
            ArrowFired?.Invoke(bolt);

            Quaternion look = Quaternion.LookRotation(vel);
            FXManager.Spawn(FXType.DustSmall, muzzle, look, 0.35f);
            AudioManager.Play(Sfx.CrossbowFire, muzzle, 1f, Random.Range(0.96f, 1.04f));
            CameraDirector.Instance?.Shake(cb.shake, -vel.normalized);
            CameraDirector.Instance?.FovKick(cb.fovKick);
            input.Rumble(0.45f, 0.6f, 0.12f);
            Vector3 back = -vel; back.y = 0f;
            if (back.sqrMagnitude > 1e-3f) motor.SetPlanarVelocityImmediate(back.normalized * cb.recoil);
            cannonRig.Kick();
            animator.SetFloat(HActionSpeed, 1f);
            animator.CrossFadeInFixedTime("CrossbowShoot", 0.03f);
        }

        void UpdateCrossbowReloadAnim()
        {
            float since = Time.time - cannonShootAt;
            if (!crossbowReloadPlaying && since >= CrossbowReloadDelay && Time.time < cannonReadyAt - 0.12f)
            {
                crossbowReloadPlaying = true;
                animator.SetFloat(HActionSpeed, CrossbowReloadSpeed());
                animator.CrossFadeInFixedTime("CrossbowReload", 0.15f);
                AudioManager.Play(Sfx.CrossbowReload, cannonRig.MuzzleWorld, 0.8f, Mathf.Lerp(1.2f, 0.9f, Mathf.InverseLerp(0.8f, 2f, Crossbow.reloadTime)));
            }
            if (crossbowReloadPlaying && Time.time >= cannonReadyAt - 0.12f)
            {
                crossbowReloadPlaying = false;
                animator.SetFloat(HActionSpeed, 1f);
                animator.CrossFadeInFixedTime("CrossbowAim", 0.18f);
            }
        }

        // ================================================================== special: Shark Tornado (spin slash)
        enum SpinPhase { Coil, Spin, Recover }
        SpinSlashData spinData;
        bool spinMode;
        SpinPhase spinPhase;
        float spinT, spinStartYaw, spinAngle;
        int spinPass, spinPose = -1;
        float spinLift, spinLean;
        readonly HashSet<IDamageable> spinHits = new();
        static readonly Collider[] spinCols = new Collider[48];

        public bool Spinning => State == PlayerState.Special && spinMode;

        bool TryStartSpin(SpinSlashData s)
        {
            if (weapon == null) return false;
            if (!stats.HasSpecialCharge)
            {
                stats.TrySpendSpecialCharge();   // raises SpecialDenied for the HUD
                AudioManager.Play2D(Sfx.ManaEmpty, 0.8f);
                return false;
            }
            if (!stats.TrySpendStamina(s.staminaCost)) return false;
            stats.TrySpendSpecialCharge();
            EndSwing();
            if (State == PlayerState.Aim) ExitAim();
            spinData = s;
            spinMode = true;
            SetState(PlayerState.Special);
            IsSprinting = false;
            spinPhase = SpinPhase.Coil;
            spinT = 0f;
            spinPass = 0;
            spinPose = -1;
            spinAngle = 0f;
            spinLift = spinLean = 0f;
            spiritedSwing = false;
            if (lockOn.Current != null) FaceInstant(lockOn.Current.transform.position - transform.position);
            else if (moveInputMag > 0.2f) FaceInstant(moveInputWorld);
            motor.SetPlanarVelocityImmediate(Vector3.zero);
            // coil: the sweep's wind-up and opening cut play out and arrive on the level-blade frame as the spin starts
            animator.SetFloat(HActionSpeed, Mathf.Clamp(spinPoseNorm * spinClipLength / Mathf.Max(0.05f, s.windupTime), 0.05f, 3f));
            animator.CrossFadeInFixedTime("Sword_Spin", 0.1f, 0, 0f);
            AudioManager.Play(Sfx.BladeRing, transform.position + Vector3.up, 1f, 1f);
            AudioManager.Play(Sfx.SneakerSqueak, transform.position, 0.8f, 1.1f);
            FXManager.Spawn(FXType.DustSmall, transform.position, Quaternion.identity, 1.2f);
            input.Rumble(0.15f, 0.25f, s.windupTime);
            squash = 0.1f;
            return true;
        }

        void UpdateSpin(float dt)
        {
            var s = spinData;
            spinT += dt;
            switch (spinPhase)
            {
                case SpinPhase.Coil:
                    motor.SetDesiredPlanarVelocity(Vector3.zero, 14f, dt);
                    if (lockOn.Current != null) FaceTowards(lockOn.Current.transform.position, 720f, dt);
                    if (spinT >= s.windupTime)
                    {
                        spinPhase = SpinPhase.Spin;
                        spinT = 0f;
                        spinStartYaw = transform.eulerAngles.y;
                        // hold the pose with the blade swept out wide; the body does the turning
                        // (a cross-fade into the state already playing is ignored, so pin the frame directly)
                        animator.SetFloat(HActionSpeed, 0f);
                        animator.Play("Sword_Spin", 0, spinPoseNorm);
                        spinPose = 0;
                        if (trail) trail.Emitting = true;
                        BeginSlash();
                        CameraDirector.Instance?.FovKick(s.fovKick);
                        AudioManager.Play(Sfx.SpinWhirl, transform.position + Vector3.up, 1f, 0.9f);
                        AudioManager.Play(Sfx.BladeWhoosh, transform.position + Vector3.up, 0.9f, 0.85f);
                        FXManager.Spawn(FXType.Dust, transform.position, Quaternion.identity, 1.1f);
                    }
                    break;
                case SpinPhase.Spin:
                {
                    // fast from the first instant, a touch faster still mid-spin, easing out into the finisher
                    float u = Mathf.Clamp01(spinT / Mathf.Max(0.1f, s.spinTime));
                    float shaped = Mathf.Lerp(u, Ease.InOutCubic(u), 0.35f);
                    float total = 360f * Mathf.Max(1, s.turns);
                    spinAngle = shaped * total;
                    transform.rotation = Quaternion.Euler(0f, spinStartYaw + spinDirection * spinAngle, 0f);

                    // drift: toward the lock target (stopping short of it) or with the stick
                    Vector3 drift = Vector3.zero;
                    if (lockOn.Current != null)
                    {
                        Vector3 to = lockOn.Current.transform.position - transform.position; to.y = 0f;
                        if (to.magnitude > 1.6f) drift = to.normalized;
                    }
                    else if (moveInputMag > 0.2f) drift = moveInputWorld.normalized;
                    motor.SetDesiredPlanarVelocity(drift * s.driftSpeed, s.driftSharpness, dt);

                    UpdateSpinPose(spinAngle / 360f, s);

                    int passes = Mathf.FloorToInt(spinAngle / 360f + 0.001f);
                    while (spinPass < passes)
                    {
                        spinPass++;
                        bool last = spinPass >= s.turns;
                        if (last) SpinSweep(s.finisherRadius, s.finisherDamage, s.finisherPoise, true);
                        else SpinSweep(s.radius, s.damagePerPass, s.poisePerPass, false);
                        // every turn leaves its own ring of slash in the air
                        if (slashArc) slashArc.End();
                        if (!last) BeginSlash();
                    }
                    if (u >= 1f)
                    {
                        spinPhase = SpinPhase.Recover;
                        spinT = 0f;
                        if (trail) trail.Emitting = false;
                        if (slashArc) slashArc.End();
                        // follow-through: the rest of the sweep plays out
                        animator.SetFloat(HActionSpeed, 1.1f);
                    }
                    break;
                }
                case SpinPhase.Recover:
                    motor.SetDesiredPlanarVelocity(Vector3.zero, 10f, dt);
                    spinLift = Mathf.MoveTowards(spinLift, 0f, dt * 3f);
                    spinLean = Mathf.MoveTowards(spinLean, 0f, dt * 40f);
                    if (spinT >= s.recoverTime)
                    {
                        spinMode = false;
                        ReturnToLocomotion(0.25f);
                    }
                    break;
            }
        }

        /// <summary>
        /// The tornado changes shape as it turns: each turn holds its own pose (blade low, at the waist, overhead), the
        /// body rises off the ground and leans into the spin, then crashes back down for the finisher.
        /// </summary>
        void UpdateSpinPose(float turns, SpinSlashData s)
        {
            if (spinTurns == null || spinTurns.Length == 0) return;
            int n = Mathf.Max(1, s.turns);
            int turn = Mathf.Clamp(Mathf.FloorToInt(turns), 0, n - 1);
            // the last configured entry is the landing; middle turns walk the list (repeating the climb if there are more)
            int idx = turn == n - 1 ? spinTurns.Length - 1 : Mathf.Min(turn, spinTurns.Length - 2);
            var cur = spinTurns[Mathf.Max(0, idx)];
            var prev = spinTurns[Mathf.Max(0, turn == 0 ? 0 : (turn - 1 == n - 1 ? spinTurns.Length - 1 : Mathf.Min(turn - 1, spinTurns.Length - 2)))];
            float f = Ease.InOutCubic(Mathf.Clamp01((turns - turn) / 0.45f));   // blend over the first part of each turn
            spinLift = Mathf.Lerp(turn == 0 ? 0f : prev.lift, cur.lift, f);
            spinLean = Mathf.Lerp(turn == 0 ? 0f : prev.lean, cur.lean, f);
            if (idx != spinPose)
            {
                bool landing = turn == n - 1 && n > 1;
                spinPose = idx;
                animator.CrossFadeInFixedTime(cur.state, landing ? 0.06f : 0.14f, 0, cur.norm);
                if (landing)
                {
                    squash = 0.16f;
                    FXManager.Spawn(FXType.LandDust, transform.position, Quaternion.identity, 1.2f);
                    AudioManager.Play(Sfx.Land, transform.position, 0.9f, 0.85f);
                }
                else if (cur.lift > 0.3f)
                {
                    AudioManager.Play(Sfx.Jump, transform.position, 0.7f, 1.05f);
                    FXManager.Spawn(FXType.DustSmall, transform.position, Quaternion.identity, 1.3f);
                }
            }
        }

        /// <summary>Spin special body offset (lift, lean), applied on the visual root.</summary>
        Vector3 SpinVisualLift => spinMode && State == PlayerState.Special ? Vector3.up * spinLift : Vector3.zero;
        float SpinVisualLean => spinMode && State == PlayerState.Special ? spinLean : 0f;

        /// <summary>One full turn of the spin: everything in reach is cut once (vines included).</summary>
        void SpinSweep(float radius, float damage, float poise, bool finisher)
        {
            var s = spinData;
            Vector3 c = transform.position + Vector3.up * (1.0f + spinLift);
            spinHits.Clear();
            int landed = 0;
            int n = Physics.OverlapSphereNonAlloc(c, radius, spinCols, hurtboxMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var col = spinCols[i];
                if (col.transform.IsChildOf(transform)) continue;
                var hb = col.GetComponent<Hurtbox>();
                var target = hb ? hb.Owner : col.GetComponentInParent<IDamageable>();
                if (target == null || !target.IsAlive || spinHits.Contains(target) || target.Root.IsChildOf(transform)) continue;
                spinHits.Add(target);
                bool cuttable = target is World.Cuttable;
                Vector3 dir = target.Root.position - transform.position; dir.y = 0f;
                dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward;
                bool critical = target is IStaggerable st && st.IsStaggered;
                var info = new DamageInfo
                {
                    damage = Mathf.Round(damage * SpiritDamageMultiplier * (critical ? 2f : 1f) * Random.Range(0.96f, 1.04f)),
                    poiseDamage = poise,
                    critical = critical,
                    point = cuttable ? c : col.ClosestPoint(c),
                    direction = dir,
                    weight = finisher ? ImpactWeight.Heavy : ImpactWeight.Light,
                    cutPower = s.cutPower,
                    cutWidth = radius * 2f,
                    source = gameObject
                };
                if (!target.ReceiveHit(info) || cuttable) continue;
                landed++;
                HitLanded?.Invoke(info, target);
                GainSpirit(null, false);
                Vector3 tangent = Vector3.Cross(Vector3.up, dir) * spinDirection;
                FXManager.Spawn(FXType.SlashHit, info.point, Quaternion.LookRotation(tangent, -dir), finisher ? 1.3f : 0.95f);
                AudioManager.Play(Sfx.BladeHit, info.point, 0.9f, finisher ? 0.88f : Random.Range(0.98f, 1.1f));
            }

            // the ring on the ground sells the reach of each turn
            FXManager.Spawn(FXType.SlamRing, transform.position + Vector3.up * 0.04f, Quaternion.identity, radius / 2f * (finisher ? 1.15f : 0.9f));
            FXManager.Spawn(FXType.DustSmall, transform.position, Quaternion.identity, finisher ? 1.6f : 1.1f);
            AudioManager.Play(Sfx.SpinWhirl, c, finisher ? 1f : 0.8f, finisher ? 0.85f : Random.Range(0.95f, 1.08f));
            CameraDirector.Instance?.Shake(finisher ? s.finisherShake : s.shakePerPass, transform.right);
            input.Rumble(finisher ? 0.8f : 0.3f, finisher ? 0.7f : 0.45f, finisher ? 0.2f : 0.08f);
            if (landed > 0)
            {
                TimeDirector.HitStop(finisher ? 0.1f : 0.035f);
                if (finisher) TimeDirector.SlowMo(0.6f, 0.3f);
            }
            if (finisher)
            {
                AudioManager.Play(Sfx.BladeWhoosh, c, 1f, 0.75f);
                CameraDirector.Instance?.FovKick(-3f);
            }
        }

        /// <summary>The spin was cut short (hit, death, cinematic): stop the blade effects.</summary>
        void AbortSpin()
        {
            if (!spinMode) return;
            spinMode = false;
            spinLift = spinLean = 0f;
            if (trail) trail.Emitting = false;
            if (slashArc) slashArc.End();
        }
    }
}
