// The Fairy grotto's ceiling of stars: an additive layer drawn over the cave rock (the same mesh, the same PSX
// vertex snap so it sits exactly on it). Points of light are hashed on a 3D world grid, so they stay the same
// size everywhere on the dome whatever its UVs do. Two layers (a few big glow-worm stars, many faint pinpricks),
// each star twinkling on its own clock, gathered in a band like a milky way; a faint violet nebula drifts behind.
// Fades out down the walls so the stars live overhead. Fog dims it toward black (additive).
Shader "ElderTree/CaveStars"
{
    Properties
    {
        _Cell("Big Star Cell (m)", Float) = 1.1
        _Size("Big Star Radius (m)", Float) = 0.11
        _Keep("Big Star Fraction", Range(0, 1)) = 0.55
        _SmallCell("Small Star Cell (m)", Float) = 0.42
        _SmallSize("Small Star Radius (m)", Float) = 0.05
        _SmallKeep("Small Star Fraction", Range(0, 1)) = 0.5
        _Brightness("Brightness", Float) = 3.5
        _ColorA("Star Blue", Color) = (0.55, 0.75, 1, 1)
        _ColorB("Star Violet", Color) = (0.8, 0.55, 1, 1)
        _ColorC("Star Cyan", Color) = (0.55, 1, 0.95, 1)
        _NebulaColor("Nebula", Color) = (0.22, 0.08, 0.42, 1)
        _NebulaStrength("Nebula Strength", Float) = 0.35
        _BandDir("Band Direction (xyz)", Vector) = (0.6, 0.15, 0.78, 0)
        _BandWidth("Band Width (m)", Float) = 9
        _FadeMin("Fade Start Height", Float) = 4
        _FadeMax("Fade Full Height", Float) = 11
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-200" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Stars"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorA, _ColorB, _ColorC, _NebulaColor, _BandDir;
                float _Cell, _Size, _Keep, _SmallCell, _SmallSize, _SmallKeep, _Brightness, _NebulaStrength, _BandWidth, _FadeMin, _FadeMax;
            CBUFFER_END
            float4 _PSXSnap;

            float4 PSXSnapCS(float4 cs)
            {
                if (_PSXSnap.x <= 0.0 || cs.w <= 1e-4) return cs;
                float2 g = _PSXSnap.xy * 0.5;
                float2 ndc = cs.xy / cs.w;
                ndc = round(ndc * g) / g;
                cs.xy = ndc * cs.w;
                return cs;
            }

            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; float fog : TEXCOORD1; };

            V Vert(A i)
            {
                V o;
                o.wp = TransformObjectToWorld(i.pos.xyz);
                float4 cs = TransformWorldToHClip(o.wp);
                o.pos = PSXSnapCS(cs);
                o.fog = ComputeFogFactor(cs.z);
                return o;
            }

            float3 Hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            float VNoise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash33(i).x, n100 = Hash33(i + float3(1, 0, 0)).x, n010 = Hash33(i + float3(0, 1, 0)).x, n110 = Hash33(i + float3(1, 1, 0)).x;
                float n001 = Hash33(i + float3(0, 0, 1)).x, n101 = Hash33(i + float3(1, 0, 1)).x, n011 = Hash33(i + float3(0, 1, 1)).x, n111 = Hash33(i + float3(1, 1, 1)).x;
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y), lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            // one star per cell, kept inside the cell so only this cell needs checking
            float3 StarLayer(float3 p, float cell, float size, float keep, float t, float seed)
            {
                float3 c = floor(p / cell);
                float3 h = Hash33(c + seed);
                if (h.x > keep) return 0;
                float3 h2 = Hash33(c * 1.7 + seed + 11.0);
                float3 sp = (c + 0.25 + 0.5 * h2) * cell;
                float d = length(p - sp);
                float big = pow(h.y, 3.0);
                float r = size * (0.55 + 1.6 * big);
                float core = saturate(1.0 - d / r);
                core = core * core * (3.0 - 2.0 * core);
                float tw = 0.5 + 0.5 * sin(t * (0.8 + 3.2 * h2.x) + h2.y * 50.0);
                tw = lerp(1.0, tw, 0.75);
                float3 col = lerp(_ColorA.rgb, _ColorB.rgb, h.z);
                col = lerp(col, _ColorC.rgb, step(0.86, h2.z));
                return col * core * tw * (0.5 + 2.5 * big);
            }

            float4 Frag(V i) : SV_Target
            {
                float3 p = i.wp;
                float t = _Time.y;
                float fade = saturate((p.y - _FadeMin) / max(0.01, _FadeMax - _FadeMin));
                if (fade <= 0.0) return 0;
                // the band: denser, brighter stars along a great circle through the dome
                float3 bd = normalize(_BandDir.xyz);
                float band = exp(-pow(dot(p, bd) / _BandWidth, 2.0));
                float clump = VNoise(p * 0.11 + 3.1);
                float3 col = StarLayer(p, _Cell, _Size, _Keep * (0.55 + 0.6 * band), t, 0.0);
                col += StarLayer(p, _SmallCell, _SmallSize, _SmallKeep * (0.25 + 0.9 * band) * (0.6 + 0.8 * clump), t * 1.3, 17.0) * 0.6;
                col *= _Brightness;
                float neb = VNoise(p * 0.045 + float3(0, t * 0.004, 0)) * 0.65 + VNoise(p * 0.13 - float3(t * 0.006, 0, 0)) * 0.35;
                col += _NebulaColor.rgb * _NebulaStrength * smoothstep(0.42, 0.85, neb) * (0.35 + band);
                col *= fade;
                col = MixFogColor(col, half3(0, 0, 0), i.fog);
                return float4(col, 1);
            }
            ENDHLSL
        }
    }
}
