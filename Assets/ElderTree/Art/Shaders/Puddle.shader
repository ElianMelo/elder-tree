// Still cave puddles for the Fairy grotto: a real mirror of the cave (PlanarReflection renders the scene from
// under the water plane into _PlanarReflectionTex), darkened and tinted, broken only by drip rings that start up
// at random spots and spread out. Shallow at the rim (depth fade against the floor) so the edges melt into the
// ground. PSX: no vertex snap (one flat sheet: snapping it would make it swim against the floor), 15-bit dither.
Shader "ElderTree/Puddle"
{
    Properties
    {
        _Tint("Reflection Tint", Color) = (0.78, 0.72, 0.95, 1)
        _DeepColor("Water Colour", Color) = (0.015, 0.01, 0.03, 1)
        _Reflect("Reflection Strength", Range(0, 1)) = 0.9
        _Distort("Ripple Distortion", Float) = 0.02
        _EdgeFade("Edge Fade (m)", Float) = 0.05
        _DripCell("Drip Spacing (m)", Float) = 1.4
        _DripSpeed("Drip Ring Speed", Float) = 0.55
        _RimColor("Ring Glint", Color) = (0.55, 0.5, 0.9, 1)
        [Toggle] _FlipY("Flip Reflection V", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Puddle"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_PlanarReflectionTex); SAMPLER(sampler_PlanarReflectionTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Tint, _DeepColor, _RimColor;
                float _Reflect, _Distort, _EdgeFade, _DripCell, _DripSpeed, _FlipY;
            CBUFFER_END
            float _PSXDither;

            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; float eye : TEXCOORD1; float fog : TEXCOORD2; };

            V Vert(A i)
            {
                V o;
                o.wp = TransformObjectToWorld(i.pos.xyz);
                float4 cs = TransformWorldToHClip(o.wp);
                o.pos = cs;
                o.eye = -TransformWorldToView(o.wp).z;
                o.fog = ComputeFogFactor(cs.z);
                return o;
            }

            float2 Hash22(float2 p)
            {
                float3 q = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                q += dot(q, q.yzx + 33.33);
                return frac((q.xx + q.yz) * q.zy);
            }

            static const float BAYER[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            float4 Frag(V i) : SV_Target
            {
                float t = _Time.y;
                float2 p = i.wp.xz;
                // drip rings: each cell drips now and then at its own spot; the ring spreads and fades
                float2 slope = 0;
                float glint = 0;
                float2 cell = floor(p / _DripCell);
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 id = cell + float2(x, y);
                    float2 h = Hash22(id);
                    float2 h2 = Hash22(id + 19.7);
                    float period = 2.5 + 6.0 * h2.x;
                    float ph = frac(t / period + h2.y);
                    float2 c = (id + 0.15 + 0.7 * h) * _DripCell;
                    float2 dv = p - c;
                    float d = length(dv);
                    // the ring spreads over the first part of the period, then the spot is still until the next drop
                    float life = saturate(ph * period * _DripSpeed / _DripCell);
                    float r = life * _DripCell * 0.85;
                    float k = (d - r) / 0.05;
                    float ring = exp(-k * k) * pow(1.0 - life, 2.0) * step(life, 0.999) * step(0.35, h2.x + h.y * 0.3);
                    slope += dv / max(d, 1e-3) * ring * sin(k * 2.2);
                    glint += ring;
                }
                // a slow shiver on the surface
                slope += float2(sin(p.x * 3.1 + t * 0.7) * cos(p.y * 2.3 - t * 0.5), cos(p.x * 2.7 - t * 0.6) * sin(p.y * 3.3 + t * 0.4)) * 0.04;
                float3 n = normalize(float3(slope.x, 1.0, slope.y));

                float2 suv = GetNormalizedScreenSpaceUV(i.pos);
                float2 ruv = float2(1.0 - suv.x, lerp(suv.y, 1.0 - suv.y, _FlipY)) + n.xz * _Distort;
                float3 refl = SAMPLE_TEXTURE2D(_PlanarReflectionTex, sampler_PlanarReflectionTex, ruv).rgb;

                float3 v = normalize(GetCameraPositionWS() - i.wp);
                float fres = pow(1.0 - saturate(dot(n, v)), 3.0);
                float3 col = lerp(_DeepColor.rgb, refl * _Tint.rgb, _Reflect * lerp(0.6, 1.0, fres));
                col += _RimColor.rgb * saturate(glint) * 0.25;

                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float depth = max(0.0, sceneEye - i.eye);
                float alpha = saturate(depth / _EdgeFade);

                col = MixFog(col, i.fog);
                uint2 px = uint2(i.pos.xy) & 3u;
                float b = (BAYER[px.y * 4 + px.x] + 0.5) / 16.0 - 0.5;
                if (_PSXDither > 0.0) col = max(0.0, floor(col * 31.0 + 0.5 + b * _PSXDither) / 31.0);
                return float4(col, alpha);
            }
            ENDHLSL
        }
    }
}
