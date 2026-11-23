using UnityEngine;

namespace ElderTree.FX
{
    /// <summary>
    /// Sweeps a searchlight head back and forth across the arena, pausing at the ends like a bored sentry.
    /// While a boss fight is on, it hunts the player instead (slower, it lags behind).
    /// </summary>
    public class Searchlight : MonoBehaviour
    {
        public Transform head;
        [Tooltip("World point the sweep is centred on.")]
        public Vector3 target;
        public float sweepDegrees = 55f;
        public float period = 11f;
        public float pitchWobble = 4f;
        public float trackSpeed = 25f;
        public bool trackPlayerInFights = true;

        Transform player;
        float seed;
        Quaternion current;

        void Start()
        {
            seed = Random.value * 10f;
            var pc = FindAnyObjectByType<Player.PlayerController>();
            if (pc != null) player = pc.transform;
            if (head != null) current = head.rotation;
        }

        void LateUpdate()
        {
            if (head == null) return;
            Vector3 aim;
            bool fight = trackPlayerInFights && player != null && Enemies.BossEncounter.Instance != null && Enemies.BossEncounter.Instance.Active;
            if (fight) aim = player.position + Vector3.up * 0.5f;
            else
            {
                // smooth sweep with dwell at the ends: sin shaped through a soft clamp
                float s = Mathf.Sin((Time.time + seed) * Mathf.PI * 2f / period);
                s = Mathf.Clamp(s * 1.35f, -1f, 1f);
                Vector3 to = target - head.position;
                var baseRot = Quaternion.LookRotation(to.normalized, Vector3.up);
                var yaw = Quaternion.AngleAxis(s * sweepDegrees * 0.5f, Vector3.up);
                var pitch = Quaternion.AngleAxis(Mathf.Sin((Time.time + seed) * 0.83f) * pitchWobble, Vector3.right);
                aim = head.position + yaw * baseRot * pitch * Vector3.forward * to.magnitude;
            }
            var want = Quaternion.LookRotation((aim - head.position).normalized, Vector3.up);
            current = Quaternion.RotateTowards(current, want, (fight ? trackSpeed : 90f) * Time.deltaTime);
            head.rotation = current;
        }
    }
}
