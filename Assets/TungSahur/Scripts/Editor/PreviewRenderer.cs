using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TungSahur.EditorTools
{
    /// <summary>
    /// Offline preview helper: samples humanoid clips on the character and renders contact
    /// sheets to PNG, and measures weapon-hand speed so attack hit windows can be derived from
    /// the actual animation instead of guessed.
    /// </summary>
    public static class PreviewRenderer
    {
        public static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/TungPreview"));

        public static GameObject SpawnCharacter(out Animator anim)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(TungSahurBuilder.ModelPath);
            var go = (GameObject)Object.Instantiate(model);
            go.hideFlags = HideFlags.DontSave;
            anim = go.GetComponent<Animator>();
            if (anim == null) anim = go.AddComponent<Animator>();
            anim.avatar = AssetDatabase.LoadAllAssetsAtPath(TungSahurBuilder.ModelPath).OfType<Avatar>().First();
            anim.applyRootMotion = false;
            // attach club
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var club = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TungSahurBuilder.ClubPath), hand);
            club.name = "Club";
            TungSahurBuilder.ApplyClubGrip(club.transform);
            return go;
        }

        static (Camera cam, Light light, RenderTexture rt, GameObject floor) Rig(int w, int h)
        {
            var camGo = new GameObject("PreviewCam") { hideFlags = HideFlags.DontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
            cam.fieldOfView = 30;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var lgo = new GameObject("PreviewLight") { hideFlags = HideFlags.DontSave };
            var light = lgo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.shadows = LightShadows.Soft;
            lgo.transform.rotation = Quaternion.Euler(40, 150, 0);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.hideFlags = HideFlags.DontSave;
            floor.transform.localScale = Vector3.one * 3;
            floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(TungSahurBuilder.MatDir + "/M_Ground.mat");
            return (cam, light, rt, floor);
        }

        static Texture2D Grab(Camera cam, RenderTexture rt)
        {
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }

        /// <summary>Renders the character from several angles (bind pose or a sampled clip time).</summary>
        public static string RenderTurnaround(string file, string clipPath = null, float time = 0f, float camDist = 4.2f, float lookY = 1.0f)
        {
            Directory.CreateDirectory(OutDir);
            var go = SpawnCharacter(out var anim);
            if (clipPath != null)
            {
                var clip = LoadClip(clipPath);
                clip.SampleAnimation(go, time);
            }
            var (cam, light, rt, floor) = Rig(480, 640);
            var sheet = new Texture2D(480 * 4, 640, TextureFormat.RGB24, false);
            float[] yaws = { 180, 135, 90, 0 };
            for (int i = 0; i < 4; i++)
            {
                var dir = Quaternion.Euler(0, yaws[i], 0) * Vector3.forward;
                cam.transform.position = new Vector3(0, lookY + 0.15f, 0) - dir * camDist;
                cam.transform.LookAt(new Vector3(0, lookY, 0));
                var tex = Grab(cam, rt);
                sheet.SetPixels(i * 480, 0, 480, 640, tex.GetPixels());
                Object.DestroyImmediate(tex);
            }
            string path = Path.Combine(OutDir, file);
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Cleanup(go, cam, light, rt, floor);
            Object.DestroyImmediate(sheet);
            return path;
        }

        /// <summary>Contact sheet of a clip: N frames side by side, viewed from the front-right.</summary>
        public static string RenderClipSheet(string file, string clipPath, int frames = 8, float yaw = 210f, float camDist = 4.6f)
        {
            Directory.CreateDirectory(OutDir);
            var go = SpawnCharacter(out var anim);
            var clip = LoadClip(clipPath);
            var (cam, light, rt, floor) = Rig(300, 420);
            var sheet = new Texture2D(300 * frames, 420, TextureFormat.RGB24, false);
            var dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            cam.transform.position = new Vector3(0, 1.3f, 0) - dir * camDist;
            cam.transform.LookAt(new Vector3(0, 1.0f, 0));
            for (int i = 0; i < frames; i++)
            {
                float t = clip.length * i / (frames - 1);
                clip.SampleAnimation(go, t);
                go.transform.position = Vector3.zero;
                var tex = Grab(cam, rt);
                sheet.SetPixels(i * 300, 0, 300, 420, tex.GetPixels());
                Object.DestroyImmediate(tex);
            }
            string path = Path.Combine(OutDir, file);
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Cleanup(go, cam, light, rt, floor);
            Object.DestroyImmediate(sheet);
            return path;
        }

        public const string ReferenceModelPath = "Assets/Kevin Iglesias/Human Animations/Models/HumanM_Model.fbx";

        /// <summary>
        /// Retarget check: the pack's own HumanM model (top row) and the Elder Tree (bottom row)
        /// playing the same clip at the same frames, so pose differences are easy to spot.
        /// </summary>
        public static string RenderCompareSheet(string file, string clipPath, int frames = 6, float yaw = 210f, float camDist = 4.8f)
        {
            Directory.CreateDirectory(OutDir);
            var ours = SpawnCharacter(out _);
            var refModel = AssetDatabase.LoadAssetAtPath<GameObject>(ReferenceModelPath);
            var theirs = (GameObject)Object.Instantiate(refModel);
            theirs.hideFlags = HideFlags.DontSave;
            var clip = LoadClip(clipPath);
            const int W = 260, H = 380;
            var (cam, light, rt, floor) = Rig(W, H);
            var sheet = new Texture2D(W * frames, H * 2, TextureFormat.RGB24, false);
            var dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            cam.transform.position = new Vector3(0, 1.3f, 0) - dir * camDist;
            cam.transform.LookAt(new Vector3(0, 1.0f, 0));
            for (int row = 0; row < 2; row++)
            {
                var go = row == 0 ? theirs : ours;
                var other = row == 0 ? ours : theirs;
                other.SetActive(false);
                go.SetActive(true);
                for (int i = 0; i < frames; i++)
                {
                    float t = clip.length * i / Mathf.Max(1, frames - 1);
                    clip.SampleAnimation(go, t);
                    go.transform.position = Vector3.zero;
                    var tex = Grab(cam, rt);
                    sheet.SetPixels(i * W, (1 - row) * H, W, H, tex.GetPixels());
                    Object.DestroyImmediate(tex);
                }
            }
            string path = Path.Combine(OutDir, file);
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Object.DestroyImmediate(theirs);
            Cleanup(ours, cam, light, rt, floor);
            Object.DestroyImmediate(sheet);
            return path;
        }

        /// <summary>Close-ups of one bone (e.g. a hand gripping the club) at a clip time, from 4 sides.</summary>
        public static string RenderBoneCloseup(string file, string clipPath, float normalizedTime, HumanBodyBones bone, float dist = 0.8f)
        {
            Directory.CreateDirectory(OutDir);
            var go = SpawnCharacter(out var anim);
            var clip = LoadClip(clipPath);
            clip.SampleAnimation(go, clip.length * normalizedTime);
            go.transform.position = Vector3.zero;
            var target = anim.GetBoneTransform(bone).position;
            var (cam, light, rt, floor) = Rig(360, 360);
            var sheet = new Texture2D(360 * 4, 360, TextureFormat.RGB24, false);
            float[] yaws = { 180, 90, 0, 270 };
            for (int i = 0; i < 4; i++)
            {
                var dir = Quaternion.Euler(15, yaws[i], 0) * Vector3.forward;
                cam.transform.position = target - dir * dist;
                cam.transform.LookAt(target);
                cam.nearClipPlane = 0.02f;
                var tex = Grab(cam, rt);
                sheet.SetPixels(i * 360, 0, 360, 360, tex.GetPixels());
                Object.DestroyImmediate(tex);
            }
            string path = Path.Combine(OutDir, file);
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Cleanup(go, cam, light, rt, floor);
            Object.DestroyImmediate(sheet);
            return path;
        }

        /// <summary>Samples the club tip trajectory, returns speed profile and the high-speed window.</summary>
        public static string AnalyzeSwing(string clipPath, int samples = 90)
        {
            var go = SpawnCharacter(out var anim);
            var clip = LoadClip(clipPath);
            var club = go.GetComponentsInChildren<Transform>().First(t => t.name == "Club");
            var sb = new StringBuilder();
            Vector3 prev = Vector3.zero;
            var speeds = new List<(float t, float v, Vector3 p)>();
            for (int i = 0; i <= samples; i++)
            {
                float t = clip.length * i / samples;
                clip.SampleAnimation(go, t);
                var p = club.TransformPoint(new Vector3(0, 0.75f, 0));
                if (i > 0) speeds.Add((t / clip.length, (p - prev).magnitude / (clip.length / samples), p));
                prev = p;
            }
            float max = speeds.Max(s => s.v);
            var fast = speeds.Where(s => s.v > max * 0.55f).ToList();
            sb.Append($"{Path.GetFileNameWithoutExtension(clipPath)} len={clip.length:F2} peak={max:F1}m/s at {speeds.First(s => s.v == max).t:F2} window={fast.First().t:F2}-{fast.Last().t:F2} | ");
            for (int i = 0; i < speeds.Count; i += 6) sb.Append($"{speeds[i].t:F2}:{speeds[i].v:F1}(y{speeds[i].p.y:F1}) ");
            Object.DestroyImmediate(go);
            return sb.ToString();
        }


        public static AnimationClip LoadClip(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));

        static void Cleanup(GameObject go, Camera cam, Light light, RenderTexture rt, GameObject floor)
        {
            Object.DestroyImmediate(go);
            cam.targetTexture = null;
            Object.DestroyImmediate(cam.gameObject);
            Object.DestroyImmediate(light.gameObject);
            Object.DestroyImmediate(floor);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
