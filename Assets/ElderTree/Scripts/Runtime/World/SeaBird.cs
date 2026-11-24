using UnityEngine;

namespace ElderTree.World
{
    /// <summary>
    /// A gull circling over something dead: wide, wandering loops around <see cref="center"/>, banked into the turn,
    /// a few beats of the wings and then a long glide with the wings held in a shallow V. The model
    /// (Tools/Blender/build_beach.py SeaBird) has wings "WingL"/"WingR" pivoting at the shoulder and an empty "Beak".
    /// </summary>
    public class SeaBird : MonoBehaviour
    {
        public Vector3 center;
        public float radius = 14f;
        public float height = 16f;
        public float speed = 6.5f;
        [Tooltip("+1 counter-clockwise, -1 clockwise (seen from above).")]
        public float direction = 1f;
        public float flapRate = 2.6f;

        Transform wingL, wingR;
        Quaternion wingLRest, wingRRest, modelFix = Quaternion.identity;
        Vector3 frontLocal = Vector3.forward;
        float angle, flap, glideT, flapT, seed;

        void Start()
        {
            seed = Random.value * 100f;
            angle = Random.value * Mathf.PI * 2f;
            foreach (var t in GetComponentsInChildren<Transform>())
            {
                if (t.name == "WingL") { wingL = t; wingLRest = t.localRotation; }
                else if (t.name == "WingR") { wingR = t; wingRRest = t.localRotation; }
                else if (t.name == "Beak") frontLocal = transform.InverseTransformPoint(t.position).normalized;
            }
            frontLocal = Vector3.ProjectOnPlane(frontLocal, Vector3.up).normalized;
            if (frontLocal.sqrMagnitude < 0.5f) frontLocal = Vector3.forward;
            modelFix = Vector3.Dot(frontLocal, Vector3.forward) < -0.9f ? Quaternion.Euler(0, 180f, 0) : Quaternion.FromToRotation(frontLocal, Vector3.forward);
            glideT = Random.Range(0f, 3f);
        }

        void Update()
        {
            float dt = Time.deltaTime, t = Time.time + seed;
            // the loop breathes in and out so the flock never looks like a carousel
            float r = radius * (1f + 0.25f * Mathf.Sin(t * 0.13f));
            angle += direction * speed / r * dt;
            Vector3 pos = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * r
                        + new Vector3(Mathf.Sin(t * 0.21f) * 2.5f, height + Mathf.Sin(t * 0.37f) * 1.6f, Mathf.Cos(t * 0.17f) * 2.5f);
            Vector3 vel = (pos - transform.position) / Mathf.Max(dt, 1e-4f);
            transform.position = pos;
            if (vel.sqrMagnitude > 0.01f)
            {
                var look = Quaternion.LookRotation(vel.normalized, Vector3.up);
                float bank = -direction * 24f + Mathf.Sin(t * 0.5f) * 6f;
                transform.rotation = look * Quaternion.Euler(0, 0, bank) * modelFix;
            }

            // a few beats, then a long glide (wings in a shallow V, rocking a little)
            if (flapT > 0f) flapT -= dt;
            else if ((glideT -= dt) <= 0f) { flapT = Random.Range(0.8f, 2f); glideT = Random.Range(2.5f, 6f); }
            float target = flapT > 0f ? 1f : 0f;
            flap = Mathf.MoveTowards(flap, target, dt * 3f);
            float beat = Mathf.Sin(t * flapRate * Mathf.PI * 2f) * 42f * flap;
            float hold = 9f * (1f - flap) + Mathf.Sin(t * 1.3f) * 3f * (1f - flap);
            float a = beat + hold;
            // rotate each wing about the body's long axis, in the root's space
            Vector3 axis = frontLocal;
            if (wingL) wingL.localRotation = Quaternion.AngleAxis(-a, axis) * wingLRest;
            if (wingR) wingR.localRotation = Quaternion.AngleAxis(a, axis) * wingRRest;
        }
    }
}
