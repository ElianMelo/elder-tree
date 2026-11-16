using System.Collections.Generic;
using UnityEngine;

namespace TungSahur.Combat
{
    /// <summary>Collider marker routing hits to an IDamageable owner.</summary>
    public class Hurtbox : MonoBehaviour
    {
        public IDamageable Owner { get; private set; }

        void Awake() => Owner = GetComponentInParent<IDamageable>();
    }
}
