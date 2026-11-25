using System.Collections;
using ElderTree.Combat;
using ElderTree.Equipment;
using UnityEngine;

namespace ElderTree.Enemies
{
    /// <summary>
    /// What the encounter, the boss bar, the F1 roster and the autopilot need from any boss.
    /// Each boss keeps its own tuning fields (so prefab values and defaults stay per boss) and
    /// exposes them through these properties.
    /// </summary>
    public abstract class BossBase : MonoBehaviour, IDamageable, IStaggerable
    {
        public abstract string DisplayName { get; }
        public abstract float MaxHealth { get; }
        public abstract float Health { get; }
        public abstract float Poise { get; }
        public abstract bool PhaseTwo { get; }
        public abstract string CurrentMove { get; }
        /// <summary>Short state label for debug readouts (F1 roster, autopilot log).</summary>
        public abstract string StateLabel { get; }
        public abstract bool IsDead { get; }
        public abstract Vector3 ArenaCenter { get; }

        public abstract bool IsAlive { get; }
        public abstract bool IsStaggered { get; }
        public Transform Root => transform;

        [Header("Reward")]
        [Tooltip("Mask that rises from the wreck when this boss is felled, unless the player already owns it.")]
        public MaskData rewardMask;

        public event System.Action<BossBase, DamageInfo, bool> Damaged;
        public event System.Action<BossBase> Felled;

        protected void RaiseDamaged(in DamageInfo info, bool poiseBroken) => Damaged?.Invoke(this, info, poiseBroken);
        protected void RaiseFelled() => Felled?.Invoke(this);

        /// <summary>Enter the arena and start fighting (restarts if already there).</summary>
        public abstract void BeginFight();
        /// <summary>Leave the world: dormant, inactive, full health.</summary>
        public abstract void ResetBoss();
        /// <summary>Debug: the next move the brain picks.</summary>
        public abstract void ForceNextMove(string move);
        /// <summary>Remove anything the boss spawned into the world (bombs, walkers...).</summary>
        public virtual void ClearProjectiles() { }
        public abstract bool ReceiveHit(in DamageInfo info);

        // ------------------------------------------------------------------ first-meeting cinematic
        /// <summary>Does this boss have a first-meeting cinematic (<see cref="BossCinematic"/> plays it once per save)?</summary>
        public virtual bool HasIntroCinematic => false;
        /// <summary>The cinematic itself: stage the boss, move the camera with <see cref="BossCinematic.Shot"/>. Runs on the director.</summary>
        public virtual IEnumerator IntroCinematic(BossCinematic director) { yield break; }
        /// <summary>The player skipped: jump straight to where the cinematic would have left the boss.</summary>
        public virtual void SkipIntroCinematic() { }
        /// <summary>Start fighting from where the cinematic left the boss (no entrance of its own).</summary>
        public virtual void BeginFightAfterIntro() => BeginFight();
    }
}
