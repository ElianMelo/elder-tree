using System.Collections;
using System.Collections.Generic;
using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.Combat;
using ElderTree.FX;
using UnityEngine;

namespace ElderTree.World
{
    /// <summary>
    /// Vines (a curtain hanging across a gap, or a bramble thicket growing out of the ground). Only an
    /// edge cuts them: hits whose <see cref="DamageInfo.cutPower"/> reaches <see cref="requiredCut"/> sever
    /// every strand within the cut's width at the height the blade met them. The severed part drops and
    /// tumbles; a hanging stub snaps back up. Blunt blows (club, hammer, arrows, blasts) only thrash the
    /// leaves. Once enough strands are gone the blocking collider opens. <see cref="RestoreAll"/> regrows all.
    /// </summary>
    public class Cuttable : MonoBehaviour, IDamageable
    {
        [System.Serializable]
        public class Strand
        {
            [Tooltip("Strand pivot on the anchor (beam or ground): it sways and a cut stub retracts toward it.")]
            public Transform root;
            [Tooltip("Pieces from the anchor outward (down for a hanging curtain, up for a thicket).")]
            public Transform[] segments;
            [HideInInspector] public bool cut;
            [HideInInspector] public Vector3[] restPos;
            [HideInInspector] public Quaternion[] restRot;
            [HideInInspector] public Vector3[] restScale;
            [HideInInspector] public Transform[] restParent;
            [HideInInspector] public Quaternion rootRot;
            [HideInInspector] public Vector3 rootScale;
        }

        public string displayName = "Vines";
        [Tooltip("Minimum DamageInfo.cutPower that cuts (blades: 1).")]
        public int requiredCut = 1;
        [Tooltip("True: strands hang from the top (curtain). False: they grow from the ground (thicket).")]
        public bool anchoredTop = true;
        public Strand[] strands = System.Array.Empty<Strand>();
        [Tooltip("Blocks the way until enough strands are cut.")]
        public Collider[] solidColliders;
        [Range(0.1f, 1f)] public float clearFraction = 0.55f;
        [Tooltip("Hanging stubs snap back up to this share of their length.")]
        [Range(0.1f, 1f)] public float stubRetract = 0.35f;
        public float fallenLifetime = 5f;
        public Color leafColor = new Color(0.22f, 0.3f, 0.14f);

        public static readonly List<Cuttable> All = new();
        public bool Cleared { get; private set; }
        public bool IsAlive => !Cleared || CutCount < strands.Length;
        public Transform Root => transform;
        public int CutCount { get; private set; }
        public event System.Action<Cuttable> Opened;

        float lastRustle = -9f;
        readonly List<GameObject> fallen = new();
        readonly Dictionary<Strand, float> sway = new();

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDestroy() => All.Remove(this);

        void Awake()
        {
            foreach (var s in strands)
            {
                if (s.root == null && s.segments.Length > 0) s.root = s.segments[0].parent;
                s.rootRot = s.root.localRotation;
                s.rootScale = s.root.localScale;
                int n = s.segments.Length;
                s.restPos = new Vector3[n]; s.restRot = new Quaternion[n]; s.restScale = new Vector3[n]; s.restParent = new Transform[n];
                for (int i = 0; i < n; i++)
                {
                    var t = s.segments[i];
                    s.restParent[i] = t.parent;
                    s.restPos[i] = t.localPosition; s.restRot[i] = t.localRotation; s.restScale[i] = t.localScale;
                }
            }
        }

        public bool ReceiveHit(in DamageInfo info)
        {
            if (info.cutPower < requiredCut)
            {
                // blunt: leaves thrash, nothing parts
                if (Time.time - lastRustle > 0.15f)
                {
                    lastRustle = Time.time;
                    FXManager.Spawn(FXType.LeafBurst, info.point, Quaternion.LookRotation(-info.direction + Vector3.up * 0.3f), 0.6f);
                    AudioManager.Play(Sfx.VineRustle, info.point, 0.9f, Random.Range(0.92f, 1.06f));
                    Thrash(info.point, 2f, 1f);
                }
                return false;
            }
            float half = Mathf.Max(0.3f, info.cutWidth * 0.5f);
            int cutNow = 0;
            foreach (var s in strands)
            {
                if (s.cut || s.segments.Length == 0) continue;
                Vector3 a = s.segments[0].position;
                Vector2 d = new Vector2(a.x - info.point.x, a.z - info.point.z);
                if (d.magnitude > half) continue;
                if (Sever(s, info.point.y, info.direction)) cutNow++;
            }
            if (cutNow == 0)
            {
                // the edge met only stubs above / below the cut line: a small rustle so it still reads
                Thrash(info.point, half, 0.5f);
                return false;
            }
            CutCount += cutNow;
            FXManager.Spawn(FXType.LeafBurst, info.point, Quaternion.LookRotation(info.direction + Vector3.up * 0.4f), Mathf.Clamp(0.7f + cutNow * 0.12f, 0.7f, 1.6f));
            AudioManager.Play(Sfx.VineCut, info.point, Mathf.Clamp01(0.6f + cutNow * 0.1f), Random.Range(0.92f, 1.06f));
            Thrash(info.point, half * 1.8f, 0.6f);
            if (!Cleared && CutCount >= Mathf.CeilToInt(strands.Length * clearFraction)) Open();
            return true;
        }

        /// <summary>Cut one strand at world height <paramref name="y"/>. Returns false if the cut misses it.</summary>
        bool Sever(Strand s, float y, Vector3 dir)
        {
            var segs = s.segments;
            int n = segs.Length;
            // segment the cut passes through (pieces are ordered from the anchor outward)
            int k = -1;
            for (int i = 0; i < n; i++)
            {
                float y0 = segs[i].position.y;
                float y1 = i + 1 < n ? segs[i + 1].position.y : y0 + (anchoredTop ? -1f : 1f) * SegLength(s);
                float lo = Mathf.Min(y0, y1), hi = Mathf.Max(y0, y1);
                if (y >= lo - 0.05f && y <= hi + 0.05f) { k = i; break; }
            }
            if (k < 0)
            {
                // above a curtain / below a thicket's reach: cut at the nearest end that is still in reach
                float top = Mathf.Max(segs[0].position.y, segs[n - 1].position.y), bottom = Mathf.Min(segs[0].position.y, segs[n - 1].position.y);
                if (y > top + 0.6f || y < bottom - 0.6f) return false;
                k = anchoredTop ? Mathf.Clamp(n - 2, 1, n - 1) : 1;
            }
            k = Mathf.Clamp(k, 1, n - 1);   // always leave a stub on the anchor
            s.cut = true;

            // the free part falls as one floppy piece
            var piece = new GameObject($"{name}_Cut");
            piece.layer = 2;
            piece.transform.position = segs[k].position;
            for (int i = k; i < n; i++) segs[i].SetParent(piece.transform, true);
            var rb = piece.AddComponent<Rigidbody>();
            rb.mass = 2f;
            rb.linearDamping = 0.6f;
            rb.angularDamping = 1.5f;
            var col = piece.AddComponent<CapsuleCollider>();
            float len = SegLength(s) * (n - k);
            col.direction = 1;
            col.height = Mathf.Max(0.3f, len);
            col.radius = 0.06f;
            col.center = new Vector3(0f, (anchoredTop ? -0.5f : 0.5f) * len, 0f);
            Vector3 push = dir; push.y = 0f;
            rb.linearVelocity = (push.sqrMagnitude > 1e-3f ? push.normalized : Random.insideUnitSphere) * Random.Range(1.2f, 2.6f) + Vector3.up * (anchoredTop ? 0.5f : 1.6f);
            rb.angularVelocity = Random.insideUnitSphere * 4f;
            fallen.Add(piece);
            StartCoroutine(Wither(piece, fallenLifetime + Random.Range(0f, 1.2f)));

            // a hanging stub snaps back up; a thicket stump stays put
            if (anchoredTop) StartCoroutine(Retract(s, k));
            sway[s] = 1f;
            return true;
        }

        static float SegLength(Strand s) =>
            s.segments.Length > 1 ? Vector3.Distance(s.segments[0].position, s.segments[1].position) : 0.5f;

        IEnumerator Retract(Strand s, int k)
        {
            // the stub (k pieces) snaps up toward the beam: scale the strand pivot so it keeps stubRetract of its length
            float f = Mathf.Clamp(stubRetract * s.segments.Length / Mathf.Max(1, k), 0.15f, 1f);
            Vector3 from = s.rootScale, to = new Vector3(from.x, from.y * f, from.z);
            float t = 0f;
            while (t < 0.35f)
            {
                t += Time.deltaTime;
                s.root.localScale = Vector3.LerpUnclamped(from, to, Ease.OutBack(Mathf.Clamp01(t / 0.35f)));
                yield return null;
            }
            s.root.localScale = to;
        }

        IEnumerator Wither(GameObject piece, float after)
        {
            yield return new WaitForSeconds(after);
            if (piece == null) yield break;
            var rb = piece.GetComponent<Rigidbody>();
            if (rb) rb.isKinematic = true;
            foreach (var c in piece.GetComponentsInChildren<Collider>()) c.enabled = false;
            float t = 0f;
            Vector3 s0 = piece.transform.localScale;
            while (piece != null && t < 1.2f)
            {
                t += Time.deltaTime;
                piece.transform.position += Vector3.down * Time.deltaTime * 0.25f;
                piece.transform.localScale = s0 * (1f - 0.6f * t / 1.2f);
                yield return null;
            }
            // segments are restored from their rest data, so hide (not destroy) them with the piece
            if (piece != null) piece.SetActive(false);
        }

        void Thrash(Vector3 at, float radius, float amount)
        {
            foreach (var s in strands)
            {
                if (s.segments.Length == 0) continue;
                Vector3 a = s.segments[0].position;
                float d = new Vector2(a.x - at.x, a.z - at.z).magnitude;
                if (d > radius) continue;
                float k = amount * (1f - d / Mathf.Max(0.01f, radius));
                sway[s] = Mathf.Max(sway.TryGetValue(s, out var v) ? v : 0f, k);
            }
        }

        static readonly List<Strand> swayKeys = new();

        void Update()
        {
            if (sway.Count == 0) return;
            float dt = Time.deltaTime;
            swayKeys.Clear();
            swayKeys.AddRange(sway.Keys);
            float time = Time.time;
            foreach (var s in swayKeys)
            {
                float a = Mathf.MoveTowards(sway[s], 0f, dt * 0.9f);
                if (a <= 0f) sway.Remove(s); else sway[s] = a;
                var root = s.root;
                if (root == null) continue;
                float ang = Mathf.Sin(time * 9f + root.GetInstanceID() * 0.37f) * 14f * a;
                root.localRotation = s.rootRot * Quaternion.Euler(ang, 0f, ang * 0.6f);
            }
        }

        void Open()
        {
            Cleared = true;
            foreach (var c in solidColliders) if (c) c.enabled = false;
            Opened?.Invoke(this);
            CameraDirector.Instance?.Shake(0.12f, Vector3.down);
        }

        public void Restore()
        {
            StopAllCoroutines();
            foreach (var s in strands)
            {
                for (int i = 0; i < s.segments.Length; i++)
                {
                    var t = s.segments[i];
                    if (t == null) continue;
                    t.SetParent(s.restParent[i], false);
                    t.localPosition = s.restPos[i]; t.localRotation = s.restRot[i]; t.localScale = s.restScale[i];
                    t.gameObject.SetActive(true);
                }
                s.cut = false;
                if (s.root != null) { s.root.localRotation = s.rootRot; s.root.localScale = s.rootScale; }
            }
            foreach (var g in fallen) if (g) Destroy(g);
            fallen.Clear();
            sway.Clear();
            CutCount = 0;
            Cleared = false;
            foreach (var c in solidColliders) if (c) c.enabled = true;
        }

        public static void RestoreAll()
        {
            foreach (var c in All) if (c != null) c.Restore();
        }
    }
}
