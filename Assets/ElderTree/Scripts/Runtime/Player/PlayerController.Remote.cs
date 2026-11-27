using System.Collections.Generic;
using UnityEngine;

namespace ElderTree.Player
{
    /// <summary>
    /// Co-op: another player's tree standing in this world. It is a stripped copy of the local player (no input,
    /// no camera, no masks, no animators; its bones are posed from the network) that keeps the PlayerController so
    /// bosses and blasts can treat it like any player: it can be hunted, its velocity read, and blows that land on it
    /// are forwarded to its owner (<see cref="TakeHit"/>).
    /// </summary>
    public partial class PlayerController
    {
        /// <summary>Every tree in the stage (yours first, then the other players' in co-op).</summary>
        public static readonly List<PlayerController> All = new();

        /// <summary>Set around Instantiate by the network layer: the copy being made is a remote player.</summary>
        public static bool SpawningRemote;

        /// <summary>This tree belongs to another player (co-op).</summary>
        public bool IsRemote { get; private set; }
        /// <summary>Network id of the player this tree belongs to (co-op; -1 for the local tree outside co-op).</summary>
        public int NetId { get; set; } = -1;

        Vector3 remoteVel;
        bool remoteGrounded = true;

        void AwakeRemote()
        {
            IsRemote = true;
            if (!motor) motor = GetComponent<PlayerMotor>();
            if (!stats) stats = GetComponent<PlayerStats>();
        }

        void UpdateRemote(float dt)
        {
            // keep the motor's velocity readable (bosses lead their shots with it) without moving anything
            if (motor) motor.SetRemote(remoteVel, remoteGrounded);
        }

        /// <summary>What the owner says about their tree (co-op). The body itself is placed by the network layer.</summary>
        public void ApplyRemoteState(PlayerState state, bool invulnerable, bool grounded, Vector3 velocity, float health01)
        {
            if (!IsRemote) return;
            if (State != state) State = state;
            Invulnerable = invulnerable;
            remoteGrounded = grounded;
            remoteVel = velocity;
            if (stats != null) stats.SetHealthFraction(health01);
        }

        /// <summary>The state and flags this player shares with the others (co-op).</summary>
        public void ReadNetState(out PlayerState state, out bool invulnerable, out bool grounded, out Vector3 velocity)
        {
            state = State;
            invulnerable = Invulnerable;
            grounded = motor == null || motor.Grounded;
            velocity = motor != null ? motor.PlanarVelocity : Vector3.zero;
        }
    }
}
