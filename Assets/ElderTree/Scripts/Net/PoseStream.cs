using System.Collections.Generic;
using UnityEngine;

namespace ElderTree.Net
{
    /// <summary>
    /// Mirrors a transform hierarchy across the wire without knowing what drives it (animators, procedural rigs,
    /// IK, scripts that hide weapons or light particles). The owner side writes what changed since it last sent
    /// (local position, rotation, scale and a state byte: active, renderers, particles, light, collider, audio), plus a
    /// slow refresh so a lost packet heals itself. The mirror side eases toward what it last heard.
    /// Transforms are keyed by a hash of their path (name + rank among same-named siblings), so the two sides may
    /// differ in children added at runtime without the rest falling out of step.
    /// </summary>
    public sealed class PoseStream
    {
        const float PosEps = 0.0015f, ScaleEps = 0.002f, RotDot = 0.99999f;
        const float Refresh = 1.1f;         // every live transform is re-sent at least this often
        const int ChunkBytes = 950;

        [System.Flags]
        enum State : byte { Active = 1, Renderer = 2, Emitting = 4, Light = 8, Collider = 16, Audio = 32 }

        sealed class Node
        {
            public uint id;
            public Transform t;
            public Renderer[] renderers;
            public ParticleSystem ps;
            public Light light;
            public Collider[] colliders;
            public AudioSource audio;
            // owner: last sent
            public Vector3 sPos, sScale;
            public Quaternion sRot;
            public byte sState, sVol, sPitch;
            public float sAt = -99f;
            // mirror: targets
            public bool has;
            public Vector3 tPos, tScale;
            public Quaternion tRot;
            public ushort seq;
        }

        readonly Transform root;
        readonly bool includeRoot;
        readonly System.Func<Transform, bool> skip;
        readonly List<Node> nodes = new();
        readonly Dictionary<uint, Node> byId = new();
        readonly NetWriter w = new();
        ushort sendSeq;

        /// <summary>Ease rate of the mirror toward what it heard (1/s).</summary>
        public float sharpness = 20f;
        /// <summary>A jump larger than this (metres) snaps instead of easing (teleports, respawns).</summary>
        public float snapDistance = 4f;

        public Transform Root => root;
        public int Count => nodes.Count;

        /// <param name="includeRoot">Also stream the root itself (its local pose, full precision).</param>
        /// <param name="skip">Leave out a transform and everything under it.</param>
        public PoseStream(Transform root, bool includeRoot, System.Func<Transform, bool> skip = null)
        {
            this.root = root;
            this.includeRoot = includeRoot;
            this.skip = skip;
            Rebuild();
        }

        /// <summary>Re-index the hierarchy (children were added: a pool grew).</summary>
        public void Rebuild()
        {
            var old = new Dictionary<uint, Node>(byId);
            nodes.Clear();
            byId.Clear();
            if (root == null) return;
            if (includeRoot) Add(root, 2166136261u, old);
            Walk(root, 2166136261u, old);
        }

        void Walk(Transform parent, uint parentHash, Dictionary<uint, Node> old)
        {
            var seen = new Dictionary<string, int>();
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                seen.TryGetValue(c.name, out int rank);
                seen[c.name] = rank + 1;
                uint h = Hash(parentHash, c.name, rank);
                if (skip != null && skip(c)) continue;
                Add(c, h, old);
                Walk(c, h, old);
            }
        }

        void Add(Transform t, uint id, Dictionary<uint, Node> old)
        {
            if (byId.ContainsKey(id)) return;
            if (!old.TryGetValue(id, out var n) || n.t != t)
            {
                n = new Node { id = id, t = t };
                n.renderers = t.GetComponents<Renderer>();
                if (n.renderers.Length == 0) n.renderers = null;
                n.ps = t.GetComponent<ParticleSystem>();
                n.light = t.GetComponent<Light>();
                var cols = new List<Collider>();
                foreach (var c in t.GetComponents<Collider>()) if (c is not CharacterController) cols.Add(c);
                n.colliders = cols.Count > 0 ? cols.ToArray() : null;
                n.audio = t.GetComponent<AudioSource>();
            }
            nodes.Add(n);
            byId[id] = n;
        }

        static uint Hash(uint h, string name, int rank)
        {
            unchecked
            {
                for (int i = 0; i < name.Length; i++) { h ^= name[i]; h *= 16777619u; }
                h ^= (uint)rank + 0x9e37u; h *= 16777619u;
                h ^= 0x2fu; h *= 16777619u;
                return h;
            }
        }

        byte ReadState(Node n)
        {
            State s = 0;
            if (n.t.gameObject.activeSelf) s |= State.Active;
            if (n.renderers != null && n.renderers[0] != null && n.renderers[0].enabled) s |= State.Renderer;
            if (n.ps != null && n.ps.isEmitting) s |= State.Emitting;
            if (n.light != null && n.light.enabled) s |= State.Light;
            if (n.colliders != null && n.colliders[0] != null && n.colliders[0].enabled) s |= State.Collider;
            if (n.audio != null && n.audio.isPlaying) s |= State.Audio;
            return (byte)s;
        }

        // ================================================================== owner
        /// <summary>
        /// Write everything that changed (or is due a refresh) as chunks small enough for one unreliable packet.
        /// <paramref name="header"/> writes the message's own header at the start of each chunk.
        /// </summary>
        public void WriteChunks(List<byte[]> chunks, System.Action<NetWriter> header, bool full = false)
        {
            float now = Time.unscaledTime;
            sendSeq++;
            int countAt = -1, count = 0;
            void Begin()
            {
                w.Reset();
                header(w);
                w.UShort(sendSeq);
                countAt = w.Length;
                w.UShort(0);
                count = 0;
            }
            void End()
            {
                if (count == 0) return;
                w.PatchUShort(countAt, (ushort)count);
                chunks.Add(w.ToArray());
            }
            Begin();
            foreach (var n in nodes)
            {
                if (n.t == null) continue;
                var t = n.t;
                byte st = ReadState(n);
                bool live = t.gameObject.activeInHierarchy;
                // hidden parts only re-send their state, and less often
                bool due = full || now - n.sAt > (live ? Refresh : Refresh * 3f);
                Vector3 p = t.localPosition, sc = t.localScale;
                Quaternion r = t.localRotation;
                byte mask = 0;
                if (due || st != n.sState) mask |= 8;
                if (live || full)
                {
                    if (due || (p - n.sPos).sqrMagnitude > PosEps * PosEps) mask |= 1;
                    if (due || Mathf.Abs(Quaternion.Dot(r, n.sRot)) < RotDot) mask |= 2;
                    if (due || (sc - n.sScale).sqrMagnitude > ScaleEps * ScaleEps) mask |= 4;
                }
                byte vol = 0, pitch = 0;
                if (n.audio != null)
                {
                    vol = (byte)Mathf.Clamp(Mathf.RoundToInt(n.audio.volume * 255f), 0, 255);
                    pitch = (byte)Mathf.Clamp(Mathf.RoundToInt(n.audio.pitch * 85f), 0, 255);
                    if (due || vol != n.sVol || pitch != n.sPitch) mask |= 16;
                }
                if (mask == 0) continue;
                if (w.Length > ChunkBytes) { End(); Begin(); }
                w.UInt(n.id);
                w.Byte(mask);
                bool isRoot = t == root;
                if ((mask & 1) != 0) { if (isRoot || p.sqrMagnitude > 3000f) { w.Byte(1); w.Vec3(p); } else { w.Byte(0); w.HalfVec3(p); } n.sPos = p; }
                if ((mask & 2) != 0) { w.Quat(r); n.sRot = r; }
                if ((mask & 4) != 0) { w.HalfVec3(sc); n.sScale = sc; }
                if ((mask & 8) != 0) { w.Byte(st); n.sState = st; }
                if ((mask & 16) != 0) { w.Byte(vol); w.Byte(pitch); n.sVol = vol; n.sPitch = pitch; }
                if (due) n.sAt = now;
                count++;
            }
            End();
        }

        /// <summary>Forget what was sent: the next write sends everything (someone new is listening).</summary>
        public void ForceFull()
        {
            foreach (var n in nodes) n.sAt = -99f;
        }

        // ================================================================== mirror
        /// <summary>Read one chunk (after its message header).</summary>
        public void Read(ref NetReader r)
        {
            ushort seq = r.UShort();
            int count = r.UShort();
            for (int i = 0; i < count && r.More; i++)
            {
                uint id = r.UInt();
                byte mask = r.Byte();
                byId.TryGetValue(id, out var n);
                bool fresh = n != null && (!n.has || (short)(seq - n.seq) >= 0);
                Vector3 p = default, sc = default;
                Quaternion q = default;
                byte st = 0, vol = 0, pitch = 0;
                if ((mask & 1) != 0) p = r.Byte() != 0 ? r.Vec3() : r.HalfVec3();
                if ((mask & 2) != 0) q = r.Quat();
                if ((mask & 4) != 0) sc = r.HalfVec3();
                if ((mask & 8) != 0) st = r.Byte();
                if ((mask & 16) != 0) { vol = r.Byte(); pitch = r.Byte(); }
                if (n == null || n.t == null || !fresh) continue;
                n.seq = seq;
                if (!n.has)
                {
                    n.tPos = n.t.localPosition; n.tRot = n.t.localRotation; n.tScale = n.t.localScale;
                    n.has = true;
                }
                if ((mask & 1) != 0)
                {
                    if ((p - n.t.localPosition).sqrMagnitude > snapDistance * snapDistance) n.t.localPosition = p;
                    n.tPos = p;
                }
                if ((mask & 2) != 0) n.tRot = q;
                if ((mask & 4) != 0) n.tScale = sc;
                if ((mask & 8) != 0) ApplyState(n, st);
                if ((mask & 16) != 0 && n.audio != null) { n.audio.volume = vol / 255f; n.audio.pitch = pitch / 85f; }
            }
        }

        static void ApplyState(Node n, byte b)
        {
            var s = (State)b;
            bool active = (s & State.Active) != 0;
            if (n.t.gameObject.activeSelf != active) n.t.gameObject.SetActive(active);
            if (n.renderers != null) foreach (var r in n.renderers) if (r) r.enabled = (s & State.Renderer) != 0;
            if (n.ps != null)
            {
                bool emit = (s & State.Emitting) != 0;
                if (emit && !n.ps.isEmitting) n.ps.Play(false);
                else if (!emit && n.ps.isEmitting) n.ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
            if (n.light != null) n.light.enabled = (s & State.Light) != 0;
            if (n.colliders != null) foreach (var c in n.colliders) if (c) c.enabled = (s & State.Collider) != 0;
            if (n.audio != null)
            {
                bool play = (s & State.Audio) != 0;
                if (play && !n.audio.isPlaying && n.audio.isActiveAndEnabled) n.audio.Play();
                else if (!play && n.audio.isPlaying) n.audio.Stop();
            }
        }

        /// <summary>Ease every mirrored transform toward what was last heard. Call each frame (LateUpdate).</summary>
        public void Apply(float dt)
        {
            float k = 1f - Mathf.Exp(-sharpness * dt);
            foreach (var n in nodes)
            {
                if (!n.has || n.t == null) continue;
                var t = n.t;
                t.localPosition = Vector3.Lerp(t.localPosition, n.tPos, k);
                t.localRotation = Quaternion.Slerp(t.localRotation, n.tRot, k);
                t.localScale = Vector3.Lerp(t.localScale, n.tScale, k);
            }
        }
    }
}
