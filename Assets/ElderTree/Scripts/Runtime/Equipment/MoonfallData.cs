using UnityEngine;

namespace ElderTree.Equipment
{
    /// <summary>
    /// The fairy form's special (light + heavy): she rises off the ground on her wings, raises a hand to the sky, and a
    /// huge moon falls out of the night onto the target (lock-on, else ahead of her). A ring of light marks where it
    /// will land; the impact hits everything in a wide radius. One special charge.
    /// </summary>
    [CreateAssetMenu(menuName = "Elder Tree/Equipment/Moonfall", fileName = "Special_")]
    public class MoonfallData : SpecialData
    {
        [Header("Rise")]
        public float riseHeight = 0.8f;
        public float riseTime = 0.35f;
        [Tooltip("Hand raised to the sky before the moon is called down.")]
        public float callTime = 0.35f;

        [Header("The moon")]
        [Tooltip("Radius of the falling moon (metres).")]
        public float moonRadius = 3.6f;
        [Tooltip("It starts this high above the target...")]
        public float dropHeight = 24f;
        [Tooltip("...and this far beyond it (so it comes in on a slant, in front of the camera).")]
        public float dropDistance = 20f;
        [Tooltip("Seconds from the call to the impact.")]
        public float fallTime = 1.7f;

        [Header("Impact")]
        public float impactRadius = 7.5f;
        public float impactDamage = 210f;
        public float impactPoise = 120f;
        [Tooltip("With no lock-on, the target is this far ahead.")]
        public float aheadDistance = 8f;
        public float maxDistance = 16f;

        [Header("Camera")]
        public float cameraZoomOut = 6f;
        public float cameraPitch = 8f;

        [Header("Recover")]
        public float hangAfter = 0.45f;
        public float landRecovery = 0.25f;
    }
}
