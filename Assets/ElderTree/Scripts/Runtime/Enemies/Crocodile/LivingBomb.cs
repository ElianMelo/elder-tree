using System.Collections.Generic;
using ElderTree.Audio;
using ElderTree.Combat;
using ElderTree.FX;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree.Enemies
{
    /// <summary>
    /// A bomb that grew croc legs. Dropped by Crocodile, it lands, wakes, and waddles after the
    /// player. Close enough, it plants its feet and blinks red twice (beep, beep) before it blows,
    /// so you can still roll out. Kill it first and it only pops, which hurts the boss if it's near.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class LivingBomb : MonoBehaviour, IDamageable
    {
        public enum Phase { Inactive, Falling, Waking, Chasing, Arming }

        public Transform model;
        public Transform[] legs;
        public Renderer bodyRenderer;
        public int eyeMaterialIndex = -1;
        public ParticleSystem fuseSpark;

        [Header("Tuning")]
        public float maxHealth = 45f;
        public float moveSpeed = 3.3f;
        public float turnSpeed = 260f;
        public float triggerDistance = 1.7f;
        public float armTime = 1.05f;
        public float blastRadius = 2.4f;
        public float blastDamage = 95f;
        public float lifetime = 15f;
        public float gravity = 22f;

        public static readonly List<LivingBomb> Active = new();
        public Phase State { get; private set; } = Phase.Inactive;
        public bool IsAlive => State != Phase.Inactive;
        public Transform Root => transform;
        public float Health { get; private set; }

        CharacterController cc;
        Vector3 velocity, knock;
        float stateTime, life, flash, squash, gait;
        CrocodileBoss boss;
        int beeps;
        MaterialPropertyBlock mpb;
        Quaternion[] legRest;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly Color EyeGlow = new Color(1f, 0.16f, 0.06f) * 2.4f;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            mpb = new MaterialPropertyBlock();
            legRest = new Quaternion[legs.Length];
            for (int i = 0; i < legs.Length; i++) legRest[i] = legs[i].localRotation;
        }

        void OnDisable() => Active.Remove(this);

        public void Launch(Vector3 position, Vector3 vel, CrocodileBoss owner, float speedScale = 1f)
        {
            boss = owner;
            gameObject.SetActive(true);
            cc.enabled = false;
            transform.position = position;
            Vector3 f = vel; f.y = 0;
            transform.rotation = Quaternion.LookRotation(f.sqrMagnitude > 0.01f ? f : Vector3.forward);
            cc.enabled = true;
            velocity = vel;
            knock = Vector3.zero;
            Health = maxHealth;
            life = 0f;
            beeps = 0;
            flash = 0f;
            moveSpeedScaled = moveSpeed * speedScale;
            if (!Active.Contains(this)) Active.Add(this);
            SetState(Phase.Falling);
            if (fuseSpark) fuseSpark.Play(true);
        }

        float moveSpeedScaled;

        void SetState(Phase p) { State = p; stateTime = 0f; }

        public void Despawn()
        {
            State = Phase.Inactive;
            Active.Remove(this);
            gameObject.SetActive(false);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || State == Phase.Inactive) return;
            stateTime += dt;
            life += dt;
            var pc = PlayerController.Instance;
            Vector3 toPlayer = pc != null ? pc.transform.position - transform.position : Vector3.zero;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;

            Vector3 move = Vector3.zero;
            switch (State)
            {
                case Phase.Falling:
                    velocity.y -= gravity * dt;
                    move = velocity * dt;
                    model.Rotate(Vector3.right, 220f * dt, Space.Self);
                    if (cc.isGrounded && velocity.y < 0f)
                    {
                        model.localRotation = Quaternion.identity;
                        squash = 0.35f;
                        FXManager.Spawn(FXType.LandDust, transform.position, Quaternion.identity, 0.6f);
                        AudioManager.Play(Sfx.Land, transform.position, 0.7f, 1.5f);
                        AudioManager.Play(Sfx.Knock, transform.position, 0.5f, 1.8f);
                        velocity = Vector3.zero;
                        SetState(Phase.Waking);
                    }
                    break;

                case Phase.Waking:
                    // shake awake, then turn toward the prey
                    model.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(stateTime * 40f) * 8f * (1f - stateTime / 0.6f));
                    Face(toPlayer, dt);
                    if (stateTime > 0.6f) { model.localRotation = Quaternion.identity; SetState(Phase.Chasing); }
                    move = Vector3.down * 2f * dt;
                    break;

                case Phase.Chasing:
                {
                    bool hunting = pc != null && !pc.IsDead;
                    if (hunting) Face(toPlayer, dt);
                    // keep a little space from the other bombs
                    Vector3 sep = Vector3.zero;
                    foreach (var o in Active)
                    {
                        if (o == this || o.State == Phase.Inactive) continue;
                        Vector3 d = transform.position - o.transform.position; d.y = 0;
                        float m = d.magnitude;
                        if (m < 1.1f && m > 1e-3f) sep += d / m * (1.1f - m);
                    }
                    float spd = hunting ? moveSpeedScaled : 0f;
                    move = (transform.forward * spd + sep * 3f) * dt + Vector3.down * 4f * dt;
                    gait += dt * (hunting ? 13f : 4f);
                    if ((hunting && dist < triggerDistance) || life > lifetime) { SetState(Phase.Arming); AudioManager.Play(Sfx.JawSnap, transform.position, 0.5f, 1.8f); }
                    break;
                }

                case Phase.Arming:
                {
                    move = Vector3.down * 4f * dt;
                    // blink twice, explode on the third beat
                    float beat = armTime / 2.2f;
                    int b = Mathf.FloorToInt(stateTime / beat);
                    if (b >= beeps && beeps < 2)
                    {
                        beeps++;
                        flash = 1f;
                        AudioManager.Play(Sfx.BombBeep, transform.position, 0.9f, beeps == 1 ? 1f : 1.12f);
                    }
                    squash = -0.06f - 0.12f * (stateTime / armTime);   // swells up
                    if (stateTime >= armTime) { Explode(true); return; }
                    break;
                }
            }

            knock = Vector3.Lerp(knock, Vector3.zero, Ease.Damp(8f, dt));
            if (cc.enabled) cc.Move(move + knock * dt);
            if (transform.position.y < -10f) { Despawn(); return; }

            Animate(dt);
        }

        void Face(Vector3 dir, float dt)
        {
            if (dir.sqrMagnitude < 1e-3f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), turnSpeed * dt);
        }

        void Animate(float dt)
        {
            bool walking = State == Phase.Chasing;
            float swing = walking ? Mathf.Sin(gait) * 38f : 0f;
            for (int i = 0; i < legs.Length; i++)
            {
                float s = (i == 0 || i == 3) ? swing : -swing;    // diagonal pairs, like a croc
                legs[i].localRotation = Quaternion.Slerp(legs[i].localRotation, Quaternion.AngleAxis(s, Vector3.right) * legRest[i], Ease.Damp(18f, dt));
            }
            if (walking)
                model.localRotation = Quaternion.Euler(Mathf.Sin(gait * 2f) * 3f, 0f, Mathf.Sin(gait) * 9f);
            float bob = walking ? Mathf.Abs(Mathf.Sin(gait)) * 0.05f : 0f;
            squash = Mathf.Lerp(squash, State == Phase.Arming ? squash : 0f, Ease.Damp(10f, dt));
            model.localPosition = new Vector3(0, bob, 0);
            model.localScale = new Vector3(1f + squash * 0.5f, 1f - squash, 1f + squash * 0.5f);

            // flash: whole body red on each blink, eyes always smoulder
            flash = Mathf.MoveTowards(flash, 0f, dt * 4.5f);
            if (bodyRenderer == null) return;
            var mats = bodyRenderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                bodyRenderer.GetPropertyBlock(mpb, i);
                Color e = new Color(1f, 0.1f, 0.04f) * (flash * flash * 3.2f);
                if (i == eyeMaterialIndex) e += EyeGlow * (State == Phase.Falling ? 0.3f : 1f);
                mpb.SetColor(EmissionId, e);
                bodyRenderer.SetPropertyBlock(mpb, i);
            }
        }

        public bool ReceiveHit(in DamageInfo info)
        {
            if (!IsAlive) return false;
            Health -= info.damage;
            flash = Mathf.Max(flash, 0.6f);
            squash = 0.2f;
            knock += info.direction * (info.weight == ImpactWeight.Light ? 5f : 9f);
            FXManager.Spawn(FXType.HitSpark, info.point, Quaternion.LookRotation(-info.direction), 0.8f);
            AudioManager.Play(Sfx.MetalHit, info.point, 0.7f, 1.45f);
            if (Health <= 0f) Explode(false);
            return true;
        }

        void Explode(bool armed)
        {
            Vector3 c = transform.position;
            if (fuseSpark) fuseSpark.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Despawn();
            // armed: the full blast hurts the player; shot down: a pop that only hurts the boss
            if (armed) Blast.Detonate(c, blastRadius, blastDamage, true, gameObject, 0.85f, boss, 40f);
            else Blast.Detonate(c, blastRadius * 0.9f, 0f, false, gameObject, 0.55f, boss, 70f);
        }
    }
}
