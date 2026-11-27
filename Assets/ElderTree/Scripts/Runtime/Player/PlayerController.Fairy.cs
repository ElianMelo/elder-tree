using System.Collections.Generic;
using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.Combat;
using ElderTree.Enemies;
using ElderTree.Equipment;
using ElderTree.FX;
using ElderTree.Masks;
using UnityEngine;

namespace ElderTree.Player
{
    /// <summary>
    /// The Fairy Mask form:
    ///  * Twin white daggers: a dagger in each hand. Each swing says which blade is live (AttackData.hand); a blow that
    ///    lands with both blades counts once.
    ///  * Moon Chakram (LT raises it, RT throws): the pink crescent spins out like a boomerang and comes back to the
    ///    hand, cutting on the way out and on the way back. One chakram, so it must be caught before the next throw;
    ///    the daggers work while it is out. While it is in the right hand, the right dagger rides on the hip.
    ///  * Moonfall (light + heavy): she rises on her wings, raises a hand, and a huge moon falls out of the sky onto the
    ///    target: a wide blast where it lands.
    /// </summary>
    public partial class PlayerController
    {
        [Header("Fairy (mask form)")]
        public Chakram chakramPrefab;
        [Tooltip("Throw clip (ThrowBoomerang01_R) length and the normalized frame where the chakram leaves the hand.")]
        public float chakramThrowClipLength = 1.2f;
        public float chakramReleaseNorm = 0.45f;
        public float chakramCatchClipLength = 0.8f;

        // ================================================================== twin daggers
        WeaponHitbox offWeapon;
        WeaponTrail offTrail;
        SlashArc offSlash;

        bool HitboxActive => (weapon != null && weapon.Active) || (offWeapon != null && offWeapon.Active);

        void BeginHitboxes(AttackData a)
        {
            var hand = offWeapon != null ? a.hand : WeaponHand.Main;
            weapon?.ClearHits();
            offWeapon?.ClearHits();
            if (hand != WeaponHand.Off && weapon) { weapon.Begin(); if (trail) trail.Emitting = true; }
            if (hand != WeaponHand.Main && offWeapon) { offWeapon.Begin(); if (offTrail) offTrail.Emitting = true; }
        }

        void EndHitboxes()
        {
            if (weapon) weapon.End();
            if (trail) trail.Emitting = false;
            if (slashArc) slashArc.End();
            if (offWeapon) offWeapon.End();
            if (offTrail) offTrail.Emitting = false;
            if (offSlash) offSlash.End();
        }

        /// <summary>A target struck by both blades in one swing takes the blow once.</summary>
        void OnMainBladeHit(IDamageable target, Collider col, Vector3 point)
        {
            if (offWeapon != null) { if (offWeapon.HasHit(target)) return; offWeapon.MarkHit(target); }
            OnWeaponHit(target, col, point);
        }

        void OnOffBladeHit(IDamageable target, Collider col, Vector3 point)
        {
            if (weapon != null) { if (weapon.HasHit(target)) return; weapon.MarkHit(target); }
            OnWeaponHit(target, col, point);
        }

        // ================================================================== Moon Chakram (ranged)
        ChakramData ChakramWeapon => CurrentForm != null && CurrentForm.rangedKind == RangedKind.Chakram ? CurrentForm.chakram : null;
        bool chakramMode, chakramThrowing, chakramReleased, chakramCatching;
        float chakramT, chakramReadyAt, chakramCatchT;
        Chakram flyingChakram;
        FormRig chakramRig;
        public bool ChakramOut => flyingChakram != null;
        public bool ChakramRaised => State == PlayerState.Aim && chakramMode;

        bool TryEnterChakramAim()
        {
            var d = ChakramWeapon;
            if (!input.GameplayEnabled || !input.AimHeld || chakramPrefab == null || d == null || CurrentRig == null || CurrentRig.chakram == null) return false;
            EndSwing();
            SetState(PlayerState.Aim);
            chakramMode = true;
            cannonMode = false;
            orbMode = false;
            IsSprinting = false;
            BowPhaseProgress = 0f;
            BowDraw = 0f;
            fireQueued = false;
            fireQueuedAt = -9f;
            chakramThrowing = chakramCatching = false;
            input.ClearFire();
            input.ClearAttackBuffers();
            animator.SetFloat(HActionSpeed, 1f);
            if (!ChakramOut) ChakramToHand(true);
            animator.CrossFadeInFixedTime(ChakramOut ? "Fairy_ChakramWait" : "Fairy_ChakramHold", 0.2f);
            AudioManager.Play(Sfx.BladeRing, transform.position + Vector3.up * 1.4f, 0.35f, 1.6f);
            UpdateAimPoint();
            return true;
        }

        void UpdateChakramAim(float dt)
        {
            var d = ChakramWeapon;
            if (d == null) { ExitAim(); return; }
            if (!AimCommon(dt, d.aimMoveSpeed, 0f)) return;
            if (fireQueued && fireQueuedAt < 0f) fireQueuedAt = Time.time;
            BowPhaseProgress = Mathf.MoveTowards(BowPhaseProgress, 1f, dt / Mathf.Max(0.01f, d.raiseTime));
            bool ready = BowPhaseProgress >= 1f && !ChakramOut && !chakramThrowing && Time.time >= chakramReadyAt;   // a throw may cut the catch short
            float was = BowDraw;
            BowDraw = ready ? 1f : 0f;
            if (BowDraw >= 1f && was < 1f) FullDrawReached?.Invoke();
            if (fireQueued && !ready && Time.time - fireQueuedAt > 0.35f) { fireQueued = false; fireQueuedAt = -9f; }
            if (fireQueued && ready) StartChakramThrow(d);

            float speed = 1.25f;
            if (chakramThrowing)
            {
                chakramT += dt;
                float clip = Mathf.Max(0.1f, chakramThrowClipLength) / speed;
                if (!chakramReleased && chakramT >= chakramReleaseNorm * clip) ReleaseChakram(d);
                if (chakramT >= clip * 0.8f)
                {
                    chakramThrowing = false;
                    animator.SetFloat(HActionSpeed, 1f);
                    animator.CrossFadeInFixedTime(ChakramOut ? "Fairy_ChakramWait" : "Fairy_ChakramHold", 0.25f);
                }
            }
            if (chakramCatching)
            {
                chakramCatchT += dt;
                if (chakramCatchT >= chakramCatchClipLength * 0.7f)
                {
                    chakramCatching = false;
                    animator.SetFloat(HActionSpeed, 1f);
                    animator.CrossFadeInFixedTime("Fairy_ChakramHold", 0.25f);
                }
            }
        }

        void StartChakramThrow(ChakramData d)
        {
            fireQueued = false;
            fireQueuedAt = -9f;
            if (!stats.TrySpendMana(d.manaPerShot))
            {
                AudioManager.Play2D(Sfx.ManaEmpty, 0.8f);
                input.Rumble(0.02f, 0.1f, 0.05f);
                return;
            }
            chakramThrowing = true;
            chakramCatching = false;
            chakramReleased = false;
            chakramT = 0f;
            animator.SetFloat(HActionSpeed, 1.25f);
            animator.CrossFadeInFixedTime("Fairy_ChakramThrow", 0.06f);
        }

        void ReleaseChakram(ChakramData d)
        {
            chakramReleased = true;
            var rig = CurrentRig;
            Vector3 from = rig.chakram.position;
            Vector3 target = ProjectileTarget(from, d.outSpeed, d.leadFactor);
            if ((target - from).sqrMagnitude < 1f) target = from + transform.forward * d.range;
            var c = Instantiate(chakramPrefab);
            chakramRig = rig;
            flyingChakram = c;
            rig.chakram.gameObject.SetActive(false);
            var hand = rig.chakramHandSocket;
            c.Launch(from, target, d, transform, () => hand != null ? hand.position : transform.position + Vector3.up * 1.3f, OnChakramCaught);
            ArrowsLoosed++;
            CameraDirector.Instance?.Shake(0.06f, transform.forward);
            input.Rumble(0.15f, 0.35f, 0.08f);
        }

        void OnChakramCaught()
        {
            var rig = chakramRig;
            flyingChakram = null;
            chakramRig = null;
            if (rig == null || rig.chakram == null) return;
            rig.chakram.gameObject.SetActive(true);
            var d = ChakramWeapon;
            chakramReadyAt = Time.time + (d != null ? d.catchRecovery : 0.15f);
            if (rig != CurrentRig) { StowChakram(rig); return; }
            if (ChakramRaised)
            {
                ChakramToHand(true);
                chakramCatching = true;
                chakramCatchT = 0f;
                animator.SetFloat(HActionSpeed, 1.3f);
                animator.CrossFadeInFixedTime("Fairy_ChakramCatch", 0.05f);
            }
            else StowChakram(rig);
            AudioManager.Play(Sfx.MetalHit, rig.chakram.position, 0.6f, 1.5f);
            AudioManager.Play(Sfx.BladeRing, rig.chakram.position, 0.35f, 1.8f);
            input.Rumble(0.1f, 0.3f, 0.06f);
        }

        /// <summary>The chakram goes into the right hand (the right dagger to the hip), or back onto the back.</summary>
        void ChakramToHand(bool inHand)
        {
            var rig = CurrentRig;
            if (rig == null || rig.chakram == null) return;
            if (inHand)
            {
                Mount(rig.chakram, rig.chakramHandSocket);
                if (weapon != null && rig.mainWeaponStowSocket != null) Mount(weapon.transform, rig.mainWeaponStowSocket);
            }
            else
            {
                StowChakram(rig);
                if (weapon != null && rig.mainWeaponHandSocket != null) Mount(weapon.transform, rig.mainWeaponHandSocket);
            }
        }

        static void StowChakram(FormRig rig) => Mount(rig.chakram, rig.chakramStowSocket);

        static void Mount(Transform t, Transform socket)
        {
            if (t == null || socket == null) return;
            t.SetParent(socket, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
        }

        /// <summary>Lowering the chakram: the dagger comes back to the hand; a chakram in flight still flies home.</summary>
        void LowerChakram()
        {
            chakramThrowing = chakramCatching = false;
            ChakramToHand(false);
        }

        /// <summary>Form swap / death: a chakram in the air is back at once.</summary>
        void RecallChakram()
        {
            if (flyingChakram != null) flyingChakram.Recall();
            flyingChakram = null;
        }

        // ================================================================== special: Moonfall
        [Tooltip("The huge moon Moonfall drops on the target.")]
        public MoonDrop moonDropPrefab;

        enum MoonPhase { Rise, Call, Cast, Fall, Recover }
        bool moonMode;
        MoonPhase moonPhase;
        MoonfallData moon;
        float moonT;
        bool moonLanded;
        Vector3 moonCentre;

        bool TryStartMoonfall(MoonfallData m)
        {
            if (moonDropPrefab == null) return false;
            if (!stats.HasSpecialCharge)
            {
                stats.TrySpendSpecialCharge();   // raises SpecialDenied for the HUD
                AudioManager.Play2D(Sfx.ManaEmpty, 0.8f);
                return false;
            }
            if (!stats.TrySpendAttackStamina(m.staminaCost)) return false;
            stats.TrySpendSpecialCharge();
            EndSwing();
            if (State == PlayerState.Aim) ExitAim();
            moon = m;
            spinMode = false;
            growMode = false;
            moonMode = true;
            SetState(PlayerState.Special);
            IsSprinting = false;
            moonPhase = MoonPhase.Rise;
            moonT = 0f;
            moonLanded = false;
            motor.SetPlanarVelocityImmediate(Vector3.zero);
            motor.GravityScale = 0f;
            motor.SetVerticalVelocity(2f * m.riseHeight / Mathf.Max(0.05f, m.riseTime));
            if (lockOn.Current != null) FaceInstant(lockOn.Current.transform.position - transform.position);
            else if (moveInputMag > 0.2f) FaceInstant(moveInputWorld);
            animator.SetFloat(HActionSpeed, 1f);
            animator.CrossFadeInFixedTime("Fairy_CallLoad", 0.12f);
            Vector3 c = transform.position + Vector3.up * 1.4f;
            AudioManager.Play(Sfx.WhooshHeavy, c, 0.8f, 1.3f);
            AudioManager.Play(Sfx.SpellGather, c, 0.8f, 1.5f);
            FXManager.Spawn(FXType.Dust, transform.position, Quaternion.identity, 1.2f);
            input.Rumble(0.2f, 0.3f, m.riseTime);
            return true;
        }

        Vector3 MoonTarget()
        {
            var m = moon;
            Vector3 p;
            var locked = lockOn.Current;
            if (locked != null)
            {
                Vector3 to = locked.transform.position - transform.position; to.y = 0f;
                float d = Mathf.Min(to.magnitude, m.maxDistance);
                p = transform.position + (to.sqrMagnitude > 1e-3f ? to.normalized : transform.forward) * d;
            }
            else p = transform.position + transform.forward * m.aheadDistance;
            return Explosions.Ground(new Vector3(p.x, transform.position.y, p.z), 8f, 12f);
        }

        /// <summary>The moon comes in on a slant from high up beyond the target, so the camera behind her sees it fall.</summary>
        void CallMoon()
        {
            var m = moon;
            moonCentre = MoonTarget();
            Vector3 away = moonCentre - transform.position; away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : transform.forward;
            Vector3 start = moonCentre + away * m.dropDistance + Vector3.up * m.dropHeight;
            var drop = Instantiate(moonDropPrefab);
            drop.Landed = () => { moonLanded = true; input.Rumble(1f, 1f, 0.5f); };
            drop.Drop(start, moonCentre, m.moonRadius, m.fallTime, m.impactRadius, m.impactDamage, m.impactPoise, gameObject);
            CameraDirector.Instance?.FocusOn(moonCentre + Vector3.up * 2f, m.cameraZoomOut, m.cameraPitch, 0.3f);
        }

        void UpdateMoonfall(float dt)
        {
            var m = moon;
            moonT += dt;
            motor.SetDesiredPlanarVelocity(Vector3.zero, 12f, dt);
            if (lockOn.Current != null && moonPhase <= MoonPhase.Call) FaceTowards(lockOn.Current.transform.position, 360f, dt);
            switch (moonPhase)
            {
                case MoonPhase.Rise:
                {
                    float k = Mathf.Clamp01(moonT / m.riseTime);
                    motor.SetVerticalVelocity(Mathf.Max(0.01f, 2f * m.riseHeight / Mathf.Max(0.05f, m.riseTime) * (1f - k)));
                    if (moonT >= m.riseTime) { moonPhase = MoonPhase.Call; moonT = 0f; motor.SetVerticalVelocity(0f); }
                    break;
                }
                case MoonPhase.Call:
                    motor.SetVerticalVelocity(0f);
                    if (moonT >= m.callTime)
                    {
                        moonPhase = MoonPhase.Cast; moonT = 0f;
                        animator.CrossFadeInFixedTime("Fairy_CallCast", 0.08f);
                        AudioManager.Play(Sfx.OrbCharge, transform.position + Vector3.up * 2f, 0.8f, 1.1f);
                        CallMoon();
                    }
                    break;
                case MoonPhase.Cast:
                    motor.SetVerticalVelocity(0f);
                    if ((moonLanded && moonT >= m.fallTime + m.hangAfter) || moonT > m.fallTime + 3f)
                    {
                        moonPhase = MoonPhase.Fall; moonT = 0f;
                        motor.GravityScale = 1f;
                        animator.CrossFadeInFixedTime("Fall", 0.25f);
                        CameraDirector.Instance?.ReleaseFocus(0.5f);
                    }
                    break;
                case MoonPhase.Fall:
                    if (motor.Grounded && moonT > 0.05f)
                    {
                        moonPhase = MoonPhase.Recover; moonT = 0f;
                        animator.CrossFadeInFixedTime("Land", 0.05f, 0, 0.15f);
                        AudioManager.Play(Sfx.Land, transform.position, 0.7f, 1.2f);
                    }
                    if (moonT > 2f) { moonPhase = MoonPhase.Recover; moonT = 0f; }
                    break;
                case MoonPhase.Recover:
                    if (moonT >= m.landRecovery)
                    {
                        moonMode = false;
                        motor.GravityScale = 1f;
                        ReturnToLocomotion(0.2f);
                    }
                    break;
            }
        }

        /// <summary>Cut short (hit, death, swap): gravity comes back; a moon already called still falls.</summary>
        void AbortMoonfall()
        {
            moonMode = false;
            motor.GravityScale = 1f;
        }
    }
}
