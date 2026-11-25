using UnityEngine;

namespace ElderTree.Enemies
{
    /// <summary>
    /// The Crocodile's airstrip in the Drowned Garrison, for its first-meeting cinematic (CrocodileBoss.Intro.cs): where it
    /// sleeps, where it lines up to take off, how long its takeoff run is, and the climbing turn it flies over the garrison
    /// before it dives into the fight. Nothing here is visible: it's the flight plan. Built by the swamp builder
    /// (Elder Tree/9b, or 7a to patch the scene); move the two markers in the scene to re-plan it.
    /// </summary>
    public class CrocodileAirstrip : MonoBehaviour
    {
        [Tooltip("Where it sleeps, nose along +Z.")]
        public Transform park;
        [Tooltip("The start of the takeoff run, +Z down the runway. It taxis here from the park.")]
        public Transform lineUp;
        [Tooltip("Ground run before the wheels leave the ground (m).")]
        public float rollDistance = 9f;
        [Tooltip("Speed at lift-off (m/s).")]
        public float liftOffSpeed = 14f;
        [Tooltip("Radius of its first turn after lift-off (m). Over a lap the turn widens and its centre drifts onto the arena's, " +
                 "so the climbing spiral ends on the fight's orbit.")]
        public float firstTurnRadius = 8.5f;
        [Tooltip("Height it climbs to before it dives back in (m).")]
        public float climbAltitude = 22f;
        [Tooltip("Height it roars over you at, at the end of the dive (m).")]
        public float passAltitude = 8f;
        [Tooltip("Turn right after take-off (else left).")]
        public bool turnRight = true;

        public static CrocodileAirstrip Find() => FindAnyObjectByType<CrocodileAirstrip>();

        void OnDrawGizmos()
        {
            if (park)
            {
                Gizmos.color = new Color(1f, 0.8f, 0.3f);
                Gizmos.matrix = Matrix4x4.TRS(park.position + Vector3.up * 1.9f, park.rotation, Vector3.one);
                Gizmos.DrawWireCube(new Vector3(0f, 0f, 0.45f), new Vector3(11.6f, 3.9f, 11.1f));
                Gizmos.matrix = Matrix4x4.identity;
            }
            if (lineUp)
            {
                Gizmos.color = new Color(0.4f, 1f, 0.5f);
                Vector3 a = lineUp.position + Vector3.up * 0.1f, f = lineUp.forward;
                Gizmos.DrawLine(a, a + f * rollDistance);
                Gizmos.DrawLine(a + lineUp.right * 5.8f, a - lineUp.right * 5.8f);
                if (park) Gizmos.DrawLine(park.position + Vector3.up * 0.1f, a);
            }
            if (lineUp)
            {
                // the first turn after lift-off
                Gizmos.color = new Color(0.5f, 0.7f, 1f, 0.6f);
                Vector3 lo = lineUp.position + lineUp.forward * rollDistance + Vector3.up * 2f;
                Vector3 c = lo + lineUp.right * (turnRight ? firstTurnRadius : -firstTurnRadius), prev = lo;
                for (int i = 1; i <= 32; i++)
                {
                    float t = i / 32f * Mathf.PI * 2f * (turnRight ? 1f : -1f);
                    Vector3 p = c + Quaternion.Euler(0f, t * Mathf.Rad2Deg, 0f) * (lo - c);
                    Gizmos.DrawLine(prev, p);
                    prev = p;
                }
            }
        }
    }
}
