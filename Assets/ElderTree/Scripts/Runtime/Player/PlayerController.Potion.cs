using ElderTree.Audio;
using ElderTree.FX;
using UnityEngine;

namespace ElderTree.Player
{
    /// <summary>
    /// Health potions, Elden Ring flask rules: X / R uncorks one (spent on the press), the tree keeps walking slowly
    /// while it drinks, and the health lands at the gulp. A hit before the gulp wastes the potion; after it, a roll can
    /// cut the recovery short. The arms play the pack's Drink01_L on the upper-body "Drink" layer (the right hand keeps
    /// the weapon), and a small low-poly flask appears in the left hand for the drink.
    /// </summary>
    public partial class PlayerController
    {
        [Header("Health potion")]
        [Tooltip("Playback speed of Drink01_L. 1.2 makes the whole drink ~1.3 s.")]
        public float drinkSpeed = 1.2f;
        [Tooltip("Length of HumanM@Drink01_L in seconds.")]
        public float drinkClipLength = 1.567f;
        [Tooltip("Normalized clip time of the gulp, when the health is restored (the flask is at the mouth from ~0.3 to ~0.68).")]
        [Range(0f, 1f)] public float drinkHealAt = 0.5f;
        [Tooltip("Normalized clip time from which a roll can cut the lowering of the arm.")]
        [Range(0f, 1f)] public float drinkCancelAt = 0.72f;
        [Tooltip("Normalized clip time from which any action (attack, jump, another potion) can follow.")]
        [Range(0f, 1f)] public float drinkRecoverAt = 0.86f;
        [Tooltip("Top speed while drinking, as a fraction of walk speed.")]
        public float drinkMoveFactor = 0.6f;
        [Tooltip("Flask placement in the left hand bone's space.")]
        public Vector3 flaskLocalPos = new Vector3(0.0f, 0.09f, 0.035f);
        public Vector3 flaskLocalEuler = new Vector3(0f, 0f, 0f);
        [Tooltip("World size multiplier of the flask (1 = a 13 cm flask).")]
        public float flaskScale = 1.5f;

        static readonly int HDrinkSpeed = Animator.StringToHash("DrinkSpeed");

        bool drinkHealed;
        float drinkW;
        Animator drinkLayerAnimator;
        int drinkLayer = -1;
        Transform flask;
        Transform flaskHand;
        float flaskShow;

        /// <summary>True while a potion is being drunk.</summary>
        public bool Drinking => State == PlayerState.Drink;

        float DrinkNormalized => stateTime * drinkSpeed / Mathf.Max(0.1f, drinkClipLength);

        int DrinkLayer
        {
            get
            {
                if (drinkLayerAnimator != animator)
                {
                    drinkLayerAnimator = animator;
                    drinkLayer = animator != null ? animator.GetLayerIndex("Drink") : -1;
                }
                return drinkLayer;
            }
        }

        bool TryDrink()
        {
            if (!input.ConsumeUseItem()) return false;
            if (!motor.Grounded) return false;
            if (!stats.TryUsePotion())
            {
                // empty: a dull knock, and the HUD shakes the flask slot
                AudioManager.Play(Sfx.ManaEmpty, transform.position + Vector3.up, 0.6f, 0.8f);
                return false;
            }
            EndSwing();
            IsSprinting = false;
            SetState(PlayerState.Drink);
            drinkHealed = false;
            int layer = DrinkLayer;
            if (layer >= 0)
            {
                animator.SetFloat(HDrinkSpeed, drinkSpeed);
                animator.Play("Drink", layer, 0f);
            }
            AudioManager.Play(Sfx.Guard, transform.position + Vector3.up, 0.35f, 1.25f);   // wooden cork knock
            return true;
        }

        void UpdateDrink(float dt)
        {
            if (!motor.Grounded && Time.time - lastGroundedTime > coyoteTime)
            {
                EnterAir(false);
                return;
            }

            // keep walking, slowly
            Vector3 dir = moveInputWorld.sqrMagnitude > 1e-4f ? moveInputWorld.normalized : Vector3.zero;
            float speed = Mathf.Min(DesiredSpeed(), walkSpeed * drinkMoveFactor);
            if (lockOn.Current != null) FaceTowards(lockOn.Current.transform.position, lockTurnSpeed * 0.6f, dt);
            else if (dir != Vector3.zero) RotateTowards(dir, turnSpeed * 0.4f, dt);
            motor.SetDesiredPlanarVelocity(dir * speed, speed > 0.01f ? groundSharpness : stopSharpness, dt);
            DriveLocomotionAnimator(dt);

            float n = DrinkNormalized;
            if (!drinkHealed && n >= drinkHealAt)
            {
                drinkHealed = true;
                stats.Heal(stats.potionHeal);
                Vector3 chest = transform.position + Vector3.up * 1.3f;
                AudioManager.Play(Sfx.PotionDrink, chest, 0.9f);
                FXManager.Spawn(FXType.LeafBurst, chest, Quaternion.LookRotation(Vector3.up), 0.7f);
                input.Rumble(0.15f, 0.35f, 0.18f);
            }

            if (!input.GameplayEnabled) { if (n >= 1f) SetState(PlayerState.Locomotion); return; }
            if (n >= drinkCancelAt && input.HasBufferedDodge && TryDodge()) return;
            if (n >= drinkRecoverAt && TryStartActionFromNeutral()) return;
            if (n >= 1f) SetState(PlayerState.Locomotion);
        }

        /// <summary>The upper-body drink layer fades in for the drink and out after it (or when a hit cuts it).</summary>
        void UpdateDrinkLayer(float dt)
        {
            bool on = State == PlayerState.Drink && DrinkNormalized < 0.97f;
            drinkW = Mathf.MoveTowards(drinkW, on ? 1f : 0f, dt * (on ? 9f : 6f));
            int layer = DrinkLayer;
            if (layer >= 0) animator.SetLayerWeight(layer, Ease.InOutCubic(drinkW));
        }

        // ------------------------------------------------------------------ the flask prop
        /// <summary>LateUpdate (bones posed): the flask rides the left hand during the drink.</summary>
        void UpdateFlask(float dt)
        {
            bool show = State == PlayerState.Drink && DrinkNormalized > 0.06f && DrinkNormalized < 0.93f;
            flaskShow = Mathf.MoveTowards(flaskShow, show ? 1f : 0f, dt * 10f);
            if (flaskShow <= 0f)
            {
                if (flask != null && flask.gameObject.activeSelf) flask.gameObject.SetActive(false);
                return;
            }
            var hand = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            if (hand == null) return;
            if (flask == null) flask = BuildFlask();
            if (flaskHand != hand)
            {
                flaskHand = hand;
                flask.SetParent(hand, false);
            }
            if (!flask.gameObject.activeSelf) flask.gameObject.SetActive(true);
            flask.localPosition = flaskLocalPos;
            flask.localRotation = Quaternion.Euler(flaskLocalEuler);
            // a hand's lossy scale differs per form rig: keep the flask the same size in the world
            float s = flaskScale * Ease.OutCubic(flaskShow) / Mathf.Max(0.01f, hand.lossyScale.x);
            flask.localScale = Vector3.one * s;
        }

        static Mesh flaskMesh;
        static Material[] flaskMats;

        static Transform BuildFlask()
        {
            if (flaskMesh == null) flaskMesh = BuildFlaskMesh();
            if (flaskMats == null)
            {
                var sh = Shader.Find("ElderTree/PSXLit");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
                var liquid = new Material(sh) { name = "M_PotionLiquid" };
                liquid.SetColor("_BaseColor", new Color(0.42f, 0.04f, 0.05f));
                liquid.EnableKeyword("_EMISSION");
                liquid.SetColor("_EmissionColor", new Color(0.75f, 0.08f, 0.06f) * 1.4f);
                var cork = new Material(sh) { name = "M_PotionCork" };
                cork.SetColor("_BaseColor", new Color(0.30f, 0.21f, 0.13f));
                flaskMats = new[] { liquid, cork };
            }
            var go = new GameObject("HealthPotion");
            go.AddComponent<MeshFilter>().sharedMesh = flaskMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = flaskMats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return go.transform;
        }

        /// <summary>A 6-sided gourd flask (~13 cm): round belly, short neck, cork. Pivot at the belly's centre.</summary>
        static Mesh BuildFlaskMesh()
        {
            const int Sides = 6;
            // (height, radius) rings, bottom to top; the last two rings are the cork (submesh 1)
            var glass = new (float y, float r)[] { (-0.05f, 0f), (-0.05f, 0.03f), (-0.025f, 0.048f), (0.012f, 0.045f), (0.036f, 0.022f), (0.06f, 0.014f) };
            var cork = new (float y, float r)[] { (0.055f, 0.017f), (0.08f, 0.015f), (0.08f, 0f) };
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris0 = new System.Collections.Generic.List<int>();
            var tris1 = new System.Collections.Generic.List<int>();

            void Lathe((float y, float r)[] rings, System.Collections.Generic.List<int> tris)
            {
                for (int i = 0; i < rings.Length - 1; i++)
                {
                    var (y0, r0) = rings[i];
                    var (y1, r1) = rings[i + 1];
                    for (int s = 0; s < Sides; s++)
                    {
                        float a0 = s * Mathf.PI * 2f / Sides, a1 = (s + 1) * Mathf.PI * 2f / Sides;
                        int b = verts.Count;
                        // flat-shaded quads: own vertices per face, the PSX look
                        verts.Add(new Vector3(Mathf.Cos(a0) * r0, y0, Mathf.Sin(a0) * r0));
                        verts.Add(new Vector3(Mathf.Cos(a1) * r0, y0, Mathf.Sin(a1) * r0));
                        verts.Add(new Vector3(Mathf.Cos(a1) * r1, y1, Mathf.Sin(a1) * r1));
                        verts.Add(new Vector3(Mathf.Cos(a0) * r1, y1, Mathf.Sin(a0) * r1));
                        tris.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                    }
                }
            }
            Lathe(glass, tris0);
            // the glass top stays open under the cork; close the bottom fan via the r=0 ring above
            Lathe(cork, tris1);

            var m = new Mesh { name = "HealthPotion" };
            m.SetVertices(verts);
            m.subMeshCount = 2;
            m.SetTriangles(tris0, 0);
            m.SetTriangles(tris1, 1);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
