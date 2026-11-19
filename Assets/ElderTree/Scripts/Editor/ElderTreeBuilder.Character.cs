using UnityEngine;

namespace ElderTree.EditorTools
{
    public static partial class ElderTreeBuilder
    {
        // Club grip relative to the RightHand bone. The club mesh runs along its local +Y
        // (Blender +Z) with the origin at the grip point. Tuned by looking at contact sheets.
        public static Vector3 ClubGripPosition = new Vector3(0.005f, 0.068f, -0.008f);
        public static Vector3 ClubGripEuler = new Vector3(0f, 0f, -90f);

        public static void ApplyClubGrip(Transform club)
        {
            club.localPosition = ClubGripPosition;
            club.localRotation = Quaternion.Euler(ClubGripEuler);
            club.localScale = Vector3.one;
        }
    }
}
