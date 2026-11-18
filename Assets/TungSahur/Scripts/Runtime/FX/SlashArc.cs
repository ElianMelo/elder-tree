using System.Collections.Generic;
using UnityEngine;

namespace TungSahur.FX
{
    /// <summary>
    /// The long sword's slash: while a swing's hit window is open it records the blade's path (inner
    /// third to just past the tip); when the window closes that path becomes a crescent that hangs in
    /// the air for a moment, widening a little along the cut and fading. Thick in the middle, tapered at
    /// both ends, bright along the cutting edge. Ages in scaled time, so it freezes during hit-stop.
    /// </summary>
    public class SlashArc : MonoBehaviour
    {
        public Transform baseRef;
        public Transform tipRef;
        public Material material;
        [Tooltip("Seconds the crescent lingers after the swing.")]
        public float lifetime = 0.26f;
        [Tooltip("Share of the blade (from the base) where the crescent starts.")]
        [Range(0f, 1f)] public float inner = 0.3f;
        [Tooltip("How far past the tip the crescent reaches (share of the blade).")]
        public float overshoot = 0.18f;
        [Tooltip("Outward drift of the crescent while it fades (metres).")]
        public float drift = 0.35f;
        public float widthScale = 1f;

        struct Sample { public Vector3 a, b; }

        class Arc
        {
            public GameObject go;
            public Mesh mesh;
            public readonly List<Sample> samples = new();
            public float born = -1f;
            public Color core, edge;
            public Vector3 centre;
        }

        readonly List<Arc> arcs = new();
        Arc live;
        readonly List<Vector3> verts = new();
        readonly List<Color> cols = new();
        readonly List<Vector2> uvs = new();
        readonly List<int> tris = new();

        public bool Active => live != null;

        Arc Take()
        {
            foreach (var a in arcs) if (a.born < 0f && a != live) return a;
            if (arcs.Count >= 5)
            {
                // recycle the oldest
                Arc oldest = null;
                foreach (var a in arcs) if (a != live && (oldest == null || a.born < oldest.born)) oldest = a;
                if (oldest != null) { oldest.born = -1f; return oldest; }
            }
            var arc = new Arc { go = new GameObject("SlashArc"), mesh = new Mesh { name = "SlashArc" } };
            arc.mesh.MarkDynamic();
            arc.go.AddComponent<MeshFilter>().sharedMesh = arc.mesh;
            var mr = arc.go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            arcs.Add(arc);
            return arc;
        }

        /// <summary>Start recording a swing (the hit window opened).</summary>
        public void Begin(Color core, Color edge)
        {
            if (live != null) End();
            live = Take();
            live.samples.Clear();
            live.core = core;
            live.edge = edge;
            live.born = -1f;
            live.mesh.Clear();
            live.go.SetActive(true);
            Record();
        }

        /// <summary>The hit window closed: freeze the path into a fading crescent.</summary>
        public void End()
        {
            if (live == null) return;
            Record();
            var a = live;
            live = null;
            if (a.samples.Count < 3) { a.mesh.Clear(); a.go.SetActive(false); return; }
            a.born = Time.time;
            Vector3 c = Vector3.zero;
            foreach (var s in a.samples) c += (s.a + s.b) * 0.5f;
            a.centre = c / a.samples.Count;
            Build(a, 0f);
        }

        void Record()
        {
            if (live == null || baseRef == null || tipRef == null) return;
            Vector3 b = baseRef.position, t = tipRef.position;
            Vector3 along = t - b;
            var s = new Sample { a = b + along * inner, b = t + along * overshoot };
            int n = live.samples.Count;
            if (n > 0 && (live.samples[n - 1].b - s.b).sqrMagnitude < 0.0004f) return;
            live.samples.Add(s);
            if (live.samples.Count > 48) live.samples.RemoveAt(0);
        }

        void LateUpdate()
        {
            if (live != null)
            {
                Record();
                Build(live, 0f, true);
            }
            float now = Time.time;
            foreach (var a in arcs)
            {
                if (a.born < 0f || a == live) continue;
                float age = (now - a.born) / Mathf.Max(0.01f, lifetime);
                if (age >= 1f) { a.born = -1f; a.mesh.Clear(); a.go.SetActive(false); continue; }
                Build(a, age);
            }
        }

        void OnDisable()
        {
            live = null;
            foreach (var a in arcs) { a.born = -1f; if (a.mesh) a.mesh.Clear(); if (a.go) a.go.SetActive(false); }
        }

        void OnDestroy()
        {
            foreach (var a in arcs) if (a.go) Destroy(a.go);
        }

        static Vector3 CR(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>Crescent mesh along the recorded path. age 0..1 = fade; liveSwing = still being drawn.</summary>
        void Build(Arc arc, float age, bool liveSwing = false)
        {
            var src = arc.samples;
            arc.mesh.Clear();
            if (src.Count < 2) return;
            verts.Clear(); cols.Clear(); uvs.Clear(); tris.Clear();
            const int sub = 4;
            int total = (src.Count - 1) * sub + 1;
            float fade = liveSwing ? 1f : 1f - Ease.InCubic(age);
            float grow = liveSwing ? 0f : Ease.OutCubic(age);
            int idx = 0;
            for (int i = 0; i < src.Count - 1; i++)
            {
                var p0 = src[Mathf.Max(0, i - 1)]; var p1 = src[i]; var p2 = src[i + 1]; var p3 = src[Mathf.Min(src.Count - 1, i + 2)];
                int steps = i == src.Count - 2 ? sub + 1 : sub;
                for (int s = 0; s < steps; s++)
                {
                    float k = s / (float)sub;
                    Vector3 a = CR(p0.a, p1.a, p2.a, p3.a, k);
                    Vector3 b = CR(p0.b, p1.b, p2.b, p3.b, k);
                    float u = idx / (float)Mathf.Max(1, total - 1);
                    // taper both ends: thin crescent tips, full width in the middle (a live swing keeps its head wide)
                    float profile = liveSwing ? Mathf.Sin(Mathf.PI * Mathf.Lerp(0f, 0.5f, u)) : Mathf.Sin(Mathf.PI * u);
                    float w = Mathf.Clamp01(profile * 1.15f) * widthScale;
                    Vector3 mid = b;
                    Vector3 inner = Vector3.Lerp(b, a, w);
                    // drift outward from the arc's centre while fading
                    if (!liveSwing)
                    {
                        Vector3 outDir = (b - arc.centre);
                        float m = outDir.magnitude;
                        if (m > 1e-4f) { outDir /= m; mid += outDir * drift * grow; inner += outDir * drift * grow * 0.6f; }
                    }
                    verts.Add(inner);
                    verts.Add(mid);
                    float alpha = fade * (liveSwing ? Mathf.Lerp(0.25f, 1f, u) : profile);
                    cols.Add(new Color(arc.edge.r, arc.edge.g, arc.edge.b, arc.edge.a * alpha * 0.5f));
                    cols.Add(new Color(arc.core.r, arc.core.g, arc.core.b, arc.core.a * alpha));
                    uvs.Add(new Vector2(u, 0f));
                    uvs.Add(new Vector2(u, 1f));
                    if (idx > 0)
                    {
                        int v = (idx - 1) * 2;
                        tris.Add(v); tris.Add(v + 1); tris.Add(v + 2);
                        tris.Add(v + 1); tris.Add(v + 3); tris.Add(v + 2);
                    }
                    idx++;
                }
            }
            arc.mesh.SetVertices(verts);
            arc.mesh.SetColors(cols);
            arc.mesh.SetUVs(0, uvs);
            arc.mesh.SetTriangles(tris, 0);
            arc.mesh.RecalculateBounds();
        }
    }
}
