using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.Combat;
using ElderTree.Equipment;
using ElderTree.FX;
using UnityEngine;

namespace ElderTree.Player
{
    /// <summary>
    /// Druid Mask (Druid reward): a caster. Magic replaces the melee weapon and an energy ball replaces the bow.
    ///  * Vine Seeker (light / RB), Elden Ring style: the druid slows to a walk while the spell gathers in the staff
    ///    (the cast time), then a bundle of writhing vines leaves the seed and homes onto the target. Where it hits,
    ///    leaves explode and vines burst out and squeeze. FP is spent when the spell leaves the staff; a roll during
    ///    the wind-up cancels it for free.
    ///  * Charged Vine Seeker (heavy / RT, hold): a longer gather that keeps charging while held: a thicker bundle,
    ///    more damage, a bigger burst.
    ///  * Verdant Orb (LT raises both hands, RT throws): a big ball of green energy gathers between the hands, then
    ///    flies slow and straight and bursts on impact. Slower to cycle than the bow.
    /// Spell numbers live in <see cref="SpellData"/>, the ball's in <see cref="OrbData"/> (the form's assets).
    /// </summary>
    public partial class PlayerController
    {
        [Header("Druid Mask")]
        public VineSeeker vineSeekerPrefab;
        public DruidOrb orbPrefab;
        [Tooltip("Release clip lengths (filled by the builder): light cast, charged cast, orb throw.")]
        public float castClipLength = 0.93f;
        public float chargeCastClipLength = 0.97f;
        public float orbCastClipLength = 1.33f;

        SpellData Spell => CurrentForm != null ? CurrentForm.magic : null;
        OrbData Orb => CurrentForm != null && CurrentForm.rangedKind == RangedKind.Orb ? CurrentForm.orb : null;
        /// <summary>This form casts spells instead of swinging a weapon.</summary>
        public bool IsCaster => Spell != null;
        public bool Casting => State == PlayerState.Cast;
        /// <summary>0..1 while a heavy cast charges (HUD / FX).</summary>
        public float CastCharge01 => Casting && castHeavy && Spell != null ? Mathf.Clamp01(castCharge / Mathf.Max(0.01f, Spell.chargeMaxTime)) : 0f;
        public event System.Action<SpellData> SpellCast;

        enum CastPhase { Gather, Release }
        CastPhase castPhase;
        bool castHeavy, castFired;
        float castT, castCharge;
        Targetable castTarget;

        // ================================================================== casting
        bool TryStartCast(bool heavy)
        {
            var s = Spell;
            if (s == null || !motor.Grounded) return false;
            // FP is spent on release, but a dry staff doesn't even start (the bar blinks, a dry click)
            if (!stats.HasMana(s.manaCost))
            {
                stats.TrySpendMana(s.manaCost);
                AudioManager.Play2D(Sfx.ManaEmpty, 0.8f);
                input.Rumble(0.02f, 0.1f, 0.05f);
                return false;
            }
            if (!stats.TrySpendStamina(s.staminaCost)) return false;
            EndSwing();
            if (State == PlayerState.Aim) ExitAim();
            SetState(PlayerState.Cast);
            IsSprinting = false;
            castHeavy = heavy;
            castPhase = CastPhase.Gather;
            castT = 0f;
            castCharge = 0f;
            castFired = false;
            UpdateAimPoint();
            castTarget = PickSpellTarget(s);
            if (castTarget != null) FaceInstant(castTarget.transform.position - transform.position);
            else FaceInstant(CameraFlatForward());
            animator.SetFloat(HActionSpeed, 1f);
            animator.CrossFadeInFixedTime(heavy ? "Druid_ChargeLoad" : "Druid_CastLoad", 0.12f);
            var rig = CurrentRig;
            if (rig != null && rig.spellGather) { rig.spellGather.Clear(true); rig.spellGather.Play(true); }
            StirLeaves(heavy ? 0.7f : 0.5f);
            AudioManager.Play(Sfx.SpellGather, SpellFocus, heavy ? 0.85f : 0.7f, heavy ? 0.85f : 1f);
            return true;
        }

        Vector3 SpellFocus => CurrentRig != null && CurrentRig.spellFocus != null ? CurrentRig.spellFocus.position : transform.position + Vector3.up * 1.6f + transform.forward * 0.4f;

        Vector3 CameraFlatForward()
        {
            Vector3 f = cam != null ? cam.transform.forward : transform.forward;
            f.y = 0f;
            return f.sqrMagnitude > 1e-4f ? f.normalized : transform.forward;
        }

        /// <summary>The lock-on target, else the foe nearest the aim within the seek cone (none: fly at the crosshair).</summary>
        Targetable PickSpellTarget(SpellData s)
        {
            if (lockOn != null && lockOn.Current != null && lockOn.Current.CanBeTargeted) return lockOn.Current;
            Vector3 fwd = cam != null ? cam.transform.forward : transform.forward;
            Vector3 eye = transform.position + Vector3.up * 1.5f;
            Targetable best = null;
            float bestScore = float.MaxValue;
            foreach (var t in Targetable.All)
            {
                if (t == null || !t.CanBeTargeted || t.transform.IsChildOf(transform)) continue;
                Vector3 to = t.LockPosition - eye;
                float d = to.magnitude;
                if (d > s.seekRange * Mathf.Max(1f, t.rangeScale) || d < 0.5f) continue;
                float ang = Vector3.Angle(fwd, to);
                if (ang > s.seekCone) continue;
                float score = ang * 0.08f + d * 0.04f;
                if (score < bestScore) { bestScore = score; best = t; }
            }
            return best;
        }

        void UpdateCast(float dt)
        {
            var s = Spell;
            if (s == null) { ReturnToLocomotion(0.2f); return; }
            castT += dt;

            // the caster slows right down: a walk, facing the target or the camera's aim
            Vector3 dir = moveInputWorld.sqrMagnitude > 1e-4f ? moveInputWorld.normalized : Vector3.zero;
            float speed = s.castMoveSpeed * Mathf.Clamp01(moveInputMag);
            motor.SetDesiredPlanarVelocity(dir * speed, speed > 0.01f ? groundSharpness : stopSharpness, dt);
            var face = castTarget != null && castTarget.CanBeTargeted ? castTarget.transform.position : transform.position + CameraFlatForward();
            if (castPhase == CastPhase.Gather || castT < 0.1f) FaceTowards(face, s.castTurnSpeed, dt);
            DriveLocomotionAnimator(dt);

            if (!motor.Grounded && Time.time - lastGroundedTime > coyoteTime) { EndCastFx(); EnterAir(false); return; }
            // a roll always wins (before the release nothing is spent)
            if (castPhase == CastPhase.Gather && input.HasBufferedDodge)
            {
                EndCastFx();
                if (!TryDodge()) ReturnToLocomotion(0.2f);
                return;
            }

            var rig = CurrentRig;
            if (castPhase == CastPhase.Gather)
            {
                float min = castHeavy ? s.chargeMinTime : s.castTime;
                bool charging = castHeavy && castT >= min && input.HeavyHeld && castCharge < s.chargeMaxTime;
                if (charging)
                {
                    castCharge += dt;
                    float c = CastCharge01;
                    input.Rumble(0.05f + 0.25f * c, 0.1f + 0.3f * c, dt * 1.5f);
                    StirLeaves(0.6f + 0.5f * c);
                    squash = Mathf.Max(squash, 0.02f * c);
                }
                if (rig != null && rig.focusLight)
                    rig.focusLight.intensity = Mathf.Lerp(0.4f, 2.6f, Mathf.Clamp01(castT / Mathf.Max(0.05f, min))) * (1f + CastCharge01 * 0.8f);
                if (castT >= min && !charging)
                {
                    castPhase = CastPhase.Release;
                    castT = 0f;
                    animator.CrossFadeInFixedTime(castHeavy ? "Druid_ChargeCast" : "Druid_Cast", 0.07f);
                }
                return;
            }

            // release: the vines leave the staff at releaseAt, control returns at recoverAt
            float len = castHeavy ? chargeCastClipLength : castClipLength;
            if (!castFired && castT >= s.releaseAt * len) FireSpell(s);
            if (rig != null && rig.focusLight) rig.focusLight.intensity = Mathf.MoveTowards(rig.focusLight.intensity, 0.4f, dt * 8f);
            if (castT >= s.recoverAt * len)
            {
                // chain straight into the next cast, or step out of the follow-through
                if (input.HasBufferedLight) { input.ConsumeLight(); if (TryStartCast(false)) return; }
                if (input.HasBufferedHeavy) { input.ConsumeHeavy(); if (TryStartCast(true)) return; }
                if (input.HasBufferedDodge) { EndCastFx(); if (TryDodge()) return; }
                if (moveInputMag > 0.2f || castT >= len * 0.92f)
                {
                    EndCastFx();
                    ReturnToLocomotion(0.25f);
                }
            }
        }

        void FireSpell(SpellData s)
        {
            castFired = true;
            var rig = CurrentRig;
            if (rig != null && rig.spellGather) rig.spellGather.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            float charge = castHeavy ? Mathf.Clamp01(castCharge / Mathf.Max(0.01f, s.chargeMaxTime)) : 0f;
            float cost = castHeavy ? Mathf.Lerp(s.manaCost * 1.4f, s.chargedManaCost, charge) : s.manaCost;
            Vector3 from = SpellFocus;
            if (!stats.TrySpendMana(cost))
            {
                // the spell fizzles out of the staff in a puff of leaves
                AudioManager.Play2D(Sfx.ManaEmpty, 0.8f);
                FXManager.Spawn(FXType.LeafFall, from, Quaternion.identity, 0.6f);
                return;
            }
            if (castTarget == null || !castTarget.CanBeTargeted) castTarget = PickSpellTarget(s);
            UpdateAimPoint();
            Vector3 aim = castTarget != null ? castTarget.LockPosition : AimPoint;
            Vector3 dir = aim - from;
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            dir.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            if (right.sqrMagnitude > 1e-4f) dir = Quaternion.AngleAxis(-s.launchPitch, right.normalized) * dir;

            float power = castHeavy ? 0.35f + 0.65f * charge : 0f;
            int strands = castHeavy ? Mathf.RoundToInt(Mathf.Lerp(s.strands + 1, s.chargedStrands, charge)) : s.strands;
            float dmgMul = castHeavy ? Mathf.Lerp(1.3f, s.chargeDamageMultiplier, charge) : 1f;
            if (vineSeekerPrefab != null)
            {
                var v = Instantiate(vineSeekerPrefab);
                v.Launch(from, dir, s, power, strands, dmgMul, castTarget, transform);
            }
            FXManager.Spawn(FXType.LeafFall, from, Quaternion.LookRotation(dir), 0.6f + 0.5f * power);
            AudioManager.Play(Sfx.VineCast, from, 1f, castHeavy ? 0.88f : Random.Range(0.97f, 1.07f));
            CameraDirector.Instance?.Shake(0.08f + 0.18f * power, -dir);
            CameraDirector.Instance?.FovKick(1.2f + 2f * power);
            input.Rumble(0.15f + 0.4f * power, 0.3f + 0.4f * power, 0.1f);
            StirLeaves(1f);
            SpellCast?.Invoke(s);
        }

        /// <summary>Stir the leaf cover (no-op for forms without one).</summary>
        void StirLeaves(float amount)
        {
            var rig = CurrentRig;
            if (rig != null && rig.leafSway != null) rig.leafSway.Agitate(amount);
        }

        /// <summary>Stops the gather glow (any way the cast ends: release, roll, hit, form swap).</summary>
        void EndCastFx()
        {
            var rig = CurrentRig;
            if (rig == null) return;
            if (rig.spellGather) rig.spellGather.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (rig.focusLight) rig.focusLight.intensity = 0.4f;
        }

        // ================================================================== Verdant Orb (ranged)
        bool orbMode;
        float orbReadyAt, orbShotAt = -9f;
        bool orbWasReady;
        DruidOrb heldOrb;
        public bool OrbRaised => State == PlayerState.Aim && orbMode;

        bool TryEnterOrbAim()
        {
            var o = Orb;
            if (!input.GameplayEnabled || !input.AimHeld || orbPrefab == null || o == null) return false;
            EndSwing();
            SetState(PlayerState.Aim);
            orbMode = true;
            cannonMode = false;
            IsSprinting = false;
            BowPhaseProgress = 0f;
            BowDraw = 0f;
            fireQueued = false;
            fireQueuedAt = -9f;
            orbWasReady = false;
            input.ClearFire();
            input.ClearAttackBuffers();
            animator.SetFloat(HActionSpeed, 1f);
            animator.CrossFadeInFixedTime("Druid_OrbAim", 0.2f);
            AudioManager.Play(Sfx.OrbCharge, transform.position + Vector3.up * 1.4f, 0.55f, 1f);
            StirLeaves(0.4f);
            UpdateAimPoint();
            return true;
        }

        void UpdateOrbAim(float dt)
        {
            var o = Orb;
            if (o == null) { ExitAim(); return; }
            if (!AimCommon(dt, o.aimMoveSpeed, 0f)) return;
            if (fireQueued && fireQueuedAt < 0f) fireQueuedAt = Time.time;
            BowPhaseProgress = Mathf.MoveTowards(BowPhaseProgress, 1f, dt / Mathf.Max(0.01f, o.raiseTime));
            float ready = Time.time >= orbReadyAt ? 1f : 1f - (orbReadyAt - Time.time) / Mathf.Max(0.01f, o.fireInterval);
            BowDraw = BowPhaseProgress >= 1f ? Mathf.Clamp01(ready) : 0f;
            bool isReady = BowDraw >= 0.999f;
            if (heldOrb == null && BowPhaseProgress > 0.05f && Time.time > orbShotAt + 0.2f)
            {
                heldOrb = Instantiate(orbPrefab);
                heldOrb.Hold(0f);
                AudioManager.Play(Sfx.OrbCharge, HandsMid(), 0.5f, 1.1f);
            }
            if (isReady && !orbWasReady)
            {
                FullDrawReached?.Invoke();
                input.Rumble(0.04f, 0.14f, 0.06f);
            }
            orbWasReady = isReady;
            if (fireQueued && !isReady && Time.time - fireQueuedAt > 0.35f) { fireQueued = false; fireQueuedAt = -9f; }
            if (fireQueued && isReady) FireOrb(o);
            if (Time.time - orbShotAt > 0.6f && Time.time - orbShotAt < 0.65f + dt) animator.CrossFadeInFixedTime("Druid_OrbAim", 0.3f);
        }

        /// <summary>Between the palms, a little in front: where the ball gathers.</summary>
        Vector3 HandsMid()
        {
            var a = animator;
            var l = a != null ? a.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            var r = a != null ? a.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (l == null || r == null) return transform.position + Vector3.up * 1.3f + transform.forward * 0.5f;
            return (l.position + r.position) * 0.5f + transform.forward * 0.22f;
        }

        /// <summary>LateUpdate (posed bones): the gathering ball sits between the hands and grows with readiness.</summary>
        void UpdateHeldOrb()
        {
            if (heldOrb == null) return;
            if (!OrbRaised) { DropHeldOrb(); return; }
            heldOrb.transform.position = HandsMid();
            heldOrb.Hold(BowPhaseProgress * Mathf.Max(0.15f, BowDraw));
        }

        void DropHeldOrb()
        {
            if (heldOrb == null) return;
            heldOrb.Dissipate();
            heldOrb = null;
        }

        void FireOrb(OrbData o)
        {
            fireQueued = false;
            fireQueuedAt = -9f;
            if (!stats.TrySpendMana(o.manaPerShot))
            {
                AudioManager.Play2D(Sfx.ManaEmpty, 0.8f);
                input.Rumble(0.02f, 0.1f, 0.05f);
                return;
            }
            orbReadyAt = Time.time + o.fireInterval;
            orbShotAt = Time.time;
            Vector3 from = heldOrb != null ? heldOrb.transform.position : HandsMid();
            Vector3 target = ProjectileTarget(from, o.speed, o.leadFactor);
            Vector3 vel = SolveLaunch(from, target, o.speed, o.gravity);
            var orb = heldOrb != null ? heldOrb : Instantiate(orbPrefab);
            heldOrb = null;
            orb.Launch(from, vel, o, transform);
            ArrowsLoosed++;

            AudioManager.Play(Sfx.OrbFire, from, 1f, Random.Range(0.95f, 1.05f));
            CameraDirector.Instance?.Shake(o.shake, -vel.normalized);
            CameraDirector.Instance?.FovKick(o.fovKick);
            input.Rumble(0.4f, 0.5f, 0.14f);
            Vector3 back = -vel; back.y = 0f;
            if (back.sqrMagnitude > 1e-3f) motor.SetPlanarVelocityImmediate(back.normalized * o.recoil);
            StirLeaves(0.9f);
            animator.CrossFadeInFixedTime("Druid_OrbCast", 0.05f);
        }

        // ================================================================== special: Wrath of the Grove
        // Light + heavy: roar and swell to a giant, stomp one foot then the other (each stomp sends a ring of vines
        // rising out of the dirt from the foot, spreading outward), then shrink back. Hyper armour (Special state).
        [Tooltip("Ring of rising vines a giant stomp sends out (Druid Mask special).")]
        public VineRing vineRingPrefab;
        [Tooltip("Normalized frame of the stomp-raise clip (AttackKick02) where the knee is up: the pose held before the slam.")]
        public float stompChamberNorm = 0.3f;
        [Tooltip("Stomp-raise clip length (AttackKick02_R), filled by the builder.")]
        public float stompRaiseClipLength = 0.8f;

        enum GrowPhase { Grow, Raise, Slam, Hold, Shrink }
        GrowPhase growPhase;
        GrowthStompData grow;
        bool growMode, growChambered;
        float growT, growNow = 1f, growFrom = 1f;
        int stompIndex;

        /// <summary>The druid's size right now (1 = normal; the special swells it).</summary>
        public float GrowScale => growNow;
        public bool Giant => growMode && State == PlayerState.Special;

        bool TryStartGrowth(GrowthStompData s)
        {
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
            grow = s;
            spinMode = false;
            growMode = true;
            SetState(PlayerState.Special);
            IsSprinting = false;
            growPhase = GrowPhase.Grow;
            growT = 0f;
            stompIndex = 0;
            growFrom = growNow;
            motor.SetPlanarVelocityImmediate(Vector3.zero);
            if (lockOn.Current != null) FaceInstant(lockOn.Current.transform.position - transform.position);
            else if (moveInputMag > 0.2f) FaceInstant(moveInputWorld);
            animator.SetFloat(HActionSpeed, 1.1f);
            animator.CrossFadeInFixedTime("MaskRoar", 0.12f, 0, 0.15f);
            Vector3 c = transform.position + Vector3.up * 1.2f;
            AudioManager.Play(Sfx.WoodGroan, c, 1f, 0.75f);
            AudioManager.Play(Sfx.SpellGather, c, 0.8f, 0.7f);
            FXManager.Spawn(FXType.LeafExplosion, c, Quaternion.LookRotation(Vector3.up), 1.4f);
            FXManager.Spawn(FXType.Dust, transform.position, Quaternion.identity, 1.6f);
            StirLeaves(1.2f);
            input.Rumble(0.3f, 0.4f, s.growTime);
            CameraDirector.Instance?.FocusOn(transform.position + Vector3.up * 2f, s.cameraZoomOut, s.cameraPitch, 0f);
            return true;
        }

        void UpdateGrowth(float dt)
        {
            var s = grow;
            growT += dt;
            motor.SetDesiredPlanarVelocity(Vector3.zero, 12f, dt);
            if (lockOn.Current != null && growPhase != GrowPhase.Slam) FaceTowards(lockOn.Current.transform.position, 240f, dt);
            CameraDirector.Instance?.FocusOn(transform.position + Vector3.up * (1f + growNow), s.cameraZoomOut * Mathf.InverseLerp(1f, s.growScale, Mathf.Max(1.001f, growNow)) + 0.5f, s.cameraPitch, 0f);
            switch (growPhase)
            {
                case GrowPhase.Grow:
                {
                    // swell up with a little overshoot, the body creaking and the leaves thrashing
                    float k = Mathf.Clamp01(growT / Mathf.Max(0.05f, s.growTime));
                    growNow = Mathf.LerpUnclamped(growFrom, s.growScale, Ease.OutBack(k));
                    if (growT < s.growTime && Random.value < dt * 6f)
                        FXManager.Spawn(FXType.LeafFall, transform.position + Vector3.up * growNow * 1.2f + Random.insideUnitSphere * 0.5f * growNow, Quaternion.identity, 0.7f * growNow);
                    if (k >= 1f) StartStompRaise();
                    break;
                }
                case GrowPhase.Raise:
                    // the knee comes up to the chamber and holds there for a beat
                    if (!growChambered && growT >= s.raiseTime * 0.65f) { growChambered = true; animator.SetFloat(HActionSpeed, 0f); }
                    if (growT >= s.raiseTime) StompSlam();
                    break;
                case GrowPhase.Slam:
                    if (growT >= s.stompRecover)
                    {
                        if (++stompIndex < Mathf.Max(1, s.stomps)) StartStompRaise();
                        else { growPhase = GrowPhase.Hold; growT = 0f; }
                    }
                    break;
                case GrowPhase.Hold:
                    if (growT >= s.holdAfter)
                    {
                        growPhase = GrowPhase.Shrink;
                        growT = 0f;
                        growFrom = growNow;
                        animator.SetFloat(HActionSpeed, 1f);
                        animator.CrossFadeInFixedTime("Locomotion", 0.3f);
                        AudioManager.Play(Sfx.WoodGroan, transform.position + Vector3.up, 0.6f, 1.2f);
                        StirLeaves(0.8f);
                    }
                    break;
                case GrowPhase.Shrink:
                {
                    float k = Mathf.Clamp01(growT / Mathf.Max(0.05f, s.shrinkTime));
                    growNow = Mathf.Lerp(growFrom, 1f, Ease.InOutCubic(k));
                    if (k >= 1f)
                    {
                        growNow = 1f;
                        growMode = false;
                        CameraDirector.Instance?.ReleaseFocus(0.1f);
                        ReturnToLocomotion(0.2f);
                    }
                    break;
                }
            }
        }

        void StartStompRaise()
        {
            growPhase = GrowPhase.Raise;
            growT = 0f;
            growChambered = false;
            bool right = stompIndex % 2 == 0;
            const float from = 0.1f;
            // play the kick's lift from 0.1 to the chamber over most of the raise time, then pin it
            float span = Mathf.Max(0.01f, (stompChamberNorm - from) * stompRaiseClipLength);
            animator.SetFloat(HActionSpeed, span / Mathf.Max(0.05f, grow.raiseTime * 0.65f));
            animator.CrossFadeInFixedTime(right ? "Druid_StompRaise_R" : "Druid_StompRaise_L", 0.12f, 0, from * stompRaiseClipLength);
            AudioManager.Play(Sfx.WhooshHeavy, transform.position + Vector3.up * growNow, 0.8f, 0.6f);
            AudioManager.Play(Sfx.RootSnap, transform.position, 0.6f, 0.7f);
            StirLeaves(0.9f);
        }

        void StompSlam()
        {
            var s = grow;
            growPhase = GrowPhase.Slam;
            growT = 0f;
            animator.SetFloat(HActionSpeed, 1f);
            animator.CrossFadeInFixedTime("Land", 0.05f, 0, 0.12f);
            bool right = stompIndex % 2 == 0;
            var footBone = animator.GetBoneTransform(right ? HumanBodyBones.RightFoot : HumanBodyBones.LeftFoot);
            Vector3 foot = footBone != null ? footBone.position : transform.position;
            // the foot comes down a stride from the body: the ring starts under it
            Vector3 at = transform.position + Vector3.ProjectOnPlane(foot - transform.position, Vector3.up) * 0.6f;
            at = Explosions.Ground(at, 2f, 4f);
            bool last = stompIndex >= Mathf.Max(1, s.stomps) - 1;
            if (vineRingPrefab != null)
            {
                var ring = Instantiate(vineRingPrefab);
                ring.Begin(at, s, last ? s.lastStompMultiplier : 1f, transform);
            }
            float k = last ? 1.25f : 1f;
            FXManager.Spawn(FXType.SlamRing, at + Vector3.up * 0.05f, Quaternion.identity, 2.4f * k);
            FXManager.Spawn(FXType.Dust, at, Quaternion.identity, 2.2f * k);
            FXManager.Spawn(FXType.SlamDebris, at, Quaternion.identity, 1.2f * k);
            FXManager.Spawn(FXType.LeafExplosion, at + Vector3.up * 0.3f, Quaternion.LookRotation(Vector3.up), 1.6f * k);
            AudioManager.Play(Sfx.Slam, at, 1f, 0.6f);
            AudioManager.Play(Sfx.DeepRumble, at, 1f, 1f);
            AudioManager.Play(Sfx.VineBurst, at, 1f, 0.75f);
            CameraDirector.Instance?.Shake(s.shake * k, Vector3.down);
            CameraDirector.Instance?.FovKick(-3f * k);
            TimeDirector.HitStop(0.06f * k);
            input.Rumble(0.9f, 0.8f, 0.3f);
            squash = 0.16f;
            StirLeaves(1.3f);
        }

        /// <summary>A special cut short (hit, death, swap): the druid shrinks back on its own (see UpdateGrowScale).</summary>
        void AbortGrowth()
        {
            growMode = false;
            CameraDirector.Instance?.ReleaseFocus(0.1f);
        }

        /// <summary>Every frame: out of the special, any leftover size eases back to normal.</summary>
        void UpdateGrowScale(float dt)
        {
            if (!growMode && growNow != 1f) growNow = Mathf.MoveTowards(growNow, 1f, dt * 3f);
        }
    }
}
