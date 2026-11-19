using UnityEngine;

namespace ElderTree.Player
{
    /// <summary>
    /// Kinematic CharacterController motor. The controller asks for a planar velocity (smoothed
    /// here), or pushes explicit displacements for lunges and dodges; gravity, ground snapping and
    /// landing detection live here.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        public float gravity = 24f;
        public float fallGravityMultiplier = 1.45f;
        public float maxFallSpeed = 30f;
        public float groundSnap = 0.35f;
        public LayerMask groundMask = ~0;

        public CharacterController Controller { get; private set; }
        public Vector3 PlanarVelocity { get; private set; }
        public float VerticalVelocity { get; private set; }
        public bool Grounded { get; private set; }
        public float AirTime { get; private set; }
        public bool LandedThisFrame { get; private set; }
        public float LandingSpeed { get; private set; }
        public float GravityScale { get; set; } = 1f;

        Vector3 pendingDisplacement;
        bool jumpedThisFrame;

        void Awake() => Controller = GetComponent<CharacterController>();

        /// <summary>Moves the planar velocity towards a target with exponential sharpness.</summary>
        public void SetDesiredPlanarVelocity(Vector3 desired, float sharpness, float dt)
        {
            desired.y = 0f;
            PlanarVelocity = Vector3.Lerp(PlanarVelocity, desired, Ease.Damp(sharpness, dt));
        }

        public void SetPlanarVelocityImmediate(Vector3 v) { v.y = 0; PlanarVelocity = v; }

        public void AddDisplacement(Vector3 d) => pendingDisplacement += d;

        public void Jump(float height)
        {
            VerticalVelocity = Mathf.Sqrt(2f * gravity * height);
            Grounded = false;
            jumpedThisFrame = true;
        }

        /// <summary>Direct vertical control (special attacks hang in the air, slam down).</summary>
        public void SetVerticalVelocity(float v)
        {
            VerticalVelocity = v;
            if (v > 0f) { Grounded = false; jumpedThisFrame = true; }
        }

        public void Tick(float dt)
        {
            LandedThisFrame = false;
            bool wasGrounded = Grounded;

            // gravity
            float g = gravity * GravityScale * (VerticalVelocity < 0f ? fallGravityMultiplier : 1f);
            if (!Grounded || jumpedThisFrame) VerticalVelocity = Mathf.Max(-maxFallSpeed, VerticalVelocity - g * dt);
            else VerticalVelocity = -2f; // keep pressed to the ground

            Vector3 motion = PlanarVelocity * dt + pendingDisplacement + Vector3.up * VerticalVelocity * dt;
            pendingDisplacement = Vector3.zero;
            float preY = VerticalVelocity;
            var flags = Controller.Move(motion);

            bool hitGround = (flags & CollisionFlags.Below) != 0;
            if ((flags & CollisionFlags.Above) != 0 && VerticalVelocity > 0f) VerticalVelocity = 0f;

            // snap down small steps/slopes when we were grounded and are not jumping
            if (!hitGround && wasGrounded && !jumpedThisFrame && VerticalVelocity <= 0f)
            {
                if (Physics.SphereCast(transform.position + Vector3.up * (Controller.radius + 0.05f), Controller.radius * 0.9f,
                        Vector3.down, out var hit, groundSnap + 0.05f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    Controller.Move(Vector3.down * hit.distance);
                    hitGround = true;
                }
            }

            Grounded = hitGround && !(jumpedThisFrame && VerticalVelocity > 0f);
            if (Grounded)
            {
                if (!wasGrounded)
                {
                    LandedThisFrame = true;
                    LandingSpeed = -preY;
                }
                AirTime = 0f;
            }
            else AirTime += dt;
            jumpedThisFrame = false;
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            Controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            Controller.enabled = true;
            PlanarVelocity = Vector3.zero;
            VerticalVelocity = 0f;
        }
    }
}
