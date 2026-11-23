using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace ElderTree.FX
{
    /// <summary>
    /// URP renderer feature that draws <see cref="VolumetricFog"/> after transparents (so water, ground-fog shells
    /// and particles sit inside the haze) and before post-processing (so bloom picks up the light halos).
    /// </summary>
    public class VolumetricFogFeature : ScriptableRendererFeature
    {
        public Shader shader;
        Material material;
        FogPass pass;

        public override void Create()
        {
            pass = new FogPass { renderPassEvent = RenderPassEvent.AfterRenderingTransparents };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var v = VolumeManager.instance.stack?.GetComponent<VolumetricFog>();
            if (v == null || !v.IsActive()) return;
            if (material == null)
            {
                if (shader == null) shader = Shader.Find("Hidden/ElderTree/VolumetricFog");
                if (shader == null) return;
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            FogLight.Upload();
            pass.Setup(material, v);
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing) => CoreUtils.Destroy(material);

        class FogPass : ScriptableRenderPass
        {
            Material mat;
            class PassData { public Material mat; }

            static readonly int Density = Shader.PropertyToID("_VFDensity"), BaseHeight = Shader.PropertyToID("_VFBaseHeight"),
                Falloff = Shader.PropertyToID("_VFFalloff"), MaxDist = Shader.PropertyToID("_VFMaxDistance"), Steps = Shader.PropertyToID("_VFSteps"),
                Aniso = Shader.PropertyToID("_VFAnisotropy"), NScale = Shader.PropertyToID("_VFNoiseScale"), NStr = Shader.PropertyToID("_VFNoiseStrength"),
                Wind = Shader.PropertyToID("_VFWind"), Albedo = Shader.PropertyToID("_VFAlbedo"), Ambient = Shader.PropertyToID("_VFAmbient"),
                MainScatter = Shader.PropertyToID("_VFMainScatter"), LocalScatter = Shader.PropertyToID("_VFLocalScatter"),
                GroundClear = Shader.PropertyToID("_VFGroundClear");

            public void Setup(Material m, VolumetricFog v)
            {
                mat = m;
                m.SetFloat(Density, v.density.value);
                m.SetFloat(BaseHeight, v.baseHeight.value);
                m.SetFloat(Falloff, v.heightFalloff.value);
                m.SetFloat(MaxDist, v.maxDistance.value);
                m.SetFloat(Steps, v.steps.value);
                m.SetFloat(Aniso, v.anisotropy.value);
                m.SetFloat(NScale, v.noiseScale.value);
                m.SetFloat(NStr, v.noiseStrength.value);
                m.SetVector(Wind, v.wind.value);
                m.SetColor(Albedo, v.albedo.value);
                m.SetColor(Ambient, v.ambient.value);
                m.SetFloat(MainScatter, v.mainLightScatter.value);
                m.SetFloat(LocalScatter, v.localLightScatter.value);
                m.SetFloat(GroundClear, v.groundClear.value);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var res = frameData.Get<UniversalResourceData>();
                var cam = frameData.Get<UniversalCameraData>();
                if (mat == null || res.isActiveTargetBackBuffer) return;
                if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
                if (!cam.postProcessEnabled) return;   // the fog belongs to the post stack (UI / mask cameras skip it)

                using var b = renderGraph.AddRasterRenderPass<PassData>("Elder Tree Volumetric Fog", out var data);
                data.mat = mat;
                b.SetRenderAttachment(res.activeColorTexture, 0, AccessFlags.ReadWrite);
                if (res.cameraDepthTexture.IsValid()) b.UseTexture(res.cameraDepthTexture);
                if (res.mainShadowsTexture.IsValid()) b.UseTexture(res.mainShadowsTexture);
                b.UseAllGlobalTextures(true);
                b.AllowPassCulling(false);
                b.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.mat, 0, MeshTopology.Triangles, 3, 1));
            }
        }
    }
}
