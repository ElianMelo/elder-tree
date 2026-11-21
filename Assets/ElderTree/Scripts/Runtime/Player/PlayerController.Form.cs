using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.Combat;
using ElderTree.Equipment;
using ElderTree.FX;
using ElderTree.Masks;
using ElderTree.World;
using UnityEngine;

namespace ElderTree.Player
{
    /// <summary>
    /// Forms (masks): which body, moveset and weapons are in use, and the actions that only exist
    /// in a mask form. Everything numeric comes from the form's ScriptableObjects
    /// (<see cref="FormData"/>, <see cref="MeleeWeaponData"/>, <see cref="CannonData"/>, <see cref="SpecialAttackData"/>).
    ///  * Bomb hammer: every swing that connects detonates once (enemy, obstacle or the ground it strikes).
    ///  * Cannon: raise with LT / RMB, fire with RT / LMB; slower than the bow but longer reach, harder hits, blast.
    ///  * Sky Bombardment (light + heavy): leap up, hang, hurl the hammer down, huge explosion, it flies back.
    ///  * Quick transformation when a known mask is put on or taken off.
    /// </summary>
    public partial class PlayerController
    {
        [Header("Forms (masks)")]
        public FormData baseForm;
        [Tooltip("Every body model under Visual. The active form's rig is shown, the others hidden.")]
        public FormRig[] rigs = System.Array.Empty<FormRig>();
        public CannonShell shellPrefab;
        [Tooltip("Root yaw relative to the aim while holding the cannon (the Bazooka clips aim slightly off the root).")]
        public float cannonStanceYaw = 0f;
        public FootstepFX footsteps;

        public FormData CurrentForm { get; private set; }
        public FormRig CurrentRig { get; private set; }
        public MeleeWeaponData Melee => CurrentForm != null ? CurrentForm.melee : null;
        public bool CannonRaised => State == PlayerState.Aim && cannonMode;
        public bool HammerThrown { get; private set; }
        public event System.Action<FormData> FormChanged;

        float baseWalk, baseRun, baseSprint, baseRoll, baseBackstep;
        bool formsReady;
        CannonRig cannonRig;
        bool cannonMode;
        float cannonReadyAt, cannonShootAt = -9f, fireQueuedAt = -9f;
        bool cannonWasReady;
        bool blastDone;

        void InitForms()
        {
            if (formsReady) return;
            formsReady = true;
            baseWalk = walkSpeed; baseRun = runSpeed; baseSprint = sprintSpeed; baseRoll = rollDistance; baseBackstep = backstepDistance;
            if (baseForm != null) ApplyForm(baseForm);
        }

        FormRig FindRig(string id)
        {
            foreach (var r in rigs) if (r != null && r.rigId == id) return r;
            return null;
        }

        /// <summary>Become another body right now (no animation): swaps model, weapons, moveset and handling.</summary>
        public void ApplyForm(FormData form)
        {
            if (form == null) form = baseForm;
            if (form == null) return;
            var rig = FindRig(form.rigId);
            if (rig == null) { Debug.LogWarning($"[Forms] No rig '{form.rigId}' for {form.name}"); return; }
            if (State == PlayerState.Aim) ExitAim();
            EndSwing();
            ReturnHammer(true);

            // carry the pose over so the swap is seamless (same controller, same state names)
            var prev = animator;
            int hash = 0; float nt = 0f;
            float mx = 0f, mz = 0f, loco = 1f, act = 1f;
            if (prev != null && prev.isActiveAndEnabled && prev.runtimeAnimatorController != null)
            {
                var si = prev.GetCurrentAnimatorStateInfo(0);
                hash = si.fullPathHash; nt = si.normalizedTime;
                mx = prev.GetFloat(HMoveX); mz = prev.GetFloat(HMoveZ); loco = prev.GetFloat(HLocoSpeed); act = prev.GetFloat(HActionSpeed);
            }
            foreach (var r in rigs) if (r != null) r.gameObject.SetActive(r == rig);

            CurrentRig = rig;
            CurrentForm = form;
            animator = rig.animator;
            weapon = rig.weapon;
            if (weapon) weapon.OnHit = OnWeaponHit;
            CaptureWeaponRest();
            clubTip = rig.weaponTip;
            clubRenderer = rig.weaponRenderer;
            trail = rig.trail;
            slashArc = rig.slash;
            bowRig = rig.bowRig;
            cannonRig = rig.cannonRig;
            twoHand = rig.twoHandGrip;
            twoHandW = twoHandArmsW = 0f;
            ResetSpirit();
            spinMode = false;
            growMode = false;
            growNow = 1f;
            moveset = form.moveset != null ? form.moveset : (baseForm != null ? baseForm.moveset : moveset);
            walkSpeed = baseWalk * form.moveSpeedMultiplier;
            runSpeed = baseRun * form.moveSpeedMultiplier;
            sprintSpeed = baseSprint * form.moveSpeedMultiplier;
            rollDistance = baseRoll * form.rollDistanceMultiplier;
            backstepDistance = baseBackstep * form.rollDistanceMultiplier;
            if (footsteps) footsteps.SetAnimator(animator);

            animator.Rebind();
            if (hash != 0 && animator.HasState(0, hash)) animator.Play(hash, 0, nt % 1f);
            else animator.Play("Locomotion", 0, 0f);
            animator.SetFloat(HMoveX, mx); animator.SetFloat(HMoveZ, mz);
            animator.SetFloat(HLocoSpeed, loco); animator.SetFloat(HActionSpeed, act);
            animator.SetBool(HGrounded, motor.Grounded);
            animator.SetLayerWeight(1, guardWeight);
            animator.SetLayerWeight(BowLegsLayer, 0f);
            if (animator.layerCount > TwoHandLayer) animator.SetLayerWeight(TwoHandLayer, 0f);
            animator.Update(0f);
            FormChanged?.Invoke(form);
        }

        // ================================================================== two-handed hold
        // Two-handed weapons (the bomb hammer): the off hand stays on the shaft through idle, walk/run,
        // jumps and every swing. Walking and running keep the pack's 2H guard on the arms (arms-only
        // layer), and TwoHandGrip pins the left fist to the handle after the pose is final.
        const int TwoHandLayer = 3;
        TwoHandGrip twoHand;
        float twoHandW, twoHandArmsW;

        bool TwoHandGripWanted()
        {
            if (twoHand == null || HammerThrown) return false;
            switch (State)
            {
                case PlayerState.Locomotion:
                case PlayerState.Air:
                case PlayerState.Landing:
                    return true;
                case PlayerState.Attack:
                    return attack == null || !attack.oneHanded;
                case PlayerState.Special:
                    return spinMode || spPhase == SpecialPhase.Crouch || spPhase == SpecialPhase.Rise;
                default:
                    return false;   // roll, hits, death, cannon (hammer on the back), throw, transformations
            }
        }

        void UpdateTwoHand(float dt)
        {
            bool grip = TwoHandGripWanted();
            twoHandW = Mathf.MoveTowards(twoHandW, grip ? 1f : 0f, dt * (grip ? 7f : 12f));
            if (twoHand != null) twoHand.Weight = Ease.InOutCubic(twoHandW);

            bool arms = twoHand != null && (State == PlayerState.Locomotion || State == PlayerState.Air || State == PlayerState.Landing);
            twoHandArmsW = Mathf.MoveTowards(twoHandArmsW, arms ? 1f : 0f, dt * (arms ? 6f : 10f));
            if (animator.layerCount > TwoHandLayer) animator.SetLayerWeight(TwoHandLayer, twoHandArmsW * (1f - guardWeight));
        }

        // ================================================================== melee: bomb hammer
        /// <summary>Detonate once per swing at the contact point (no-op for weapons that don't explode).</summary>
        void TryWeaponBlast(Vector3 point, IDamageable directTarget)
        {
            var m = Melee;
            if (m == null || !m.explodeOnHit || blastDone || attack == null) return;
            blastDone = true;
            float k = (attack.weight != ImpactWeight.Light || chargeTime > 0.05f) ? m.heavyExplosionScale : 1f;
            k *= ChargeMultiplier > 1.01f ? 1.15f : 1f;
            Explosions.Detonate(new Explosions.Blast
            {
                center = point, radius = m.explosionRadius * Mathf.Sqrt(k), damage = m.explosionDamage * k, poise = m.explosionPoise * k,
                breakPower = m.breakPower, fxScale = m.explosionFxScale * k, weight = ImpactWeight.Heavy, source = gameObject
            });
            input.Rumble(0.7f, 0.5f, 0.18f);
            CameraDirector.Instance?.FovKick(-2f * k);
            TimeDirector.HitStop(0.05f * k);
        }

        /// <summary>During the active window: the hammer head strikes the ground (overheads, slams) and blows.</summary>
        void CheckGroundBlast()
        {
            var m = Melee;
            if (m == null || !m.explodeOnHit || !m.explodeOnGround || blastDone || weapon == null || !weapon.Active) return;
            Vector3 tip = weapon.TipWorld;
            if (Physics.Raycast(tip + Vector3.up * 0.6f, Vector3.down, out var hit, 1.2f, groundMask, QueryTriggerInteraction.Ignore)
                && tip.y - hit.point.y < 0.3f)
                TryWeaponBlast(hit.point + Vector3.up * 0.05f, null);
        }

        // ================================================================== ranged: cannon / crossbow (shouldered)
        CannonData Cannon => CurrentForm != null ? CurrentForm.cannon : null;
        CrossbowData Crossbow => CurrentForm != null ? CurrentForm.crossbow : null;
        /// <summary>The raised shouldered weapon is the crossbow (otherwise the cannon).</summary>
        bool UsingCrossbow => CurrentForm != null && CurrentForm.rangedKind == RangedKind.Crossbow && Crossbow != null;
        /// <summary>A round is in the weapon (the crossbow shows its bolt in the groove late in the crank).</summary>
        public bool RangedLoaded => !UsingCrossbow || Time.time >= cannonReadyAt - Crossbow.reloadTime * 0.3f;
        /// <summary>The crossbow string is being cranked back (the rig lets the reload clip move the weapon).</summary>
        public bool RangedReloading => State == PlayerState.Aim && cannonMode && UsingCrossbow &&
                                       Time.time > cannonShootAt + CrossbowReloadDelay && Time.time < cannonReadyAt - 0.12f;
        const float CrossbowReloadDelay = 0.2f;
        bool crossbowReloadPlaying;

        float ShoulderedInterval => UsingCrossbow ? Crossbow.reloadTime : Cannon.fireInterval;
        float ShoulderedRaise => UsingCrossbow ? Crossbow.raiseTime : Cannon.raiseTime;
        float ShoulderedMoveSpeed => UsingCrossbow ? Crossbow.aimMoveSpeed : Cannon.aimMoveSpeed;

        bool TryEnterCannonAim()
        {
            if (!input.GameplayEnabled || !input.AimHeld || cannonRig == null) return false;
            if (UsingCrossbow ? boltPrefab == null : (Cannon == null || shellPrefab == null)) return false;
            EndSwing();
            SetState(PlayerState.Aim);
            cannonMode = true;
            IsSprinting = false;
            BowPhaseProgress = 0f;
            BowDraw = 0f;
            fireQueued = false;
            cannonWasReady = false;
            input.ClearFire();
            input.ClearAttackBuffers();
            animator.SetFloat(HActionSpeed, 1f);
            crossbowReloadPlaying = false;
            animator.CrossFadeInFixedTime(UsingCrossbow ? (RangedLoaded ? "CrossbowAim" : "CrossbowReload") : "CannonAim", 0.22f);
            if (UsingCrossbow && !RangedLoaded) { crossbowReloadPlaying = true; animator.SetFloat(HActionSpeed, CrossbowReloadSpeed()); }
            AudioManager.Play(Sfx.MetalHit, transform.position + Vector3.up * 1.4f, 0.45f, UsingCrossbow ? 1.1f : 0.7f);
            UpdateAimPoint();
            return true;
        }

        void UpdateCannonAim(float dt)
        {
            bool xbow = UsingCrossbow;
            float interval = ShoulderedInterval;
            if (!AimCommon(dt, ShoulderedMoveSpeed, xbow ? crossbowStanceYaw : cannonStanceYaw)) return;
            if (fireQueued && fireQueuedAt < 0f) fireQueuedAt = Time.time;
            BowPhaseProgress = Mathf.MoveTowards(BowPhaseProgress, 1f, dt / Mathf.Max(0.01f, ShoulderedRaise));
            float ready = Time.time >= cannonReadyAt ? 1f : 1f - (cannonReadyAt - Time.time) / Mathf.Max(0.01f, interval);
            BowDraw = BowPhaseProgress >= 1f ? Mathf.Clamp01(ready) : 0f;
            bool isReady = BowDraw >= 0.999f;
            if (isReady && !cannonWasReady && Time.time - cannonShootAt < interval + 0.1f)
            {
                // reloaded: the cannon's breech clunks; the crossbow's latch is in the crank sound
                if (!xbow) AudioManager.Play(Sfx.BowNock, transform.position + Vector3.up * 1.4f, 0.5f, 0.6f);
                FullDrawReached?.Invoke();
            }
            cannonWasReady = isReady;
            // a press while reloading is kept briefly, then dropped (no surprise shots seconds later)
            if (fireQueued && !isReady && Time.time - fireQueuedAt > 0.35f) { fireQueued = false; fireQueuedAt = -9f; }
            if (fireQueued && isReady)
            {
                if (xbow) FireCrossbow(); else FireCannon();
            }
            if (xbow) UpdateCrossbowReloadAnim();
            else if (Time.time - cannonShootAt > 0.55f && Time.time - cannonShootAt < 0.6f + dt)
                animator.CrossFadeInFixedTime("CannonAim", 0.3f);
        }

        void FireCannon()
        {
            var c = Cannon;
            fireQueued = false;
            fireQueuedAt = -9f;
            if (!stats.TrySpendMana(c.manaPerShot))
            {
                AudioManager.Play2D(Sfx.ManaEmpty, 0.8f);
                input.Rumble(0.02f, 0.1f, 0.05f);
                return;
            }
            cannonReadyAt = Time.time + c.fireInterval;
            cannonShootAt = Time.time;
            Vector3 from = cannonRig.MuzzleWorld;
            Vector3 target = ProjectileTarget(from, c.shellSpeed, 1f);
            Vector3 vel = SolveLaunch(from, target, c.shellSpeed, c.shellGravity);
            var shell = Instantiate(shellPrefab);
            shell.Launch(from, vel, c, transform);
            ArrowsLoosed++;

            Quaternion look = Quaternion.LookRotation(vel);
            FXManager.Spawn(FXType.ChargeFlash, from, look, 1.3f);
            FXManager.Spawn(FXType.Smoke, from + vel.normalized * 0.4f, look, 0.45f);
            FXManager.Spawn(FXType.DustSmall, transform.position, Quaternion.identity, 1.2f);
            AudioManager.Play(Sfx.CannonFire, from, 1f, Random.Range(0.95f, 1.05f));
            CameraDirector.Instance?.Shake(c.shake, -vel.normalized);
            CameraDirector.Instance?.FovKick(c.fovKick);
            input.Rumble(0.8f, 0.6f, 0.2f);
            Vector3 back = -vel; back.y = 0f;
            if (back.sqrMagnitude > 1e-3f) motor.SetPlanarVelocityImmediate(back.normalized * c.recoil);
            cannonRig.Kick();
            animator.CrossFadeInFixedTime("CannonShoot", 0.04f);
        }

        // ================================================================== special: Sky Bombardment
        enum SpecialPhase { Crouch, Rise, Hang, Throw, Fall, Recover }
        SpecialPhase spPhase;
        SpecialAttackData sp;
        float spT;
        Vector3 spTarget;
        bool spThrown;
        Transform hammerParent;
        Vector3 hammerLocalPos; Quaternion hammerLocalRot;
        Vector3 hammerFrom;
        float hammerFlightT, hammerReturnAt = -1f;
        bool hammerFlying;

        /// <summary>Light + heavy together: the form's special, or the moveset's skill attack for the base form.</summary>
        bool TryStartSpecial()
        {
            var special = CurrentForm != null ? CurrentForm.special : null;
            if (special == null) return !IsCaster && moveset.skill != null && StartAttack(moveset.skill);
            if (special is SpinSlashData spin) return TryStartSpin(spin);
            if (special is GrowthStompData gs) return TryStartGrowth(gs);
            if (!(special is SpecialAttackData s)) return false;
            if (weapon == null || HammerThrown) return false;
            if (!stats.HasMana(s.manaCost))
            {
                stats.TrySpendMana(s.manaCost);   // raises ManaDenied for the HUD
                AudioManager.Play2D(Sfx.ManaEmpty, 0.8f);
                return false;
            }
            if (!stats.TrySpendStamina(s.staminaCost)) return false;
            stats.TrySpendMana(s.manaCost);
            EndSwing();
            if (State == PlayerState.Aim) ExitAim();
            sp = s;
            spinMode = false;
            SetState(PlayerState.Special);
            IsSprinting = false;
            spPhase = SpecialPhase.Crouch;
            spT = 0f;
            spThrown = false;
            if (lockOn.Current != null) FaceInstant(lockOn.Current.transform.position - transform.position);
            else if (moveInputMag > 0.2f) FaceInstant(moveInputWorld);
            motor.SetPlanarVelocityImmediate(Vector3.zero);
            squash = 0.16f;
            animator.SetFloat(HActionSpeed, 1f);
            animator.CrossFadeInFixedTime("Land", 0.08f, 0, 0.1f);
            AudioManager.Play(Sfx.GroundGrind, transform.position, 1f, 0.85f);
            input.Rumble(0.2f, 0.3f, s.crouchTime);
            return true;
        }

        Vector3 SpecialTargetPoint()
        {
            Vector3 p;
            var locked = lockOn.Current;
            if (locked != null)
            {
                Vector3 to = locked.transform.position - transform.position; to.y = 0f;
                float d = Mathf.Min(to.magnitude, sp.maxThrowDistance);
                p = transform.position + (to.sqrMagnitude > 1e-3f ? to.normalized : transform.forward) * d;
            }
            else p = transform.position + transform.forward * sp.throwDistance;
            return Explosions.Ground(new Vector3(p.x, transform.position.y, p.z), 8f, 12f);
        }

        void UpdateSpecial(float dt)
        {
            if (spinMode) { UpdateSpin(dt); return; }
            if (growMode) { UpdateGrowth(dt); return; }
            spT += dt;
            motor.SetDesiredPlanarVelocity(Vector3.zero, 12f, dt);
            switch (spPhase)
            {
                case SpecialPhase.Crouch:
                    if (spT >= sp.crouchTime)
                    {
                        spPhase = SpecialPhase.Rise; spT = 0f;
                        motor.GravityScale = 0f;
                        motor.SetVerticalVelocity(3f * sp.riseHeight / Mathf.Max(0.05f, sp.riseTime));
                        animator.CrossFadeInFixedTime("JumpStart", 0.06f);
                        AudioManager.Play(Sfx.Jump, transform.position, 1f, 0.75f);
                        AudioManager.Play(Sfx.WhooshHeavy, transform.position, 1f, 0.7f);
                        FXManager.Spawn(FXType.Dust, transform.position, Quaternion.identity, 1.6f);
                        FXManager.Spawn(FXType.SlamRing, transform.position + Vector3.up * 0.05f, Quaternion.identity, 1.4f);
                        CameraDirector.Instance?.Shake(0.3f, Vector3.down);
                    }
                    break;
                case SpecialPhase.Rise:
                {
                    // decelerate into the apex (ease out)
                    float k = Mathf.Clamp01(spT / sp.riseTime);
                    float v = 3f * sp.riseHeight / Mathf.Max(0.05f, sp.riseTime) * (1f - k) * (1f - k);   // integrates to riseHeight
                    motor.SetVerticalVelocity(Mathf.Max(0.01f, v));
                    if (lockOn.Current != null) FaceTowards(lockOn.Current.transform.position, 540f, dt);
                    if (spT >= sp.riseTime)
                    {
                        spPhase = SpecialPhase.Hang; spT = 0f;
                        motor.SetVerticalVelocity(0f);
                        animator.CrossFadeInFixedTime("SpecialHold", 0.12f);
                        spTarget = SpecialTargetPoint();
                        CameraDirector.Instance?.FovKick(4f);
                        FocusSpecialCamera();
                    }
                    break;
                }
                case SpecialPhase.Hang:
                    motor.SetVerticalVelocity(0f);
                    if (lockOn.Current != null) FaceTowards(lockOn.Current.transform.position, 540f, dt);
                    if (spT >= sp.hangTime)
                    {
                        spPhase = SpecialPhase.Throw; spT = 0f;
                        spTarget = SpecialTargetPoint();
                        FocusSpecialCamera();
                        animator.CrossFadeInFixedTime("SpecialThrow", 0.06f);
                    }
                    break;
                case SpecialPhase.Throw:
                    motor.SetVerticalVelocity(0f);
                    if (!spThrown && spT >= sp.throwWindup)
                    {
                        spThrown = true;
                        ThrowHammer();
                    }
                    if (spT >= sp.throwWindup + 0.12f)
                    {
                        spPhase = SpecialPhase.Fall; spT = 0f;
                        motor.GravityScale = sp.fallGravityScale;
                        animator.CrossFadeInFixedTime("Fall", 0.25f);
                    }
                    break;
                case SpecialPhase.Fall:
                    if (motor.Grounded && spT > 0.05f)
                    {
                        motor.GravityScale = 1f;
                        spPhase = SpecialPhase.Recover; spT = 0f;
                        animator.CrossFadeInFixedTime("Land", 0.05f, 0, 0.15f);
                        FXManager.Spawn(FXType.LandDust, transform.position, Quaternion.identity, 1.3f);
                        AudioManager.Play(Sfx.Land, transform.position, 1f, 0.8f);
                        squash = 0.15f;
                    }
                    break;
                case SpecialPhase.Recover:
                    if (spT >= sp.landRecovery)
                    {
                        motor.GravityScale = 1f;
                        ReturnToLocomotion(0.2f);
                    }
                    break;
            }
            UpdateThrownHammer(dt);
        }

        /// <summary>The camera pulls back and frames the impact point; control returns after the blast.</summary>
        void FocusSpecialCamera()
        {
            if (sp == null || !sp.cameraFocus) return;
            CameraDirector.Instance?.FocusOn(spTarget, sp.cameraZoomOut, sp.cameraPitch, sp.cameraAimBlend);
        }

        void ThrowHammer()
        {
            var h = weapon != null ? weapon.transform : null;
            if (h == null) return;
            hammerParent = h.parent;
            hammerLocalPos = h.localPosition;
            hammerLocalRot = h.localRotation;
            h.SetParent(null, true);
            hammerFrom = h.position;
            hammerFlightT = 0f;
            hammerFlying = true;
            HammerThrown = true;
            if (trail) trail.Emitting = true;
            AudioManager.Play(Sfx.WhooshHeavy, h.position, 1f, 0.6f);
            AudioManager.Play(Sfx.BombWhistle, h.position, 0.8f, 0.8f);
            input.Rumble(0.3f, 0.6f, 0.12f);
        }

        void UpdateThrownHammer(float dt)
        {
            var h = weapon != null ? weapon.transform : null;
            if (h == null || !HammerThrown) return;
            if (hammerFlying)
            {
                hammerFlightT += dt;
                float s = Mathf.Clamp01(hammerFlightT / Mathf.Max(0.05f, sp.weaponFlightTime));
                Vector3 p = Vector3.Lerp(hammerFrom, spTarget, s * s) + Vector3.up * Mathf.Sin(s * Mathf.PI) * 0.6f;
                h.position = p;
                h.Rotate(transform.right, sp.weaponSpin * dt, Space.World);
                if (s >= 1f)
                {
                    hammerFlying = false;
                    if (trail) trail.Emitting = false;
                    SkyBlast(spTarget);
                    h.gameObject.SetActive(false);
                    hammerReturnAt = Time.time + sp.weaponReturnTime;
                }
            }
            else if (hammerReturnAt > 0f && Time.time >= hammerReturnAt) ReturnHammer(false);
        }

        void SkyBlast(Vector3 at)
        {
            Explosions.Detonate(new Explosions.Blast
            {
                center = at + Vector3.up * 0.1f, radius = sp.radius, damage = sp.damage, poise = sp.poiseDamage,
                breakPower = sp.breakPower, fxScale = sp.fxScale, weight = ImpactWeight.Slam, source = gameObject
            });
            // a ring of secondary pops sells the size
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
                Vector3 p = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * sp.radius * Random.Range(0.55f, 0.85f);
                FXManager.Spawn(FXType.BombPop, Explosions.Ground(p), Quaternion.identity, 0.9f);
            }
            FXManager.Spawn(FXType.Dust, at, Quaternion.identity, sp.radius * 0.6f);
            FXManager.Spawn(FXType.Smoke, at + Vector3.up, Quaternion.identity, 1.6f);
            AudioManager.Play(Sfx.Slam, at, 1f, 0.7f);
            AudioManager.Play(Sfx.RockBreak, at, 1f, 0.8f);
            CameraDirector.Instance?.Shake(sp.shake, Vector3.down);
            CameraDirector.Instance?.FovKick(-5f);
            TimeDirector.HitStop(0.12f);
            TimeDirector.SlowMo(0.5f, 0.45f);
            input.Rumble(1f, 1f, 0.45f);
            CameraDirector.Instance?.ReleaseFocus(sp.cameraHoldAfterBlast);
        }

        /// <summary>The thrown hammer reappears in the hand (instantly when a form swap / death interrupts).</summary>
        void ReturnHammer(bool instant)
        {
            if (!HammerThrown) return;
            var h = weapon != null ? weapon.transform : null;
            HammerThrown = false;
            hammerFlying = false;
            hammerReturnAt = -1f;
            if (h == null || hammerParent == null) return;
            h.gameObject.SetActive(true);
            h.SetParent(hammerParent, false);
            h.localPosition = hammerLocalPos;
            h.localRotation = hammerLocalRot;
            if (trail) trail.Emitting = false;
            if (!instant)
            {
                FXManager.Spawn(FXType.ChargeFlash, h.position, Quaternion.identity, 0.8f);
                FXManager.Spawn(FXType.Smoke, h.position, Quaternion.identity, 0.3f);
                AudioManager.Play(Sfx.MetalHit, h.position, 0.6f, 0.8f);
            }
        }

        // ================================================================== quick transformation
        FormData transformTo;
        float transformTime, transformSwapAt;
        bool transformSwapped;
        System.Action transformDone;

        /// <summary>Put a mask on / take it off (after the first time): a quick hand-to-face, puff, new body.</summary>
        public bool BeginQuickTransform(FormData to, float duration, System.Action onDone = null)
        {
            if (IsDead) return false;
            if (State != PlayerState.Locomotion && State != PlayerState.Aim && State != PlayerState.Landing) return false;
            if (State == PlayerState.Aim) ExitAim();
            EndSwing();
            IsSprinting = false;
            SetState(PlayerState.Transform);
            transformTo = to;
            transformTime = Mathf.Max(0.2f, duration);
            transformSwapAt = transformTime * 0.45f;
            transformSwapped = false;
            transformDone = onDone;
            Invulnerable = true;
            animator.SetFloat(HActionSpeed, 1.6f);
            animator.CrossFadeInFixedTime("MaskDon", 0.1f, 0, 0.15f);
            AudioManager.Play(Sfx.MaskOn, transform.position + Vector3.up * 1.6f, 0.8f, 1.1f);
            return true;
        }

        void UpdateTransform(float dt)
        {
            motor.SetDesiredPlanarVelocity(Vector3.zero, 16f, dt);
            if (!transformSwapped && stateTime >= transformSwapAt)
            {
                transformSwapped = true;
                Vector3 c = transform.position + Vector3.up * 1.1f;
                FXManager.Spawn(FXType.ChargeFlash, c, Quaternion.identity, 2.2f);
                FXManager.Spawn(FXType.Smoke, c, Quaternion.identity, 0.7f);
                FXManager.Spawn(FXType.Dust, transform.position, Quaternion.identity, 1.4f);
                AudioManager.Play(Sfx.Transform, c, 1f);
                CameraDirector.Instance?.Shake(0.3f, Vector3.down);
                CameraDirector.Instance?.FovKick(-3f);
                input.Rumble(0.5f, 0.4f, 0.2f);
                ApplyForm(transformTo);
                animator.SetFloat(HActionSpeed, 1.2f);
                animator.CrossFadeInFixedTime(transformTo != baseForm ? "MaskRoar" : "Locomotion", 0.08f, 0, transformTo != baseForm ? 0.2f : 0f);
                if (transformTo != baseForm && transformTo != null) AudioManager.Play(transformTo.roar, c, 0.55f, transformTo.roarPitch);
                squash = 0.14f;
            }
            if (stateTime >= transformTime)
            {
                Invulnerable = false;
                var done = transformDone;
                transformDone = null;
                ReturnToLocomotion(0.25f);
                done?.Invoke();
            }
        }

        // ================================================================== cinematic hold
        /// <summary>A cinematic owns the body: no input, no movement, animator driven from outside.</summary>
        public void EnterCinematic()
        {
            if (State == PlayerState.Aim) ExitAim();
            EndSwing();
            ReturnHammer(true);
            spinMode = false;
            IsSprinting = false;
            Invulnerable = true;
            if (lockOn.Current != null) lockOn.Toggle();
            motor.SetPlanarVelocityImmediate(Vector3.zero);
            SetState(PlayerState.Cinematic);
            animator.SetFloat(HActionSpeed, 1f);
        }

        public void ExitCinematic()
        {
            Invulnerable = false;
            input.ClearAll();
            ReturnToLocomotion(0.3f);
        }

        void UpdateCinematic(float dt) => motor.SetDesiredPlanarVelocity(Vector3.zero, 20f, dt);
    }
}
