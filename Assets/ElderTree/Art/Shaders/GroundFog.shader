// Ground-hugging fog shells (ElderTree.FX.GroundFog). Each layer of the stack is a big horizontal quad;
// per pixel we take the height above the baked ground, a drifting noise warped by the fluid field's flow,
// the field's "cleared" amount, soft depth intersection with the scene, and light it with the moon
// (shadow mapped) and the FogLight array. Premultiplied alpha, PSX 15-bit dither on the output.
Shader "ElderTree/GroundFog"
{
    Properties
    {
        _Color("Fog Colour", Color) = (0.55, 0.68, 0.56, 1)
        _Ambient("Ambient", Color) = (0.07, 0.11, 0.08, 1)
        _Density("Layer Opacity", Range(0, 1)) = 0.16
        _Height("Fog Height (m)", Float) = 1.3
        _NoiseScale("Noise Scale", Float) = 0.22
        _Wind("Wind (xz m/s)", Vector) = (0.5, 0, 0.2, 0)
        _SoftDistance("Soft Intersection (m)", Float) = 0.6
        _EdgeRadius("Edge Fade Start (m)", Float) = 24
        _EdgeWidth("Edge Fade Width (m)", Float) = 6
        _MoonScatter("Moon Scatter", Float) = 0.7
        _LightScatter("Local Light Scatter", Float) = 0.5
        _Anisotropy("Anisotropy", Range(-0.9, 0.95)) = 0.4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "GroundFog"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "FogCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _Ambient, _Wind;
                float _Density, _Height, _NoiseScale, _SoftDistance, _EdgeRadius, _EdgeWidth, _MoonScatter, _LightScatter, _Anisotropy;
            CBUFFER_END
            float _PSXDither;

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V
            {
                float4 pos : SV_POSITION;
                float3 wp : TEXCOORD0;
                float layer : TEXCOORD1;
                float eye : TEXCOORD2;
                float fog : TEXCOORD3;
            };

            V Vert(A i)
            {
                V o;
                o.wp = TransformObjectToWorld(i.pos.xyz);
                o.pos = TransformWorldToHClip(o.wp);
                o.layer = i.uv.y;
                o.eye = -TransformWorldToView(o.wp).z;
                o.fog = ComputeFogFactor(o.pos.z);
                return o;
            }

            static const float BAYER[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            float4 Frag(V i) : SV_Target
            {
                float3 p = i.wp;
                // height above the baked ground under this pixel
                float2 guv = GFUV(p);
                float g = (_GFRect.w > 0.5 && all(guv > 0) && all(guv < 1)) ? SAMPLE_TEXTURE2D_LOD(_GFHeightTex, sampler_GFHeightTex, guv, 0).r : -0.2;
                float above = p.y - g;
                if (above < 0.0) discard;
                float hk = saturate(1.0 - above / _Height);
                hk *= hk;

                // the fluid field: cleared amount + flow that drags the noise along
                float4 field = (_GFRect.w > 0.5 && all(guv > 0) && all(guv < 1)) ? SAMPLE_TEXTURE2D_LOD(_GFFieldTex, sampler_GFFieldTex, guv, 0) : 0;
                float cleared = field.r;
                float2 flow = field.gb;
                float t = _Time.y;
                float3 q = float3(p.x, p.y * 1.7 + i.layer * 0.37, p.z) * _NoiseScale;
                q.xz -= (_Wind.xz * t + flow * 0.45) * _NoiseScale;
                float n = FogFbm(q);
                float n2 = FogNoise(q * 2.7 + float3(0, t * 0.15, 0));
                float puff = saturate((n * 0.8 + n2 * 0.35) * 1.5 - 0.35);
                // pushed-away fog leaves wispy edges instead of a clean hole
                float dens = hk * puff * saturate(1.0 - cleared * (1.15 + 0.5 * n2));

                // soft intersection with the scene + fade right in front of the camera + fade at the far edge
                float2 suv = GetNormalizedScreenSpaceUV(i.pos);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float soft = saturate((sceneEye - i.eye) / _SoftDistance);
                float nearFade = saturate((i.eye - 0.4) / 2.2);
                float edge = saturate(1.0 - (length(p.xz - (_GFRect.xy + 0.5 / _GFRect.z)) - _EdgeRadius) / _EdgeWidth);
                float a = saturate(dens * _Density * soft * nearFade * edge);
                if (a < 0.002) discard;

                // light: ambient + moon through the shadow map + torches / lamps
                Light ml = GetMainLight();
                float3 view = normalize(p - GetCameraPositionWS());
                float sh = MainLightRealtimeShadow(TransformWorldToShadowCoord(p));
                float3 light = _Ambient.rgb
                    + ml.color * (_MoonScatter * sh * (0.35 + FogPhase(dot(view, ml.direction), _Anisotropy) * 4.0))
                    + FogLocalLights(p, view, _Anisotropy) * _LightScatter;
                float3 c = _Color.rgb * light;
                c = MixFog(c, i.fog);

                // PSX 15-bit output dither, like PSXLit
                uint2 px = uint2(i.pos.xy) & 3u;
                float b = (BAYER[px.y * 4 + px.x] + 0.5) / 16.0 - 0.5;
                if (_PSXDither > 0.0) c = max(0.0, floor(c * 31.0 + 0.5 + b * _PSXDither) / 31.0);
                return float4(c * a, a);
            }
            ENDHLSL
        }
    }
}
