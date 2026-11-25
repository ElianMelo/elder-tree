using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ElderTree.FX
{
    /// <summary>
    /// A planar mirror at this transform's height: once a frame, just before the cameras draw, a hidden camera
    /// renders the scene seen from under the plane into a low-res texture that the Puddle shader samples
    /// (_PlanarReflectionTex). The hidden camera is a proper (not mirrored) camera, so culling stays normal; its
    /// picture is the true mirror image flipped left-right, which the shader undoes by reading u as 1 - u. An
    /// oblique near plane cuts away everything below the water. Point filtered at a fraction of the screen size,
    /// like the rest of the PSX frame. Used by the Fairy grotto's puddles (all of them share one water level).
    /// </summary>
    [ExecuteAlways]
    public class PlanarReflection : MonoBehaviour
    {
        [Tooltip("Reflection size as a fraction of the main camera's pixel size (that camera already renders at the PSX render scale).")]
        [Range(0.1f, 1f)] public float resolution = 0.35f;
        [Tooltip("Layers the reflection draws (the water's own layer is always left out).")]
        public LayerMask cullingMask = ~0;
        [Tooltip("Lifts the clip plane a little so the floor at the rim doesn't leak into the mirror.")]
        public float clipOffset = 0.03f;
        public bool shadows = true;

        Camera reflCam;
        RenderTexture rt;
        static readonly int TexId = Shader.PropertyToID("_PlanarReflectionTex");

        void OnEnable()
        {
            Application.onBeforeRender += OnBeforeRender;
            Shader.SetGlobalTexture(TexId, Texture2D.blackTexture);
        }

        void OnDisable()
        {
            Application.onBeforeRender -= OnBeforeRender;
            if (reflCam != null) DestroyImmediate(reflCam.gameObject);
            if (rt != null) { rt.Release(); DestroyImmediate(rt); }
            reflCam = null; rt = null;
        }

        void OnBeforeRender()
        {
            if (!Application.isPlaying) return;      // edit-mode shots call RenderFor themselves
            var cam = Camera.main;
            if (cam != null && cam.isActiveAndEnabled) RenderFor(cam);
        }

        /// <summary>Render the mirror for this camera now (play mode does it every frame on its own).</summary>
        public void RenderFor(Camera cam)
        {
            EnsureCamera();
            int w = Mathf.Max(64, Mathf.RoundToInt(cam.pixelWidth * resolution));
            int h = Mathf.Max(36, Mathf.RoundToInt(cam.pixelHeight * resolution));
            if (rt == null || rt.width != w || rt.height != h)
            {
                if (rt != null) { rt.Release(); DestroyImmediate(rt); }
                rt = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR) { name = "PlanarReflection", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                rt.Create();
            }

            float y = transform.position.y;
            var ct = cam.transform;
            Vector3 p = ct.position, f = ct.forward, u = ct.up;
            reflCam.transform.SetPositionAndRotation(new Vector3(p.x, 2f * y - p.y, p.z), Quaternion.LookRotation(new Vector3(f.x, -f.y, f.z), new Vector3(u.x, -u.y, u.z)));
            reflCam.fieldOfView = cam.fieldOfView;
            reflCam.aspect = cam.aspect;
            reflCam.nearClipPlane = cam.nearClipPlane;
            reflCam.farClipPlane = cam.farClipPlane;
            reflCam.clearFlags = cam.clearFlags;
            reflCam.backgroundColor = cam.backgroundColor;
            reflCam.cullingMask = cam.cullingMask & cullingMask & ~(1 << gameObject.layer);
            reflCam.ResetProjectionMatrix();
            reflCam.projectionMatrix = reflCam.CalculateObliqueMatrix(CameraSpacePlane(reflCam, new Vector3(0, y + clipOffset, 0), Vector3.up));
            reflCam.targetTexture = rt;
            reflCam.Render();
            Shader.SetGlobalTexture(TexId, rt);
        }

        static Vector4 CameraSpacePlane(Camera c, Vector3 pos, Vector3 normal)
        {
            var m = c.worldToCameraMatrix;
            Vector3 cp = m.MultiplyPoint(pos);
            Vector3 cn = m.MultiplyVector(normal).normalized;
            return new Vector4(cn.x, cn.y, cn.z, -Vector3.Dot(cp, cn));
        }

        void EnsureCamera()
        {
            if (reflCam != null) return;
            var go = new GameObject("PlanarReflectionCamera") { hideFlags = HideFlags.HideAndDontSave };
            reflCam = go.AddComponent<Camera>();
            reflCam.enabled = false;
            reflCam.cameraType = CameraType.Reflection;     // the volumetric fog pass skips it
            reflCam.allowMSAA = false;
            reflCam.allowHDR = true;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = shadows;
            data.requiresDepthTexture = false;
            data.requiresColorTexture = false;
            data.antialiasing = AntialiasingMode.None;
        }
    }
}
