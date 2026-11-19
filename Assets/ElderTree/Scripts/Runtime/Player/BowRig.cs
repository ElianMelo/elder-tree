using UnityEngine;

namespace ElderTree.Player
{
    /// <summary>
    /// Presentation side of the bow (gameplay lives in PlayerController.Bow). Sits on the animated
    /// model so it receives OnAnimatorIK:
    ///  * both hands are placed with humanoid IK on the aim line (camera crosshair → bow), so the
    ///    arrow points where the reticle points at any pitch;
    ///  * the bow swings from the back slot into the left fist (the club goes to the back), its
    ///    limbs bend with a "Draw" blend shape measured from where the string hand actually is;
    ///  * the string is a 3-point line (tips → nock) that follows the drawing hand, snaps and
    ///    vibrates on release, and the right hand reaches over the shoulder for the next arrow.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class BowRig : MonoBehaviour
    {
        [Header("References")]
        public PlayerController player;
        public Animator animator;
        public Transform bow;                    // bow model root (grip at origin)
        public SkinnedMeshRenderer bowRenderer;  // has the "Draw" blend shape
        public Transform stringTop, stringBottom, stringTopDrawn, stringBottomDrawn, arrowRest;
        public LineRenderer bowString;
        public Transform nockedArrow;            // visual arrow (origin = nock, +Z = forward)
        public Transform bowBackSocket;          // bow slung on the back
        public Transform club;
        public Transform clubBackSocket;
        public Transform quiverTop;              // where the hand reaches for a fresh arrow

        [Header("Aim line (character space, metres)")]
        [Tooltip("The arrow line passes through this point: height / forward / right of the feet, in the body's (stance-turned) frame.")]
        // Matches where BowShot01 - Hold puts the hands on the Elder Tree (root faces the target,
        // arrow line ~0.2 m right of the root at shoulder height), so IK only trims the pack pose.
        public Vector3 anchor = new Vector3(0.21f, 1.38f, 0.0f);
        [Tooltip("Distance from the anchor to the arrow rest (how far the bow arm reaches).")]
        public float bowReach = 0.56f;
        [Tooltip("Arrow rest → nock distance at full draw.")]
        public float fullDrawLength = 0.68f;
        [Tooltip("Bow canted (top tilted right) for the archer's view.")]
        public float cant = 14f;

        [Header("Hand offsets (aim frame: x right, y up, z forward)")]
        [Tooltip("Left-hand bone position relative to the bow grip.")]
        public Vector3 bowHandOffset = new Vector3(0.0f, -0.01f, -0.02f);
        [Tooltip("Right-hand bone position relative to the nock.")]
        public Vector3 drawHandOffset = new Vector3(0.015f, -0.035f, -0.03f);
        public Vector3 leftElbowHint = new Vector3(-0.06f, -0.14f, 0.30f);
        public Vector3 rightElbowHint = new Vector3(0.16f, 0.06f, -0.42f);

        [Header("Timing")]
        public float raiseBlend = 0.16f;
        public float lowerBlend = 0.22f;

        public Vector3 NockWorld { get; private set; }
        public Vector3 ArrowRestWorld => bow != null && arrowRest != null ? arrowRest.position : transform.position;
        public float VisualDraw { get; private set; }

        Vector3 topRest, botRest, topDrawn, botDrawn, restLocal;   // in bow-root space
        Quaternion bowAxes = Quaternion.identity;
        float braceLength = 0.14f;
        int drawShape = -1;
        float weight, weightLinear;
        bool clubOnBack;
        Transform clubHandParent;
        Vector3 clubLocalPos; Quaternion clubLocalRot;
        float releaseKick, stringBuzz;
        Vector3 buzzAxis;
        Transform leftHand, rightHand, rightShoulder;

        void Awake()
        {
            if (!animator) animator = GetComponent<Animator>();
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            rightShoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (club)
            {
                clubHandParent = club.parent;
                clubLocalPos = club.localPosition;
                clubLocalRot = club.localRotation;
            }
            CacheBowGeometry();
            if (bowRenderer && bowRenderer.sharedMesh) drawShape = bowRenderer.sharedMesh.GetBlendShapeIndex("Draw");
            if (bowString)
            {
                bowString.positionCount = 3;
                bowString.useWorldSpace = true;
            }
            if (nockedArrow) nockedArrow.gameObject.SetActive(false);
        }

        void CacheBowGeometry()
        {
            if (!bow || !stringTop || !stringBottom || !arrowRest) return;
            // measured in the bow root's space, whatever axis conversion the FBX import applied
            topRest = bow.InverseTransformPoint(stringTop.position);
            botRest = bow.InverseTransformPoint(stringBottom.position);
            topDrawn = stringTopDrawn ? bow.InverseTransformPoint(stringTopDrawn.position) : topRest;
            botDrawn = stringBottomDrawn ? bow.InverseTransformPoint(stringBottomDrawn.position) : botRest;
            restLocal = bow.InverseTransformPoint(arrowRest.position);
            Vector3 up = (topRest - botRest).normalized;
            Vector3 mid = (topRest + botRest) * 0.5f;
            Vector3 fwd = restLocal - mid;
            fwd.x = 0f;   // the shelf sits off to the side; the bend plane is the bow's YZ plane
            fwd -= up * Vector3.Dot(fwd, up);
            braceLength = fwd.magnitude;
            bowAxes = Quaternion.LookRotation(fwd.normalized, up);
        }

        // ------------------------------------------------------------------ aim frame
        Vector3 Anchor
        {
            get
            {
                var root = player.transform;
                return root.position + root.rotation * anchor;
            }
        }

        Vector3 AimDir
        {
            get
            {
                Vector3 d = player.AimPoint - Anchor;
                if (d.sqrMagnitude < 0.25f) d = player.transform.forward;
                return d.normalized;
            }
        }

        Quaternion AimRot => Quaternion.LookRotation(AimDir, Vector3.up);

        Quaternion BowWorldRot(Vector3 aimDir)
        {
            Vector3 up = Quaternion.AngleAxis(-cant, aimDir) * Vector3.ProjectOnPlane(Vector3.up, aimDir).normalized;
            return Quaternion.LookRotation(aimDir, up) * Quaternion.Inverse(bowAxes);
        }

        float DrawDistance()
        {
            // where the string hand should be for the current gameplay phase
            float d = player.BowDraw;
            return Mathf.Lerp(braceLength, fullDrawLength, d);
        }

        // ------------------------------------------------------------------ IK
        void OnAnimatorIK(int layerIndex)
        {
            if (player == null || weight <= 0.001f)
            {
                animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
                animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
                animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);
                animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
                return;
            }
            Vector3 dir = AimDir;
            Quaternion q = AimRot;
            Vector3 a = Anchor;
            Vector3 rest = a + dir * bowReach;
            Quaternion bowRot = BowWorldRot(dir);
            Vector3 grip = rest - bowRot * restLocal;                      // bow origin = grip
            Vector3 nock = rest - dir * DrawDistance();

            // release follow-through: string hand flies back, bow hand pushes through
            nock += q * new Vector3(0.05f, 0.02f, -0.16f) * releaseKick;
            grip += dir * 0.025f * releaseKick;

            // reaching over the shoulder for a fresh arrow
            float reach = player.CurrentBowPhase == BowPhase.Renock ? Mathf.Sin(Mathf.Clamp01(player.BowPhaseProgress) * Mathf.PI) : 0f;
            if (reach > 0f && quiverTop)
            {
                Vector3 overShoulder = quiverTop.position + Vector3.up * 0.06f;
                if (rightShoulder) overShoulder = Vector3.Lerp(overShoulder, rightShoulder.position + Vector3.up * 0.22f, 0.35f);
                nock = Vector3.Lerp(nock, overShoulder, Ease.InOutCubic(reach));
            }

            float w = weight;
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, w);
            animator.SetIKPosition(AvatarIKGoal.LeftHand, grip + q * bowHandOffset);
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, w);
            animator.SetIKPosition(AvatarIKGoal.RightHand, nock + q * drawHandOffset);
            animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, w * 0.75f);
            animator.SetIKHintPosition(AvatarIKHint.LeftElbow, a + q * leftElbowHint);
            animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, w * 0.75f);
            animator.SetIKHintPosition(AvatarIKHint.RightElbow, a + q * rightElbowHint);
        }

        // ------------------------------------------------------------------ public hooks
        /// <summary>String snap + follow-through when an arrow is loosed.</summary>
        public void OnRelease(float power)
        {
            releaseKick = Mathf.Lerp(0.5f, 1f, power);
            stringBuzz = Mathf.Lerp(0.5f, 1f, power);
            buzzAxis = Random.value > 0.5f ? Vector3.right : Vector3.left;
        }

        // ------------------------------------------------------------------ presentation
        void LateUpdate()
        {
            if (player == null || bow == null) return;
            float dt = Time.deltaTime;
            bool raised = player.BowRaised;
            weightLinear = Mathf.MoveTowards(weightLinear, raised ? 1f : 0f, dt / Mathf.Max(0.01f, raised ? raiseBlend : lowerBlend));
            weight = raised ? Ease.OutCubic(weightLinear) : Ease.InOutCubic(weightLinear);
            releaseKick = Mathf.MoveTowards(releaseKick, 0f, dt * 5f);
            stringBuzz = Mathf.MoveTowards(stringBuzz, 0f, dt * 3.2f);

            // club <-> back slot
            if (raised && !clubOnBack) SetClubOnBack(true);
            else if (!raised && clubOnBack && weight <= 0.001f) SetClubOnBack(false);

            // bow pose: back slot blended toward the left fist
            Vector3 backPos = bowBackSocket ? bowBackSocket.position : bow.position;
            Quaternion backRot = bowBackSocket ? bowBackSocket.rotation : bow.rotation;
            if (weight > 0.001f)
            {
                Vector3 dir = AimDir;
                Quaternion handRot = BowWorldRot(dir);
                Vector3 handPos = leftHand.position - AimRot * bowHandOffset; // grip follows the solved fist
                float k = Ease.OutCubic(Mathf.Clamp01(weight * 1.15f));
                bow.SetPositionAndRotation(Vector3.Lerp(backPos, handPos, k), Quaternion.Slerp(backRot, handRot, k));
            }
            else bow.SetPositionAndRotation(backPos, backRot);

            // nock point: on the string at rest, in the drawing hand while an arrow is drawn
            Vector3 topR = bow.TransformPoint(topRest), botR = bow.TransformPoint(botRest);
            Vector3 restMid = (topR + botR) * 0.5f;
            Vector3 bowFwd = bow.rotation * bowAxes * Vector3.forward;
            bool drawing = weight > 0.5f && player.ArrowNocked &&
                           (player.CurrentBowPhase == BowPhase.Draw || player.CurrentBowPhase == BowPhase.Raise);
            Vector3 nock = restMid;
            if (drawing)
            {
                Vector3 handNock = rightHand.position - AimRot * drawHandOffset;
                // only pull backwards along the arrow line; never push the string through the bow
                float pull = Vector3.Dot(arrowRest.position - handNock, bowFwd);
                nock = pull > braceLength ? handNock : restMid;
            }
            float measured = Vector3.Dot(arrowRest.position - nock, bowFwd);
            VisualDraw = Mathf.Clamp01(Mathf.InverseLerp(braceLength, fullDrawLength, measured));
            if (drawShape >= 0) bowRenderer.SetBlendShapeWeight(drawShape, VisualDraw * 100f);

            Vector3 top = bow.TransformPoint(Vector3.Lerp(topRest, topDrawn, VisualDraw));
            Vector3 bot = bow.TransformPoint(Vector3.Lerp(botRest, botDrawn, VisualDraw));
            if (!drawing)
            {
                nock = (top + bot) * 0.5f;
                if (stringBuzz > 0f)
                {
                    float buzz = Mathf.Sin(Time.time * 140f) * stringBuzz * stringBuzz * 0.03f;
                    nock += bow.rotation * bowAxes * (buzzAxis * buzz + Vector3.forward * buzz * 0.6f);
                }
            }
            NockWorld = nock;
            if (bowString)
            {
                bowString.SetPosition(0, top);
                bowString.SetPosition(1, nock);
                bowString.SetPosition(2, bot);
            }

            UpdateNockedArrow(drawing, nock);
        }

        void UpdateNockedArrow(bool drawing, Vector3 nock)
        {
            if (!nockedArrow) return;
            var phase = player.CurrentBowPhase;
            bool inHand = weight > 0.3f && phase == BowPhase.Renock && player.BowPhaseProgress > 0.45f;
            bool show = (drawing || inHand) && weight > 0.3f;
            if (nockedArrow.gameObject.activeSelf != show) nockedArrow.gameObject.SetActive(show);
            if (!show) return;
            if (drawing)
            {
                Vector3 dir = (arrowRest.position - nock);
                if (dir.sqrMagnitude < 1e-4f) dir = AimDir;
                nockedArrow.SetPositionAndRotation(nock, Quaternion.LookRotation(dir.normalized, bow.rotation * bowAxes * Vector3.up));
            }
            else
            {
                // fresh arrow travelling from the quiver to the string, swinging into line
                float t = Mathf.InverseLerp(0.45f, 1f, player.BowPhaseProgress);
                Vector3 pos = rightHand.position - AimRot * drawHandOffset;
                Quaternion from = Quaternion.LookRotation(Vector3.up * 0.7f + AimDir * 0.3f);
                Quaternion to = Quaternion.LookRotation(AimDir);
                nockedArrow.SetPositionAndRotation(pos, Quaternion.Slerp(from, to, Ease.OutCubic(t)));
            }
        }

        void SetClubOnBack(bool onBack)
        {
            clubOnBack = onBack;
            if (!club) return;
            if (onBack && clubBackSocket)
            {
                club.SetParent(clubBackSocket, false);
                club.localPosition = Vector3.zero;
                club.localRotation = Quaternion.identity;
            }
            else if (!onBack && clubHandParent)
            {
                club.SetParent(clubHandParent, false);
                club.localPosition = clubLocalPos;
                club.localRotation = clubLocalRot;
            }
        }
    }
}
