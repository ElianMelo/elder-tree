using System.Collections.Generic;
using ElderTree.Audio;
using ElderTree.Equipment;
using ElderTree.FX;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree.Masks
{
    /// <summary>
    /// A won mask, waiting to be claimed. It arrives as a show: a column of light strikes down where it will rest, the
    /// mask drifts down the beam spinning, and lands in a burst. Then it hovers and turns over a glowing ring with the
    /// beam standing over it (in the mask's own colour) so it can be found from across the arena; the HUD points at
    /// it too (<see cref="Active"/>). Walking into it picks it up. Nobody leaves without their mask: after
    /// <see cref="autoCollectAfter"/> seconds it comes to the player by itself.
    /// </summary>
    public class MaskPickup : MonoBehaviour
    {
        /// <summary>Every mask waiting to be claimed on this machine (the HUD's markers).</summary>
        public static readonly List<MaskPickup> Active = new();

        public MaskData mask;
        [Tooltip("Hover / spin root.")]
        public Transform model;
        [Tooltip("The mask's own model is put here when it spawns (MaskData.maskModel).")]
        public Transform meshSlot;
        public Light glow;
        public ParticleSystem motes;
        [Tooltip("Column of light over the mask (crossed quads, additive).")]
        public MeshRenderer beam;
        [Tooltip("Glowing ring on the ground under it.")]
        public MeshRenderer ring;
        [Tooltip("A ring that swells out of the ground ring every so often.")]
        public MeshRenderer pulse;
        public float pickupRadius = 1.4f;
        public float hover = 1.2f;
        public float modelScale = 1.45f;
        [Header("Arrival")]
        public float dropHeight = 7.5f;
        public float dropTime = 1.4f;
        public float beamHeight = 14f;
        public float beamWidth = 0.9f;
        [Tooltip("The beam starts this far above the mask's centre, so it never crosses the mask itself.")]
        public float beamGap = 0.45f;
        [Header("Nobody leaves without it")]
        [Tooltip("Seconds after landing before the mask flies to its player by itself.")]
        public float autoCollectAfter = 15f;
        public float homingSpeed = 11f;

        enum Phase { Falling, Waiting, Homing }

        Phase phase;
        float t, phaseT, baseIntensity, pulseT, beamK = 1f;
        Color tint = Color.white;
        Vector3 homeVel;
        MaterialPropertyBlock mpb;
        bool taken;

        /// <summary>Seconds until it flies to its player (0 once it is on its way).</summary>
        public float SecondsLeft => phase == Phase.Waiting ? Mathf.Max(0f, autoCollectAfter - phaseT) : phase == Phase.Falling ? autoCollectAfter : 0f;
        public bool Homing => phase == Phase.Homing;
        public Color Tint => tint;
        /// <summary>Where the HUD marker sits (just over the mask).</summary>
        public Vector3 MarkerPoint => (model != null ? model.position : transform.position + Vector3.up * hover) + Vector3.up * 0.75f;

        public static MaskPickup Spawn(MaskPickup prefab, MaskData m, Vector3 groundPos)
        {
            if (prefab == null || m == null) return null;
            var p = Instantiate(prefab, groundPos, Quaternion.identity);
            p.Setup(m);
            using (Coop.LocalOnly())        // co-op: every player gets their own mask; nobody sees anyone else's
            {
                AudioManager.Play(Sfx.ChargeUp, groundPos + Vector3.up * 3f, 0.7f, 0.7f);
                FXManager.Spawn(FXType.ChargeFlash, groundPos + Vector3.up * 0.3f, Quaternion.identity, 1.2f);
            }
            return p;
        }

        void Setup(MaskData m)
        {
            mask = m;
            // the mask's own face, not a stand-in
            if (meshSlot != null)
            {
                for (int i = meshSlot.childCount - 1; i >= 0; i--) Destroy(meshSlot.GetChild(i).gameObject);
                if (m.maskModel != null)
                {
                    var go = Instantiate(m.maskModel, meshSlot, false);
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one * modelScale;
                    foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
            }
            // its colour: the mask's cinematic accent, brought up to full brightness so it reads in the dark
            Color c = m.vortexA;
            float mx = Mathf.Max(0.001f, c.maxColorComponent);
            tint = new Color(c.r / mx, c.g / mx, c.b / mx, 1f);
            if (glow) glow.color = tint;
            if (motes)
            {
                var main = motes.main;
                main.startColor = new ParticleSystem.MinMaxGradient(tint, Color.Lerp(tint, Color.white, 0.6f));
            }
        }

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        void Start()
        {
            if (glow) baseIntensity = glow.intensity;
            if (motes) motes.Play(true);
            mpb = new MaterialPropertyBlock();
            if (beam) beam.GetComponent<MeshFilter>().sharedMesh = BeamMesh();
            if (ring) ring.GetComponent<MeshFilter>().sharedMesh = QuadMesh();
            if (pulse) pulse.GetComponent<MeshFilter>().sharedMesh = QuadMesh();
            if (model) model.localPosition = Vector3.up * dropHeight;
            Paint(beam, tint, 1f);
        }

        void Update()
        {
            if (taken) return;
            float dt = Time.deltaTime;
            t += dt;
            phaseT += dt;
            switch (phase)
            {
                case Phase.Falling: TickFalling(); break;
                case Phase.Waiting: TickWaiting(); break;
                case Phase.Homing: TickHoming(dt); break;
            }
            TickLight(dt);
        }

        // ------------------------------------------------------------------ arrival
        void TickFalling()
        {
            float k = Mathf.Clamp01(phaseT / dropTime);
            float e = 1f - (1f - k) * (1f - k) * (1f - k);           // eases into the hover
            if (model)
            {
                model.localPosition = Vector3.up * Mathf.Lerp(dropHeight, hover, e);
                model.localRotation = Quaternion.Euler(Mathf.Sin(t * 5f) * 14f * (1f - k), t * 50f + (1f - e) * 900f, 0f);
            }
            if (k < 1f) return;
            phase = Phase.Waiting;
            phaseT = 0f;
            using (Coop.LocalOnly())
            {
                Vector3 p = transform.position;
                FXManager.Spawn(FXType.ChargeFlash, p + Vector3.up * hover, Quaternion.identity, 2.4f);
                FXManager.Spawn(FXType.Dust, p, Quaternion.identity, 1.6f);
                AudioManager.Play(Sfx.MaskReceived, p, 0.75f, 1.2f);
                AudioManager.Play(Sfx.Slam, p, 0.35f, 1.5f);
            }
            pulseT = 0f;
            var pc = PlayerController.Instance;
            if (pc != null && (pc.transform.position - transform.position).sqrMagnitude < 30f * 30f)
                CameraSystem.CameraDirector.Instance?.Shake(0.25f);
        }

        // ------------------------------------------------------------------ waiting to be claimed
        void TickWaiting()
        {
            if (model)
            {
                model.localPosition = Vector3.up * (hover + Mathf.Sin(t * 2.1f) * 0.08f);
                model.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.3f) * 8f, t * 50f, 0f);
            }
            var pc = PlayerController.Instance;
            if (pc == null || pc.IsDead) return;
            if (phaseT >= autoCollectAfter) { BeginHoming(); return; }
            Vector3 d = pc.transform.position - transform.position; d.y = 0f;
            if (d.magnitude <= pickupRadius && CanTake(pc)) Take();
        }

        void BeginHoming()
        {
            phase = Phase.Homing;
            phaseT = 0f;
            // the mask leaves its post: from here on, the whole pickup (motes and all) travels with it
            if (model)
            {
                Vector3 w = model.position;
                transform.position = w;
                model.localPosition = Vector3.zero;
            }
            homeVel = Vector3.up * 4f;
            using (Coop.LocalOnly())
            {
                AudioManager.Play(Sfx.WhooshHeavy, transform.position, 0.6f, 0.8f);
                FXManager.Spawn(FXType.ChargeFlash, transform.position, Quaternion.identity, 1.4f);
            }
        }

        void TickHoming(float dt)
        {
            var pc = PlayerController.Instance;
            if (pc == null) return;
            Vector3 target = pc.transform.position + Vector3.up * 1.5f;
            Vector3 to = target - transform.position;
            float dist = to.magnitude;
            // steer: a short climb, then an accelerating curve in to the chest
            float speed = Mathf.Lerp(3f, homingSpeed, Mathf.Clamp01(phaseT / 1.2f));
            Vector3 want = dist > 0.01f ? to / dist * speed : Vector3.zero;
            if (dist < 2.2f && !CanTake(pc))
            {
                // the player is busy (mid-swing, rolling): circle their head until they settle
                Vector3 orbit = pc.transform.position + Vector3.up * 2.1f + Quaternion.Euler(0f, t * 140f, 0f) * Vector3.forward * 1.1f;
                want = (orbit - transform.position) * 4f;
            }
            homeVel = Vector3.Lerp(homeVel, want, 1f - Mathf.Exp(-5f * dt));
            transform.position += homeVel * dt;
            if (model) model.localRotation = Quaternion.Euler(0f, t * 420f, 0f);
            if (dist < 0.9f && CanTake(pc) && !pc.IsDead) Take();
        }

        static bool CanTake(PlayerController pc) =>
            pc.State == PlayerState.Locomotion || pc.State == PlayerState.Landing || pc.State == PlayerState.Aim;

        void Take()
        {
            taken = true;
            var ms = MaskSystem.Instance;
            if (ms != null) ms.Acquire(mask);
            Destroy(gameObject);
        }

        // ------------------------------------------------------------------ light show
        void TickLight(float dt)
        {
            // the beam strikes in wide and white-hot, settles into a slim column, and is drawn up after the mask
            // when it flies off
            float strike = phase == Phase.Falling ? 1f - Mathf.Clamp01(phaseT / dropTime) : 0f;
            float target = phase == Phase.Homing ? 0f : 1f;
            beamK = Mathf.MoveTowards(beamK, target, dt * (phase == Phase.Homing ? 2.5f : 1f));
            float breathe = 0.82f + 0.18f * Mathf.Sin(t * 2.4f);
            if (beam)
            {
                // it rises from just over the mask (following it down the fall), never through it
                float baseY = (model != null ? model.localPosition.y : hover) + beamGap * modelScale / 1.45f;
                beam.transform.localPosition = Vector3.up * baseY;
                beam.transform.localScale = new Vector3(beamWidth * (1f + 2.2f * strike * strike) * (0.9f + 0.1f * breathe),
                                                        beamHeight * Mathf.Max(0.05f, beamK), beamWidth * (1f + 2.2f * strike * strike));
                Paint(beam, Color.Lerp(tint, Color.white, strike * 0.7f), (0.55f + 0.45f * strike) * breathe * beamK);
                beam.enabled = beamK > 0.01f;
            }
            if (ring)
            {
                float on = phase == Phase.Falling ? Mathf.Clamp01(phaseT / dropTime) : beamK;
                ring.transform.localScale = Vector3.one * (1.9f + 0.12f * Mathf.Sin(t * 3f));
                ring.transform.localRotation = Quaternion.Euler(90f, t * 20f, 0f);
                Paint(ring, tint, 0.9f * on * breathe);
                ring.enabled = on > 0.01f;
            }
            if (pulse)
            {
                pulseT += dt;
                const float Every = 1.8f;
                float k = phase == Phase.Waiting ? Mathf.Repeat(pulseT, Every) / Every : 1f;
                pulse.transform.localScale = Vector3.one * Mathf.Lerp(1.6f, 5.5f, 1f - (1f - k) * (1f - k));
                pulse.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Paint(pulse, tint, (1f - k) * 0.8f);
                pulse.enabled = phase == Phase.Waiting;
            }
            if (glow)
            {
                float rise = phase == Phase.Falling ? Mathf.Clamp01(phaseT / dropTime) : 1f;
                glow.intensity = baseIntensity * (0.75f + 0.25f * Mathf.Sin(t * 3.3f)) * (rise + 2f * strike);
            }
        }

        void Paint(Renderer r, Color c, float alpha)
        {
            if (r == null) return;
            mpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            c.a = Mathf.Clamp01(alpha);
            mpb.SetColor("_BaseColor", c);
            r.SetPropertyBlock(mpb);
        }

        // ------------------------------------------------------------------ meshes (built once, shared)
        static Mesh beamMesh, quadMesh;

        /// <summary>Three crossed vertical quads, 1 wide and 1 tall from the ground up: a soft column from every side.</summary>
        static Mesh BeamMesh()
        {
            if (beamMesh != null) return beamMesh;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int k = 0; k < 3; k++)
            {
                float a = k * Mathf.PI / 3f;
                Vector3 side = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.5f;
                int b = v.Count;
                v.Add(-side); v.Add(side); v.Add(side + Vector3.up); v.Add(-side + Vector3.up);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                tri.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            beamMesh = new Mesh { name = "MaskBeam" };
            beamMesh.SetVertices(v); beamMesh.SetUVs(0, uv); beamMesh.SetTriangles(tri, 0);
            beamMesh.RecalculateBounds();
            return beamMesh;
        }

        /// <summary>A unit quad in the XY plane (laid flat by its transform).</summary>
        static Mesh QuadMesh()
        {
            if (quadMesh != null) return quadMesh;
            quadMesh = new Mesh { name = "MaskQuad" };
            quadMesh.SetVertices(new List<Vector3> { new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0), new(0.5f, 0.5f, 0), new(-0.5f, 0.5f, 0) });
            quadMesh.SetUVs(0, new List<Vector2> { new(0, 0), new(1, 0), new(1, 1), new(0, 1) });
            quadMesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            quadMesh.RecalculateBounds();
            return quadMesh;
        }
    }
}
