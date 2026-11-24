using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ElderTree.FX
{
    /// <summary>
    /// Pixelation option: the PSX look renders at a low internal resolution (URP render scale, 0.4 by
    /// default) upscaled with nearest filtering. Turning it off renders at native resolution. The URP
    /// asset is shared, so the authored values are restored when the game quits (and when play mode
    /// stops in the editor) to keep the asset clean.
    /// </summary>
    public static class PsxPipeline
    {
        const float FallbackScale = 0.4f;

        static UniversalRenderPipelineAsset asset;
        static float authoredScale;
        static UpscalingFilterSelection authoredFilter;

        public static bool Pixelated { get; private set; } = true;

        public static void SetPixelation(bool on)
        {
            Pixelated = on;
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (rp == null) return;
            if (asset != rp)
            {
                Restore();
                asset = rp;
                authoredScale = rp.renderScale;
                authoredFilter = rp.upscalingFilter;
                Application.quitting -= Restore;
                Application.quitting += Restore;
            }
            bool authoredPixelated = authoredScale < 0.99f;
            if (on)
            {
                rp.renderScale = authoredPixelated ? authoredScale : FallbackScale;
                rp.upscalingFilter = authoredPixelated ? authoredFilter : UpscalingFilterSelection.Point;
            }
            else
            {
                rp.renderScale = 1f;
                rp.upscalingFilter = UpscalingFilterSelection.Auto;
            }
        }

        static void Restore()
        {
            if (asset == null) return;
            asset.renderScale = authoredScale;
            asset.upscalingFilter = authoredFilter;
            asset = null;
        }
    }
}
