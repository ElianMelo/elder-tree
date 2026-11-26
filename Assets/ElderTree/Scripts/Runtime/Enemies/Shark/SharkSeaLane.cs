using UnityEngine;

namespace ElderTree.Enemies
{
    /// <summary>
    /// The Shark's run in from the open sea for its first-meeting cinematic (SharkBoss.Intro.cs) on the Bleached Atoll. Nothing
    /// here is visible: it's the route. The fin rises at <see cref="rise"/>, circles along the coast, breaches at
    /// <see cref="breach1"/> and <see cref="breach2"/> (each marker is where it leaves the water, +Z the way it leaps), then charges
    /// the dock and bursts out of the water at <see cref="launch"/>, over the rail, onto the planks at <see cref="land"/>.
    /// Built by the beach builder (Elder Tree/9f, or 7b2 to patch the scene); move the markers to re-plan it.
    /// </summary>
    public class SharkSeaLane : MonoBehaviour
    {
        [Tooltip("Where the fin first breaks the surface, +Z the way it swims off.")]
        public Transform rise;
        [Tooltip("The first breach: where it leaves the water, +Z the way it leaps.")]
        public Transform breach1;
        [Tooltip("The second, bigger breach (a spinning leap).")]
        public Transform breach2;
        [Tooltip("Where it bursts out of the water to leap onto the dock.")]
        public Transform launch;
        [Tooltip("Where it comes down on the planks, +Z its heading as it lands.")]
        public Transform land;

        [Header("Leaps")]
        public float breach1Length = 11f;
        [Tooltip("Height of its body's centre over the water at the top of the leap (m).")]
        public float breach1Apex = 3.4f;
        public float breach2Length = 15f;
        public float breach2Apex = 5.2f;
        [Tooltip("Turns round its long axis in the second breach (a spinner).")]
        public float breach2Spins = 2f;
        [Tooltip("Height over the deck at the top of the leap onto the dock (m).")]
        public float launchApex = 4.2f;

        [Header("Swimming")]
        [Tooltip("Speed while it circles, then as it charges (m/s).")]
        public float cruiseSpeed = 8f;
        public float chargeSpeed = 14f;
        [Tooltip("How much of its dorsal fin shows above the water (m).")]
        public float finShow = 0.85f;

        public static SharkSeaLane Find() => FindAnyObjectByType<SharkSeaLane>();

        public Vector3 Breach1Lands => breach1 ? breach1.position + Flat(breach1.forward) * breach1Length : Vector3.zero;
        public Vector3 Breach2Lands => breach2 ? breach2.position + Flat(breach2.forward) * breach2Length : Vector3.zero;

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.normalized; }

        /// <summary>The swim between two points: a Hermite curve leaving along <paramref name="d0"/>, arriving along <paramref name="d1"/>.</summary>
        public static Vector3 Swim(Vector3 a, Vector3 d0, Vector3 b, Vector3 d1, float s, out Vector3 dir)
        {
            float len = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z)) * 1.15f;
            Vector3 m0 = Flat(d0) * len, m1 = Flat(d1) * len;
            float s2 = s * s, s3 = s2 * s;
            dir = (6f * s2 - 6f * s) * a + (3f * s2 - 4f * s + 1f) * m0 + (-6f * s2 + 6f * s) * b + (3f * s2 - 2f * s) * m1;
            dir.y = 0f;
            return (2f * s3 - 3f * s2 + 1f) * a + (s3 - 2f * s2 + s) * m0 + (-2f * s3 + 3f * s2) * b + (s3 - s2) * m1;
        }

        void OnDrawGizmos()
        {
            if (!rise || !breach1 || !breach2 || !launch || !land) return;
            Gizmos.color = new Color(0.4f, 0.8f, 1f);
            void Curve(Vector3 a, Vector3 d0, Vector3 b, Vector3 d1)
            {
                Vector3 prev = a;
                for (int i = 1; i <= 24; i++)
                {
                    Vector3 p = Swim(a, d0, b, d1, i / 24f, out _);
                    p.y = a.y;
                    Gizmos.DrawLine(prev, p);
                    prev = p;
                }
            }
            void Leap(Vector3 a, Vector3 b, float h)
            {
                Gizmos.color = new Color(1f, 0.7f, 0.3f);
                Vector3 prev = a;
                for (int i = 1; i <= 16; i++)
                {
                    float s = i / 16f;
                    Vector3 p = Vector3.Lerp(a, b, s) + Vector3.up * (4f * h * s * (1f - s));
                    Gizmos.DrawLine(prev, p);
                    prev = p;
                }
                Gizmos.color = new Color(0.4f, 0.8f, 1f);
            }
            Vector3 b1 = Breach1Lands, b2 = Breach2Lands;
            b1.y = breach1.position.y; b2.y = breach2.position.y;
            Curve(rise.position, rise.forward, breach1.position, breach1.forward);
            Leap(breach1.position, b1, breach1Apex);
            Curve(b1, breach1.forward, breach2.position, breach2.forward);
            Leap(breach2.position, b2, breach2Apex);
            Curve(b2, breach2.forward, launch.position, land.position - launch.position);
            Leap(launch.position, land.position, launchApex);
            Gizmos.DrawWireSphere(rise.position, 0.8f);
            Gizmos.matrix = Matrix4x4.TRS(land.position + Vector3.up * 1.25f, land.rotation, Vector3.one);
            Gizmos.DrawWireCube(new Vector3(0f, 0f, -0.45f), new Vector3(1.8f, 1.8f, 8.4f));
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
