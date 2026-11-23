// Shared by VolumetricFog.shader, GroundFog.shader and SwampWater.shader.
// Globals come from ElderTree.FX.FogLight (local lights) and ElderTree.FX.GroundFog (the fluid field).
#ifndef ELDERTREE_FOG_COMMON
#define ELDERTREE_FOG_COMMON

// ---------------------------------------------------------------- local lights (torches, floodlights, searchlights)
#define FOG_MAX_LIGHTS 16
float  _FogLightCount;
float4 _FogLightPos[FOG_MAX_LIGHTS];   // xyz position, w = 1 / range^2
float4 _FogLightCol[FOG_MAX_LIGHTS];   // rgb = colour * intensity * scatter, w = 1 spot / 0 point
float4 _FogLightDir[FOG_MAX_LIGHTS];   // xyz spot forward, w = cos(outer half angle)
float4 _FogLightSpot[FOG_MAX_LIGHTS];  // x = 1 / (cos inner - cos outer)

// ---------------------------------------------------------------- ground fog field (OoT-remake style disturbance)
TEXTURE2D(_GFFieldTex);  SAMPLER(sampler_GFFieldTex);    // r = cleared (0 full fog .. 1 pushed away), gb = flow velocity (m/s)
TEXTURE2D(_GFHeightTex); SAMPLER(sampler_GFHeightTex);   // r = ground height (m) under each texel
float4 _GFRect;          // xy = world min x/z, z = 1 / size, w = 1 when the field is live

float2 GFUV(float3 p) { return (p.xz - _GFRect.xy) * _GFRect.z; }

float GFCleared(float3 p)
{
    if (_GFRect.w < 0.5) return 0;
    float2 uv = GFUV(p);
    if (any(uv < 0) || any(uv > 1)) return 0;
    return SAMPLE_TEXTURE2D_LOD(_GFFieldTex, sampler_GFFieldTex, uv, 0).r;
}

// ---------------------------------------------------------------- noise
float FogHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float FogNoise(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(FogHash(i + float3(0, 0, 0)), FogHash(i + float3(1, 0, 0)), f.x),
                     lerp(FogHash(i + float3(0, 1, 0)), FogHash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(FogHash(i + float3(0, 0, 1)), FogHash(i + float3(1, 0, 1)), f.x),
                     lerp(FogHash(i + float3(0, 1, 1)), FogHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

float FogFbm(float3 p)
{
    float a = 0.5, s = 0;
    [unroll] for (int k = 0; k < 3; k++) { s += a * FogNoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
    return s / 0.875;
}

// Henyey-Greenstein: g > 0 scatters forward (bright halo looking toward a light)
float FogPhase(float cosTheta, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / (12.566371 * pow(max(1e-4, 1.0 + g2 - 2.0 * g * cosTheta), 1.5));
}

// In-scattered radiance from the registered local lights at p, looking along viewDir (camera -> p).
float3 FogLocalLights(float3 p, float3 viewDir, float g)
{
    float3 sum = 0;
    int n = (int)_FogLightCount;
    for (int i = 0; i < FOG_MAX_LIGHTS; i++)
    {
        if (i >= n) break;
        float3 d = _FogLightPos[i].xyz - p;
        float d2 = max(dot(d, d), 0.04);
        float f = d2 * _FogLightPos[i].w;
        float win = saturate(1.0 - f * f);
        float atten = win * win / d2;
        float3 l = d * rsqrt(d2);
        if (_FogLightCol[i].w > 0.5)
        {
            float cd = dot(-l, _FogLightDir[i].xyz);
            float s = saturate((cd - _FogLightDir[i].w) * _FogLightSpot[i].x);
            atten *= s * s;
        }
        sum += _FogLightCol[i].rgb * atten * FogPhase(dot(viewDir, -l), g) * 12.566371;
    }
    return sum;
}

// Interleaved gradient noise (Jimenez 2014): per-pixel ray jitter, reads as PSX-ish dither once quantised.
float FogIGN(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

#endif
