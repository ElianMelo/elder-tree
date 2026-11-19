using System.Collections.Generic;
using UnityEngine;

namespace ElderTree.Combat
{
    /// <summary>
    /// Swept capsule along the club. Every frame of an active window the capsule is interpolated
    /// between last frame's and this frame's pose in sub-steps, so a fast club can't tunnel
    /// through a thin target at low frame rates.
    /// </summary>
    public class WeaponHitbox : MonoBehaviour
    {
        [Tooltip("Local-space points along the club (handle end to tip).")]
        public Vector3 baseLocal = new Vector3(0, 0.12f, 0);
        public Vector3 tipLocal = new Vector3(0, 0.88f, 0);
        public float radius = 0.11f;
        public LayerMask hurtboxMask = ~0;
        public int maxSubSteps = 6;

        public bool Active { get; private set; }
        public Vector3 TipWorld => transform.TransformPoint(tipLocal);
        public Vector3 BaseWorld => transform.TransformPoint(baseLocal);

        public System.Action<IDamageable, Collider, Vector3> OnHit;
        public static bool DebugLog;

        readonly HashSet<IDamageable> hitThisSwing = new();
        readonly Collider[] buffer = new Collider[16];
        Vector3 prevBase, prevTip;
        Transform ownerRoot;

        void Awake()
        {
            // the wielder (not transform.root: scenes group actors under shared parents)
            var cc = GetComponentInParent<CharacterController>();
            ownerRoot = cc != null ? cc.transform : transform.root;
        }

        public void Begin()
        {
            Active = true;
            hitThisSwing.Clear();
            prevBase = BaseWorld;
            prevTip = TipWorld;
        }

        public void End() => Active = false;
        public bool HasHit(IDamageable d) => hitThisSwing.Contains(d);
        public void MarkHit(IDamageable d) => hitThisSwing.Add(d);

        public void Tick()
        {
            if (!Active) return;
            Vector3 b = BaseWorld, t = TipWorld;
            float travel = Mathf.Max((t - prevTip).magnitude, (b - prevBase).magnitude);
            int steps = Mathf.Clamp(Mathf.CeilToInt(travel / (radius * 1.2f)), 1, maxSubSteps);
            for (int s = 1; s <= steps; s++)
            {
                float k = s / (float)steps;
                Vector3 sb = Vector3.Lerp(prevBase, b, k), st = Vector3.Lerp(prevTip, t, k);
                int n = Physics.OverlapCapsuleNonAlloc(sb, st, radius, buffer, hurtboxMask, QueryTriggerInteraction.Collide);
                if (DebugLog) Debug.Log($"[Hitbox] f{Time.frameCount} base={sb:F2} tip={st:F2} n={n}");
                for (int i = 0; i < n; i++)
                {
                    var col = buffer[i];
                    if (col.transform.IsChildOf(ownerRoot)) continue;
                    var hb = col.GetComponent<Hurtbox>();
                    var dmg = hb != null ? hb.Owner : col.GetComponentInParent<IDamageable>();
                    if (DebugLog) Debug.Log($"[Hitbox] col={col.name} root={col.transform.root.name} hb={(hb != null)} dmg={(dmg != null)} alive={dmg?.IsAlive} already={(dmg != null && hitThisSwing.Contains(dmg))}");
                    if (dmg == null || !dmg.IsAlive || hitThisSwing.Contains(dmg)) continue;
                    hitThisSwing.Add(dmg);
                    // contact point: closest point on the collider to the swept segment midpoint
                    Vector3 mid = Vector3.Lerp(sb, st, 0.75f);
                    Vector3 p = col.ClosestPoint(mid);
                    if ((p - mid).sqrMagnitude < 1e-6f) p = mid;
                    OnHit?.Invoke(dmg, col, p);
                }
            }
            prevBase = b;
            prevTip = t;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Active ? Color.red : new Color(1, 0.6f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(BaseWorld, radius);
            Gizmos.DrawWireSphere(TipWorld, radius);
            Gizmos.DrawLine(BaseWorld, TipWorld);
        }
    }
}
