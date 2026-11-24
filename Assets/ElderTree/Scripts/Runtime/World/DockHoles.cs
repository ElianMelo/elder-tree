using System.Collections;
using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.FX;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree.World
{
    /// <summary>
    /// Rotten holes in the Shark's dock. Step into one and you drop through into the sea: a splash, the screen dips
    /// to black, you are put back on the last solid spot you stood on (facing away from the hole), and the fall
    /// costs you health. Rolls don't save you (it ignores i-frames). Holes are world-space rectangles set by the
    /// builder from each module's "HoleCorner" empties.
    /// </summary>
    public class DockHoles : MonoBehaviour
    {
        [System.Serializable]
        public struct Hole
        {
            public Vector3 center;
            [Tooltip("Half the opening, x and z (m).")]
            public Vector2 halfSize;
        }

        public Hole[] holes = System.Array.Empty<Hole>();
        public float damage = 90f;
        [Tooltip("How far the feet must be inside the opening before you fall (m): brushing the edge is safe.")]
        public float margin = 0.18f;
        [Tooltip("Ground this close to a hole (m) is not remembered as a safe spot.")]
        public float safeClearance = 1.1f;
        public float waterLevel = -1f;

        PlayerController player;
        UI.HUD hud;
        Vector3 lastSafe;
        Quaternion lastSafeRot;
        bool haveSafe, falling;
        float sampleT;

        void Start()
        {
            player = FindAnyObjectByType<PlayerController>();
            hud = FindAnyObjectByType<UI.HUD>();
        }

        bool Inside(Vector3 p, float pad, out int index)
        {
            for (int i = 0; i < holes.Length; i++)
            {
                var h = holes[i];
                if (Mathf.Abs(p.x - h.center.x) < h.halfSize.x + pad && Mathf.Abs(p.z - h.center.z) < h.halfSize.y + pad) { index = i; return true; }
            }
            index = -1;
            return false;
        }

        void Update()
        {
            if (player == null) { player = FindAnyObjectByType<PlayerController>(); return; }
            if (falling || player.IsDead) return;
            Vector3 p = player.transform.position;
            bool grounded = player.motor != null && player.motor.Grounded;

            // remember where you last stood on something solid, well clear of any hole
            if ((sampleT -= Time.deltaTime) <= 0f)
            {
                sampleT = 0.15f;
                if (grounded && p.y > waterLevel + 0.7f && !Inside(p, safeClearance, out _))
                {
                    lastSafe = p; lastSafeRot = player.transform.rotation; haveSafe = true;
                }
            }

            // feet over the opening: standing on its lip with your centre over the gap, or already dropping
            if (Inside(p, -margin, out int hole) && (p.y < -0.05f || (grounded && p.y < 0.25f)))
                StartCoroutine(Fall(holes[hole]));
        }

        IEnumerator Fall(Hole h)
        {
            falling = true;
            Vector3 at = new Vector3(player.transform.position.x, waterLevel + 0.05f, player.transform.position.z);
            FXManager.Spawn(FXType.Splash, at, Quaternion.identity, 1.1f);
            FXManager.Spawn(FXType.WoodChips, new Vector3(at.x, 0f, at.z), Quaternion.LookRotation(Vector3.up), 1.2f);
            AudioManager.Play(Sfx.WaterSplash, at, 1f, Random.Range(0.92f, 1.04f));
            CameraDirector.Instance?.Shake(0.3f, Vector3.down);

            if (hud != null) yield return hud.FadeScreen(1f, 0.22f);
            else yield return new WaitForSeconds(0.25f);

            if (player != null && !player.IsDead)
            {
                Vector3 back = haveSafe ? lastSafe : transform.position;
                Vector3 away = Vector3.ProjectOnPlane(back - h.center, Vector3.up);
                Quaternion rot = away.sqrMagnitude > 0.01f ? Quaternion.LookRotation(away.normalized) : (haveSafe ? lastSafeRot : player.transform.rotation);
                player.motor.Teleport(back + Vector3.up * 0.05f, rot);
                yield return null;
                player.TakeHazardDamage(damage, away);
            }
            if (hud != null) yield return hud.FadeScreen(0f, 0.35f);
            yield return new WaitForSeconds(0.3f);
            falling = false;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.6f);
            foreach (var h in holes) Gizmos.DrawWireCube(h.center, new Vector3(h.halfSize.x * 2f, 0.1f, h.halfSize.y * 2f));
        }
    }
}
