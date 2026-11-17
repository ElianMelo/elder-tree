using System.Collections.Generic;
using TungSahur.Audio;
using TungSahur.CameraSystem;
using TungSahur.FX;
using TungSahur.Player;
using UnityEngine;

namespace TungSahur.Combat
{
    /// <summary>
    /// Explosions the player causes (bomb hammer, cannon shells, Sky Bombardment): FX, sound, shake by
    /// distance, and falloff damage to every damageable thing in range (enemies, walkers, breakable
    /// obstacles). The player is never hurt by their own blasts.
    /// </summary>
    public static class Explosions
    {
        public const int HurtboxLayer = 9;
        static readonly Collider[] buffer = new Collider[64];
        static readonly HashSet<IDamageable> seen = new();

        public struct Blast
        {
            public Vector3 center;
            public float radius, damage, poise, fxScale;
            public int breakPower;
            public GameObject source;
            public ImpactWeight weight;
        }

        /// <summary>Detonate; returns how many targets took damage.</summary>
        public static int Detonate(in Blast b, IDamageable skip = null)
        {
            FXManager.Spawn(b.fxScale >= 0.8f ? FXType.Explosion : FXType.BombPop, b.center + Vector3.up * 0.1f, Quaternion.identity, b.fxScale);
            FXManager.Spawn(FXType.SlamRing, b.center + Vector3.up * 0.05f, Quaternion.identity, b.radius * 0.6f);
            if (b.fxScale > 1.2f) FXManager.Spawn(FXType.SlamDebris, b.center, Quaternion.identity, b.fxScale * 0.7f);
            AudioManager.Play(b.fxScale >= 0.8f ? Sfx.Explosion : Sfx.BombPop, b.center, Mathf.Clamp01(0.55f + b.fxScale * 0.35f),
                Random.Range(0.94f, 1.06f) / Mathf.Max(0.8f, Mathf.Sqrt(b.fxScale)));

            var pc = PlayerController.Instance;
            if (pc != null)
            {
                float d = Vector3.Distance(pc.transform.position, b.center);
                float shake = Mathf.Clamp01(1.15f - d / (b.radius * 5f)) * 0.55f * b.fxScale;
                if (shake > 0.02f) CameraDirector.Instance?.Shake(shake, Vector3.down);
            }

            seen.Clear();
            if (skip != null) seen.Add(skip);
            int n = Physics.OverlapSphereNonAlloc(b.center, b.radius, buffer, 1 << HurtboxLayer, QueryTriggerInteraction.Collide);
            int hits = 0;
            for (int i = 0; i < n; i++)
            {
                var col = buffer[i];
                var hb = col.GetComponent<Hurtbox>();
                var target = hb != null ? hb.Owner : col.GetComponentInParent<IDamageable>();
                if (target == null || !target.IsAlive || seen.Contains(target)) continue;
                if (pc != null && target.Root.IsChildOf(pc.transform)) continue;
                seen.Add(target);
                Vector3 p = col.ClosestPoint(b.center);
                float dist = Vector3.Distance(p, b.center);
                float k = Mathf.Lerp(1f, 0.45f, Mathf.InverseLerp(b.radius * 0.35f, b.radius, dist));
                Vector3 dir = target.Root.position - b.center; dir.y = 0f;
                var info = new DamageInfo
                {
                    damage = Mathf.Round(b.damage * k),
                    poiseDamage = b.poise * k,
                    point = p,
                    direction = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward,
                    weight = b.weight,
                    isSlamWave = true,
                    explosive = true,
                    breakPower = b.breakPower,
                    source = b.source
                };
                if (target.ReceiveHit(info))
                {
                    hits++;
                    Landed?.Invoke(info, target);
                }
            }
            return hits;
        }

        /// <summary>Raised for every target an explosion damages (HUD enemy bars, hit markers).</summary>
        public static event System.Action<DamageInfo, IDamageable> Landed;

        public static Vector3 Ground(Vector3 p, float up = 2f, float down = 6f)
        {
            if (Physics.Raycast(p + Vector3.up * up, Vector3.down, out var hit, up + down, 1, QueryTriggerInteraction.Ignore))
                return hit.point;
            return p;
        }
    }
}
