using UnityEngine;

namespace ElderTree.World
{
    public enum SurfaceKind { Earth, Sand, Wood }

    /// <summary>
    /// Tags a walkable collider with what it is made of, so effects can tell dirt from sand from a dock over water
    /// (the Shark tears up through the planks in splinters and spray instead of dirt). Untagged ground is Earth.
    /// </summary>
    public class GroundSurface : MonoBehaviour
    {
        public SurfaceKind kind = SurfaceKind.Earth;

        static readonly RaycastHit[] hits = new RaycastHit[8];

        /// <summary>The surface under <paramref name="p"/> (the highest ground hit).</summary>
        public static SurfaceKind At(Vector3 p)
        {
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 3f, Vector3.down, hits, 8f, 1, QueryTriggerInteraction.Ignore);
            float best = float.MinValue;
            Collider top = null;
            for (int i = 0; i < n; i++)
                if (hits[i].point.y > best) { best = hits[i].point.y; top = hits[i].collider; }
            if (top == null) return SurfaceKind.Earth;
            var s = top.GetComponentInParent<GroundSurface>();
            return s != null ? s.kind : SurfaceKind.Earth;
        }
    }
}
