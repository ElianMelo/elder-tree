// The Shark arena's sea ("Bleached Atoll"): a grid that rolls with four Gerstner swells coming in from the open
// south, damped by the vertex colour where the lagoon is shallow (R = 0 at the shore, 1 in open water). Colour
// runs from murky turquoise over the sand to deep navy by the water's thickness (scene depth); whitecaps ride
// the crests, foam rims every pile, rock and the beach, and bands of surf wash up the shore. A grey day: the sky
// reflection is a cold fresnel, the sun leaves a hard glint where the clouds let it, the deck's shadow darkens
// the water under it. PSX: 15-bit ordered dither, point-sampled foam noise (no vertex snap: see SwampWater).
Shader "ElderTree/Ocean"
{
    Properties
    {
        _DeepColor("Deep", Color) = (0.02, 0.07, 0.14, 1)
        _ShallowColor("Shallow", Color) = (0.12, 0.33, 0.36, 1)
        _SkyColor("Sky Reflection", Color) = (0.42, 0.52, 0.62, 1)
        _CrestColor("Crest (light through the wave)", Color) = (0.15, 0.42, 0.44, 1)
        _FoamColor("Foam", Color) = (0.78, 0.84, 0.86, 1)
        _DepthFade("Depth Fade (m)", Float) = 3.2
        _ShallowAlpha("Shallow Alpha", Range(0, 1)) = 0.55
        _WaveAmp("Swell Height (m)", Float) = 0.32
        _WaveLength("Swell Length (m)", Float) = 16
        _WaveSpeed("Swell Speed", Float) = 1
        _Steepness("Steepness", Range(0, 1)) = 0.55
        _FoamWidth("Contact Foam (m)", Float) = 0.45
        _ShoreBands("Surf Bands", Float) = 5
        _CapAmount("Whitecaps", Range(0, 1)) = 0.35
        _GlintPower("Sun Glint Power", Float) = 260
        _GlintStrength("Sun Glint", Float) = 3
        _RippleScale("Ripple Scale", Float) = 0.9
        _RippleStrength("Ripple Strength", Float) = 0.12
        _FoamPixel("Foam Pixel (m)", Float) = 0.12
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Ocean"
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

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor, _ShallowColor, _SkyColor, _CrestColor, _FoamColor;
                float _DepthFade, _ShallowAlpha, _WaveAmp, _WaveLength, _WaveSpeed, _Steepness, _FoamWidth, _ShoreBands, _CapAmount;
                float _GlintPower, _GlintStrength, _RippleScale, _RippleStrength, _FoamPixel;
            CBUFFER_END
            float _PSXDither;

            struct A { float4 pos : POSITION; float4 col : COLOR; };
            struct V
            {
                float4 pos : SV_POSITION;
                float3 wp : TEXCOORD0;
                float3 n : TEXCOORD1;
                float eye : TEXCOORD2;
                float fog : TEXCOORD3;
                float2 sw : TEXCOORD4;    // x = open-water factor, y = crest height 0..1
            };

            // one Gerstner swell: displacement + the partial derivatives for the normal
            void Swell(float2 dir, float len, float amp, float q, float speed, float2 xz, float t, inout float3 d, inout float3 tx, inout float3 tz)
            {
                float k = 6.2831853 / len;
                float c = sqrt(9.8 / k) * speed;
                dir = normalize(dir);
                float f = k * (dot(dir, xz) - c * t);
                float s = sin(f), co = cos(f);
                float qa = q / k;           // horizontal pull toward the crests: sharp crests, wide troughs
                d += float3(dir.x * qa * co, amp * s, dir.y * qa * co);
                tx += float3(-dir.x * dir.x * qa * k * s, dir.x * amp * k * co, -dir.x * dir.y * qa * k * s);
                tz += float3(-dir.x * dir.y * qa * k * s, dir.y * amp * k * co, -dir.y * dir.y * qa * k * s);
            }

            V Vert(A i)
            {
                V o;
                float3 wp = TransformObjectToWorld(i.pos.xyz);
                float open = saturate(i.col.r);
                float amp = _WaveAmp * lerp(0.18, 1.0, open);
                float q = _Steepness * lerp(0.4, 1.0, open) / 4.0;
                float t = _Time.y * _WaveSpeed;
                float3 d = 0, tx = float3(1, 0, 0), tz = float3(0, 0, 1);
                float L = _WaveLength;
                // the swell comes in from the open sea to the south (+z is the island)
                Swell(float2(0.12, 1.0), L, amp * 0.55, q, 1.0, wp.xz, t, d, tx, tz);
                Swell(float2(-0.45, 0.9), L * 0.61, amp * 0.25, q, 1.0, wp.xz, t, d, tx, tz);
                Swell(float2(0.55, 0.85), L * 0.37, amp * 0.13, q, 1.0, wp.xz, t, d, tx, tz);
                Swell(float2(-0.1, 1.0), L * 0.21, amp * 0.07, q, 1.0, wp.xz, t, d, tx, tz);
                wp += d;
                o.wp = wp;
                o.n = normalize(cross(tz, tx));
                float4 cs = TransformWorldToHClip(wp);
                o.pos = cs;
                o.eye = -TransformWorldToView(wp).z;
                o.fog = ComputeFogFactor(cs.z);
                o.sw = float2(open, saturate(d.y / max(amp, 1e-3) * 0.5 + 0.5));
                return o;
            }

            static const float BAYER[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            float4 Frag(V i) : SV_Target
            {
                float t = _Time.y;
                float3 p = i.wp;
                // small ripples on top of the swell (finite differences of drifting noise)
                float2 rq = p.xz * _RippleScale;
                float2 drift = float2(t * 0.05, t * 0.21);
                float h0 = FogNoise(float3(rq + drift, t * 0.3));
                float hx = FogNoise(float3(rq + float2(0.15, 0) + drift, t * 0.3));
                float hz = FogNoise(float3(rq + float2(0, 0.15) + drift, t * 0.3));
                float3 n = normalize(i.n + float3(h0 - hx, 0, h0 - hz) * _RippleStrength * 14.0);

                float3 camPos = GetCameraPositionWS();
                float3 v = normalize(camPos - p);
                float fres = pow(1.0 - saturate(dot(n, v)), 5.0);

                // water thickness along the view ray -> colour + alpha
                float2 suv = GetNormalizedScreenSpaceUV(i.pos);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float thick = max(0.0, sceneEye - i.eye);
                float murk = saturate(thick / _DepthFade);

                Light ml = GetMainLight();
                float sh = MainLightRealtimeShadow(TransformWorldToShadowCoord(p));
                float3 amb = SampleSH(float3(0, 1, 0));
                float ndl = saturate(dot(n, ml.direction));
                float3 lightTerm = amb * lerp(0.55, 1.0, sh) + ml.color * (0.35 + 0.65 * ndl) * lerp(0.25, 1.0, sh) * 0.55;   // gloomy in the dock's shade

                float3 col = lerp(_ShallowColor.rgb, _DeepColor.rgb, murk * murk * (3.0 - 2.0 * murk));
                // light through the crests on the sun side
                float back = saturate(dot(-v.xz, ml.direction.xz) * 0.5 + 0.5);
                col += _CrestColor.rgb * pow(i.sw.y, 3.0) * (0.35 + 0.65 * back) * murk;
                col *= lightTerm;
                col = lerp(col, _SkyColor.rgb * (0.45 + 0.55 * sh), fres * 0.75);

                // foam: point-sampled noise cells so it stays chunky
                float3 fp = floor(p / _FoamPixel) * _FoamPixel;
                float fn = FogNoise(float3(fp.xz * 1.6 + float2(t * 0.15, -t * 0.1), t * 0.4)) * 0.65
                         + FogNoise(float3(fp.xz * 4.1 - float2(t * 0.2, 0), t * 0.7)) * 0.35;
                // contact foam: rims every pile, rock and the beach
                float contact = 1.0 - saturate(thick / _FoamWidth);
                float foam = step(1.0 - contact * 1.15, fn);
                // surf bands washing up the shore where the lagoon shoals (open < 0.5)
                float shore = saturate(1.0 - i.sw.x * 2.0);
                float band = frac(i.sw.x * _ShoreBands - t * 0.09);
                foam = max(foam, step(band, 0.16 * shore) * step(0.42, fn) * shore);
                // whitecaps on the tallest crests out at sea
                foam = max(foam, step(1.0 - _CapAmount * 0.5, i.sw.y) * step(0.55, fn) * i.sw.x);
                float3 foamCol = _FoamColor.rgb * (amb * 1.4 + ml.color * (0.5 + 0.5 * sh) * 0.6);

                // the sun glint (only where the sun reaches the water)
                float3 hv = normalize(ml.direction + v);
                float glint = pow(saturate(dot(n, hv)), _GlintPower) * _GlintStrength * sh;
                col += ml.color * glint * (1.0 - foam);
                col = lerp(col, foamCol, foam * 0.9);

                float alpha = saturate(lerp(_ShallowAlpha, 1.0, saturate(murk * 1.4)) + fres * 0.3 + foam);
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
