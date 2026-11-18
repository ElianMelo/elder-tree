using TungSahur.Audio;
using TungSahur.Combat;
using UnityEngine;

namespace TungSahur.CameraSystem
{
    /// <summary>
    /// Target acquisition and switching. Picks the target closest to screen centre (weighted by
    /// distance) with line of sight; switching picks the nearest target on screen in the
    /// requested direction. Breaks on distance or prolonged loss of sight.
    /// </summary>
    public class LockOnSystem : MonoBehaviour
    {
        public Transform eye;                 // player head height reference
        public float acquireRange = 20f;
        public float breakRange = 26f;
        public float losBreakTime = 1.4f;
        public LayerMask occluders = 1;

        public Targetable Current { get; private set; }
        public event System.Action<Targetable> Changed;

        float losLostTime;
        Camera cam;

        Camera Cam => cam != null ? cam : (cam = Camera.main);

        /// <summary>Dev / scripted: lock straight onto a target.</summary>
        public void ForceLock(Targetable t) => Set(t);

        public bool Toggle()
        {
            if (Current != null)
            {
                Set(null);
                AudioManager.Play2D(Sfx.LockOff, 0.6f);
                return false;
            }
            var best = FindBest();
            if (best != null)
            {
                Set(best);
                AudioManager.Play2D(Sfx.LockOn);
                return true;
            }
            return false;
        }

        public void Switch(int dir)
        {
            if (Current == null || dir == 0 || Cam == null) return;
            Vector3 cur = Cam.WorldToViewportPoint(Current.LockPosition);
            Targetable best = null;
            float bestScore = float.MaxValue;
            foreach (var t in Targetable.All)
            {
                if (t == Current || !t.CanBeTargeted) continue;
                if (!Valid(t, acquireRange)) continue;
                Vector3 vp = Cam.WorldToViewportPoint(t.LockPosition);
                if (vp.z <= 0) continue;
                float dx = vp.x - cur.x;
                if (Mathf.Sign(dx) != dir || Mathf.Abs(dx) < 0.01f) continue;
                float score = Mathf.Abs(dx) + Mathf.Abs(vp.y - cur.y) * 0.5f;
                if (score < bestScore) { bestScore = score; best = t; }
            }
            if (best != null)
            {
                Set(best);
                AudioManager.Play2D(Sfx.LockOn, 0.7f, 1.12f);
            }
        }

        public void ForceRelease() => Set(null);

        /// <summary>Could this target be held right now (alive, targetable, inside the break range)?</summary>
        public bool CanHold(Targetable t) =>
            t != null && t.CanBeTargeted && eye != null && Vector3.Distance(eye.position, t.LockPosition) <= breakRange * t.rangeScale;

        void Set(Targetable t)
        {
            if (Current == t) return;
            Current = t;
            losLostTime = 0f;
            Changed?.Invoke(t);
        }

        Targetable FindBest()
        {
            if (Cam == null) return null;
            Targetable best = null;
            float bestScore = float.MaxValue;
            foreach (var t in Targetable.All)
            {
                if (!t.CanBeTargeted || !Valid(t, acquireRange)) continue;
                Vector3 vp = Cam.WorldToViewportPoint(t.LockPosition);
                if (vp.z <= 0) continue;
                float off = new Vector2(vp.x - 0.5f, (vp.y - 0.5f) * 0.6f).magnitude;
                bool onScreen = vp.x > -0.05f && vp.x < 1.05f && vp.y > -0.05f && vp.y < 1.05f;
                float dist = Vector3.Distance(eye.position, t.LockPosition);
                // off-screen targets are allowed only if very close (souls games pick up enemies right beside you)
                if (!onScreen && dist > 4f) continue;
                float score = off * 2f + dist * 0.04f + (onScreen ? 0 : 1.5f);
                if (score < bestScore) { bestScore = score; best = t; }
            }
            return best;
        }

        bool Valid(Targetable t, float range)
        {
            Vector3 from = eye.position;
            Vector3 to = t.LockPosition;
            range *= t.rangeScale;
            if ((to - from).sqrMagnitude > range * range) return false;
            return HasLOS(from, to, t);
        }

        bool HasLOS(Vector3 from, Vector3 to, Targetable t)
        {
            Vector3 d = to - from;
            if (Physics.Raycast(from, d.normalized, out var hit, d.magnitude - t.lockRadius, occluders, QueryTriggerInteraction.Ignore))
                return hit.transform.IsChildOf(t.transform);
            return true;
        }

        void Update()
        {
            if (Current == null) return;
            if (!Current.CanBeTargeted)
            {
                // ER behaviour: hop to the next valid target, else release
                var prev = Current;
                Set(null);
                var next = FindBest();
                if (next != null && next != prev) Set(next);
                return;
            }
            float dist = Vector3.Distance(eye.position, Current.LockPosition);
            if (dist > breakRange * Current.rangeScale) { Set(null); AudioManager.Play2D(Sfx.LockOff, 0.5f); return; }
            if (!HasLOS(eye.position, Current.LockPosition, Current))
            {
                losLostTime += Time.deltaTime;
                if (losLostTime > losBreakTime) { Set(null); AudioManager.Play2D(Sfx.LockOff, 0.5f); }
            }
            else losLostTime = 0f;
        }
    }
}
