using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ElderTree.FX
{
    /// <summary>
    /// Stretches the main light's shadow distance while this scene runs (and puts it back after). The forest's god
    /// rays are the volumetric fog sampling the sun's shadow map: past the shadow distance the haze is lit
    /// everywhere and the shafts melt into a bright wall, so the canopy needs shadows further out than the
    /// 40 m the other stages use. Play mode only, so the shared pipeline asset is never saved with it.
    /// </summary>
    public class ShadowRange : MonoBehaviour
    {
        public float distance = 60f;

        float previous = -1f;

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset rp)
            {
                previous = rp.shadowDistance;
                rp.shadowDistance = distance;
            }
        }

        void OnDisable()
        {
            if (previous < 0f) return;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset rp) rp.shadowDistance = previous;
            previous = -1f;
        }
    }
}
