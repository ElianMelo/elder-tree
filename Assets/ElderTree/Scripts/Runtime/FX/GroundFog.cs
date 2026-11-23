using System.Collections.Generic;
using ElderTree.Enemies;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree.FX
{
    /// <summary>
    /// Ground-hugging swamp fog in the style of the modern Ocarina of Time remake (and AtSaturn's CC0 Godot
    /// recreation of it): not a true volume but a stack of shader shells that fakes one.
    ///
    /// * <b>Hugs the terrain:</b> the ground under the arena is ray-cast once into a height texture; the shells
    ///   compute height-above-ground per pixel, so fog pools in hollows and thins over banks.
    /// * <b>Bodies push it like a fluid:</b> a small CPU grid holds "cleared" + a flow velocity. Moving bodies
    ///   stamp a hole and drag the flow along (leaving little eddies behind), the flow advects the hole, and
    ///   the fog slowly refills.
    /// * <b>Impacts blow it out in a ring:</b> <see cref="Poof"/> (explosions, slams, landings) clears an expanding ring.
    /// * <b>Lit:</b> moonlight through the shadow map (pools of light between the trees) and every FogLight.
    ///
    /// The field is shared through globals (FogCommon.hlsl) so the volumetric haze parts near the ground too.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class GroundFog : MonoBehaviour
    {
        public static GroundFog Instance { get; private set; }

        [Header("Area")]
        public Vector3 center;
        public float size = 64f;
        [Tooltip("Field resolution (cells per side). 96 over 64 m = 0.67 m cells.")]
        public int resolution = 96;
        [Tooltip("Collider sampled for the ground heights (the swamp terrain). Null = physics ray-cast against everything.")]
        public Collider ground;
        public LayerMask groundMask = 1;

        [Header("Shells")]
        public Material material;
        public int layers = 14;
        public float bottom = 0.06f, top = 1.55f;

        [Header("Fluid")]
        [Tooltip("Per second: how fast cleared fog flows back in.")]
        public float refill = 0.32f;
        [Tooltip("Flow velocity damping per second.")]
        public float drag = 1.6f;
        [Tooltip("How strongly moving bodies push the fog.")]
        public float pushStrength = 1f;
        public float diffusion = 0.18f;

        // field
        float[] clear, vx, vz, tmpC, tmpX, tmpZ;
        Texture2D fieldTex, heightTex;
        Color[] pixels;
        Mesh mesh;
        float cell;

        struct Body { public Transform t; public float radius; public float maxHeight; public Vector3 last; }
        readonly List<Body> bodies = new();
        struct Ring { public Vector3 p; public float r, max, speed, age; }
        readonly List<Ring> rings = new();

        static readonly int FieldId = Shader.PropertyToID("_GFFieldTex"), HeightId = Shader.PropertyToID("_GFHeightTex"),
            RectId = Shader.PropertyToID("_GFRect");

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) { Instance = null; Shader.SetGlobalVector(RectId, Vector4.zero); }
            if (fieldTex) Destroy(fieldTex);
            if (heightTex) Destroy(heightTex);
            if (mesh) Destroy(mesh);
        }

        void Start()
        {
            int n = resolution;
            cell = size / n;
            clear = new float[n * n]; vx = new float[n * n]; vz = new float[n * n];
            tmpC = new float[n * n]; tmpX = new float[n * n]; tmpZ = new float[n * n];
            pixels = new Color[n * n];
            fieldTex = new Texture2D(n, n, TextureFormat.RGBAHalf, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "GroundFogField" };
            BakeHeights();
            BuildShells();
            CollectBodies();
            Upload();
        }

        // ------------------------------------------------------------------ setup
        Vector3 Min => center - new Vector3(size, 0, size) * 0.5f;

        void BakeHeights()
        {
            const int H = 128;
            heightTex = new Texture2D(H, H, TextureFormat.RFloat, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "GroundFogHeight" };
            var h = new Color[H * H];
            var min = Min;
            for (int y = 0; y < H; y++)
            for (int x = 0; x < H; x++)
            {
                var p = new Vector3(min.x + (x + 0.5f) / H * size, center.y + 60f, min.z + (y + 0.5f) / H * size);
                var ray = new Ray(p, Vector3.down);
                float gy = center.y - 1f;
                if (ground != null) { if (ground.Raycast(ray, out var hit, 200f)) gy = hit.point.y; }
                else if (Physics.Raycast(ray, out var hit2, 200f, groundMask, QueryTriggerInteraction.Ignore)) gy = hit2.point.y;
                h[y * H + x] = new Color(gy, 0, 0, 0);
            }
            heightTex.SetPixels(h);
            heightTex.Apply(false, false);
            Shader.SetGlobalTexture(HeightId, heightTex);
        }

        void BuildShells()
        {
            if (material == null) return;
            // one quad per layer, bottom to top (the camera is usually above most of the stack)
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
            float hs = size * 0.5f;
            for (int k = 0; k < layers; k++)
            {
                float t = layers == 1 ? 0f : k / (float)(layers - 1);
                float y = Mathf.Lerp(bottom, top, t * t * 0.4f + t * 0.6f);   // a little denser near the ground
                int b = v.Count;
                v.Add(new Vector3(-hs, y, -hs)); v.Add(new Vector3(hs, y, -hs)); v.Add(new Vector3(hs, y, hs)); v.Add(new Vector3(-hs, y, hs));
                for (int j = 0; j < 4; j++) uv.Add(new Vector2(t, k));
                idx.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            mesh = new Mesh { name = "GroundFogShells" };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(idx, 0);
            mesh.bounds = new Bounds(new Vector3(0, (bottom + top) * 0.5f, 0), new Vector3(size, top - bottom + 2f, size));
            var go = new GameObject("GroundFogShells");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(center.x, 0, center.z);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        void CollectBodies()
        {
            foreach (var p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None)) AddBody(p.transform, 0.85f, 2.5f);
            foreach (var b in FindObjectsByType<BossBase>(FindObjectsInactive.Include, FindObjectsSortMode.None)) AddBody(b.transform, 2.4f, 6f);
        }

        /// <summary>Register a body that pushes the fog while it is within maxHeight of the ground.</summary>
        public void AddBody(Transform t, float radius, float maxHeight = 3f)
        {
            if (t == null) return;
            foreach (var b in bodies) if (b.t == t) return;
            bodies.Add(new Body { t = t, radius = radius, maxHeight = maxHeight, last = t.position });
        }

        /// <summary>Blow the ground fog out in an expanding ring (explosions, slams, hard landings).</summary>
        public static void Poof(Vector3 p, float radius, float speed = 9f)
        {
            if (Instance == null || radius <= 0.1f) return;
            Instance.rings.Add(new Ring { p = p, r = 0f, max = radius, speed = speed, age = 0f });
            Instance.Stamp(p, Mathf.Min(radius * 0.4f, 1.5f), 1f, Vector3.zero, 0f);
        }

        // ------------------------------------------------------------------ sim
        void Update()
        {
            if (clear == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f) { Upload(); return; }

            // living cannonballs come and go: pick them up as they appear
            foreach (var lb in LivingBomb.Active) if (lb != null) AddBody(lb.transform, 0.7f, 2f);

            for (int i = bodies.Count - 1; i >= 0; i--)
            {
                var b = bodies[i];
                if (b.t == null) { bodies.RemoveAt(i); continue; }
                Vector3 p = b.t.position;
                Vector3 vel = (p - b.last) / dt;
                b.last = p;
                bodies[i] = b;
                if (!b.t.gameObject.activeInHierarchy) continue;
                float hAbove = p.y - GroundHeightApprox(p);
                if (hAbove > b.maxHeight) continue;
                float k = 1f - Mathf.Clamp01(hAbove / b.maxHeight);
                float speed = new Vector2(vel.x, vel.z).magnitude;
                // a standing body keeps a small hole; a moving one ploughs a wider one and drags the flow
                Stamp(p, b.radius * (1f + Mathf.Min(speed, 8f) * 0.06f), (0.55f + Mathf.Min(speed * 0.12f, 0.45f)) * k * pushStrength,
                      vel, k * pushStrength);
            }

            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var r = rings[i];
                r.age += dt;
                float prev = r.r;
                r.r = Mathf.Min(r.max, r.r + r.speed * dt * (1.2f - r.r / r.max));
                StampRing(r.p, prev, r.r, r.speed * (1f - r.r / r.max) + 1f);
                if (r.r >= r.max - 0.05f || r.age > 2f) rings.RemoveAt(i); else rings[i] = r;
            }

            Step(dt);
            Upload();
        }

        float GroundHeightApprox(Vector3 p) => Blast.Floor(p).y;

        int N => resolution;

        void Stamp(Vector3 p, float radius, float amount, Vector3 vel, float drag)
        {
            var min = Min;
            float fx = (p.x - min.x) / cell, fz = (p.z - min.z) / cell;
            float rc = radius / cell;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(fx - rc - 1)), x1 = Mathf.Min(N - 1, Mathf.CeilToInt(fx + rc + 1));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(fz - rc - 1)), z1 = Mathf.Min(N - 1, Mathf.CeilToInt(fz + rc + 1));
            Vector2 v2 = new Vector2(vel.x, vel.z);
            float sp = v2.magnitude;
            Vector2 side = sp > 0.01f ? new Vector2(-v2.y, v2.x) / sp : Vector2.zero;
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - fx, dz = z + 0.5f - fz;
                float d = Mathf.Sqrt(dx * dx + dz * dz) / Mathf.Max(rc, 0.01f);
                if (d >= 1.6f) continue;
                int i = z * N + x;
                float w = Mathf.Clamp01(1f - d);
                w = w * w * (3f - 2f * w);
                clear[i] = Mathf.Max(clear[i], w * amount);
                if (drag <= 0f) continue;
                // drag the flow with the body, push it out to the sides, and a swirl on each flank (eddies)
                float ring = Mathf.Clamp01(1.6f - d) * Mathf.Clamp01(d * 1.5f);
                Vector2 outward = new Vector2(dx, dz) / Mathf.Max(0.001f, Mathf.Sqrt(dx * dx + dz * dz));
                float flank = Vector2.Dot(outward, side);
                Vector2 add = v2 * (0.55f * w) + outward * (Mathf.Min(sp, 6f) * 0.35f * ring) - side * (flank * Mathf.Min(sp, 6f) * 0.25f * ring);
                vx[i] = Mathf.Lerp(vx[i], add.x, 0.35f * drag);
                vz[i] = Mathf.Lerp(vz[i], add.y, 0.35f * drag);
            }
        }

        void StampRing(Vector3 p, float r0, float r1, float speed)
        {
            var min = Min;
            float fx = (p.x - min.x) / cell, fz = (p.z - min.z) / cell;
            float rc0 = r0 / cell - 0.8f, rc1 = r1 / cell + 0.8f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(fx - rc1)), x1 = Mathf.Min(N - 1, Mathf.CeilToInt(fx + rc1));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(fz - rc1)), z1 = Mathf.Min(N - 1, Mathf.CeilToInt(fz + rc1));
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - fx, dz = z + 0.5f - fz;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > rc1) continue;
                int i = z * N + x;
                clear[i] = 1f;
                if (d < rc0 || d < 0.01f) continue;
                vx[i] = dx / d * speed; vz[i] = dz / d * speed;
            }
        }

        void Step(float dt)
        {
            int n = N;
            float decay = Mathf.Exp(-drag * dt);
            float refillStep = refill * dt;
            float inv = dt / cell;
            float diff = Mathf.Clamp01(diffusion * dt * 10f);
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                // semi-Lagrangian advection of clearance and velocity
                float sx = x - vx[i] * inv, sz = z - vz[i] * inv;
                float c = Sample(clear, sx, sz), ux = Sample(vx, sx, sz), uz = Sample(vz, sx, sz);
                // a little diffusion so holes soften like a gas rather than a stencil
                int xl = x > 0 ? i - 1 : i, xr = x < n - 1 ? i + 1 : i, zd = z > 0 ? i - n : i, zu = z < n - 1 ? i + n : i;
                float avg = (clear[xl] + clear[xr] + clear[zd] + clear[zu]) * 0.25f;
                c = Mathf.Lerp(c, avg, diff);
                tmpC[i] = Mathf.Max(0f, c - refillStep * (0.4f + 0.6f * c));
                tmpX[i] = ux * decay; tmpZ[i] = uz * decay;
            }
            (clear, tmpC) = (tmpC, clear);
            (vx, tmpX) = (tmpX, vx);
            (vz, tmpZ) = (tmpZ, vz);
        }

        float Sample(float[] a, float x, float z)
        {
            int n = N;
            x = Mathf.Clamp(x, 0, n - 1.001f); z = Mathf.Clamp(z, 0, n - 1.001f);
            int x0 = (int)x, z0 = (int)z;
            float tx = x - x0, tz = z - z0;
            int i = z0 * n + x0;
            return Mathf.Lerp(Mathf.Lerp(a[i], a[i + 1], tx), Mathf.Lerp(a[i + n], a[i + n + 1], tx), tz);
        }

        void Upload()
        {
            int n = N;
            for (int i = 0; i < n * n; i++) pixels[i] = new Color(clear[i], vx[i], vz[i], 1f);
            fieldTex.SetPixels(pixels);
            fieldTex.Apply(false, false);
            var min = Min;
            Shader.SetGlobalTexture(FieldId, fieldTex);
            Shader.SetGlobalTexture(HeightId, heightTex);
            Shader.SetGlobalVector(RectId, new Vector4(min.x, min.z, 1f / size, 1f));
            FogLight.Upload();
        }
    }
}
