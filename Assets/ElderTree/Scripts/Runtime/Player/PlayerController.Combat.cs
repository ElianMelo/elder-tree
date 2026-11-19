using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.Combat;
using ElderTree.FX;
using UnityEngine;

namespace ElderTree.Player
{
    public partial class PlayerController
    {
        enum Queued { None, Light, Heavy, Skill, Dodge, Jump }

        [Header("Combat")]
        public FX.WeaponTrail trail;
        public LayerMask hurtboxMask = ~0;
        public LayerMask groundMask = 1;

        AttackData attack;
        float attackT;
        float chargeTime;
        bool charging, chargeLocked;
        bool windowOpen, slamDone;
        Queued queued;
        float prevLunge, lungeScale = 1f, prevSpin;
        bool inStance;
        float stanceT;
        int hitsThisSwing;

        public event System.Action<AttackData> AttackStarted;
        public event System.Action<DamageInfo, IDamageable> HitLanded;

        bool IsLight(AttackData a) => System.Array.IndexOf(moveset.lightCombo, a) >= 0;
        bool IsHeavy(AttackData a) => System.Array.IndexOf(moveset.heavyCombo, a) >= 0;

        public bool StartAttack(AttackData a)
        {
            if (a == null) return false;
            if (a.manaCost > 0f && !stats.TrySpendMana(a.manaCost))
            {
                AudioManager.Play2D(Sfx.UIBack, 0.5f, 0.8f);
                return false;
            }
            if (!stats.TrySpendStamina(a.staminaCost)) return false;

            EndSwing();
            bool fromRun = State == PlayerState.Locomotion && (IsSprinting || motor.PlanarVelocity.magnitude > runSpeed);
            SetState(PlayerState.Attack);
            IsSprinting = false;
            attack = a;
            attackT = a.startOffset;
            chargeTime = 0f;
            charging = false;
            chargeLocked = !a.chargeable;
            windowOpen = false;
            slamDone = false;
            queued = Queued.None;
            hitsThisSwing = 0;
            blastDone = false;
            prevLunge = LungeCurve(a, attackT);
            prevSpin = SpinCurve(a, attackT);
            spiritedSwing = IsHeavy(a) && TrySpendSpirit();

            // pick a direction: lock-on snaps to the target, otherwise the stick decides
            if (lockOn.Current != null) FaceInstant(lockOn.Current.transform.position - transform.position);
            else if (moveInputMag > 0.2f) FaceInstant(moveInputWorld);

            // don't lunge through a target that's already in our face
            lungeScale = 1f;
            var near = a.lungeDistance > 0f ? NearestTargetAhead(3.5f) : null;   // stepping back (fade slash): never clamped
            if (near != null)
            {
                float d = FlatDistance(near.position, transform.position);
                lungeScale = Mathf.Clamp01((d - 1.15f) / Mathf.Max(0.1f, a.lungeDistance));
            }

            motor.SetPlanarVelocityImmediate(fromRun && a == moveset.runningAttack ? motor.PlanarVelocity * 0.5f : Vector3.zero);
            inStance = !string.IsNullOrEmpty(a.stanceState);
            stanceT = 0f;
            if (inStance)
            {
                // settle into the stance pose and hold it; the swing is drawn from it (UpdateStance)
                chargeLocked = true;
                animator.CrossFadeInFixedTime(a.stanceState, 0.1f, 0, a.stanceNorm);
                animator.SetFloat(HActionSpeed, 0f);
            }
            else
            {
                float fade = a.startOffset > 0f ? 0.12f : 0.07f;
                animator.CrossFadeInFixedTime(a.state, fade, 0, a.startOffset * a.clipLength);
                animator.SetFloat(HActionSpeed, a.windupSpeed);
            }
            AttackStarted?.Invoke(a);
            return true;
        }

        static float LungeCurve(AttackData a, float t) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a.lungeStart, a.lungeEnd, t));

        /// <summary>0..1 share of a roundslash turn done at normalized time t (eases in and out around the hit window).</summary>
        static float SpinCurve(AttackData a, float t) =>
            a.spinDegrees == 0f ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a.hitStart - 0.08f, a.hitEnd + 0.06f, t));

        static float FlatDistance(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }

        Transform NearestTargetAhead(float range)
        {
            Transform best = null;
            float bestD = range;
            foreach (var t in Targetable.All)
            {
                if (!t.CanBeTargeted) continue;
                Vector3 to = t.transform.position - transform.position;
                to.y = 0;
                float d = to.magnitude;
                if (d < bestD && Vector3.Dot(to.normalized, transform.forward) > 0.5f) { bestD = d; best = t.transform; }
            }
            return best;
        }

        /// <summary>
        /// Iai stance: the pose holds (hand on the hilt) for stanceTime; holding heavy keeps it and charges. On
        /// release the blade is drawn: a fast cross-fade straight into the swing.
        /// </summary>
        void UpdateStance(AttackData a, float dt)
        {
            stanceT += dt;
            animator.SetFloat(HActionSpeed, 0f);
            motor.SetDesiredPlanarVelocity(Vector3.zero, 12f, dt);
            if (lockOn.Current != null) FaceTowards(lockOn.Current.transform.position, a.trackTurnSpeed, dt);
            else if (moveInputMag > 0.2f) RotateTowards(moveInputWorld, a.trackTurnSpeed * 0.6f, dt);
            if (input.GameplayEnabled && input.ConsumeDodge())
            {
                inStance = false;
                EndSwing();
                if (TryDodgeDirect()) return;
            }
            bool hold = a.chargeable && input.HeavyHeld && chargeTime < a.chargeMaxTime;
            if (stanceT >= a.stanceTime && hold)
            {
                if (!charging)
                {
                    charging = true;
                    AudioManager.Play(Sfx.ChargeUp, transform.position, 0.7f, 1.15f);
                }
                chargeTime += dt;
                squash = Mathf.Max(squash, 0.05f * ChargeNormalized);
                if (Random.value < dt * 20f) input.Rumble(0.05f, 0.18f * ChargeNormalized, 0.06f);
                return;
            }
            if (stanceT < a.stanceTime) return;

            // draw!
            inStance = false;
            if (charging)
            {
                charging = false;
                AudioManager.Play(Sfx.ChargeRelease, transform.position, 0.6f + 0.4f * ChargeNormalized, 1.1f);
                CameraDirector.Instance?.FovKick(-2.5f * ChargeNormalized);
                input.Rumble(0.4f * ChargeNormalized, 0.6f, 0.1f);
            }
            animator.CrossFadeInFixedTime(a.state, a.drawFade, 0, a.startOffset * a.clipLength);
            animator.SetFloat(HActionSpeed, a.windupSpeed);
            AudioManager.Play(Sfx.BladeWhoosh, clubTip.position, 0.7f, 1.3f);
            stateTime = 0f;
        }

        void UpdateAttack(float dt)
        {
            var a = attack;
            if (inStance) { UpdateStance(a, dt); return; }
            float speed = attackT < a.hitStart ? a.windupSpeed : attackT < a.hitEnd ? a.activeSpeed : a.recoverySpeed;

            // ---- charge: hold the heavy button at the top of the swing
            if (!chargeLocked)
            {
                if (!input.HeavyHeld && !charging) chargeLocked = true;           // released before the hold point
                else if (attackT >= a.chargeHold)
                {
                    if (input.HeavyHeld && chargeTime < a.chargeMaxTime)
                    {
                        if (!charging)
                        {
                            charging = true;
                            AudioManager.Play(Sfx.ChargeUp, transform.position, 0.7f);
                        }
                        chargeTime += dt;
                        speed = 0.05f;
                        if (Random.value < dt * 20f) input.Rumble(0.05f, 0.18f * ChargeNormalized, 0.06f);
                    }
                    else
                    {
                        chargeLocked = true;
                        if (charging)
                        {
                            charging = false;
                            AudioManager.Play(Sfx.ChargeRelease, transform.position, 0.6f + 0.4f * ChargeNormalized);
                            CameraDirector.Instance?.FovKick(-2.5f * ChargeNormalized);
                            input.Rumble(0.4f * ChargeNormalized, 0.5f, 0.08f);
                        }
                    }
                }
            }

            // ---- jump attack hangs at the top until we land
            bool airHold = a == moveset.jumpAttack && !motor.Grounded && attackT >= a.hitStart - 0.02f;
            if (a == moveset.jumpAttack && !motor.Grounded)
            {
                motor.GravityScale = 1.7f;
                if (airHold) speed = 0.02f;
            }
            else motor.GravityScale = 1f;

            attackT += dt * speed / Mathf.Max(0.05f, a.clipLength);
            animator.SetFloat(HActionSpeed, speed);

            // ---- early tracking (souls-style: you can steer the start of a swing)
            if (attackT < a.trackUntil)
            {
                if (lockOn.Current != null) FaceTowards(lockOn.Current.transform.position, a.trackTurnSpeed, dt);
                else if (moveInputMag > 0.2f) RotateTowards(moveInputWorld, a.trackTurnSpeed * 0.6f, dt);
            }

            // ---- roundslash: the body turns through the swing
            if (a.spinDegrees != 0f)
            {
                float sc = SpinCurve(a, attackT);
                transform.rotation = Quaternion.AngleAxis((sc - prevSpin) * a.spinDegrees, Vector3.up) * transform.rotation;
                prevSpin = sc;
            }

            // ---- lunge
            float lc = LungeCurve(a, attackT);
            motor.AddDisplacement(transform.forward * (lc - prevLunge) * a.lungeDistance * lungeScale);
            prevLunge = lc;
            if (a != moveset.jumpAttack || motor.Grounded) motor.SetDesiredPlanarVelocity(Vector3.zero, 9f, dt);

            // ---- active window
            if (!windowOpen && attackT >= a.hitStart && !airHold)
            {
                windowOpen = true;
                weapon.Begin();
                if (trail) trail.Emitting = true;
                bool heavy = a.weight != ImpactWeight.Light;
                if (Melee != null && Melee.slashing)
                {
                    BeginSlash();
                    AudioManager.Play(Sfx.BladeWhoosh, clubTip.position, heavy ? 1f : 0.85f, heavy ? 0.85f : 1f);
                }
                else AudioManager.Play(heavy ? Sfx.WhooshHeavy : Sfx.WhooshLight, clubTip.position, heavy ? 0.9f : 0.75f, charging ? 0.9f : 1f);
            }
            if (windowOpen && weapon.Active) CheckGroundBlast();
            if (windowOpen && weapon.Active && attackT >= a.hitEnd)
            {
                weapon.End();
                if (trail) trail.Emitting = false;
                if (slashArc) slashArc.End();
            }
            if (a.slamRadius > 0f && !slamDone && attackT >= a.hitEnd - 0.03f && motor.Grounded)
            {
                slamDone = true;
                DoSlam(a);
            }

            // ---- input queue (last press wins, executed at the window)
            if (input.GameplayEnabled)
            {
                if (input.ConsumeLight()) queued = Queued.Light;
                if (input.ConsumeHeavy()) queued = Queued.Heavy;
                if (input.ConsumeSkill()) queued = Queued.Skill;
                if (input.ConsumeDodge()) queued = Queued.Dodge;
                if (input.ConsumeJump()) queued = Queued.Jump;
            }

            // raising the bow cancels the recovery like a dodge would
            if (input.AimHeld && attackT >= a.cancelOpen && motor.Grounded && queued != Queued.Dodge)
            {
                EndSwing();
                if (TryEnterAim()) return;
            }
            if ((queued == Queued.Dodge) && attackT >= a.cancelOpen)
            {
                queued = Queued.None;
                EndSwing();
                if (TryDodgeDirect()) return;
            }
            if (queued == Queued.Jump && attackT >= a.cancelOpen && motor.Grounded)
            {
                queued = Queued.None;
                EndSwing();
                if (stats.TrySpendStamina(jumpStamina)) { motor.Jump(jumpHeight); EnterAir(true); return; }
            }
            if (queued == Queued.Skill && attackT >= a.comboOpen && motor.Grounded && CurrentForm != null && CurrentForm.special != null)
            {
                queued = Queued.None;
                EndSwing();
                if (TryStartSpecial()) return;
            }
            if ((queued == Queued.Light || queued == Queued.Heavy || queued == Queued.Skill) && attackT >= a.comboOpen && motor.Grounded)
            {
                AttackData next = queued switch
                {
                    Queued.Light => IsLight(a) || a.next.StartsWith("Light") ? (moveset.Find(a.next) ?? moveset.lightCombo[0]) : moveset.lightCombo[0],
                    Queued.Heavy => IsHeavy(a) ? (moveset.Find(a.next) ?? moveset.heavyCombo[0]) : moveset.heavyCombo[0],
                    _ => moveset.skill
                };
                if (!IsLight(next) && queued == Queued.Light) next = moveset.lightCombo[0];
                queued = Queued.None;
                if (StartAttack(next)) return;
            }

            // ---- exit
            if (attackT >= a.exitTime && moveInputMag > 0.2f && queued == Queued.None)
            {
                EndSwing();
                ReturnToLocomotion(0.25f);
                return;
            }
            if (attackT >= 1f)
            {
                EndSwing();
                ReturnToLocomotion(0.2f);
            }
        }

        void EndSwing()
        {
            inStance = false;
            if (weapon) weapon.End();
            if (trail) trail.Emitting = false;
            if (slashArc) slashArc.End();
            motor.GravityScale = 1f;
            charging = false;
        }

        // ------------------------------------------------------------------ hits
        float ChargeMultiplier => attack != null && attack.chargeable && chargeTime > 0.05f
            ? Mathf.Lerp(1.15f, attack.chargeDamageMultiplier, ChargeNormalized) : 1f;

        void OnWeaponHit(IDamageable target, Collider col, Vector3 point)
        {
            if (attack == null) return;
            var melee = Melee;
            bool charged = chargeTime > 0.05f;
            bool critical = target is IStaggerable st && st.IsStaggered;
            float dmgMul = (melee != null ? melee.damageMultiplier : 1f) * SpiritDamageMultiplier;
            float poiseMul = melee != null ? melee.poiseMultiplier : 1f;
            Vector3 dir = target.Root.position - transform.position;
            dir.y = 0;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward;
            var info = new DamageInfo
            {
                damage = Mathf.Round(attack.damage * dmgMul * ChargeMultiplier * (critical ? 2.6f : 1f) * Random.Range(0.96f, 1.04f)),
                critical = critical,
                poiseDamage = attack.poiseDamage * poiseMul * ChargeMultiplier,
                breakPower = melee != null ? melee.breakPower : 0,
                cutPower = melee != null && melee.slashing ? melee.cutPower : 0,
                cutWidth = melee != null ? melee.cutWidth : 0f,
                point = point,
                direction = dir,
                weight = attack.weight,
                charged = charged,
                source = gameObject
            };
            if (!target.ReceiveHit(info))
            {
                // clanked off something this weapon can't break: a jolt back up the arm
                if (target is World.Breakable)
                {
                    TimeDirector.HitStop(0.06f);
                    input.Rumble(0.3f, 0.5f, 0.1f);
                }
                return;
            }
            if (target is World.Cuttable)
            {
                // vines part under the edge: a small bite of hit-stop, no spirit for cutting plants
                TimeDirector.HitStop(0.035f);
                input.Rumble(0.15f, 0.3f, 0.06f);
                return;
            }
            hitsThisSwing++;
            GainSpirit(attack, IsHeavy(attack));
            HitLanded?.Invoke(info, target);
            TryWeaponBlast(point, target);

            float k = (critical ? 1.8f : charged ? 1.35f : 1f) * (melee != null ? melee.hitStopMultiplier : 1f);
            if (critical)
            {
                TimeDirector.SlowMo(0.45f, 0.4f);
                AudioManager.Play(Sfx.Slam, point, 0.7f, 1.2f);
                FXManager.Spawn(FXType.ChargeFlash, point, Quaternion.identity, 1.6f);
            }
            TimeDirector.HitStop(attack.hitStop * k * (hitsThisSwing > 1 ? 0.5f : 1f));
            CameraDirector.Instance?.Shake(attack.shake * k, dir);
            if (attack.weight != ImpactWeight.Light) CameraDirector.Instance?.FovKick(-1.5f * k);
            input.Rumble(attack.rumbleLow * k, attack.rumbleHigh * k, 0.08f + attack.hitStop);

            Vector3 swing = weapon.TipWorld - weapon.BaseWorld;
            if (melee != null && melee.slashing)
            {
                // the edge bites: a streak of sparks along the cut instead of wood chips
                Vector3 cutDir = weapon.TipWorld - slashPrevTip;
                if (cutDir.sqrMagnitude < 1e-4f) cutDir = transform.right;
                FXManager.Spawn(FXType.SlashHit, point, Quaternion.LookRotation(cutDir.normalized, -dir), attack.weight == ImpactWeight.Light ? 0.9f : 1.25f);
                FXManager.Spawn(FXType.HitSpark, point, Quaternion.LookRotation(-dir + Vector3.up * 0.3f), 0.6f);
                AudioManager.Play(Sfx.BladeHit, point, 1f, attack.weight == ImpactWeight.Light ? 1.05f : 0.9f);
            }
            else
            {
                FXManager.Spawn(FXType.HitSpark, point, Quaternion.LookRotation(-dir + Vector3.up * 0.3f), attack.weight == ImpactWeight.Light ? 0.8f : 1.2f);
                FXManager.Spawn(FXType.WoodChips, point, Quaternion.LookRotation(dir + swing.normalized * 0.3f + Vector3.up * 0.4f));
                AudioManager.Play(Sfx.Knock, point, 1f, attack.weight == ImpactWeight.Light ? 1.05f : 0.85f);
            }
            if (attack.weight != ImpactWeight.Light || charged) AudioManager.Play(Sfx.ImpactHeavy, point, 0.9f);
        }

        void DoSlam(AttackData a)
        {
            Vector3 p = clubTip.position;
            if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var hit, 4f, groundMask, QueryTriggerInteraction.Ignore)) p = hit.point;
            else p.y = transform.position.y;
            float k = chargeTime > 0.05f ? 1.3f : 1f;
            FXManager.Spawn(FXType.SlamRing, p + Vector3.up * 0.03f, Quaternion.identity, a.slamRadius / 2f * k);
            FXManager.Spawn(FXType.SlamDebris, p, Quaternion.identity, k);
            FXManager.Spawn(FXType.Dust, p, Quaternion.identity, a.slamRadius / 1.6f);
            AudioManager.Play(Sfx.Slam, p, 1f, a == moveset.skill ? 0.85f : 1f);
            TryWeaponBlast(p + Vector3.up * 0.05f, null);
            CameraDirector.Instance?.Shake(a.shake * 0.8f * k, Vector3.down);
            input.Rumble(0.9f, 0.5f, 0.22f);
            if (hitsThisSwing == 0) TimeDirector.HitStop(0.05f);

            // shockwave damages anything nearby the club missed
            var cols = Physics.OverlapSphere(p, a.slamRadius * k, hurtboxMask, QueryTriggerInteraction.Collide);
            foreach (var c in cols)
            {
                var hb = c.GetComponent<Hurtbox>();
                var target = hb ? hb.Owner : c.GetComponentInParent<IDamageable>();
                if (target == null || !target.IsAlive || weapon.HasHit(target) || target.Root.IsChildOf(transform)) continue;
                weapon.MarkHit(target);
                Vector3 dir = target.Root.position - p; dir.y = 0;
                var info = new DamageInfo
                {
                    damage = Mathf.Round(a.damage * a.slamDamageScale * ChargeMultiplier),
                    poiseDamage = a.poiseDamage * 0.6f,
                    point = c.ClosestPoint(p + Vector3.up * 0.5f),
                    direction = dir.sqrMagnitude > 1e-4f ? dir.normalized : transform.forward,
                    weight = ImpactWeight.Heavy,
                    isSlamWave = true,
                    breakPower = Melee != null ? Melee.breakPower : 0,
                    source = gameObject
                };
                if (target.ReceiveHit(info))
                {
                    HitLanded?.Invoke(info, target);
                    AudioManager.Play(Sfx.Knock, info.point, 0.8f, 0.8f);
                }
            }
        }
    }
}
