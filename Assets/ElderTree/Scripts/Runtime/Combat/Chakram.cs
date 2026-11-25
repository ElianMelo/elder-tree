using System.Collections.Generic;
using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.Equipment;
using ElderTree.FX;
using UnityEngine;

namespace ElderTree.Combat
{
    /// <summary>
    /// The fairy's thrown crescent (<see cref="ChakramData"/>). It spins out along a gently bowed path, slows into the
    /// turn at the end of its reach (or rebounds off a wall), then homes back to the thrower's hand, speeding up. It
    /// cuts everything it passes once on the way out and once more on the way back, and bites through vines. When it
    /// reaches the hand it reports the catch and is gone; the hand's own chakram reappears.
    /// </summary>
    public class Chakram : MonoBehaviour
    {
        public Transform model;
        public TrailRenderer trail;
        public Light glow;
        public LayerMask worldMask = 1;
        public LayerMask hitMask = 1 << 9;

        /// <summary>Raised when it cuts something that takes damage (HUD hit marker).</summary>
        public static event System.Action<Chakram, IDamageable, DamageInfo> HitTarget;

        public bool Returning { get; private set; }

        ChakramData data;
        Transform owner;
        System.Func<Vector3> catchPoint;
        System.Action caught;
        Vector3 from, apex, side, vel;
        float outLen, s, t, backSpeed, whooshT, tilt;
        readonly HashSet<IDamageable> hitOut = new(), hitBack = new();
        readonly Collider[] buf = new Collider[24];
        bool done;

        /// <summary>Throw from <paramref name="start"/> toward <paramref name="target"/> (capped at the range).</summary>
        public void Launch(Vector3 start, Vector3 target, ChakramData d, Transform thrower, System.Func<Vector3> catchAt, System.Action onCaught)
        {
            data = d;
            owner = thrower;
            catchPoint = catchAt;
            caught = onCaught;
            from = start;
            Vector3 dir = target - start;
            float len = dir.magnitude;
            if (len < 0.01f) { dir = thrower != null ? thrower.forward : Vector3.forward; len = d.range; }
            dir /= len;
            outLen = Mathf.Clamp(len, 4f, d.range);
            apex = start + dir * outLen;
            // bows to the thrower's left on the way out (a right-handed throw), flatter for short throws
            side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-3f) side = thrower != null ? -thrower.right : Vector3.left;
            side = side.normalized * d.curve * Mathf.Clamp01(outLen / d.range + 0.3f);
            s = t = 0f;
            Returning = false;
            done = false;
            tilt = Random.Range(-14f, -8f);
            transform.position = start;
            vel = dir * d.outSpeed;
            if (trail) { trail.Clear(); trail.emitting = true; }
            AudioManager.Play(Sfx.BladeWhoosh, start, 0.9f, 1.25f);
        }

        /// <summary>Gone at once (form change, death, fight reset): the hand gets it back without a catch.</summary>
        public void Recall()
        {
            if (done) return;
            done = true;
            caught?.Invoke();
            Destroy(gameObject);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || done || data == null) return;
            t += dt;
            Vector3 prev = transform.position;
            if (!Returning)
            {
                // out: speed eases off into the turn
                float rate = data.outSpeed / Mathf.Max(1f, outLen) * Mathf.Lerp(1f, 0.35f, s * s);
                s = Mathf.Min(1f, s + rate * dt);
                Vector3 p = Vector3.Lerp(from, apex, s) + side * Mathf.Sin(s * Mathf.PI);
                Vector3 step = p - prev;
                if (step.sqrMagnitude > 1e-6f && Physics.SphereCast(prev, 0.12f, step.normalized, out var hit, step.magnitude, worldMask, QueryTriggerInteraction.Ignore))
                {
                    // rebounds off the wall: sparks, a ring of steel, and it turns for home
                    transform.position = hit.point + hit.normal * 0.15f;
                    FXManager.Spawn(FXType.HitSpark, hit.point, Quaternion.LookRotation(hit.normal), 0.9f);
                    AudioManager.Play(Sfx.MetalHit, hit.point, 0.7f, 1.4f);
                    TurnBack();
                }
                else
                {
                    transform.position = p;
                    if (s >= 1f) TurnBack();
                }
            }
            else
            {
                // back: homes on the hand, accelerating, curving in rather than snapping round
                Vector3 to = catchPoint != null ? catchPoint() : (owner != null ? owner.position + Vector3.up * 1.2f : transform.position);
                Vector3 d = to - transform.position;
                float dist = d.magnitude;
                backSpeed = Mathf.MoveTowards(backSpeed, data.returnSpeed, data.returnSpeed * 3f * dt);
                Vector3 want = dist > 1e-3f ? d / dist * backSpeed : Vector3.zero;
                vel = Vector3.RotateTowards(vel, want, 10f * dt, data.returnSpeed * 4f * dt);
                if (dist < 3f) vel = want;                          // last metres: straight into the hand
                transform.position += vel * Mathf.Min(dt, dist / Mathf.Max(0.01f, vel.magnitude) + 1e-3f);
                if (dist < data.catchRadius || t > data.maxFlightTime) { Catch(); return; }
            }
            Vector3 moved = transform.position - prev;
            if (moved.sqrMagnitude > 1e-6f) vel = Returning ? vel : moved / dt;

            Cut(Returning ? hitBack : hitOut, moved);

            // spin flat about its axis, banked a little
            if (model) model.Rotate(Vector3.up, data.spin * dt, Space.Self);
            transform.rotation = Quaternion.AngleAxis(tilt, Vector3.Cross(Vector3.up, vel.sqrMagnitude > 1e-4f ? vel.normalized : Vector3.forward));
            if (glow) glow.intensity = 1.2f + 0.3f * Mathf.Sin(t * 30f);
            if ((whooshT -= dt) <= 0f)
            {
                whooshT = 0.16f;
                AudioManager.Play(Sfx.WhooshLight, transform.position, 0.35f, Random.Range(1.5f, 1.7f));
            }
        }

        void TurnBack()
        {
            Returning = true;
            backSpeed = data.returnSpeed * 0.55f;
            vel = vel.sqrMagnitude > 0.01f ? vel.normalized * backSpeed : Vector3.zero;
        }

        void Cut(HashSet<IDamageable> already, Vector3 moved)
        {
            int n = Physics.OverlapSphereNonAlloc(transform.position, data.radius, buf, hitMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var col = buf[i];
                if (owner != null && col.transform.IsChildOf(owner)) continue;
                var hb = col.GetComponent<Hurtbox>();
                var target = hb != null ? hb.Owner : col.GetComponentInParent<IDamageable>();
                if (target == null || !target.IsAlive || already.Contains(target)) continue;
                if (owner != null && target.Root != null && target.Root.IsChildOf(owner)) continue;
                already.Add(target);
                Vector3 dir = moved.sqrMagnitude > 1e-5f ? moved.normalized : transform.forward;
                Vector3 push = new Vector3(dir.x, 0f, dir.z);
                bool critical = target is IStaggerable st && st.IsStaggered;
                Vector3 point = col.ClosestPoint(transform.position);
                var info = new DamageInfo
                {
                    damage = Mathf.Round(data.damage * (critical ? 1.6f : 1f) * Random.Range(0.95f, 1.05f)),
                    poiseDamage = data.poiseDamage,
                    point = point,
                    direction = push.sqrMagnitude > 1e-4f ? push.normalized : dir,
                    weight = ImpactWeight.Light,
                    critical = critical,
                    cutPower = data.cutPower,
                    cutWidth = data.cutWidth,
                    source = owner != null ? owner.gameObject : null
                };
                if (!target.ReceiveHit(info)) continue;
                if (target is World.Cuttable) continue;
                HitTarget?.Invoke(this, target, info);
                FXManager.Spawn(FXType.SlashHit, point, Quaternion.LookRotation(dir, Vector3.up), 0.9f);
                AudioManager.Play(Sfx.BladeHit, point, 0.9f, Random.Range(1.15f, 1.3f));
                CameraDirector.Instance?.Shake(data.shake, dir);
                TimeDirector.HitStop(0.035f);
            }
        }

        void Catch()
        {
            if (done) return;
            done = true;
            if (trail) trail.emitting = false;
            caught?.Invoke();
            Destroy(gameObject);
        }
    }
}
