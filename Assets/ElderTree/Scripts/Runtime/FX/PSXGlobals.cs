using UnityEngine;

namespace ElderTree.FX
{
    /// <summary>
    /// Pushes the PSX look globals read by the ElderTree/PSXLit shader (Docs/ART_DIRECTION.md):
    /// clip-space vertex snapping grid and the 15-bit output dither strength. The low internal
    /// resolution itself comes from the URP asset (render scale + point upscaling).
    /// </summary>
    [ExecuteAlways]
    public class PSXGlobals : MonoBehaviour
    {
        [Tooltip("Vertex snap grid across the screen (PS1 ran at 320x240). 0 disables the wobble.")]
        public Vector2 snapResolution = new Vector2(320f, 180f);
        [Range(0f, 1f)] public float ditherStrength = 0.45f;
        [Tooltip("Match the grid to the screen aspect so snapping steps are square.")]
        public bool matchAspect = true;

        static readonly int SnapId = Shader.PropertyToID("_PSXSnap");
        static readonly int DitherId = Shader.PropertyToID("_PSXDither");

        void OnEnable() => Push();
        void OnValidate() => Push();
        void Update() => Push();

        void OnDisable()
        {
            Shader.SetGlobalVector(SnapId, Vector4.zero);
            Shader.SetGlobalFloat(DitherId, 0f);
        }

        void Push()
        {
            var g = snapResolution;
            if (matchAspect && g.y > 0f && Screen.height > 0)
                g.x = g.y * Screen.width / (float)Screen.height;
            Shader.SetGlobalVector(SnapId, new Vector4(g.x, g.y, 0, 0));
            Shader.SetGlobalFloat(DitherId, ditherStrength);
        }
    }
}
