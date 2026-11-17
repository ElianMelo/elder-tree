using System.Collections.Generic;
using UnityEngine;

namespace TungSahur.Combat
{
    public struct DamageInfo
    {
        public float damage;
        public float poiseDamage;
        public Vector3 point;
        public Vector3 direction;     // world-space push direction (attacker -> victim)
        public ImpactWeight weight;
        public bool charged;
        public bool isSlamWave;
        public bool critical;
        public GameObject source;
    }

    /// <summary>Enemies that can be stance-broken: hits landed while staggered are criticals.</summary>
    public interface IStaggerable
    {
        bool IsStaggered { get; }
    }

    public interface IDamageable
    {
        bool IsAlive { get; }
        /// <summary>Returns true if the hit landed (not blocked / invulnerable).</summary>
        bool ReceiveHit(in DamageInfo info);
        Transform Root { get; }
    }

}
