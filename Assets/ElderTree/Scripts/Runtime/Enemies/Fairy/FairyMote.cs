using ElderTree.Audio;
using ElderTree.Combat;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree.Enemies
{
    /// <summary>
    /// One of the Fairy's motes of cold light. Pooled by the boss. It can:
    ///  - <see cref="Hold"/>: sit where the boss puts it (gathering in a hand, blooming in the halo behind her head);
    ///  - <see cref="Fire"/>: fly, optionally homing for a while, and burst on the player, the ground or a wall;
    ///  - <see cref="Mine"/>: hang in the air (phase 2 flight trail), arm, and burst when you come near.
    /// A roll's i-frames let it pass straight through you. Only the player can be hit.
    /// </summary>
    public class FairyMote : MonoBehaviour
    {
        public Transform core;
        public Renderer coreRenderer;
        public TrailRenderer trail;
        public ParticleSystem pop;
        public Light glow;
        [Tooltip("World layers a mote bursts on (walls, props, the ground).")]
        public LayerMask worldMask = 1;

        enum Mode { Idle, Held, Flying, Mine, Popping }
        Mode mode = Mode.Idle;
        Vector3 vel;
        float speed, homing, homingLeft, life, radius, damage, poise, age, armAt, fuse = -1f, size = 1f, glowBase;
        ImpactWeight weight;
        GameObject source;
        MaterialPropertyBlock mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        Color baseColor = Color.white;

        public bool Free => mode == Mode.Idle;
        public bool Flying => mode == Mode.Flying;

        // co-op: motes home on the nearest tree, and burst on whichever tree they touch
        PlayerController Player => Coop.Nearest(transform.position);
        PlayerController touched;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            if (glow) glowBase = glow.intensity;
            if (coreRenderer && coreRenderer.sharedMaterial && coreRenderer.sharedMaterial.HasProperty(BaseColorId))
                baseColor = coreRenderer.sharedMaterial.GetColor(BaseColorId);
            Hide();
        }

        void Hide()
        {
            mode = Mode.Idle;
            if (core) core.gameObject.SetActive(false);
            if (trail) { trail.emitting = false; trail.Clear(); }
            if (glow) glow.enabled = false;
            gameObject.SetActive(false);
        }

        void Show(float s)
        {
            gameObject.SetActive(true);
            size = s;
            transform.localScale = Vector3.one * s;
            if (core) core.gameObject.SetActive(true);
            if (glow) { glow.enabled = true; glow.intensity = glowBase; }
            age = 0f;
            fuse = -1f;
            Tint(1f);
        }

        /// <summary>Sit at <paramref name="pos"/> (call every frame while the boss holds it). <paramref name="grow"/> 0..1 sizes it.</summary>
        public void Hold(Vector3 pos, float grow)
        {
            if (mode != Mode.Held) { Show(0.1f); mode = Mode.Held; if (trail) trail.emitting = false; }
            size = Mathf.Lerp(0.15f, 1f, Ease.OutCubic(Mathf.Clamp01(grow)));
            transform.localScale = Vector3.one * size;
            transform.position = pos;
        }

        /// <summary>Fly from <paramref name="pos"/> along <paramref name="dir"/>; homing in deg/s for <paramref name="homingTime"/>.</summary>
        public void Fire(Vector3 pos, Vector3 dir, float speed, float homing, float homingTime, float damage, float poise,
                         GameObject owner, float life = 4f, float radius = 0.32f, ImpactWeight weight = ImpactWeight.Light)
        {
            if (mode != Mode.Held) Show(1f);
            size = 1f;
            transform.localScale = Vector3.one;
            transform.position = pos;
            mode = Mode.Flying;
            this.speed = speed;
            this.homing = homing;
            homingLeft = homingTime;
            this.damage = damage;
            this.poise = poise;
            this.life = life;
            this.radius = radius;
            this.weight = weight;
            source = owner;
            vel = (dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward) * speed;
            age = 0f;
            if (trail) { trail.Clear(); trail.emitting = true; }
        }

        /// <summary>Hang at <paramref name="pos"/>, arm after <paramref name="armTime"/>, burst when the player comes near.</summary>
        public void Mine(Vector3 pos, float armTime, float damage, GameObject owner, float life = 6f)
        {
            Show(0.75f);
            mode = Mode.Mine;
            transform.position = pos;
            armAt = armTime;
            this.damage = damage;
            poise = 30f;
            this.life = life;
            radius = 1.7f;
            weight = ImpactWeight.Heavy;
            source = owner;
            vel = Vector3.zero;
            if (trail) trail.emitting = false;
        }

        /// <summary>Gone without a burst (boss reset, form change, fight over).</summary>
        public void Cancel() => Hide();

        void Tint(float k)
        {
            if (!coreRenderer) return;
            coreRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, baseColor * k);
            coreRenderer.SetPropertyBlock(mpb);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            age += dt;
            if (core && mode != Mode.Popping) core.Rotate(new Vector3(140f, 260f, 60f) * dt, Space.Self);
            if (glow && mode != Mode.Popping) glow.intensity = glowBase * size * (0.8f + 0.2f * Mathf.Sin(age * 23f));
            switch (mode)
            {
                case Mode.Flying: Fly(dt); break;
                case Mode.Mine: MineTick(dt); break;
                case Mode.Popping:
                    if (glow) glow.intensity = Mathf.MoveTowards(glow.intensity, 0f, glowBase * 6f * dt);
                    if (age > 0.9f && (pop == null || !pop.IsAlive(true))) Hide();
                    break;
            }
        }

        void Fly(float dt)
        {
            if (homingLeft > 0f && homing > 0f && Player != null && !Player.IsDead)
            {
                homingLeft -= dt;
                Vector3 to = PlayerChest - transform.position;
                vel = Vector3.RotateTowards(vel, to.normalized * speed, homing * Mathf.Deg2Rad * dt, 0f);
            }
            Vector3 step = vel * dt;
            float dist = step.magnitude;
            if (dist > 1e-5f && Physics.Raycast(transform.position, step / dist, out var hit, dist + 0.1f, worldMask, QueryTriggerInteraction.Ignore))
            {
                transform.position = hit.point + hit.normal * 0.1f;
                Burst(false);
                return;
            }
            transform.position += step;
            if (TouchesPlayer(radius) && HitPlayer(vel)) { Burst(true); return; }
            if (transform.position.y < Blast.Ground(transform.position).y + 0.05f) { Burst(false); return; }
            if (age > life) Burst(false);
        }

        void MineTick(float dt)
        {
            // bobs where it was dropped; dim until armed, then a slow pulse; a near player lights the fuse
            transform.position += Vector3.up * Mathf.Sin(age * 3f) * 0.12f * dt;
            bool armed = age >= armAt;
            float pulse = armed ? 0.75f + 0.25f * Mathf.Sin(age * (fuse >= 0f ? 40f : 7f)) : 0.45f;
            Tint(pulse * (fuse >= 0f ? 1.6f : 1f));
            transform.localScale = Vector3.one * size * (fuse >= 0f ? 1f + 0.3f * Mathf.Sin(age * 40f) : 1f);
            if (armed && fuse < 0f && TouchesPlayer(radius * 0.85f))
            {
                fuse = 0.38f;
                AudioManager.Play(Sfx.OrbCharge, transform.position, 0.5f, 1.9f);
            }
            if (fuse >= 0f && (fuse -= dt) <= 0f)
            {
                if (TouchesPlayer(radius)) HitPlayer(PlayerChest - transform.position);
                Burst(true, 1.5f);
                return;
            }
            if (age > life) Burst(false, 0.6f);
        }

        Vector3 PlayerChest { get { var p = touched != null ? touched : Player; return p != null ? p.transform.position + Vector3.up * 1.1f : Vector3.zero; } }

        /// <summary>Distance from the mote to the player's body (a capsule from the ankles to the head).</summary>
        bool TouchesPlayer(float r)
        {
            touched = null;
            foreach (var p in PlayerController.All)
            {
                if (p == null || p.IsDead) continue;
                Vector3 a = p.transform.position + Vector3.up * 0.25f, b = p.transform.position + Vector3.up * 1.75f;
                Vector3 x = transform.position;
                float t = Mathf.Clamp01(Vector3.Dot(x - a, b - a) / (b - a).sqrMagnitude);
                if (Vector3.Distance(x, a + (b - a) * t) < r + 0.35f) { touched = p; return true; }
            }
            return false;
        }

        bool HitPlayer(Vector3 dir)
        {
            dir.y = 0f;
            var victim = touched != null ? touched : Player;
            if (victim == null) return false;
            return victim.TakeHit(new DamageInfo
            {
                damage = damage, poiseDamage = poise, point = transform.position,
                direction = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward, weight = weight, source = source
            });
        }

        void Burst(bool big, float scale = 1f)
        {
            mode = Mode.Popping;
            age = 0f;
            if (core) core.gameObject.SetActive(false);
            if (trail) trail.emitting = false;
            if (glow) glow.intensity = glowBase * (big ? 3f : 1.6f) * scale;
            if (pop)
            {
                pop.transform.localScale = Vector3.one * (big ? 1.3f : 0.8f) * scale;
                pop.Play(true);
            }
            AudioManager.Play(Sfx.OrbImpact, transform.position, big ? 0.55f * scale : 0.3f, Random.Range(1.6f, 1.9f));
        }
    }
}
