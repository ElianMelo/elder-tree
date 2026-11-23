using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace ElderTree.FX
{
    /// <summary>
    /// Volume override for the ray-marched volumetric fog (Hidden/ElderTree/VolumetricFog). Only scenes whose
    /// post-process profile carries it get the pass (the swamp arena); everywhere else the feature is idle.
    /// Exponential height fog + drifting noise, lit by the main light through its shadow map (light shafts)
    /// and by every <see cref="FogLight"/> (torch halos, searchlight beams).
    /// </summary>
    [Serializable, VolumeComponentMenu("Elder Tree/Volumetric Fog")]
    public sealed class VolumetricFog : VolumeComponent, IPostProcessComponent
    {
        public BoolParameter enabled = new(false);
        [Tooltip("Extinction per metre at and below the base height.")]
        public ClampedFloatParameter density = new(0.06f, 0f, 0.5f);
        public FloatParameter baseHeight = new(0.4f);
        [Tooltip("Metres above the base for the density to fall by e.")]
        public MinFloatParameter heightFalloff = new(3.5f, 0.1f);
        public MinFloatParameter maxDistance = new(70f, 5f);
        public ClampedIntParameter steps = new(28, 8, 64);
        [Tooltip("Henyey-Greenstein g: > 0 makes the fog glow when looking toward a light.")]
        public ClampedFloatParameter anisotropy = new(0.55f, -0.9f, 0.95f);
        public MinFloatParameter noiseScale = new(0.09f, 0f);
        public ClampedFloatParameter noiseStrength = new(0.7f, 0f, 1f);
        public Vector3Parameter wind = new(new Vector3(0.35f, 0.04f, 0.15f));
        public ColorParameter albedo = new(new Color(0.75f, 0.9f, 0.75f), false, false, true);
        public ColorParameter ambient = new(new Color(0.05f, 0.09f, 0.06f), true, false, true);
        public MinFloatParameter mainLightScatter = new(0.9f, 0f);
        public MinFloatParameter localLightScatter = new(0.6f, 0f);
        [Tooltip("How much bodies pushing the ground fog (GroundFog field) also part this fog near the ground.")]
        public ClampedFloatParameter groundClear = new(0.6f, 0f, 1f);

        public bool IsActive() => enabled.value && density.value > 0f;
    }
}
