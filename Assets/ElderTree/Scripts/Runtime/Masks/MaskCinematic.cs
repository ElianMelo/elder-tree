using System.Collections;
using System.Collections.Generic;
using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.Equipment;
using ElderTree.FX;
using ElderTree.InputHandling;
using ElderTree.Player;
using ElderTree.UI;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ElderTree.Masks
{
    /// <summary>
    /// First-time transformation, after Majora's Mask: the hero lifts the mask to the face, it clamps on,
    /// the world drops away into a churning vortex while the body screams and flickers between the two
    /// forms in hard camera cuts (face close-up, low spin, overhead, profile), then a white-out and the
    /// new body roars. Ends with the "ALLIGATOR MASK" card and the controls to wear it.
    /// The camera is taken from Cinemachine for the duration; the player is invulnerable and has no input.
    /// Skippable after a couple of seconds (Start / Esc).
    /// </summary>
    public class MaskCinematic : MonoBehaviour
    {
        public PlayerController player;
        public HUD hud;
        public Material vortexMaterial;
        public Material vortexGlowMaterial;
        public float skipAfter = 2.5f;

        public bool Playing { get => playing; private set { playing = value; Coop.CinematicPlaying = value; } }
        bool playing;
        /// <summary>The new body's voice (alligator roar, shark tra-la-la...).</summary>
        Sfx Roar => mask != null && mask.form != null ? mask.form.roar : Sfx.CrocRoar;

        Camera cam;
        CinemachineBrain brain;
        GameObject vortex, vortexInner;
        Volume volume;
        ChromaticAberration ca;
        LensDistortion lens;
        ColorAdjustments color;
        Vignette vig;
        MaskData mask;
        FormRig fromRig, toRig;
        Renderer[] fromRenderers, toRenderers;
        GameObject maskObj;
        Transform head, hand;
        float horror;          // 0..1 bone distortion
        bool skip;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        public void Play(MaskData m, System.Action onComplete)
        {
            if (Playing || m == null || player == null) { onComplete?.Invoke(); return; }
            StartCoroutine(Run(m, onComplete));
        }

        // ------------------------------------------------------------------ setup / teardown
        void BuildStage()
        {
            cam = Camera.main;
            brain = cam != null ? cam.GetComponent<CinemachineBrain>() : null;
            vortex = MakeSphere("MaskVortex", 9f, vortexMaterial);
            vortexInner = MakeSphere("MaskVortexInner", 6.5f, vortexGlowMaterial);

            var go = new GameObject("MaskCinematicVolume");
            go.transform.SetParent(transform, false);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.weight = 0f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            ca = profile.Add<ChromaticAberration>(true); ca.intensity.Override(1f);
            lens = profile.Add<LensDistortion>(true); lens.intensity.Override(0f); lens.scale.Override(1f);
            color = profile.Add<ColorAdjustments>(true); color.hueShift.Override(0f); color.saturation.Override(10f); color.contrast.Override(12f);
            vig = profile.Add<Vignette>(true); vig.intensity.Override(0.55f); vig.color.Override(new Color(0.05f, 0f, 0.02f));
            volume.sharedProfile = profile;
        }

        GameObject MakeSphere(string name, float radius, Material mat)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(s.GetComponent<Collider>());
            s.name = name;
            s.transform.localScale = Vector3.one * radius * 2f;
            var r = s.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat != null ? new Material(mat) : null;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            s.SetActive(false);
            return s;
        }

        void Teardown()
        {
            if (vortex) Destroy(vortex);
            if (vortexInner) Destroy(vortexInner);
            if (volume) { Destroy(volume.sharedProfile); Destroy(volume.gameObject); }
            if (maskObj) Destroy(maskObj);
            if (agonyLight) Destroy(agonyLight.gameObject);
            RestoreCulling();
            RenderSettings.fog = fogWas;
        }

        void RestoreCulling()
        {
            // note: "Everything" is -1 as an int, so a separate flag says whether a mask was saved
            if (cam != null && cullSaved) cam.cullingMask = cullWas;
            cullSaved = false;
        }

        bool fogWas;
        int cullWas;
        bool cullSaved;
        Light agonyLight;

        // ------------------------------------------------------------------ the sequence
        IEnumerator Run(MaskData m, System.Action onComplete)
        {
            Playing = true;
            skip = false;
            mask = m;
            var input = PlayerInputReader.Instance;
            input?.SetGameplayEnabled(false);
            player.EnterCinematic();
            BuildStage();
            fogWas = RenderSettings.fog;

            fromRig = player.CurrentRig;
            toRig = null;
            foreach (var r in player.rigs) if (r != null && m.form != null && r.rigId == m.form.rigId) toRig = r;
            if (toRig == null || fromRig == null || toRig == fromRig) { Finish(onComplete); yield break; }
            fromRenderers = fromRig.GetComponentsInChildren<Renderer>(true);
            toRenderers = toRig.GetComponentsInChildren<Renderer>(true);
            head = fromRig.Head;
            hand = fromRig.animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (brain) brain.enabled = false;
            hud?.SetCinematic(true);

            Vector3 P = player.transform.position;
            Vector3 F = player.transform.forward;
            Vector3 R = player.transform.right;
            Vector3 headPos() => head != null ? head.position + Vector3.up * 0.12f : P + Vector3.up * 1.7f;
            float t;

            // ---- 1. the mask is raised to the face
            Anim(fromRig, "MaskDon", 0.15f, 0.75f);
            maskObj = m.maskModel != null ? Instantiate(m.maskModel) : null;
            if (maskObj) foreach (var r in maskObj.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            AudioManager.Play2D(Sfx.MaskReceived, 0.7f, 0.85f);
            t = 0f;
            bool onFace = false;
            while (t < 1.9f && !skip)
            {
                t += Time.deltaTime;
                float k = Ease.InOutCubic(Mathf.Clamp01(t / 1.9f));
                Vector3 look = headPos() + Vector3.up * 0.08f;
                PlaceCamera(P + F * Mathf.Lerp(3.4f, 1.7f, k) + R * Mathf.Lerp(0.6f, 0.25f, k) + Vector3.up * Mathf.Lerp(1.3f, 1.65f, k), look, Mathf.Lerp(40f, 32f, k));
                if (maskObj)
                {
                    if (!onFace && t > 1.25f)
                    {
                        onFace = true;
                        AudioManager.Play(Sfx.MaskOn, headPos(), 1f);
                        PlayerInputReader.Instance?.Rumble(0.6f, 0.8f, 0.25f);
                        hud?.Flash(0.35f);
                    }
                    if (!onFace && hand != null)
                    {
                        maskObj.transform.position = hand.position + F * 0.1f + Vector3.up * 0.04f;
                        maskObj.transform.rotation = Quaternion.LookRotation(F) * Quaternion.Euler(-10f, 0, 0);
                        float fit = fromRig.faceSocket != null ? fromRig.faceSocket.lossyScale.x : 1f;
                        maskObj.transform.localScale = Vector3.one * (fit * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(t / 0.4f)));
                    }
                    else FaceMask(1f + 0.25f * Mathf.Exp(-(t - 1.25f) * 10f));
                }
                CheckSkip(t);
                yield return null;
            }

            // ---- 2. agony: vortex, scream, hard cuts, the body flickers between forms
            if (!skip)
            {
                RenderSettings.fog = false;
                vortex.SetActive(true); vortexInner.SetActive(true);
                vortex.transform.position = vortexInner.transform.position = P + Vector3.up * 1f;
                // the world falls away: only the body and the vortex are drawn
                int playerLayer = player.gameObject.layer;
                vortex.layer = vortexInner.layer = playerLayer;
                if (cam != null) { cullWas = cam.cullingMask; cullSaved = true; cam.cullingMask = 1 << playerLayer; }
                var lg = new GameObject("AgonyLight");
                lg.transform.position = P + Vector3.up * 2.2f + F * 1.2f;
                agonyLight = lg.AddComponent<Light>();
                agonyLight.type = LightType.Point; agonyLight.range = 5f; agonyLight.intensity = 0f; agonyLight.shadows = LightShadows.None;
                toRig.gameObject.SetActive(true);
                Anim(fromRig, "MaskScream", 0.05f, 1f);
                Anim(toRig, "MaskScream", 0.05f, 1f);
                SetVisible(true, false);
                hud?.Flash(0.6f);
                AudioManager.Play2D(Sfx.MaskScream, 1f);
                AudioManager.Play(Roar, headPos(), 0.5f, 0.6f);
                const float agony = 3.2f;
                t = 0f;
                int shot = -1;
                float nextFlick = 0.45f, flickT = 0f;
                bool showTo = false;
                float spin = 0f;
                while (t < agony && !skip)
                {
                    float dt = Time.deltaTime;
                    t += dt;
                    float k = t / agony;
                    volume.weight = Mathf.Clamp01(t / 0.3f);
                    lens.intensity.value = Mathf.Sin(t * 9f) * 0.35f * (0.4f + k);
                    color.hueShift.value = Mathf.Sin(t * 2.7f) * 120f * k;
                    ca.intensity.value = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(t * 13f));
                    TintVortex(t, k);
                    horror = Mathf.Clamp01(t / 1.2f);
                    if (agonyLight)
                    {
                        agonyLight.color = Color.Lerp(mask.vortexA, mask.vortexB, 0.5f + 0.5f * Mathf.Sin(t * 11f));
                        agonyLight.intensity = 1.6f + 1.4f * Mathf.Abs(Mathf.Sin(t * 17f));
                    }

                    // hard cuts, faster as it builds
                    int s = (int)(t / Mathf.Lerp(0.7f, 0.4f, k));
                    if (s != shot) { shot = s; hud?.Flash(0.12f); PlayerInputReader.Instance?.Rumble(0.4f, 0.6f, 0.12f); }
                    spin += dt * 140f;
                    Vector3 hp = headPos();
                    Vector3 jitter = Random.insideUnitSphere * 0.025f * (1f + k * 2f);
                    switch (shot % 4)
                    {
                        case 0: PlaceCamera(hp + F * 0.85f + jitter, hp - Vector3.up * 0.05f, 30f + k * 8f); break;                          // face
                        case 1:
                        {
                            Vector3 o = Quaternion.AngleAxis(spin, Vector3.up) * (F * 2.4f);
                            PlaceCamera(P + o + Vector3.up * 0.35f + jitter, hp, 55f, Mathf.Sin(t * 3f) * 18f);                             // low spin
                            break;
                        }
                        case 2: PlaceCamera(P + Vector3.up * 4.2f + F * 0.6f + jitter, P + Vector3.up * 0.9f, 48f, spin * 0.5f); break;    // overhead
                        default: PlaceCamera(hp + R * 1.4f - Vector3.up * 0.2f + jitter, hp, 36f, -12f); break;                          // profile
                    }

                    // flicker between the bodies, faster and faster
                    flickT += dt;
                    if (flickT >= nextFlick)
                    {
                        flickT = 0f;
                        nextFlick = Mathf.Lerp(0.42f, 0.04f, k * k);
                        showTo = !showTo;
                        SetVisible(!showTo, showTo);
                        if (Random.value < 0.5f) AudioManager.Play(Sfx.Transform, hp, 0.45f, Random.Range(0.7f, 1.3f));
                    }
                    if (maskObj) FaceMask(1f);
                    yield return null;
                }
            }

            // ---- 3. white-out, the new body stands
            hud?.Flash(1f);
            AudioManager.Play2D(Sfx.Transform, 1f, 0.7f);
            PlayerInputReader.Instance?.Rumble(1f, 1f, 0.35f);
            horror = 0f;
            ResetBodies();
            vortex.SetActive(false); vortexInner.SetActive(false);
            if (agonyLight) Destroy(agonyLight.gameObject);
            RestoreCulling();
            volume.weight = 0f;
            RenderSettings.fog = fogWas;
            if (maskObj) Destroy(maskObj);
            player.ApplyForm(m.form);
            player.EnterCinematic();
            onComplete?.Invoke();
            onComplete = null;
            var rig = player.CurrentRig;
            Anim(rig, "MaskRoar", 0.05f, 0.9f);
            FXManager.Spawn(FXType.Smoke, P + Vector3.up * 1f, Quaternion.identity, 1.1f);
            FXManager.Spawn(FXType.Dust, P, Quaternion.identity, 2.2f);
            FXManager.Spawn(FXType.SlamRing, P + Vector3.up * 0.05f, Quaternion.identity, 2.4f);

            if (!skip)
            {
                yield return Wait(0.25f);
                AudioManager.Play(Roar, P + Vector3.up * 1.6f, 1f, 0.95f);
                CameraDirector.Instance?.Shake(0.5f, Vector3.down);
                t = 0f;
                bool card = false;
                while (t < 3.4f && !skip)
                {
                    t += Time.deltaTime;
                    float k = Ease.OutCubic(Mathf.Clamp01(t / 3.4f));
                    Vector3 hp = rig.Head != null ? rig.Head.position : P + Vector3.up * 1.7f;
                    PlaceCamera(P + F * Mathf.Lerp(1.9f, 4.6f, k) + R * Mathf.Lerp(-0.4f, 0.9f, k) + Vector3.up * Mathf.Lerp(0.5f, 1.4f, k),
                        Vector3.Lerp(hp, P + Vector3.up * 1.1f, k), Mathf.Lerp(46f, 40f, k));
                    if (!card && t > 0.6f)
                    {
                        card = true;
                        AudioManager.Play2D(Sfx.MaskReceived, 1f);
                        hud?.ShowMaskReceived(m);
                    }
                    CheckSkip(t + 3f);
                    yield return null;
                }
                if (!card) hud?.ShowMaskReceived(m);
                yield return Wait(0.6f);
            }
            Finish(onComplete);
        }

        IEnumerator Wait(float s)
        {
            float t = 0f;
            while (t < s && !skip) { t += Time.deltaTime; yield return null; }
        }

        void Finish(System.Action onComplete)
        {
            onComplete?.Invoke();
            horror = 0f;
            ResetBodies();
            Teardown();
            if (brain) brain.enabled = true;
            CameraDirector.Instance?.Recenter();
            hud?.SetCinematic(false);
            player.ExitCinematic();
            PlayerInputReader.Instance?.SetGameplayEnabled(!(hud != null && hud.MenuOpen));
            Playing = false;
        }

        void CheckSkip(float t)
        {
            var input = PlayerInputReader.Instance;
            if (t > skipAfter && input != null && (input.CancelPressed || input.SubmitPressed)) skip = true;
        }

        // ------------------------------------------------------------------ helpers
        void PlaceCamera(Vector3 pos, Vector3 lookAt, float fov, float roll = 0f)
        {
            if (cam == null) return;
            Vector3 d = lookAt - pos;
            if (d.sqrMagnitude < 1e-4f) d = Vector3.forward;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(d) * Quaternion.Euler(0, 0, roll));
            cam.fieldOfView = fov;
        }

        static void Anim(FormRig rig, string state, float fade, float speed)
        {
            if (rig == null || rig.animator == null || !rig.animator.isActiveAndEnabled) return;
            rig.animator.SetFloat("ActionSpeed", speed);
            rig.animator.CrossFadeInFixedTime(state, fade);
        }

        void SetVisible(bool from, bool to)
        {
            foreach (var r in fromRenderers) if (r) r.enabled = from;
            foreach (var r in toRenderers) if (r) r.enabled = to;
            if (maskObj) maskObj.SetActive(from);
        }

        /// <summary>Undo the cinematic's tampering: every renderer visible again, bone scales back to 1.</summary>
        void ResetBodies()
        {
            foreach (var set in new[] { fromRenderers, toRenderers })
                if (set != null) foreach (var r in set) if (r) r.enabled = true;
            foreach (var rig in new[] { fromRig, toRig })
            {
                if (rig == null || rig.animator == null) continue;
                foreach (var b in new[] { HumanBodyBones.Head, HumanBodyBones.Chest, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm })
                {
                    var tr = rig.animator.GetBoneTransform(b);
                    if (tr) tr.localScale = Vector3.one;
                }
            }
        }

        void FaceMask(float scale)
        {
            var socket = fromRig.faceSocket != null ? fromRig.faceSocket : head;
            if (socket == null || maskObj == null) return;
            // the socket carries each body's fit: pushed out past a snout or brow, and sized to the head
            maskObj.transform.SetPositionAndRotation(socket.position, socket.rotation);
            maskObj.transform.localScale = socket.lossyScale * scale;
        }

        void TintVortex(float t, float k)
        {
            foreach (var go in new[] { vortex, vortexInner })
            {
                var r = go.GetComponent<Renderer>();
                if (r == null || r.sharedMaterial == null) continue;
                var mat = r.sharedMaterial;
                bool inner = go == vortexInner;
                float w = 0.5f + 0.5f * Mathf.Sin(t * (inner ? 3.1f : 1.7f));
                Color c = Color.Lerp(mask.vortexA, mask.vortexB, w);
                c = Color.Lerp(c, mask.vortexC, inner ? 0.2f : 0.45f * (1f - k));
                c.a = inner ? 0.55f : 1f;
                mat.SetColor(BaseColorId, c);
                mat.SetTextureOffset(BaseMapId, new Vector2(t * (inner ? -0.21f : 0.13f), t * (inner ? 0.9f : -0.55f)));
                go.transform.rotation = Quaternion.Euler(t * (inner ? 40f : -25f), t * (inner ? -70f : 55f), 0f);
            }
        }

        // body horror: the visible body bulges, stretches and twitches between forms
        void LateUpdate()
        {
            if (!Playing || horror <= 0f) return;
            foreach (var rig in new[] { fromRig, toRig })
            {
                if (rig == null || rig.animator == null) continue;
                var a = rig.animator;
                float tt = Time.time;
                void Scale(HumanBodyBones b, float amount, float freq, float phase)
                {
                    var tr = a.GetBoneTransform(b);
                    if (tr == null) return;
                    float n = Mathf.PerlinNoise(tt * freq, phase) * 2f - 1f;
                    tr.localScale = Vector3.one * (1f + n * amount * horror);
                }
                Scale(HumanBodyBones.Head, 0.35f, 7f, 1.3f);
                Scale(HumanBodyBones.Chest, 0.18f, 5f, 4.1f);
                Scale(HumanBodyBones.LeftLowerArm, 0.3f, 9f, 7.7f);
                Scale(HumanBodyBones.RightLowerArm, 0.3f, 9f, 2.2f);
                var neck = a.GetBoneTransform(HumanBodyBones.Neck);
                if (neck) neck.localRotation *= Quaternion.Euler(Mathf.Sin(tt * 31f) * 9f * horror, Mathf.Sin(tt * 23f) * 14f * horror, 0f);
            }
        }

        void OnDisable()
        {
            if (Playing) { Teardown(); if (brain) brain.enabled = true; Playing = false; }
        }
    }
}
