// Black swamp water for the Crocodile arena: murky depth fade over the mud, drifting duckweed rafts
// (reference photo 3), a faked sky reflection by fresnel, a hard white moon glint and the torch /
// floodlight glints from the FogLight array. PSX: vertex snap + 15-bit dither, point-sampled duckweed.
Shader "ElderTree/SwampWater"
{
    Properties
    {
        _DeepColor("Deep", Color) = (0.012, 0.03, 0.022, 1)
        _ShallowColor("Shallow", Color) = (0.05, 0.08, 0.045, 1)
        _ReflectColor("Sky Reflection", Color) = (0.12, 0.22, 0.14, 1)
        _WeedColor("Duckweed", Color) = (0.16, 0.26, 0.09, 1)
        _WeedTex("Duckweed (RGB, A = coverage)", 2D) = "black" {}
        _WeedScale("Duckweed Scale", Float) = 0.12
        _WeedAmount("Duckweed Amount", Range(0, 1)) = 0.55
        _DepthFade("Depth Fade (m)", Float) = 0.45
        _GlintPower("Moon Glint Power", Float) = 220
        _GlintStrength("Moon Glint", Float) = 2.4
        _LightGlint("Local Light Glint", Float) = 0.6
        _RippleScale("Ripple Scale", Float) = 0.7
        _RippleStrength("Ripple Strength", Float) = 0.07
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

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

            TEXTURE2D(_WeedTex); SAMPLER(sampler_WeedTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor, _ShallowColor, _ReflectColor, _WeedColor, _WeedTex_ST;
                float _WeedScale, _WeedAmount, _DepthFade, _GlintPower, _GlintStrength, _LightGlint, _RippleScale, _RippleStrength;
            CBUFFER_END
            float4 _PSXSnap;
            float _PSXDither;

            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; float eye : TEXCOORD1; float fog : TEXCOORD2; };

            V Vert(A i)
            {
                V o;
                o.wp = TransformObjectToWorld(i.pos.xyz);
                // no PSX vertex snap here: the water is one huge quad, and snapping its four corners makes the
                // whole sheet swim against the (differently snapped) ground, so the pools flicker along the shore
                float4 cs = TransformWorldToHClip(o.wp);
                o.pos = cs;
                o.eye = -TransformWorldToView(o.wp).z;
                o.fog = ComputeFogFactor(cs.z);
                return o;
            }

            static const float BAYER[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            float4 Frag(V i) : SV_Target
            {
                float t = _Time.y;
                float3 p = i.wp;
                // ripple normal from two drifting noise fields (finite differences)
                float2 rq = p.xz * _RippleScale;
                float h0 = FogNoise(float3(rq + float2(t * 0.11, t * 0.07), t * 0.2));
                float hx = FogNoise(float3(rq + float2(0.15, 0) + float2(t * 0.11, t * 0.07), t * 0.2));
                float hz = FogNoise(float3(rq + float2(0, 0.15) + float2(t * 0.11, t * 0.07), t * 0.2));
                float3 n = normalize(float3((h0 - hx) * _RippleStrength * 20.0, 1.0, (h0 - hz) * _RippleStrength * 20.0));

                float3 camPos = GetCameraPositionWS();
                float3 v = normalize(camPos - p);
                float fres = pow(1.0 - saturate(dot(n, v)), 4.0);

                // depth below the surface along the view ray -> murk
                float2 suv = GetNormalizedScreenSpaceUV(i.pos);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float depth = max(0.0, sceneEye - i.eye);
                float murk = saturate(depth / _DepthFade);

                Light ml = GetMainLight();
                float sh = MainLightRealtimeShadow(TransformWorldToShadowCoord(p));
                float3 amb = SampleSH(float3(0, 1, 0));
                float3 col = lerp(_ShallowColor.rgb, _DeepColor.rgb, murk);
                col += _ReflectColor.rgb * fres;

                // duckweed rafts drifting very slowly; point sampled for chunky PSX texels
                float2 wuv = p.xz * _WeedScale + float2(t * 0.004, t * 0.002);
                float4 weed = SAMPLE_TEXTURE2D(_WeedTex, sampler_WeedTex, wuv);
                float cover = step(1.0 - _WeedAmount, weed.a) * saturate(murk * 6.0);
                float3 weedLit = _WeedColor.rgb * weed.rgb * 2.0 * (amb + ml.color * saturate(ml.direction.y) * sh * 0.8);
                col = lerp(col, weedLit, cover);

                // moon glint + local light glints (open water only)
                float3 hv = normalize(ml.direction + v);
                float glint = pow(saturate(dot(n, hv)), _GlintPower) * _GlintStrength * sh;
                float3 spec = ml.color * glint;
                int cnt = (int)_FogLightCount;
                for (int k = 0; k < FOG_MAX_LIGHTS; k++)
                {
                    if (k >= cnt) break;
                    float3 d = _FogLightPos[k].xyz - p;
                    float d2 = max(dot(d, d), 0.04);
                    float f = d2 * _FogLightPos[k].w;
                    float win = saturate(1.0 - f * f);
                    float3 l = d * rsqrt(d2);
                    float3 h2 = normalize(l + v);
                    spec += _FogLightCol[k].rgb * (win * win / d2) * pow(saturate(dot(n, h2)), 60.0) * _LightGlint;
                }
                col += spec * (1.0 - cover);

                float alpha = lerp(saturate(murk * 1.6 + fres * 0.5), 1.0, cover);
                col = MixFog(col, i.fog);
                uint2 px = uint2(i.pos.xy) & 3u;
                float b = (BAYER[px.y * 4 + px.x] + 0.5) / 16.0 - 0.5;
                col = max(0.0, floor(col * 31.0 + 0.5 + b * _PSXDither) / 31.0);
                return float4(col, alpha);
            }
            ENDHLSL
        }
    }
}
