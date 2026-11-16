using System.Collections.Generic;
using UnityEngine;

namespace TungSahur.FX
{
    /// <summary>
    /// Swing ribbon between the club's base and tip. Samples are Catmull-Rom subdivided so fast
    /// arcs stay round, and they age in scaled time so the trail freezes during hit-stop.
    /// </summary>
    public class WeaponTrail : MonoBehaviour
    {
        public Transform baseRef;
        public Transform tipRef;
        public Material material;
        public float lifetime = 0.16f;
        public int subdivisions = 4;
        public Gradient colour;

        public bool Emitting { get; set; }

        struct Sample { public Vector3 b, t; public float time; }

        readonly List<Sample> raw = new();
        readonly List<Sample> pts = new();
        Mesh mesh;
        GameObject holder;
        readonly List<Vector3> verts = new();
        readonly List<Vector2> uvs = new();
        readonly List<Color> cols = new();
        readonly List<int> tris = new();

        void Awake()
        {
            holder = new GameObject("WeaponTrailMesh");
            holder.transform.SetParent(null, false);
            var mf = holder.AddComponent<MeshFilter>();
            var mr = holder.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mesh = new Mesh { name = "Trail" };
            mesh.MarkDynamic();
            mf.sharedMesh = mesh;
            if (colour == null || colour.colorKeys.Length == 0)
            {
                colour = new Gradient();
                colour.SetKeys(
                    new[] { new GradientColorKey(new Color(1f, 0.93f, 0.78f), 0f), new GradientColorKey(new Color(1f, 0.62f, 0.25f), 1f) },
                    new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            }
        }

        void OnDestroy() { if (holder) Destroy(holder); }

        void LateUpdate()
        {
            float now = Time.time;
            if (Emitting && baseRef && tipRef)
                raw.Add(new Sample { b = baseRef.position, t = tipRef.position, time = now });
            raw.RemoveAll(s => now - s.time > lifetime);
            if (raw.Count > 64) raw.RemoveRange(0, raw.Count - 64);
            Build(now);
        }

        static Vector3 CR(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        void Build(float now)
        {
            mesh.Clear();
            if (raw.Count < 2) return;
            pts.Clear();
            for (int i = 0; i < raw.Count - 1; i++)
            {
                var p0 = raw[Mathf.Max(0, i - 1)];
                var p1 = raw[i];
                var p2 = raw[i + 1];
                var p3 = raw[Mathf.Min(raw.Count - 1, i + 2)];
                for (int s = 0; s < subdivisions; s++)
                {
                    float k = s / (float)subdivisions;
                    pts.Add(new Sample
                    {
                        b = CR(p0.b, p1.b, p2.b, p3.b, k),
                        t = CR(p0.t, p1.t, p2.t, p3.t, k),
                        time = Mathf.Lerp(p1.time, p2.time, k)
                    });
                }
            }
            pts.Add(raw[raw.Count - 1]);

            verts.Clear(); uvs.Clear(); cols.Clear(); tris.Clear();
            for (int i = 0; i < pts.Count; i++)
            {
                float age = Mathf.Clamp01((now - pts[i].time) / lifetime);
                // the ribbon hugs the outer part of the club (the inner third barely moves)
                Vector3 inner = Vector3.Lerp(pts[i].b, pts[i].t, 0.3f);
                verts.Add(inner);
                verts.Add(pts[i].t);
                uvs.Add(new Vector2(age, 0));
                uvs.Add(new Vector2(age, 1));
                var c = colour.Evaluate(age);
                cols.Add(c * new Color(1, 1, 1, 0.35f));
                cols.Add(c);
                if (i > 0)
                {
                    int a = (i - 1) * 2;
                    tris.Add(a); tris.Add(a + 1); tris.Add(a + 2);
                    tris.Add(a + 1); tris.Add(a + 3); tris.Add(a + 2);
                }
            }
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }
    }
}
