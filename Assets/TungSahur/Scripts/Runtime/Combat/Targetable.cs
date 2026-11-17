using System.Collections.Generic;
using UnityEngine;

namespace TungSahur.Combat
{
    /// <summary>Anything the lock-on system can target.</summary>
    public class Targetable : MonoBehaviour
    {
        public static readonly List<Targetable> All = new();

        [Tooltip("Where the reticle sits and the camera aims.")]
        public Transform lockPoint;
        public float lockRadius = 0.5f;
        public string displayName = "Target";
        [Tooltip("Multiplies the lock-on acquire / break range (big flying bosses).")]
        public float rangeScale = 1f;

        public Vector3 LockPosition => lockPoint ? lockPoint.position : transform.position + Vector3.up;
        public IDamageable Damageable { get; private set; }
        public bool CanBeTargeted => isActiveAndEnabled && (Damageable == null || Damageable.IsAlive);

        void Awake() => Damageable = GetComponent<IDamageable>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
    }
}
