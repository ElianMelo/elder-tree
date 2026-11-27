using System.Collections;
using System.Collections.Generic;
using ElderTree.Combat;
using ElderTree.Equipment;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree.Enemies
{
    /// <summary>
    /// What the encounter, the boss bar, the F1 roster and the autopilot need from any boss.
    /// Each boss keeps its own tuning fields (so prefab values and defaults stay per boss) and
    /// exposes them through the Local* properties.
    /// <para>Co-op: the host runs the boss as usual. On the other players' machines the boss is a <see cref="Mirror"/>:
    /// its own scripts stand still, the network layer poses it, these properties read what the host sends, and hits
    /// landed on it go to the host (<see cref="Coop.ForwardBossHit"/>). The host's boss hunts one player at a time
    /// (<see cref="Aggro"/>) and switches its quarry now and then, gets more health per player and rests less between
    /// moves (<see cref="Aggression"/>).</para>
    /// </summary>
    public abstract class BossBase : MonoBehaviour, IDamageable, IStaggerable
    {
        public abstract string DisplayName { get; }
        public abstract Vector3 ArenaCenter { get; }

        protected abstract float LocalMaxHealth { get; }
        protected abstract float LocalHealth { get; }
        protected abstract float LocalPoise { get; }
        protected abstract bool LocalPhaseTwo { get; }
        protected abstract string LocalCurrentMove { get; }
        protected abstract string LocalStateLabel { get; }
        protected abstract bool LocalIsDead { get; }
        protected abstract bool LocalIsAlive { get; }
        protected abstract bool LocalIsStaggered { get; }
        /// <summary>The boss's own max-health tuning field (co-op scales it at the start of a fight).</summary>
        protected abstract float MaxHealthField { get; set; }

        public float MaxHealth => Mirror != null ? Mirror.maxHealth : LocalMaxHealth;
        public float Health => Mirror != null ? Mirror.health : LocalHealth;
        public float Poise => Mirror != null ? Mirror.poise : LocalPoise;
        public bool PhaseTwo => Mirror != null ? Mirror.phaseTwo : LocalPhaseTwo;
        public string CurrentMove => Mirror != null ? Mirror.move : LocalCurrentMove;
        /// <summary>Short state label for debug readouts (F1 roster, autopilot log).</summary>
        public string StateLabel => Mirror != null ? Mirror.label : LocalStateLabel;
        public bool IsDead => Mirror != null ? Mirror.dead : LocalIsDead;
        public bool IsAlive => Mirror != null ? Mirror.alive : LocalIsAlive;
        public bool IsStaggered => Mirror != null ? Mirror.staggered : LocalIsStaggered;
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

        /// <summary>A blow lands. In co-op on a client it goes to the host instead (true: assume it landed).</summary>
        public bool ReceiveHit(in DamageInfo info)
        {
            if (Coop.ForwardBossHit(this, null, info)) return true;
            AddThreat(info);
            return LocalReceiveHit(info);
        }
        protected abstract bool LocalReceiveHit(in DamageInfo info);

        // ------------------------------------------------------------------ first-meeting cinematic
        /// <summary>Does this boss have a first-meeting cinematic (<see cref="BossCinematic"/> plays it once per save)?</summary>
        public virtual bool HasIntroCinematic => false;
        /// <summary>The cinematic itself: stage the boss, move the camera with <see cref="BossCinematic.Shot"/>. Runs on the director.</summary>
        public virtual IEnumerator IntroCinematic(BossCinematic director) { yield break; }
        /// <summary>The player skipped: jump straight to where the cinematic would have left the boss.</summary>
        public virtual void SkipIntroCinematic() { }
        /// <summary>Start fighting from where the cinematic left the boss (no entrance of its own).</summary>
        public virtual void BeginFightAfterIntro() => BeginFight();

        // ================================================================== co-op: the mirror
        /// <summary>What the host says about its boss, shown on a client's mirror of it.</summary>
        public class MirrorState
        {
            public bool alive, dead, staggered, phaseTwo;
            public float health, maxHealth = 1f, poise;
            public string move = "-", label = "Mirror";
            /// <summary>Network id of the player the host's boss is hunting (-1 none).</summary>
            public int aggroId = -1;
        }

        /// <summary>Set on clients in a co-op stage: the boss is posed by the host and these numbers are the host's.</summary>
        public MirrorState Mirror { get; private set; }

        /// <summary>Become a puppet of the host's boss: the brain stops and stays stopped.</summary>
        public void BeginMirror()
        {
            StopAllCoroutines();
            Mirror ??= new MirrorState { maxHealth = LocalMaxHealth, health = LocalMaxHealth };
            enabled = false;
        }

        /// <summary>The host's boss took a hit (boss bar numbers on a client).</summary>
        public void MirrorDamaged(in DamageInfo info, bool poiseBroken) => RaiseDamaged(info, poiseBroken);
        /// <summary>The host's boss fell (victory on a client).</summary>
        public void MirrorFelled() => RaiseFelled();

        /// <summary>The world-space roots and prefabs of what this boss spawns (bombs, motes...), so clients can mirror them.
        /// <paramref name="create"/>: make the root if it doesn't exist yet.</summary>
        public virtual Transform NetPoolRoot(bool create) => null;
        /// <summary>What a client's mirror reports as staggered (hits land as criticals). The Druid: its head while toppled.</summary>
        public virtual bool NetCritical => LocalIsStaggered;
        public virtual GameObject[] NetPoolPrefabs => null;

        // ================================================================== co-op: scaling
        float soloMaxHealth = -1f;
        /// <summary>Shorter rests between moves: divide idle / orbit / stalk times by this (1 solo).</summary>
        public float Aggression { get; private set; } = 1f;

        /// <summary>Health and pace for the party in the session (called by the encounter before the fight starts).</summary>
        public void ApplyPartyScaling()
        {
            if (soloMaxHealth < 0f) soloMaxHealth = MaxHealthField;
            MaxHealthField = soloMaxHealth * Coop.BossHealthScale;
            Aggression = Coop.BossAggression;
            aggro = null;
            threat.Clear();
        }

        // ================================================================== co-op: aggro
        [Header("Aggro (co-op)")]
        [Tooltip("How long the boss keeps hunting one player before it may switch (seconds, min / max).")]
        public Vector2 aggroHold = new Vector2(7f, 13f);

        PlayerController aggro;
        float aggroUntil, aggroFrame = -1;
        readonly Dictionary<PlayerController, float> threat = new();

        /// <summary>The tree this boss is hunting (the local player in solo).</summary>
        public PlayerController Aggro { get { if (probe != null) return probe; UpdateAggro(); return aggro; } }

        PlayerController probe;
        readonly Dictionary<PlayerController, float> bystanderUntil = new();
        /// <summary>A bystander caught by a blow can't be caught again for this long (seconds).</summary>
        const float BystanderGrace = 0.8f;

        /// <summary>
        /// Run a melee hit test against every living tree: <paramref name="test"/> reads the boss's quarry (Aggro, and so
        /// its own Player / Rel / FlatDist helpers), which is set to each player in turn, the hunted one first. In solo it
        /// is a single plain call. <paramref name="targetDone"/>: the hunted one was already struck by this blow (skip
        /// it; bystanders are still tested, each at most once per <see cref="BystanderGrace"/>).
        /// Returns true when the hunted one was struck now.
        /// </summary>
        protected bool HitEach(bool targetDone, System.Func<bool> test)
        {
            var all = PlayerController.All;
            if (all.Count <= 1) return !targetDone && test();
            var target = Aggro;
            bool targetHit = false;
            if (!targetDone && target != null && !target.IsDead)
            {
                probe = target;
                try { targetHit = test(); } finally { probe = null; }
            }
            for (int i = 0; i < all.Count; i++)
            {
                var pc = all[i];
                if (pc == null || pc == target || pc.IsDead) continue;
                if (bystanderUntil.TryGetValue(pc, out float until) && Time.time < until) continue;
                bool h;
                probe = pc;
                try { h = test(); } finally { probe = null; }
                if (h) bystanderUntil[pc] = Time.time + BystanderGrace;
            }
            return targetHit;
        }
        /// <summary>Raised when the boss turns on another player.</summary>
        public event System.Action<BossBase, PlayerController> AggroChanged;

        /// <summary>Idle between attacks (circling, stalking, drifting): a good moment to turn on someone else.</summary>
        protected virtual bool BetweenMoves
        {
            get
            {
                var m = CurrentMove;
                return string.IsNullOrEmpty(m) || m == "-" || m == "Stalk" || m == "Drift" || m == "Grounded";
            }
        }

        /// <summary>A hit that reached the boss some other way (a Druid part): it still counts toward who it hunts.</summary>
        public void NoteThreat(in DamageInfo info) => AddThreat(info);

        void AddThreat(in DamageInfo info)
        {
            if (info.source == null || PlayerController.All.Count < 2) return;
            var pc = info.source.GetComponentInParent<PlayerController>();
            if (pc == null) return;
            threat.TryGetValue(pc, out float t);
            threat[pc] = t + info.damage;
        }

        void UpdateAggro()
        {
            if (aggroFrame == Time.frameCount) return;
            aggroFrame = Time.frameCount;
            var all = PlayerController.All;
            if (all.Count <= 1)
            {
                aggro = PlayerController.Instance != null ? PlayerController.Instance : all.Count > 0 ? all[0] : null;
                return;
            }
            bool valid = aggro != null && !aggro.IsDead && all.Contains(aggro);
            // switch only between moves, so a lunge already under way keeps its line (or once long overdue)
            bool due = Time.time > aggroUntil && (BetweenMoves || Time.time > aggroUntil + 8f);
            if (valid && !due) return;
            var next = PickQuarry(valid ? aggro : null);
            if (next == null) { if (!valid) aggro = PlayerController.Instance; return; }
            aggroUntil = Time.time + Random.Range(aggroHold.x, aggroHold.y);
            if (next == aggro) return;
            aggro = next;
            AggroChanged?.Invoke(this, aggro);
        }

        /// <summary>Who to hunt next: the players who hurt it most lately are likelier, the one it just hunted much less so.</summary>
        PlayerController PickQuarry(PlayerController current)
        {
            float total = 0f;
            var weights = new List<(PlayerController pc, float w)>();
            foreach (var pc in PlayerController.All)
            {
                if (pc == null || pc.IsDead) continue;
                threat.TryGetValue(pc, out float t);
                float dist = Vector3.Distance(pc.transform.position, transform.position);
                float w = 1f + t / Mathf.Max(1f, MaxHealth * 0.04f) + Mathf.Clamp01(1f - dist / 30f);
                if (pc == current) w *= 0.25f;
                weights.Add((pc, w));
                total += w;
            }
            // threat fades so the hunt keeps moving around the party
            var keys = new List<PlayerController>(threat.Keys);
            foreach (var k in keys) threat[k] *= 0.5f;
            if (weights.Count == 0) return null;
            float r = Random.value * total;
            foreach (var (pc, w) in weights) if ((r -= w) <= 0f) return pc;
            return weights[weights.Count - 1].pc;
        }

        /// <summary>Make the boss hunt a given player (the host applies its own pick; also debug).</summary>
        public void SetAggro(PlayerController pc)
        {
            if (pc == null || pc == aggro) return;
            aggro = pc;
            aggroUntil = Time.time + Random.Range(aggroHold.x, aggroHold.y);
            AggroChanged?.Invoke(this, aggro);
        }
    }
}
