using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.Combat;
using ElderTree.FX;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree.Enemies
{
    /// <summary>Explosions shared by the boss's bombs: FX, sound, distance-scaled shake, and damage
    /// to the player (full in the inner 50%, falling to 55% at the rim) and/or the boss.</summary>
    public static class Blast
    {
        public static readonly int GroundMask = 1;

        public static Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out var hit, 30f, GroundMask, QueryTriggerInteraction.Ignore))
                return hit.point;
            p.y = 0f;
            return p;
        }

        static readonly RaycastHit[] floorHits = new RaycastHit[16];

        /// <summary>The lowest walkable surface under p (the field itself, not a crate or wall top).</summary>
        public static Vector3 Floor(Vector3 p)
        {
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 30f, Vector3.down, floorHits, 60f, GroundMask, QueryTriggerInteraction.Ignore);
            float y = float.MaxValue;
            for (int i = 0; i < n; i++) y = Mathf.Min(y, floorHits[i].point.y);
            p.y = n > 0 ? y : 0f;
            return p;
        }

        public static void Detonate(Vector3 center, float radius, float damage, bool hurtsPlayer, GameObject source,
            float fxScale = 1f, CrocodileBoss hurtsBoss = null, float bossDamage = 0f)
        {
            FXManager.Spawn(fxScale >= 0.8f ? FXType.Explosion : FXType.BombPop, center + Vector3.up * 0.15f, Quaternion.identity, fxScale);
            FXManager.Spawn(FXType.SlamRing, center + Vector3.up * 0.05f, Quaternion.identity, radius * 0.55f);
            AudioManager.Play(fxScale >= 0.8f ? Sfx.Explosion : Sfx.BombPop, center, Mathf.Clamp01(0.6f + fxScale * 0.4f), Random.Range(0.92f, 1.06f));

            var pc = PlayerController.Instance;
            if (pc != null)
            {
                float camDist = Vector3.Distance(pc.transform.position, center);
                float shake = Mathf.Clamp01(1.2f - camDist / (radius * 6f)) * 0.9f * fxScale;
                if (shake > 0.02f) CameraDirector.Instance?.Shake(shake, Vector3.down);
            }

            if (hurtsPlayer && pc != null && !pc.IsDead)
            {
                Vector3 d = pc.transform.position - center;
                float vertical = d.y;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist < radius + 0.3f && vertical < 2.6f && vertical > -1.5f)
                {
                    float k = Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(radius * 0.5f, radius + 0.3f, dist));
                    var info = new DamageInfo
                    {
                        damage = Mathf.Round(damage * k),
                        poiseDamage = damage * 0.45f,
                        point = pc.transform.position + Vector3.up * 1f,
                        direction = dist > 0.05f ? d / dist : -pc.transform.forward,
                        weight = dist < radius * 0.6f ? ImpactWeight.Slam : ImpactWeight.Heavy,
                        source = source
                    };
                    pc.TakeHit(info);
                }
            }

            if (hurtsBoss != null && hurtsBoss.IsAlive && bossDamage > 0f)
            {
                Vector3 bp = hurtsBoss.ClosestBodyPoint(center);
                if (Vector3.Distance(bp, center) < radius + 0.5f)
                {
                    hurtsBoss.ReceiveHit(new DamageInfo
                    {
                        damage = bossDamage, poiseDamage = bossDamage * 0.5f, point = bp,
                        direction = (bp - center).normalized, weight = ImpactWeight.Heavy, source = source
                    });
                }
            }
        }
    }
}
