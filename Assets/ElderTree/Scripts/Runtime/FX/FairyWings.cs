using ElderTree.Player;
using UnityEngine;

namespace ElderTree.FX
{
    /// <summary>
    /// The fairy form's four wings: they flutter round their roots on the back like the Fairy boss's. A slow beat at
    /// rest, faster when she runs, fast and wide in the air or while a special lifts her, folded back while she rolls,
    /// and swept back hard when she's hit. Driven in LateUpdate on top of the animated chest.
    /// </summary>
    public class FairyWings : MonoBehaviour
    {
        [Tooltip("Upper and lower wings, any order (side is read from where each sits on the body).")]
        public Transform[] wings = System.Array.Empty<Transform>();
        [Tooltip("Body frame: up / forward (the form's root, not a bone).")]
        public Transform body;
        public PlayerController player;
        public float idleFreq = 5.5f, idleAmp = 16f;
        public float fastFreq = 12f, fastAmp = 34f;

        Quaternion[] rel;       // rest rotation relative to the parent bone
        float[] side;           // +1 right, -1 left
        bool[] lower;
        float phase, freq, amp, open, droop;

        void Awake() => Capture();

        void Capture()
        {
            if (wings == null) return;
            rel = new Quaternion[wings.Length];
            side = new float[wings.Length];
            lower = new bool[wings.Length];
            if (body == null) body = transform;
            for (int i = 0; i < wings.Length; i++)
            {
                var w = wings[i];
                if (!w) continue;
                rel[i] = w.localRotation;
                side[i] = Mathf.Sign(Vector3.Dot(w.position - body.position, body.right) + 1e-4f);
                lower[i] = w.name.Contains("_D_");
            }
            freq = idleFreq; amp = idleAmp;
        }

        void LateUpdate()
        {
            if (rel == null || wings.Length == 0) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float tf = idleFreq, ta = idleAmp, to = 0f, td = 0f;
            if (player != null)
            {
                float speed = player.motor != null ? player.motor.PlanarVelocity.magnitude : 0f;
                float run = Mathf.Clamp01(speed / 6f);
                tf = Mathf.Lerp(idleFreq, fastFreq * 0.8f, run);
                ta = Mathf.Lerp(idleAmp, fastAmp * 0.75f, run);
                to = -8f * run;                                         // swept back a little at speed
                switch (player.State)
                {
                    case PlayerState.Air:
                    case PlayerState.Special:
                        tf = fastFreq; ta = fastAmp; to = 8f; break;
                    case PlayerState.Dodge:
                        tf = idleFreq; ta = 6f; to = -45f; td = 12f; break;   // folded tight for the roll
                    case PlayerState.Hit:
                    case PlayerState.Dead:
                        tf = 3f; ta = 5f; to = -40f; td = 20f; break;
                    case PlayerState.Aim:
                        tf = idleFreq; ta = 8f; to = -38f; td = 10f; break;   // swept back out of the aiming camera's view
                    case PlayerState.Attack:
                        tf = Mathf.Max(tf, 8f); ta = Mathf.Max(ta, 22f); break;
                }
            }
            float k = 1f - Mathf.Exp(-6f * dt);
            freq = Mathf.Lerp(freq, tf, k);
            amp = Mathf.Lerp(amp, ta, k);
            open = Mathf.Lerp(open, to, k);
            droop = Mathf.Lerp(droop, td, k);
            phase += freq * dt * Mathf.PI * 2f;

            Vector3 up = body.up, fwd = body.forward;
            for (int i = 0; i < wings.Length; i++)
            {
                var w = wings[i];
                if (!w || w.parent == null) continue;
                float s = side[i];
                float ph = lower[i] ? phase - 0.7f : phase;
                float flap = open + Mathf.Sin(ph) * amp * (lower[i] ? 0.85f : 1f);
                float dip = droop + Mathf.Sin(ph + 1.2f) * amp * 0.3f;
                Quaternion restWorld = w.parent.rotation * rel[i];
                // sweep round the body's up axis (tips forward / back), then dip the tip down / up
                w.rotation = Quaternion.AngleAxis(-s * flap, up) * Quaternion.AngleAxis(-s * dip, fwd) * restWorld;
            }
        }
    }
}
