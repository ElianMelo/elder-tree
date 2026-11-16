// Procedural night sky for the Tung Sahur arena: gradient, twinkling star field, moon with halo,
// and a faint drifting cloud band near the horizon. Works as a skybox material under URP.
Shader "TungSahur/NightSky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.043, 0.071, 0.125, 1)
        _HorizonColor ("Horizon", Color) = (0.17, 0.23, 0.33, 1)
        _GroundColor ("Below Horizon", Color) = (0.05, 0.06, 0.08, 1)
        _HorizonSharpness ("Horizon Sharpness", Range(0.5, 8)) = 2.6
        _StarDensity ("Star Density", Range(50, 600)) = 260
        _StarBrightness ("Star Brightness", Range(0, 4)) = 1.6
        _MoonDir ("Moon Direction", Vector) = (-0.45, 0.42, 0.78, 0)
        _MoonColor ("Moon Color", Color) = (0.86, 0.9, 1, 1)
        _MoonSize ("Moon Size", Range(0.005, 0.08)) = 0.032
        _HaloStrength ("Halo", Range(0, 2)) = 0.55
        _CloudColor ("Cloud Color", Color) = (0.22, 0.27, 0.38, 1)
        _CloudStrength ("Clouds", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _ZenithColor, _HorizonColor, _GroundColor, _MoonColor, _CloudColor;
            float4 _MoonDir;
            float _HorizonSharpness, _StarDensity, _StarBrightness, _MoonSize, _HaloStrength, _CloudStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float noise3(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                float n000 = hash31(i), n100 = hash31(i + float3(1,0,0)), n010 = hash31(i + float3(0,1,0)), n110 = hash31(i + float3(1,1,0));
                float n001 = hash31(i + float3(0,0,1)), n101 = hash31(i + float3(1,0,1)), n011 = hash31(i + float3(0,1,1)), n111 = hash31(i + float3(1,1,1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y), lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            float stars(float3 d)
            {
                float3 p = d * _StarDensity;
                float3 cell = floor(p);
                float3 f = frac(p) - 0.5;
                float h = hash31(cell);
                float present = step(0.965, h);
                float3 offs = float3(hash31(cell + 11.1), hash31(cell + 23.7), hash31(cell + 37.3)) - 0.5;
                float dist = length(f - offs * 0.6);
                float size = lerp(0.04, 0.12, hash31(cell + 5.5));
                float s = smoothstep(size, 0.0, dist) * present;
                float twinkle = 0.65 + 0.35 * sin(_Time.y * (1.5 + h * 4.0) + h * 40.0);
                return s * twinkle * lerp(0.4, 1.0, frac(h * 91.7));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = d.y;
                float t = saturate(pow(saturate(up), 1.0 / _HorizonSharpness));
                float3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, t);
                col = lerp(col, _GroundColor.rgb, saturate(-up * 6.0));

                // cloud band hugging the horizon
                float band = exp(-pow((up - 0.12) / 0.12, 2));
                float c = noise3(d * float3(3, 9, 3) + float3(_Time.y * 0.01, 0, 0)) * 0.65 + noise3(d * 11 + 7) * 0.35;
                float clouds = smoothstep(0.45, 0.8, c) * band * _CloudStrength;

                // stars fade toward the horizon and behind clouds
                float st = stars(d) * smoothstep(0.02, 0.35, up) * (1 - clouds * 1.5);
                col += st * _StarBrightness;

                // moon + halo
                float3 md = normalize(_MoonDir.xyz);
                float mdot = dot(d, md);
                float ang = acos(clamp(mdot, -1, 1));
                float disc = smoothstep(_MoonSize, _MoonSize * 0.85, ang);
                float craters = 0.85 + 0.15 * noise3(d * 180);
                float halo = exp(-ang * 9.0) * _HaloStrength + exp(-ang * 40.0) * _HaloStrength * 0.6;
                col += _MoonColor.rgb * halo * 0.35;
                col = lerp(col, _MoonColor.rgb * 2.2 * craters, disc);

                // clouds lit by the moon
                float lit = 0.5 + 0.5 * saturate(mdot);
                col = lerp(col, _CloudColor.rgb * lit, clouds * (1 - disc));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
