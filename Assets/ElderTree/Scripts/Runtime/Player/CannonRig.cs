using UnityEngine;

namespace ElderTree.Player
{
    /// <summary>
    /// Presentation of the Crocodile cannon on the alligator body (gameplay is in PlayerController.Form):
    /// the cannon rides on the back and the hammer sits in the fist; raising the cannon swaps them
    /// (cannon to the shoulder grip, hammer to the back). While aiming, the spine and chest pitch the
    /// Bazooka pose onto the crosshair, and each shot kicks the chest back.
    /// The shark's crossbow uses the same rig: on the back at rest, raised on the Rifle aim pose, with
    /// the loaded bolt shown only while one is in the groove, and the aim lock eased off while reloading
    /// (the reload clip moves the weapon).
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class CannonRig : MonoBehaviour
    {
        public PlayerController player;
        public Animator animator;
        public Transform cannon;
        [Tooltip("Cannon grip pose in the aim stance (child of the right hand, calibrated by the builder).")]
        public Transform cannonHandSocket;
        public Transform cannonBackSocket;
        public Transform muzzle;
        [Tooltip("Shown while a round is loaded (the crossbow's bolt in the groove). Optional.")]
        public GameObject loadedRound;
        public Transform hammer;
        public Transform hammerBackSocket;
        [Range(0f, 1f)] public float spinePitchShare = 0.4f;
        public float maxPitch = 50f;
        public float kickDegrees = 14f;
        [Tooltip("Grip height above the right-hand bone while shouldered.")]
        public float gripLift = 0.04f;

        public Vector3 MuzzleWorld => muzzle != null ? muzzle.position : transform.position + transform.forward + Vector3.up * 1.4f;

        Transform spine, chest, hand, hammerHandParent;
        Vector3 hammerLocalPos; Quaternion hammerLocalRot;
        bool cannonInHand, hammerOnBack;
        float weight, pitch, kick, hold = 1f;

        void Awake()
        {
            if (!animator) animator = GetComponent<Animator>();
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hammer)
            {
                hammerHandParent = hammer.parent;
                hammerLocalPos = hammer.localPosition;
                hammerLocalRot = hammer.localRotation;
            }
            SetCannonInHand(false);
        }

        public void Kick() => kick = 1f;

        void SetCannonInHand(bool inHand)
        {
            cannonInHand = inHand;
            if (!cannon) return;
            var socket = inHand ? cannonHandSocket : cannonBackSocket;
            if (!socket) return;
            cannon.SetParent(socket, false);
            cannon.localPosition = Vector3.zero;
            cannon.localRotation = Quaternion.identity;
        }

        void SetHammerOnBack(bool onBack)
        {
            hammerOnBack = onBack;
            if (!hammer || player == null || player.HammerThrown) return;
            if (onBack && hammerBackSocket)
            {
                hammer.SetParent(hammerBackSocket, false);
                hammer.localPosition = Vector3.zero;
                hammer.localRotation = Quaternion.identity;
            }
            else if (hammerHandParent)
            {
                hammer.SetParent(hammerHandParent, false);
                hammer.localPosition = hammerLocalPos;
                hammer.localRotation = hammerLocalRot;
            }
        }

        void OnDisable()
        {
            weight = 0f;
            if (cannonInHand) SetCannonInHand(false);
            if (hammerOnBack) SetHammerOnBack(false);
        }

        void LateUpdate()
        {
            if (player == null) return;
            float dt = Time.deltaTime;
            bool aiming = player.CannonRaised;
            weight = Mathf.MoveTowards(weight, aiming ? 1f : 0f, dt / (aiming ? 0.18f : 0.25f));
            bool wantHand = aiming && weight > 0.35f;
            if (wantHand != cannonInHand) SetCannonInHand(wantHand);
            if (wantHand != hammerOnBack && !player.HammerThrown) SetHammerOnBack(wantHand);

            kick = Mathf.MoveTowards(kick, 0f, dt * 4f);
            if (loadedRound != null && loadedRound.activeSelf != player.RangedLoaded) loadedRound.SetActive(player.RangedLoaded);
            hold = Mathf.MoveTowards(hold, player.RangedReloading ? 0f : 1f, dt / 0.18f);
            if (weight <= 0.001f && kick <= 0f) return;

            // pitch the upper body onto the aim point (the Bazooka clip aims level)
            Vector3 from = chest != null ? chest.position : transform.position + Vector3.up * 1.3f;
            Vector3 d = player.AimPoint - from;
            float target = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            pitch = Mathf.Lerp(pitch, Mathf.Clamp(target, -maxPitch, maxPitch), Ease.Damp(14f, dt));
            float total = (pitch * weight - kick * kick * kickDegrees) * Mathf.Lerp(0.35f, 1f, hold);
            Vector3 axis = player.transform.right;
            if (spine) spine.rotation = Quaternion.AngleAxis(total * spinePitchShare, axis) * spine.rotation;
            if (chest) chest.rotation = Quaternion.AngleAxis(total * (1f - spinePitchShare), axis) * chest.rotation;

            // the shouldered cannon sits in the fist but always points down the aim line (the clip only
            // gets the hands close; the barrel itself must read as aimed), with a recoil kick up
            if (cannonInHand && cannon && hand)
            {
                Vector3 grip = hand.position + Vector3.up * gripLift;
                Vector3 aim = player.AimPoint - grip;
                if (aim.sqrMagnitude < 0.25f) aim = player.transform.forward;
                Quaternion look = Quaternion.LookRotation(aim.normalized, Vector3.up) * Quaternion.Euler(-kick * kick * 18f, 0f, 0f);
                Vector3 pos = grip - look * Vector3.forward * kick * 0.12f;
                if (hold >= 0.999f) cannon.SetPositionAndRotation(pos, look);
                else
                {
                    // reloading: the weapon rides the hand socket (the clip works the string), easing back to the aim
                    Vector3 hp = cannonHandSocket != null ? cannonHandSocket.position : pos;
                    Quaternion hr = cannonHandSocket != null ? cannonHandSocket.rotation : look;
                    float h = Ease.InOutCubic(hold);
                    cannon.SetPositionAndRotation(Vector3.Lerp(hp, pos, h), Quaternion.Slerp(hr, look, h));
                }
            }
        }
    }
}
