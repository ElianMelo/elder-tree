using System.Collections.Generic;
using ElderTree.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace ElderTree.FX
{
    /// <summary>
    /// Foam drawn on the sea (<see cref="OceanSurface"/>), riding the swell: wakes that a swimmer leaves behind it, spreading
    /// into a V and fading, and rings that spread out where something hits the water. One world-space mesh, rebuilt every frame.
    /// The material's texture is a foam band across u: lacy edges (the arms of the V) and a churned middle; rings use its edge.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SeaFoam : MonoBehaviour
    {
        public static SeaFoam Instance { get; private set; }

        /// <summary>The scene's foam layer, made on first use with <paramref name="material"/>.</summary>
        public static SeaFoam Ensure(Material material)
        {
            if (Instance != null) return Instance;
            if (material == null || !OceanSurface.Exists) return null;
            var go = new GameObject("SeaFoam");
            go.transform.position = Vector3.zero;
            var f = go.AddComponent<SeaFoam>();
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return f;
        }

        struct Pt { public Vector3 p, side; public float born, v, open; }

        class Trail
        {
            public readonly List<Pt> pts = new();
            public float width, spread, life;
            public bool live = true;
            public Vector3 head, headSide;
            public float headOpen = 1f;
        }

        struct Ring { public Vector3 c; public float born, r0, speed, width, life, open; }

        readonly Dictionary<int, Trail> trails = new();
        readonly List<Ring> rings = new();
        int nextId = 1;
        Mesh mesh;
        readonly List<Vector3> verts = new();
        readonly List<Vector2> uvs = new();
        readonly List<Color> cols = new();
        readonly List<int> tris = new();
        const float Lift = 0.05f;       // above the swell, under the spray
        const float Step = 0.45f;       // a new trail point every this many metres

        void Awake()
        {
            Instance = this;
            mesh = new Mesh { name = "SeaFoam" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (mesh) Destroy(mesh);
        }

        // ------------------------------------------------------------------ API
        /// <summary>Start a wake: <paramref name="width"/> m wide where it is laid, widening by <paramref name="spread"/> m/s,
        /// gone after <paramref name="life"/> s.</summary>
        public int BeginTrail(float width, float spread, float life)
        {
            int id = nextId++;
            trails[id] = new Trail { width = width, spread = spread, life = life };
            return id;
        }

        /// <summary>The swimmer is at <paramref name="pos"/> heading <paramref name="dir"/>: lays wake behind it.</summary>
        public void Emit(int id, Vector3 pos, Vector3 dir)
        {
            if (!trails.TryGetValue(id, out var t) || !t.live) return;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, dir.normalized);
            pos.y = 0f;
            t.head = pos;
            t.headSide = side;
            if (t.pts.Count == 0 || (pos - t.pts[^1].p).sqrMagnitude > Step * Step)
            {
                float v = t.pts.Count == 0 ? 0f : t.pts[^1].v + Vector3.Distance(pos, t.pts[^1].p) / 4f;
                float open = t.pts.Count % 8 == 0 ? OceanSurface.Openness(pos) : t.pts[^1].open;
                t.headOpen = open;
                t.pts.Add(new Pt { p = pos, side = side, born = Time.time, v = v, open = open });
            }
        }

        /// <summary>Stop laying the wake (what's there spreads and fades on its own).</summary>
        public void EndTrail(int id)
        {
            if (trails.TryGetValue(id, out var t)) t.live = false;
        }

        /// <summary>A ring of foam spreading out from <paramref name="c"/>.</summary>
        public void AddRing(Vector3 c, float r0, float speed, float width, float life)
        {
            c.y = 0f;
            rings.Add(new Ring { c = c, born = Time.time, r0 = r0, speed = speed, width = width, life = life, open = OceanSurface.Openness(c) });
        }

        public void Clear()
        {
            trails.Clear();
            rings.Clear();
        }

        // ------------------------------------------------------------------ mesh
        void LateUpdate()
        {
            float now = Time.time;
            verts.Clear(); uvs.Clear(); cols.Clear(); tris.Clear();

            var dead = new List<int>();
            foreach (var kv in trails)
            {
                var t = kv.Value;
                t.pts.RemoveAll(p => now - p.born > t.life);
                if (!t.live && t.pts.Count == 0) { dead.Add(kv.Key); continue; }
                int n = t.pts.Count;
                if (n == 0) continue;
                int start = verts.Count;
                // oldest first; the live head last, so the wake reaches the swimmer
                for (int i = 0; i <= n; i++)
                {
                    Pt p;
                    if (i < n) p = t.pts[i];
                    else if (t.live) p = new Pt { p = t.head, side = t.headSide, born = now, v = t.pts[n - 1].v + Vector3.Distance(t.head, t.pts[n - 1].p) / 4f, open = t.headOpen };
                    else break;
                    float age = now - p.born;
                    float k = Mathf.Clamp01(age / t.life);
                    float half = (t.width + t.spread * age) * 0.5f;
                    float a = Mathf.Pow(1f - k, 1.6f) * Mathf.Clamp01(age / 0.12f + 0.35f);
                    Vector3 l = p.p - p.side * half, r = p.p + p.side * half;
                    l.y = OceanSurface.Height(l.x, l.z, p.open) + Lift;
                    r.y = OceanSurface.Height(r.x, r.z, p.open) + Lift;
                    verts.Add(l); verts.Add(r);
                    uvs.Add(new Vector2(0f, p.v)); uvs.Add(new Vector2(1f, p.v));
                    var c = new Color(1f, 1f, 1f, a);
                    cols.Add(c); cols.Add(c);
                }
                int rows = (verts.Count - start) / 2;
                for (int i = 0; i < rows - 1; i++)
                {
                    int a0 = start + i * 2;
                    tris.Add(a0); tris.Add(a0 + 2); tris.Add(a0 + 1);
                    tris.Add(a0 + 1); tris.Add(a0 + 2); tris.Add(a0 + 3);
                }
            }
            foreach (var id in dead) trails.Remove(id);

            rings.RemoveAll(r => now - r.born > r.life);
            const int Seg = 40;
            foreach (var r in rings)
            {
                float age = now - r.born;
                float k = age / r.life;
                float rad = r.r0 + r.speed * age * (1f - 0.4f * k);
                float w = r.width * (1f + k);
                float a = Mathf.Pow(1f - k, 1.4f);
                int start = verts.Count;
                for (int s = 0; s <= Seg; s++)
                {
                    float ang = s / (float)Seg * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    Vector3 i0 = r.c + d * Mathf.Max(0f, rad - w * 0.5f), o0 = r.c + d * (rad + w * 0.5f);
                    i0.y = OceanSurface.Height(i0.x, i0.z, r.open) + Lift;
                    o0.y = OceanSurface.Height(o0.x, o0.z, r.open) + Lift;
                    verts.Add(i0); verts.Add(o0);
                    float v = ang * rad / 4f;
                    uvs.Add(new Vector2(0.02f, v)); uvs.Add(new Vector2(0.24f, v));
                    var c = new Color(1f, 1f, 1f, a);
                    cols.Add(c); cols.Add(c);
                }
                for (int s = 0; s < Seg; s++)
                {
                    int a0 = start + s * 2;
                    tris.Add(a0); tris.Add(a0 + 1); tris.Add(a0 + 2);
                    tris.Add(a0 + 1); tris.Add(a0 + 3); tris.Add(a0 + 2);
                }
            }

            mesh.Clear();
            if (verts.Count == 0) return;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }
    }
}
