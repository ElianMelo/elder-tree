using UnityEngine;

namespace ElderTree.Equipment
{
    /// <summary>
    /// "Wrath of the Grove" (Druid Mask special, light + heavy): the druid roars and swells to a giant, then stomps
    /// the ground, one foot after the other. Each stomp sends a <see cref="Combat.VineRing"/> out from the foot: vines
    /// rise out of the dirt in a circle that spreads outward, striking everything the ring passes. Then the druid
    /// shrinks back. Hyper armour throughout.
    /// </summary>
    [CreateAssetMenu(menuName = "Elder Tree/Equipment/Special (Growth Stomp)", fileName = "Special_")]
    public class GrowthStompData : SpecialData
    {
        void Reset() { displayName = "Wrath of the Grove"; staminaCost = 25f; }

        [Header("Growth")]
        [Tooltip("How big the druid gets (x its normal size).")]
        public float growScale = 2.2f;
        [Tooltip("Seconds to swell up (roaring), with a little overshoot.")]
        public float growTime = 0.65f;
        public float shrinkTime = 0.45f;
        [Tooltip("Seconds held at full size after the last stomp, before shrinking.")]
        public float holdAfter = 0.35f;

        [Header("Stomps")]
        [Tooltip("Stomps in a row, alternating feet. Each one sends its own ring of vines.")]
        public int stomps = 2;
        [Tooltip("Seconds the foot rises (knee up) before it comes down.")]
        public float raiseTime = 0.38f;
        [Tooltip("Seconds after a slam before the next foot rises.")]
        public float stompRecover = 0.3f;

        [Header("Vine ring")]
        [Tooltip("How far the ring spreads (metres) and how fast (m/s).")]
        public float ringRadius = 7.5f;
        public float ringSpeed = 6f;
        [Tooltip("Height of the vines rising out of the ground (metres).")]
        public Vector2 vineHeight = new Vector2(1.3f, 2.3f);
        [Tooltip("Seconds each vine stays up before it sinks back into the dirt. Short: only a band near the front stands, so the circle reads as it rolls out.")]
        public float vineLife = 0.24f;
        public float damage = 72f;
        public float poise = 34f;
        [Tooltip("The last stomp's ring hits this much harder.")]
        public float lastStompMultiplier = 1.35f;

        [Header("Feel")]
        public float shake = 0.9f;
        [Tooltip("Extra camera distance while the druid is big (the camera pulls back to frame it and the rings).")]
        public float cameraZoomOut = 3.6f;
        public float cameraPitch = 20f;
    }
}
