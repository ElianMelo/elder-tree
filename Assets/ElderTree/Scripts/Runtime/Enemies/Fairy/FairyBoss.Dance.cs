using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace ElderTree.Enemies
{
    /// <summary>
    /// Pack dance clips on the Fairy's rigid rig. She has no skeleton, so an invisible copy of the pack's own female model
    /// (<see cref="danceRig"/>) plays the clip, and every frame each of her parts takes the direction of the matching bone:
    /// the bone's axis (e.g. shoulder to elbow) plus a side vector that turns with it. Directions, not raw rotations, so
    /// her arms-down rest pose and the pack's T-pose line up. <see cref="danceW"/> blends this over her procedural pose,
    /// so the ballet can flow into a clip and back out of it. Her feet stay pointed (they follow the shins), and the
    /// toe-on-floor contact handles the knee bends, so only the hips' sideways sway is carried over.
    /// </summary>
    public partial class FairyBoss
    {
        [Header("Dance clips (intro cinematic)")]
        [Tooltip("The pack's female model (HumanF_Model.fbx): plays the clips out of sight.")]
        public GameObject danceRig;
        [Tooltip("Humanoid clips she can dance (HumanF@Dance05, 09, 12, 17).")]
        public AnimationClip[] danceClips = new AnimationClip[0];

        float danceW, danceTarget, danceFadeSpeed = 2f;
        readonly Dictionary<Transform, Quaternion> danceO = new();
        Quaternion danceTorso = Quaternion.identity, danceHead = Quaternion.identity;
        Vector3 danceHip;

        // the proxy and its playable graph
        Animator proxy;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly AnimationClipPlayable[] slot = new AnimationClipPlayable[2];
        readonly float[] slotW = new float[2];
        int slotCur = -1;
        float crossFade = 0.4f;
        Vector3 hipBind;

        struct Bind
        {
            public Transform bone;
            public Vector3 dirLocal, sideLocal;   // bone axis and side vector in the bone's own frame
            public Quaternion herRest;            // inverse of her part's rest frame (pivot space)
        }
        readonly List<(Transform part, Bind b)> binds = new();
        Bind torsoBind, headBind;
        bool torsoOk, headOk;

        // her parts' rest directions (pivot space), taken once at rest in Awake: she is posed every frame after that
        readonly Dictionary<Transform, Vector3> herRestDir = new();

        void CaptureDanceRest()
        {
            Quaternion pivInv = Quaternion.Inverse(pivot.rotation);
            void Dir(Transform a, Transform b) { if (a && b) herRestDir[a] = (pivInv * (b.position - a.position)).normalized; }
            Dir(torso, head);
            for (int i = 0; i < 2; i++)
            {
                Dir(upperArm[i], forearm[i]); Dir(forearm[i], hand[i]); Dir(hand[i], palm[i]);
                Dir(thigh[i], shin[i]); Dir(shin[i], foot[i]);
            }
        }

        /// <summary>Start (or cross-fade to) dance clip <paramref name="index"/>, and blend her into it over <paramref name="fadeIn"/> s.</summary>
        void StartDance(int index, float fadeIn = 0.5f, float speed = 1f)
        {
            if (danceClips == null || index < 0 || index >= danceClips.Length || danceClips[index] == null || !EnsureDanceRig()) return;
            int next = slotCur < 0 ? 0 : 1 - slotCur;
            if (slot[next].IsValid())
            {
                graph.Disconnect(mixer, next);
                slot[next].Destroy();
            }
            var p = AnimationClipPlayable.Create(graph, danceClips[index]);
            p.SetApplyFootIK(false);
            p.SetSpeed(speed);
            graph.Connect(p, 0, mixer, next);
            slot[next] = p;
            slotW[next] = slotCur < 0 || danceW < 0.01f ? 1f : 0f;
            if (slotW[next] >= 1f && slotCur >= 0) slotW[slotCur] = 0f;
            slotCur = next;
            danceTarget = 1f;
            danceFadeSpeed = 1f / Mathf.Max(0.05f, fadeIn);
        }

        /// <summary>Blend back to her own (procedural) pose over <paramref name="fadeOut"/> s.</summary>
        void StopDance(float fadeOut = 0.5f)
        {
            danceTarget = 0f;
            danceFadeSpeed = 1f / Mathf.Max(0.05f, fadeOut);
        }

        bool EnsureDanceRig()
        {
            if (proxy != null) return true;
            if (danceRig == null || pivot == null) return false;
            var go = Instantiate(danceRig, transform);
            go.name = "Dance Proxy (hidden)";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            proxy = go.GetComponent<Animator>();
            if (proxy == null || proxy.avatar == null || !proxy.avatar.isHuman) { Destroy(go); proxy = null; return false; }
            proxy.applyRootMotion = false;
            proxy.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // bind pose (before anything plays): each bone's axis and a side vector, matched to her part's rest frame
            Quaternion rootInv = Quaternion.Inverse(go.transform.rotation);
            Transform B(HumanBodyBones h) => proxy.GetBoneTransform(h);
            Vector3 Her(Transform a, Transform b, Vector3 fallback) =>
                a && herRestDir.TryGetValue(a, out var d) ? d : fallback;
            Bind Make(Transform bone, Vector3 dirRoot, Vector3 herDir, Vector3 side)
            {
                Quaternion boneInv = Quaternion.Inverse(rootInv * bone.rotation);
                Vector3 sideRoot = Vector3.ProjectOnPlane(side, dirRoot).normalized;
                Vector3 herSide = Vector3.ProjectOnPlane(side, herDir).normalized;
                return new Bind
                {
                    bone = bone,
                    dirLocal = boneInv * dirRoot.normalized,
                    sideLocal = boneInv * sideRoot,
                    herRest = Quaternion.Inverse(Quaternion.LookRotation(herDir, herSide)),
                };
            }
            Vector3 Dir(Transform a, Transform b) => rootInv * (b.position - a.position);
            void Add(Transform part, HumanBodyBones from, HumanBodyBones to, Vector3 herDir, Vector3 side)
            {
                var a = B(from); var b = B(to);
                if (part == null || a == null || b == null) return;
                binds.Add((part, Make(a, Dir(a, b), herDir, side)));
            }

            var hips = B(HumanBodyBones.Hips);
            var chest = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest) ?? B(HumanBodyBones.Spine);
            var neck = B(HumanBodyBones.Neck) ?? B(HumanBodyBones.Head);
            var spine = B(HumanBodyBones.Spine);
            var headB = B(HumanBodyBones.Head);
            hipBind = hips ? rootInv * (hips.position - go.transform.position) : Vector3.zero;
            Vector3 fwd = Vector3.forward, upv = Vector3.up;
            if (pelvis && hips && spine) binds.Add((pelvis, Make(hips, Dir(hips, spine), upv, fwd)));
            if (torso && chest && neck) { torsoBind = Make(chest, Dir(spine ? spine : chest, neck), Her(torso, head, upv), fwd); torsoOk = true; }
            if (head && headB) { headBind = Make(headB, upv, upv, fwd); headOk = true; }
            for (int i = 0; i < 2; i++)
            {
                bool L = i == 0;
                Add(upperArm[i], L ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm, L ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm,
                    Her(upperArm[i], forearm[i], Vector3.down), fwd);
                Add(forearm[i], L ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm, L ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand,
                    Her(forearm[i], hand[i], Vector3.down), fwd);
                Add(hand[i], L ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand, L ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal,
                    Her(hand[i], palm[i], Vector3.down), fwd);
                Add(thigh[i], L ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg, L ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg,
                    Her(thigh[i], shin[i], Vector3.down), fwd);
                Add(shin[i], L ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg, L ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot,
                    Her(shin[i], foot[i], Vector3.down), fwd);
            }

            graph = PlayableGraph.Create("FairyDance");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "Dance", proxy);
            mixer = AnimationMixerPlayable.Create(graph, 2);
            output.SetSourcePlayable(mixer);
            return true;
        }

        /// <summary>Advance the clip and read the proxy's bones into part orientations (pivot space). Called from LateUpdate.</summary>
        void SampleDance(float dt)
        {
            danceW = Mathf.MoveTowards(danceW, danceTarget, danceFadeSpeed * dt);
            if (danceW <= 0.001f || proxy == null || !graph.IsValid()) { danceO.Clear(); return; }
            for (int i = 0; i < 2; i++)
            {
                if (!slot[i].IsValid()) continue;
                slotW[i] = Mathf.MoveTowards(slotW[i], i == slotCur ? 1f : 0f, dt / crossFade);
                mixer.SetInputWeight(i, slotW[i]);
            }
            graph.Evaluate(dt);

            Quaternion rootInv = Quaternion.Inverse(proxy.transform.rotation);
            Quaternion Frame(in Bind b)
            {
                Quaternion r = rootInv * b.bone.rotation;
                return Quaternion.LookRotation(r * b.dirLocal, r * b.sideLocal) * b.herRest;
            }
            foreach (var (part, b) in binds) danceO[part] = Frame(b);
            for (int i = 0; i < 2; i++)
                if (foot[i] && shin[i] && danceO.TryGetValue(shin[i], out var sh)) danceO[foot[i]] = sh;   // toes stay pointed
            if (skirt && pelvis && danceO.TryGetValue(pelvis, out var pv)) danceO[skirt] = pv;
            if (torsoOk) danceTorso = Frame(torsoBind);
            if (headOk) danceHead = Frame(headBind);
            var hips = proxy.GetBoneTransform(HumanBodyBones.Hips);
            if (hips)
            {
                Vector3 h = rootInv * (hips.position - proxy.transform.position) - hipBind;
                danceHip = Vector3.ClampMagnitude(new Vector3(h.x, 0f, h.z) * 1.4f, 0.4f);
            }
        }

        void OnDisable()
        {
            danceW = danceTarget = 0f;
        }

        void ReleaseDanceGraph()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
