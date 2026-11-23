using System.Collections.Generic;
using UnityEngine;

namespace ElderTree.FX
{
    /// <summary>
    /// Marks a point or spot light as one that lights the fog (volumetric haze, ground fog shells, water glints).
    /// The fog shaders read up to 16 of them from global arrays (FogCommon.hlsl), nearest to the camera first.
    /// Intensity is read live, so flickering torches and sweeping searchlights show in the fog.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(Light))]
    public class FogLight : MonoBehaviour
    {
        [Tooltip("Multiplier on the light's colour x intensity inside the fog.")]
        public float scatter = 1f;

        Light lightSource;
        static readonly List<FogLight> All = new();
        const int Max = 16;
        static readonly Vector4[] pos = new Vector4[Max], col = new Vector4[Max], dir = new Vector4[Max], spot = new Vector4[Max];
        static int uploadedFrame = -1;
        static readonly int CountId = Shader.PropertyToID("_FogLightCount"), PosId = Shader.PropertyToID("_FogLightPos"),
            ColId = Shader.PropertyToID("_FogLightCol"), DirId = Shader.PropertyToID("_FogLightDir"), SpotId = Shader.PropertyToID("_FogLightSpot");

        void OnEnable() { lightSource = GetComponent<Light>(); All.Add(this); }
        void OnDisable() => All.Remove(this);

        /// <summary>Push the light arrays (once per frame; called by the fog feature and the ground fog).</summary>
        public static void Upload()
        {
            if (Application.isPlaying && uploadedFrame == Time.frameCount) return;
            uploadedFrame = Time.frameCount;
            var cam = Camera.main;
            Vector3 c = cam != null ? cam.transform.position : Vector3.zero;
            if (All.Count > Max) All.Sort((a, b) => (a.transform.position - c).sqrMagnitude.CompareTo((b.transform.position - c).sqrMagnitude));
            int n = 0;
            foreach (var f in All)
            {
                if (n >= Max) break;
                var l = f.lightSource;
                if (l == null || !l.isActiveAndEnabled || l.intensity <= 0f) continue;
                var t = f.transform;
                pos[n] = new Vector4(t.position.x, t.position.y, t.position.z, 1f / Mathf.Max(0.01f, l.range * l.range));
                var lc = l.color.linear * (l.intensity * f.scatter);
                bool isSpot = l.type == LightType.Spot;
                col[n] = new Vector4(lc.r, lc.g, lc.b, isSpot ? 1f : 0f);
                float cosOuter = Mathf.Cos(l.spotAngle * 0.5f * Mathf.Deg2Rad);
                float cosInner = Mathf.Cos(Mathf.Max(0.1f, l.innerSpotAngle) * 0.5f * Mathf.Deg2Rad);
                var fw = t.forward;
                dir[n] = new Vector4(fw.x, fw.y, fw.z, cosOuter);
                spot[n] = new Vector4(1f / Mathf.Max(1e-3f, cosInner - cosOuter), 0, 0, 0);
                n++;
            }
            Shader.SetGlobalFloat(CountId, n);
            Shader.SetGlobalVectorArray(PosId, pos);
            Shader.SetGlobalVectorArray(ColId, col);
            Shader.SetGlobalVectorArray(DirId, dir);
            Shader.SetGlobalVectorArray(SpotId, spot);
        }
    }
}
