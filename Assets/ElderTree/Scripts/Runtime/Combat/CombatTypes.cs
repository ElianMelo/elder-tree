using System.Collections.Generic;
using UnityEngine;

namespace ElderTree.Combat
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
        /// <summary>Breaks obstacles whose requiredPower is at most this (0 = can't break anything).</summary>
        public int breakPower;
        /// <summary>Damage came from an explosion (no per-swing hit-stop, splash feedback).</summary>
        public bool explosive;
        /// <summary>Cuts cuttables (vines...) whose requiredCut is at most this (0 = blunt).</summary>
        public int cutPower;
        /// <summary>How wide the cut is across a cuttable from <see cref="point"/> (metres, horizontal).</summary>
        public float cutWidth;
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
