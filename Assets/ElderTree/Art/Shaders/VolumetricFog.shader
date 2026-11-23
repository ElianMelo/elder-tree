// Ray-marched volumetric fog (ElderTree.FX.VolumetricFogFeature + the VolumetricFog volume override).
// The look we're after is the soft, global "volumetric haze" of the modern Ocarina of Time remake: an
// exponential height fog that pools in the low ground, broken up by slow drifting noise, lit by the moon
// through the tree shadows (light shafts) and glowing around every torch, floodlight and searchlight.
// One full-screen pass at the PSX internal resolution: N jittered steps from the camera to the opaque depth,
// Henyey-Greenstein in-scattering of the main light (shadow mapped) and the FogLight array, then
// out = scene * transmittance + in-scatter   (Blend One SrcAlpha).
Shader "Hidden/ElderTree/VolumetricFog"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "VolumetricFog"
            ZTest Always ZWrite Off Cull Off
            Blend One SrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "FogCommon.hlsl"

            float _VFDensity;          // extinction per metre at the base height
            float _VFBaseHeight;       // fog is full density below this
            float _VFFalloff;          // metres for density to fall by e above the base
            float _VFMaxDistance;
            float _VFSteps;
            float _VFAnisotropy;
            float _VFNoiseScale;
            float _VFNoiseStrength;
            float4 _VFWind;            // xyz drift (m/s)
            float4 _VFAlbedo;          // scattering colour
            float4 _VFAmbient;         // sky / ambient light in the fog
            float _VFMainScatter;      // moonlight strength in the fog
            float _VFLocalScatter;     // local light strength in the fog
            float _VFGroundClear;      // how much the ground-fog field also clears this fog near the ground

            struct V { float4 pos : SV_POSITION; };

            V Vert(uint id : SV_VertexID)
            {
                V o;
                o.pos = GetFullScreenTriangleVertexPosition(id);
                return o;
            }

            float Density(float3 p, float t)
            {
                float h = p.y - _VFBaseHeight;
                float d = _VFDensity * exp(-max(h, 0.0) / _VFFalloff);
                float n = FogFbm(p * _VFNoiseScale + _VFWind.xyz * t);
                d *= lerp(1.0, saturate(n * 2.2 - 0.45) * 1.8, _VFNoiseStrength);
                d *= 1.0 - GFCleared(p) * _VFGroundClear * saturate(1.0 - h / 3.0);
                return d;
            }

            float4 Frag(V i) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(i.pos);
                float raw = SampleSceneDepth(uv);
                float3 wp = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float3 cam = GetCameraPositionWS();
                float3 ray = wp - cam;
                float dist = length(ray);
                float3 dir = ray / max(dist, 1e-4);
            #if UNITY_REVERSED_Z
                bool sky = raw <= 1e-6;
            #else
                bool sky = raw >= 1.0 - 1e-6;
            #endif
                dist = sky ? _VFMaxDistance : min(dist, _VFMaxDistance);

                int steps = (int)_VFSteps;
                float stepLen = dist / steps;
                float jitter = FogIGN(i.pos.xy + (_Time.y * 60.0 % 64.0) * 5.588238);
                Light ml = GetMainLight();
                float mainPhase = FogPhase(dot(dir, ml.direction), _VFAnisotropy) * 12.566371;
                float t = _Time.y;

                float T = 1.0;
                float3 L = 0;
                [loop] for (int s = 0; s < steps; s++)
                {
                    float d = (s + jitter) * stepLen;
                    float3 p = cam + dir * d;
                    float dens = Density(p, t);
                    if (dens < 1e-5) continue;
                    float sh = MainLightRealtimeShadow(TransformWorldToShadowCoord(p));
                    float3 light = _VFAmbient.rgb
                                 + ml.color * (_VFMainScatter * sh * mainPhase)
                                 + FogLocalLights(p, dir, _VFAnisotropy) * _VFLocalScatter;
                    float ext = dens * stepLen;
                    float Ts = exp(-ext);
                    L += T * light * _VFAlbedo.rgb * (1.0 - Ts);
                    T *= Ts;
                    if (T < 0.02) break;
                }
                return float4(L, T);
            }
            ENDHLSL
        }
    }
}
