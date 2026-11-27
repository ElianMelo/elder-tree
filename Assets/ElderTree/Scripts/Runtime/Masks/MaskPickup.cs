using ElderTree.Audio;
using ElderTree.Equipment;
using ElderTree.FX;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree.Masks
{
    /// <summary>
    /// The mask lying where it was won: it hovers, turns slowly and breathes a toxic glow so it reads
    /// in the dark. Walking into it picks it up (pad and keyboard alike, no extra button).
    /// </summary>
    public class MaskPickup : MonoBehaviour
    {
        public MaskData mask;
        public Transform model;
        public Light glow;
        public ParticleSystem motes;
        public float pickupRadius = 1.4f;
        public float hover = 1.1f;
        public float delay = 0.8f;

        float t, baseIntensity;
        bool taken;

        public static MaskPickup Spawn(MaskPickup prefab, MaskData m, Vector3 groundPos)
        {
            if (prefab == null || m == null) return null;
            var p = Instantiate(prefab, groundPos, Quaternion.identity);
            p.mask = m;
            using (Coop.LocalOnly())        // co-op: every player gets their own mask; nobody sees anyone else's
            {
                FXManager.Spawn(FXType.ChargeFlash, groundPos + Vector3.up * 1.1f, Quaternion.identity, 1.8f);
                FXManager.Spawn(FXType.Dust, groundPos, Quaternion.identity, 1.2f);
                AudioManager.Play(Sfx.MaskReceived, groundPos, 0.5f, 1.25f);
            }
            return p;
        }

        void Start()
        {
            if (glow) baseIntensity = glow.intensity;
            if (motes) motes.Play(true);
        }

        void Update()
        {
            if (taken) return;
            float dt = Time.deltaTime;
            t += dt;
            float rise = Ease.OutCubic(Mathf.Clamp01(t / 0.9f));
            if (model)
            {
                model.localPosition = Vector3.up * (hover * rise + Mathf.Sin(t * 2.1f) * 0.08f);
                model.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.3f) * 8f, t * 50f, 0f);
            }
            if (glow) glow.intensity = baseIntensity * (0.75f + 0.25f * Mathf.Sin(t * 3.3f)) * rise;

            var pc = PlayerController.Instance;
            if (pc == null || pc.IsDead || t < delay) return;
            Vector3 d = pc.transform.position - transform.position; d.y = 0f;
            if (d.magnitude > pickupRadius) return;
            if (pc.State != PlayerState.Locomotion && pc.State != PlayerState.Landing && pc.State != PlayerState.Aim) return;
            taken = true;
            var ms = MaskSystem.Instance;
            if (ms != null) ms.Acquire(mask);
            Destroy(gameObject);
        }
    }
}
