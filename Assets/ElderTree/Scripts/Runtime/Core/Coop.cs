using System.Collections.Generic;
using ElderTree.Audio;
using ElderTree.Combat;
using ElderTree.Enemies;
using ElderTree.FX;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree
{
    /// <summary>
    /// What the game needs to know about online co-op, without knowing how it is networked. The ElderTree.Net assembly
    /// (FishNet over Unity Relay) fills this in and plugs itself in as the <see cref="Bridge"/>; with no session running
    /// everything here reads as a solo game (host of itself, one player).
    /// <para>The model: the host runs every boss and everything the bosses throw; each player runs their own tree.
    /// Clients mirror the bosses, and send the hits they land to the host; the host sends each client the hits the
    /// bosses land on them. Combat effects and sounds are echoed to the other players.</para>
    /// </summary>
    public static class Coop
    {
        /// <summary>A co-op session is running (lobby, vote or stage).</summary>
        public static bool Active;
        /// <summary>Co-op gameplay is running in the current stage (every player's tree is in the world).</summary>
        public static bool InStage;
        /// <summary>This machine runs the bosses (true in solo).</summary>
        public static bool IsHost = true;
        /// <summary>This machine mirrors the bosses the host runs.</summary>
        public static bool IsClient => Active && !IsHost;
        /// <summary>Everyone in the session, in slot order (empty in solo).</summary>
        public static readonly List<Member> Party = new();
        public static int PlayerCount => Active && Party.Count > 0 ? Party.Count : 1;
        public static ICoopBridge Bridge;

        /// <summary>Raised when the party changes (joins, leaves, deaths, health) so the HUD can redraw.</summary>
        public static event System.Action PartyChanged;
        public static void RaisePartyChanged() => PartyChanged?.Invoke();

        public class Member
        {
            public int id;
            public int slot;
            public string name;
            public bool isLocal, isHost;
            public float health01 = 1f;
            public bool dead;
            /// <summary>The tree standing in for this player in the current stage (the local player for yourself).</summary>
            public PlayerController avatar;
        }

        public static Member Local
        {
            get { foreach (var m in Party) if (m.isLocal) return m; return null; }
        }

        public static Member Find(int id)
        {
            foreach (var m in Party) if (m.id == id) return m;
            return null;
        }

        /// <summary>The player colour of a party slot (nameplates, vote marks).</summary>
        public static Color SlotColor(int slot) => slot switch
        {
            0 => new Color(0.93f, 0.76f, 0.42f),
            1 => new Color(0.52f, 0.78f, 0.92f),
            2 => new Color(0.66f, 0.88f, 0.52f),
            _ => new Color(0.92f, 0.55f, 0.66f),
        };

        // ------------------------------------------------------------------ boss tuning
        /// <summary>Extra boss health per player beyond the first (fraction of its solo health).</summary>
        public const float HealthPerExtraPlayer = 0.7f;
        /// <summary>Shorter pauses between boss moves per extra player (1 = solo pace).</summary>
        public const float AggressionPerExtraPlayer = 0.16f;
        public static float BossHealthScale => 1f + HealthPerExtraPlayer * (PlayerCount - 1);
        public static float BossAggression => 1f + AggressionPerExtraPlayer * (PlayerCount - 1);

        // ------------------------------------------------------------------ effect echo
        /// <summary>True while an effect another player sent is being played here (it must not be echoed back).</summary>
        public static bool ApplyingRemote;
        static int localOnly;

        /// <summary>Effects spawned inside this scope stay on this machine (your mask cinematic, your mask's arrival).</summary>
        public static LocalOnlyScope LocalOnly() { localOnly++; return new LocalOnlyScope(); }
        public readonly struct LocalOnlyScope : System.IDisposable { public void Dispose() => localOnly--; }

        /// <summary>Your mask cinematic is playing: what it spawns is yours alone.</summary>
        public static bool CinematicPlaying;

        static bool Echo => InStage && Bridge != null && !ApplyingRemote && localOnly == 0 && !CinematicPlaying;

        internal static void OnFx(FXType type, Vector3 pos, Quaternion rot, float scale)
        {
            if (Echo) Bridge.Fx(type, pos, rot, scale);
        }

        internal static void OnSfx(Sfx id, Vector3 pos, float volume, float pitch)
        {
            if (Echo) Bridge.Sfx(id, pos, volume, pitch);
        }

        /// <summary>A blast went off on the host: the others shake their own cameras.</summary>
        public static void OnBlast(Vector3 center, float radius, float fxScale)
        {
            if (InStage && IsHost && Bridge != null) Bridge.Blast(center, radius, fxScale);
        }

        // ------------------------------------------------------------------ hits across the wire
        /// <summary>A client struck a mirrored boss (or one of its parts): the host gets the hit. True when it was sent.</summary>
        public static bool ForwardBossHit(BossBase boss, Component part, in DamageInfo info) =>
            IsClient && InStage && Bridge != null && Bridge.BossHit(boss, part, info);

        /// <summary>A client struck something the host's boss put into the world (a walking bomb).</summary>
        public static bool ForwardHazardHit(Component hazard, in DamageInfo info) =>
            IsClient && InStage && Bridge != null && Bridge.HazardHit(hazard, info);

        /// <summary>The host's boss struck another player's tree: its owner takes the hit.</summary>
        public static void ForwardPlayerHit(PlayerController remote, in DamageInfo info) => Bridge?.PlayerHit(remote, info);

        // ------------------------------------------------------------------ targets
        /// <summary>Every tree in the stage: yours and, in co-op, the other players'.</summary>
        public static IReadOnlyList<PlayerController> Players => PlayerController.All;

        /// <summary>The living tree closest to a point (homing hazards), or the local player.</summary>
        public static PlayerController Nearest(Vector3 p)
        {
            PlayerController best = null;
            float bd = float.MaxValue;
            foreach (var pc in PlayerController.All)
            {
                if (pc == null || pc.IsDead) continue;
                float d = (pc.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = pc; }
            }
            return best != null ? best : PlayerController.Instance;
        }

        /// <summary>Is every tree standing (a rematch waits for the whole party)?</summary>
        public static bool AllAlive
        {
            get
            {
                foreach (var pc in PlayerController.All) if (pc != null && pc.IsDead) return false;
                return true;
            }
        }

        // ------------------------------------------------------------------ stage flow
        /// <summary>Every player has loaded the stage (or the host stopped waiting). The host's arena starts the fight then.</summary>
        public static bool StageReady => !InStage || Bridge == null || Bridge.EveryoneLoaded;

        /// <summary>This player is done with the stage after a victory (mask collected): the host takes the party back to the vote.</summary>
        public static void StageDone() => Bridge?.StageDone();

        /// <summary>Host: take the whole party back to the stage vote.</summary>
        public static void ReturnToVote() => Bridge?.ReturnToVote();

        /// <summary>Leave the session (back to the main menu, solo).</summary>
        public static void Leave() => Bridge?.Leave();

        /// <summary>Is anyone still standing?</summary>
        public static bool AnyAlive
        {
            get
            {
                foreach (var pc in PlayerController.All) if (pc != null && !pc.IsDead) return true;
                return false;
            }
        }
    }

    /// <summary>Implemented by the network layer (ElderTree.Net).</summary>
    public interface ICoopBridge
    {
        void Fx(FXType type, Vector3 pos, Quaternion rot, float scale);
        void Sfx(Sfx id, Vector3 pos, float volume, float pitch);
        void Blast(Vector3 center, float radius, float fxScale);
        bool BossHit(BossBase boss, Component part, in DamageInfo info);
        bool HazardHit(Component hazard, in DamageInfo info);
        void PlayerHit(PlayerController remote, in DamageInfo info);
        bool EveryoneLoaded { get; }
        void StageDone();
        void ReturnToVote();
        void Leave();
    }
}
