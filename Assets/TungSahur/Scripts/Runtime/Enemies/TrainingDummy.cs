using System.Collections;
using TungSahur.Audio;
using TungSahur.Combat;
using TungSahur.FX;
using UnityEngine;

namespace TungSahur.Enemies
{
    /// <summary>
    /// Straw training dummy on a sprung post. Wobbles with a damped spring in the hit direction,
    /// flashes, squashes, tracks poise (breaking it causes a big stagger) and topples when felled,
    /// then stands back up at full health.
    /// </summary>
    public class TrainingDummy : MonoBehaviour, IDamageable
    {
        public string displayName = "Training Dummy";
        public Transform pivot;              // the sprung upper body
        public Renderer[] flashRenderers;
        public float maxHealth = 1200f;
        public float maxPoise = 90f;
        public float poiseRegenDelay = 3f;
        public float healDelay = 6f;
        public float respawnDelay = 3.2f;

        [Header("Spring")]
        public float stiffness = 70f;
        public float damping = 7.5f;
        public float maxTilt = 38f;

        public float Health { get; private set; }
        public float Poise { get; private set; }
        public bool IsAlive => !felled;
        public Transform Root => transform;
        public float LastHitTime { get; private set; } = -99f;
        [Tooltip("After a stance break the dummy is open to a critical blow for this long.")]
        public float staggerWindow = 2.2f;
        public float StaggeredUntil { get; private set; } = -99f;
        public bool IsStaggered => Time.time < StaggeredUntil;

        public event System.Action<TrainingDummy, DamageInfo, bool> Damaged;   // info, poiseBroken
        public event System.Action<TrainingDummy> Felled;
        public event System.Action<TrainingDummy> Revived;

        Vector2 tilt, tiltVel;      // degrees around (x = forward tilt, y = side tilt) in world-aligned axes
        float spin, spinVel;
        float flash, squash;
        bool felled;
        MaterialPropertyBlock mpb;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        Quaternion pivotBaseRot;

        void Awake()
        {
            Health = maxHealth;
            Poise = maxPoise;
            mpb = new MaterialPropertyBlock();
            pivotBaseRot = pivot.localRotation;
        }

        public bool ReceiveHit(in DamageInfo info)
        {
            if (felled) return false;
            LastHitTime = Time.time;
            Health = Mathf.Max(0f, Health - info.damage);
            Poise -= info.poiseDamage;
            bool poiseBroken = Poise <= 0f && !info.critical;
            if (poiseBroken) { Poise = maxPoise; StaggeredUntil = Time.time + staggerWindow; }
            if (info.critical) StaggeredUntil = -99f;

            // spring impulse along hit direction (in local space of the base)
            Vector3 d = transform.InverseTransformDirection(info.direction);
            float strength = info.weight switch { ImpactWeight.Light => 160f, ImpactWeight.Heavy => 260f, _ => 340f };
            if (info.charged) strength *= 1.3f;
            if (poiseBroken) strength *= 1.5f;
            if (info.critical) strength *= 2.2f;
            tiltVel += new Vector2(d.z, -d.x) * strength;
            spinVel += Random.Range(-1f, 1f) * strength * (poiseBroken ? 2.5f : 0.6f);
            flash = 1f;
            squash = info.weight == ImpactWeight.Light ? 0.07f : 0.13f;

            FXManager.Spawn(FXType.StrawBurst, info.point, Quaternion.LookRotation(info.direction + Vector3.up * 0.5f),
                info.weight == ImpactWeight.Light ? 0.8f : 1.2f);
            AudioManager.Play(Sfx.DummyHit, info.point, 0.9f, info.weight == ImpactWeight.Light ? 1.05f : 0.9f);
            if (poiseBroken) AudioManager.Play(Sfx.Stagger, info.point, 0.8f);

            Damaged?.Invoke(this, info, poiseBroken);
            if (Health <= 0f) StartCoroutine(Fell(info.direction));
            return true;
        }

        IEnumerator Fell(Vector3 dir)
        {
            felled = true;
            Felled?.Invoke(this);
            AudioManager.Play(Sfx.Felled, transform.position, 1f);
            TimeDirector.SlowMo(0.6f, 0.35f);
            Vector3 local = transform.InverseTransformDirection(dir);
            Vector2 fallTo = new Vector2(local.z, -local.x).normalized * 84f;
            // topple with a small bounce
            float t = 0f;
            Vector2 start = tilt;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.55f;
                float e = Ease.OutBounceLike(t);
                tilt = Vector2.Lerp(start, fallTo, e);
                ApplyPose();
                yield return null;
            }
            FXManager.Spawn(FXType.Dust, pivot.position + pivot.up * 1.1f, Quaternion.identity, 1.2f);
            AudioManager.Play(Sfx.Land, transform.position, 1f, 0.7f);
            yield return new WaitForSeconds(respawnDelay);
            // rise back up
            t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.9f;
                tilt = Vector2.Lerp(fallTo, Vector2.zero, Ease.OutBack(Mathf.Clamp01(t)));
                ApplyPose();
                yield return null;
            }
            tilt = Vector2.zero; tiltVel = Vector2.zero; spin = 0; spinVel = 0;
            Health = maxHealth;
            Poise = maxPoise;
            felled = false;
            Revived?.Invoke(this);
        }

        public void ResetDummy()
        {
            StopAllCoroutines();
            felled = false;
            Health = maxHealth;
            Poise = maxPoise;
            tilt = tiltVel = Vector2.zero;
            spin = spinVel = 0;
            Revived?.Invoke(this);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (!felled)
            {
                // damped spring back to upright
                Vector2 acc = -stiffness * tilt - damping * tiltVel;
                tiltVel += acc * dt;
                tilt += tiltVel * dt;
                if (tilt.magnitude > maxTilt) { tilt = tilt.normalized * maxTilt; tiltVel *= 0.5f; }
                spinVel += (-stiffness * 0.5f * spin - damping * 0.8f * spinVel) * dt;
                spin += spinVel * dt;

                if (Time.time - LastHitTime > poiseRegenDelay) Poise = Mathf.MoveTowards(Poise, maxPoise, maxPoise * dt);
                if (Time.time - LastHitTime > healDelay && Health < maxHealth) Health = Mathf.MoveTowards(Health, maxHealth, maxHealth * 0.6f * dt);
                ApplyPose();
            }

            flash = Mathf.MoveTowards(flash, 0f, dt * 7f);
            squash = Mathf.Lerp(squash, 0f, Ease.Damp(12f, dt));
            foreach (var r in flashRenderers)
            {
                if (!r) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor(EmissionId, new Color(1f, 0.85f, 0.6f) * (flash * flash * 0.9f));
                r.SetPropertyBlock(mpb);
            }
        }

        void ApplyPose()
        {
            pivot.localRotation = Quaternion.Euler(tilt.x, spin, tilt.y) * pivotBaseRot;
            pivot.localScale = new Vector3(1f + squash * 0.6f, 1f - squash, 1f + squash * 0.6f);
        }
    }
}
