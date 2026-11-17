using UnityEngine;

namespace TungSahur.Equipment
{
    /// <summary>
    /// A transformation mask (Majora's Mask style): putting it on turns the player into its form.
    /// The first time it is obtained a transformation cinematic plays.
    /// </summary>
    [CreateAssetMenu(menuName = "Tung Sahur/Equipment/Mask", fileName = "Mask_")]
    public class MaskData : ScriptableObject
    {
        [Tooltip("Stable id used for saves (PlayerPrefs).")]
        public string id = "alligator";
        public string displayName = "Alligator Mask";
        [TextArea] public string description = "";
        public Texture2D icon;
        public FormData form;
        [Tooltip("Mask model shown on the pickup, in the hand and on the face during the cinematic.")]
        public GameObject maskModel;

        [Header("Transformation")]
        [Tooltip("Quick swap when the mask is put on / taken off after the first time.")]
        public float quickTransformTime = 0.6f;
        [Tooltip("Colours of the psychedelic backdrop in the first-time cinematic.")]
        public Color vortexA = new Color(0.72f, 0.75f, 0.23f);
        public Color vortexB = new Color(0.42f, 0.05f, 0.06f);
        public Color vortexC = new Color(0.08f, 0.02f, 0.12f);
        [Tooltip("Banner text when the mask is received.")]
        public string receivedTitle = "ALLIGATOR MASK";
        [TextArea] public string receivedLine = "You put on the Alligator Mask.\nThe Bombardiro's wrath is now yours.";
    }
}
