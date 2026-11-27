using ElderTree.Combat;
using UnityEngine;

namespace ElderTree.Enemies
{
    /// <summary>
    /// Sits on a Druid hurtbox that matters on its own (a root foot, the face) so the boss knows what was struck.
    /// <see cref="Hurtbox"/> picks this up as the owner (it is the nearest IDamageable), and everything is forwarded
    /// to the <see cref="DruidBoss"/>. The face reports itself staggered while the Druid lies toppled, so blows to it
    /// land as criticals.
    /// </summary>
    public class DruidPart : MonoBehaviour, IDamageable, IStaggerable
    {
        public DruidBoss boss;
        public DruidBoss.Part part;

        public bool IsAlive => boss != null && boss.IsAlive;
        public Transform Root => boss != null ? boss.transform : transform;
        public bool IsStaggered => boss != null && boss.PartIsCritical(part);
        public bool ReceiveHit(in DamageInfo info)
        {
            if (boss == null) return false;
            if (Coop.ForwardBossHit(boss, this, info)) return true;     // co-op client: the host's Druid takes it
            boss.NoteThreat(info);
            return boss.ReceivePartHit(info, part);
        }
    }
}
