using System.Collections;
using System.Collections.Generic;
using ElderTree.Audio;
using ElderTree.CameraSystem;
using ElderTree.Combat;
using ElderTree.Enemies;
using ElderTree.FX;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree.Net
{
    /// <summary>
    /// Co-op inside a stage (made by <see cref="CoopSession"/> when a stage loads during a session).
    /// <list type="bullet">
    /// <item>Your tree: its transform, state and every bone go out 20 times a second.</item>
    /// <item>The other players' trees: stripped copies of yours, posed from what they send. Bosses hunt and strike them
    /// on the host; a blow that lands is sent to its owner, who judges it against their own i-frames and guard.</item>
    /// <item>Bosses: the host runs them and streams each rig, its numbers and its quarry; clients pose a mirror and
    /// send the host the blows they land. What a boss throws (bombs, walkers, motes, light columns) is mirrored as
    /// ghosts under the same pool root; blows on a walker go to the host too.</item>
    /// <item>Effects and sounds each machine plays for its own tree (and the host for its bosses) are echoed.</item>
    /// </list>
    /// </summary>
    public class NetStage : MonoBehaviour
    {
        public static NetStage Instance { get; private set; }

        const float PlayerRate = 1f / 20f, BossRate = 1f / 20f, BossStateRate = 1f / 10f;

        CoopSession cs;
        PlayerController local;
        BossEncounter enc;
        PoseStream localPose;
        readonly NetWriter w = new();
        readonly List<byte[]> chunks = new();
        float playerAt, bossAt, bossStateAt;
        bool ready;

        class Remote
        {
            public int id;
            public PlayerController pc;
            public PoseStream pose;
            public Vector3 pos, vel;
            public float yaw;
            public bool has;
        }
        readonly Dictionary<int, Remote> remotes = new();

        class BossLink
        {
            public int index;
            public BossBase boss;
            public PoseStream body;
            public GameObject[] prefabs;
            public Transform poolRoot;
            public PoseStream pool;
            public int poolCount = -1;
            public float manifestAt;
            public DruidPart[] parts;
        }
        readonly List<BossLink> bosses = new();

        public void Init(CoopSession session)
        {
            cs = session;
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var r in remotes.Values) if (r.pc != null) Destroy(r.pc.gameObject);
            remotes.Clear();
            foreach (var m in Coop.Party) m.avatar = null;
        }

        IEnumerator Start()
        {
            yield return null;                    // the stage's own Start()s run first (encounter roster, HUD)
            local = PlayerController.Instance;
            enc = BossEncounter.Instance;
            if (local != null)
            {
                local.NetId = cs.LocalId;
                var me = Coop.Local;
                if (me != null) me.avatar = local;
                PlaceLocal();
                localPose = new PoseStream(local.transform, false);
            }
            if (enc != null)
            {
                var roster = enc.Roster;
                for (int i = 0; i < roster.Count; i++)
                {
                    var b = roster[i];
                    if (b == null) continue;
                    var link = new BossLink
                    {
                        index = i, boss = b,
                        body = new PoseStream(b.transform, true),
                        prefabs = b.NetPoolPrefabs,
                        parts = b.GetComponentsInChildren<DruidPart>(true),
                    };
                    bosses.Add(link);
                    if (Coop.IsHost)
                    {
                        b.Damaged += (boss, info, broke) => SendBossEvent(BossEv.Damaged, boss, ww => { ww.Float(info.damage); ww.Bool(broke); ww.Vec3(info.point); });
                        b.Felled += boss => SendBossEvent(BossEv.Felled, boss, null);
                    }
                }
                if (Coop.IsHost)
                {
                    enc.Spawned += (b, afterIntro) => SendBossEvent(BossEv.Spawn, b, ww => ww.Bool(afterIntro));
                    enc.FightEnded += keep => SendBossEvent(BossEv.EndFight, null, ww => ww.Bool(keep));
                    enc.Wiped += () => SendBossEvent(BossEv.Wipe, null, null);
                }
            }
            OnRosterChanged();
            ready = true;
            cs.ReportLoaded();
        }

        /// <summary>Each player at their own spot by the grace.</summary>
        void PlaceLocal()
        {
            Transform sp = enc != null && enc.spawnPoint != null ? enc.spawnPoint : null;
            Vector3 p = sp != null ? sp.position : local.transform.position;
            Quaternion r = sp != null ? sp.rotation : local.transform.rotation;
            local.motor.Teleport(p + BossEncounter.SpawnOffset(r), r);
            CameraDirector.Instance?.Recenter();
        }

        // ================================================================== remote trees
        /// <summary>The party changed: make a tree for each new player, clear away the ones who left.</summary>
        public void OnRosterChanged()
        {
            if (local == null) return;
            var keep = new HashSet<int>();
            foreach (var m in Coop.Party)
            {
                if (m.isLocal) { m.avatar = local; continue; }
                keep.Add(m.id);
                if (!remotes.TryGetValue(m.id, out var r) || r.pc == null)
                {
                    r = new Remote { id = m.id, pc = SpawnAvatar(m) };
                    r.pose = new PoseStream(r.pc.transform, false);
                    remotes[m.id] = r;
                }
                m.avatar = r.pc;
                r.pc.name = "Coop Tree (" + m.name + ")";
            }
            var gone = new List<int>();
            foreach (var kv in remotes) if (!keep.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone)
            {
                if (remotes[id].pc != null) Destroy(remotes[id].pc.gameObject);
                remotes.Remove(id);
            }
        }

        /// <summary>A copy of your own tree with everything that thinks taken out: posed from the network only.</summary>
        PlayerController SpawnAvatar(Coop.Member m)
        {
            var holder = new GameObject("Coop Avatar Holder");
            holder.SetActive(false);
            var go = Instantiate(local.gameObject, holder.transform);
            go.tag = "Untagged";
            // strip everything that reads input, drives the camera, or animates (the bones come from the owner)
            for (int pass = 0; pass < 3; pass++)
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null || mb is PlayerController || mb is PlayerMotor || mb is PlayerStats) continue;
                    DestroyImmediate(mb);
                }
            foreach (var a in go.GetComponentsInChildren<Animator>(true)) a.enabled = false;
            foreach (var l in go.GetComponentsInChildren<AudioListener>(true)) DestroyImmediate(l);
            var cc = go.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            // leaving the inactive holder wakes the copy up: it must know it is a remote tree by then
            PlayerController.SpawningRemote = true;
            try
            {
                go.transform.SetParent(null, true);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, local.gameObject.scene);
                if (!go.activeSelf) go.SetActive(true);
            }
            finally { PlayerController.SpawningRemote = false; }
            Destroy(holder);
            var pc = go.GetComponent<PlayerController>();
            pc.NetId = m.id;
            return pc;
        }

        // ================================================================== tick
        void Update()
        {
            if (!ready) return;
            float now = Time.unscaledTime;
            if (local != null && now - playerAt >= PlayerRate)
            {
                playerAt = now;
                SendPlayer();
            }
            if (Coop.IsHost)
            {
                if (now - bossAt >= BossRate) { bossAt = now; SendBossPoses(); }
                if (now - bossStateAt >= BossStateRate) { bossStateAt = now; SendBossStates(); }
            }
            // the local member's numbers (the party HUD reads them)
            var me = Coop.Local;
            if (me != null && local != null)
            {
                me.health01 = local.stats.maxHealth > 0f ? local.stats.Health / local.stats.maxHealth : 0f;
                me.dead = local.IsDead;
            }
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            float k = 1f - Mathf.Exp(-16f * dt);
            foreach (var r in remotes.Values)
            {
                if (r.pc == null || !r.has) continue;
                var t = r.pc.transform;
                Vector3 target = r.pos + r.vel * 0.05f;         // a touch of lead hides the interpolation lag
                if ((t.position - target).sqrMagnitude > 25f) t.position = target;
                else t.position = Vector3.Lerp(t.position, target, k);
                t.rotation = Quaternion.Slerp(t.rotation, Quaternion.Euler(0f, r.yaw, 0f), k);
                r.pose.Apply(dt);
            }
            if (Coop.IsClient)
                foreach (var b in bosses)
                {
                    b.body.Apply(dt);
                    b.pool?.Apply(dt);
                }
        }

        // ================================================================== your tree
        void SendPlayer()
        {
            local.ReadNetState(out var state, out bool invuln, out bool grounded, out Vector3 vel);
            w.Reset();
            w.Vec3(local.transform.position);
            w.Float(local.transform.eulerAngles.y);
            w.HalfVec3(vel);
            w.Byte((byte)state);
            byte flags = 0;
            if (invuln) flags |= 1;
            if (grounded) flags |= 2;
            w.Byte(flags);
            w.Unit(local.stats.maxHealth > 0f ? local.stats.Health / local.stats.maxHealth : 0f);
            cs.Send(Msg.PlayerState, w.ToArray(), false);

            chunks.Clear();
            localPose.WriteChunks(chunks, _ => { });
            foreach (var c in chunks) cs.Send(Msg.PlayerPose, c, false);
        }

        void ReadPlayerState(int from, ref NetReader r)
        {
            if (!remotes.TryGetValue(from, out var rm) || rm.pc == null) return;
            rm.pos = r.Vec3();
            rm.yaw = r.Float();
            rm.vel = r.HalfVec3();
            var state = (PlayerState)r.Byte();
            byte flags = r.Byte();
            float hp = r.Unit();
            if (!rm.has) { rm.pc.transform.SetPositionAndRotation(rm.pos, Quaternion.Euler(0f, rm.yaw, 0f)); rm.has = true; }
            rm.pc.ApplyRemoteState(state, (flags & 1) != 0, (flags & 2) != 0, rm.vel, hp);
            var m = Coop.Find(from);
            if (m != null) { m.health01 = hp; m.dead = state == PlayerState.Dead; }
        }

        // ================================================================== bosses (host)
        void SendBossPoses()
        {
            foreach (var b in bosses)
            {
                byte bi = (byte)b.index;
                chunks.Clear();
                b.body.WriteChunks(chunks, ww => { ww.Byte(bi); ww.Byte(0); });
                foreach (var c in chunks) cs.Send(Msg.BossPose, c, false);

                // what it threw: the pool's make-up first (so clients can make the ghosts), then their poses
                var root = b.boss.NetPoolRoot(false);
                if (root == null) continue;
                if (b.poolRoot != root) { b.poolRoot = root; b.pool = null; }
                if (b.pool == null || root.childCount != b.poolCount)
                {
                    b.poolCount = root.childCount;
                    b.pool = new PoseStream(root, false);
                    b.manifestAt = -99f;
                }
                if (Time.unscaledTime - b.manifestAt > 2f) SendManifest(b);
                chunks.Clear();
                b.pool.WriteChunks(chunks, ww => { ww.Byte(bi); ww.Byte(2); });
                foreach (var c in chunks) cs.Send(Msg.BossPose, c, false);
            }
        }

        void SendManifest(BossLink b)
        {
            b.manifestAt = Time.unscaledTime;
            w.Reset();
            w.Byte((byte)b.index);
            w.Byte(1);
            int n = Mathf.Min(b.poolRoot.childCount, 250);
            w.Byte((byte)n);
            for (int i = 0; i < n; i++) w.Byte((byte)KindOf(b, b.poolRoot.GetChild(i).name));
            cs.Send(Msg.BossPose, w.ToArray(), true);
        }

        static int KindOf(BossLink b, string childName)
        {
            if (b.prefabs == null) return 255;
            for (int k = 0; k < b.prefabs.Length; k++)
                if (b.prefabs[k] != null && childName.StartsWith(b.prefabs[k].name)) return k;
            return 255;
        }

        void SendBossStates()
        {
            foreach (var b in bosses)
            {
                var boss = b.boss;
                w.Reset();
                w.Byte((byte)b.index);
                byte f = 0;
                if (boss.IsAlive) f |= 1;
                if (boss.IsDead) f |= 2;
                if (boss.NetCritical) f |= 4;
                if (boss.PhaseTwo) f |= 8;
                w.Byte(f);
                w.Float(boss.Health);
                w.Float(boss.MaxHealth);
                w.Half(boss.Poise);
                w.Str(boss.CurrentMove);
                w.Str(boss.StateLabel);
                var a = boss.gameObject.activeInHierarchy && boss.IsAlive ? boss.Aggro : null;
                w.Int(a != null ? a.NetId : -1);
                cs.Send(Msg.BossState, w.ToArray(), false);
            }
        }

        void SendBossEvent(BossEv ev, BossBase b, System.Action<NetWriter> payload)
        {
            w.Reset();
            w.Byte((byte)ev);
            w.Byte(b != null && enc != null ? (byte)enc.IndexOf(b) : (byte)255);
            payload?.Invoke(w);
            cs.Send(Msg.BossEvent, w.ToArray(), true);
        }

        // ================================================================== bosses (client)
        BossLink Link(int index)
        {
            foreach (var b in bosses) if (b.index == index) return b;
            return null;
        }

        void ReadBossPose(ref NetReader r)
        {
            var b = Link(r.Byte());
            int sub = r.Byte();
            if (b == null || Coop.IsHost) return;
            switch (sub)
            {
                case 0: b.body.Read(ref r); break;
                case 1: ReadManifest(b, ref r); break;
                case 2: b.pool?.Read(ref r); break;
            }
        }

        /// <summary>Make ghosts so the pool under the mirror matches the host's, one for one.</summary>
        void ReadManifest(BossLink b, ref NetReader r)
        {
            int n = r.Byte();
            var root = b.boss.NetPoolRoot(true);
            if (root == null) return;
            bool changed = b.poolRoot != root || b.pool == null;
            b.poolRoot = root;
            for (int i = root.childCount; i < n; i++)
            {
                int kind = r.Byte();
                var prefab = b.prefabs != null && kind < b.prefabs.Length ? b.prefabs[kind] : null;
                MakeGhost(prefab, root);
                changed = true;
            }
            if (changed) b.pool = new PoseStream(root, false);
        }

        static void MakeGhost(GameObject prefab, Transform parent)
        {
            var holder = new GameObject("Ghost Holder");
            holder.SetActive(false);
            GameObject g;
            if (prefab != null)
            {
                g = Instantiate(prefab, holder.transform);
                g.name = prefab.name + "(Clone)";
                foreach (var mb in g.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb is Targetable || mb is Hurtbox) continue;
                    if (mb is LivingBomb lb) lb.Ghost = true;
                    mb.enabled = false;
                }
            }
            else g = new GameObject("Unknown(Clone)");     // keeps the indices in step even if a prefab is missing
            g.transform.SetParent(parent, false);
            Destroy(holder);
        }

        void ReadBossState(ref NetReader r)
        {
            var b = Link(r.Byte());
            byte f = r.Byte();
            float hp = r.Float(), max = r.Float(), poise = r.Half();
            string move = r.Str(), label = r.Str();
            int aggro = r.Int();
            if (b == null || b.boss.Mirror == null) return;
            var m = b.boss.Mirror;
            m.alive = (f & 1) != 0;
            m.dead = (f & 2) != 0;
            m.staggered = (f & 4) != 0;
            m.phaseTwo = (f & 8) != 0;
            m.health = hp;
            m.maxHealth = Mathf.Max(1f, max);
            m.poise = poise;
            m.move = move;
            m.label = label;
            m.aggroId = aggro;
        }

        void ReadBossEvent(ref NetReader r)
        {
            var ev = (BossEv)r.Byte();
            var b = Link(r.Byte());
            if (enc == null) return;
            switch (ev)
            {
                case BossEv.Spawn: if (b != null) enc.MirrorSpawn(b.boss); break;
                case BossEv.Felled:
                    if (b != null && b.boss.Mirror != null)
                    {
                        b.boss.Mirror.alive = false;
                        b.boss.Mirror.dead = true;
                        b.boss.Mirror.health = 0f;
                        b.boss.MirrorFelled();
                    }
                    break;
                case BossEv.EndFight: enc.MirrorEndFight(r.Bool()); break;
                case BossEv.Wipe: enc.RunWipe(); break;
                case BossEv.Damaged:
                    if (b != null)
                    {
                        float dmg = r.Float();
                        bool broke = r.Bool();
                        Vector3 p = r.Vec3();
                        b.boss.MirrorDamaged(new DamageInfo { damage = dmg, point = p }, broke);
                    }
                    break;
            }
        }

        // ================================================================== hits
        static void WriteHit(NetWriter w, in DamageInfo i)
        {
            w.Float(i.damage);
            w.Float(i.poiseDamage);
            w.Vec3(i.point);
            w.HalfVec3(i.direction);
            w.Byte((byte)i.weight);
            byte f = 0;
            if (i.charged) f |= 1;
            if (i.isSlamWave) f |= 2;
            if (i.critical) f |= 4;
            if (i.explosive) f |= 8;
            w.Byte(f);
            w.Byte((byte)Mathf.Clamp(i.breakPower, 0, 255));
            w.Byte((byte)Mathf.Clamp(i.cutPower, 0, 255));
            w.Half(i.cutWidth);
        }

        static DamageInfo ReadHit(ref NetReader r, GameObject source)
        {
            var i = new DamageInfo
            {
                damage = r.Float(),
                poiseDamage = r.Float(),
                point = r.Vec3(),
                direction = r.HalfVec3(),
                weight = (ImpactWeight)r.Byte(),
            };
            byte f = r.Byte();
            i.charged = (f & 1) != 0;
            i.isSlamWave = (f & 2) != 0;
            i.critical = (f & 4) != 0;
            i.explosive = (f & 8) != 0;
            i.breakPower = r.Byte();
            i.cutPower = r.Byte();
            i.cutWidth = r.Half();
            i.source = source;
            return i;
        }

        int HostId
        {
            get { foreach (var m in Coop.Party) if (m.isHost) return m.id; return -1; }
        }

        /// <summary>Client: a blow on the mirrored boss (or one of its parts) goes to the host.</summary>
        public bool SendBossHit(BossBase boss, Component part, in DamageInfo info)
        {
            var b = bosses.Find(x => x.boss == boss);
            if (b == null) return false;
            int partIndex = 255;
            if (part is DruidPart dp && b.parts != null) partIndex = System.Array.IndexOf(b.parts, dp);
            w.Reset();
            w.Byte((byte)b.index);
            w.Byte((byte)(partIndex < 0 ? 255 : partIndex));
            WriteHit(w, info);
            cs.Send(Msg.HitBoss, w.ToArray(), true, HostId);
            return true;
        }

        /// <summary>Client: a blow on a ghost (a walker) goes to the host's real one.</summary>
        public bool SendHazardHit(Component hazard, in DamageInfo info)
        {
            foreach (var b in bosses)
            {
                if (b.poolRoot == null || hazard.transform.parent != b.poolRoot) continue;
                w.Reset();
                w.Byte((byte)b.index);
                w.Byte((byte)hazard.transform.GetSiblingIndex());
                WriteHit(w, info);
                cs.Send(Msg.HitHazard, w.ToArray(), true, HostId);
                return true;
            }
            return false;
        }

        /// <summary>Host: a boss struck another player's tree. Its owner takes the blow.</summary>
        public void SendPlayerHit(PlayerController remote, in DamageInfo info)
        {
            if (remote == null || remote.NetId < 0) return;
            w.Reset();
            WriteHit(w, info);
            cs.Send(Msg.HitPlayer, w.ToArray(), true, remote.NetId);
        }

        GameObject AvatarOf(int id) => remotes.TryGetValue(id, out var r) && r.pc != null ? r.pc.gameObject : null;

        void ReadHitBoss(int from, ref NetReader r)
        {
            var b = Link(r.Byte());
            int part = r.Byte();
            var info = ReadHit(ref r, AvatarOf(from));
            if (b == null || !Coop.IsHost) return;
            if (part != 255 && b.parts != null && part < b.parts.Length && b.parts[part] != null) b.parts[part].ReceiveHit(info);
            else b.boss.ReceiveHit(info);
        }

        void ReadHitHazard(int from, ref NetReader r)
        {
            var b = Link(r.Byte());
            int child = r.Byte();
            var info = ReadHit(ref r, AvatarOf(from));
            if (b == null || !Coop.IsHost) return;
            var root = b.boss.NetPoolRoot(false);
            if (root == null || child >= root.childCount) return;
            root.GetChild(child).GetComponent<IDamageable>()?.ReceiveHit(info);
        }

        void ReadHitPlayer(ref NetReader r)
        {
            var info = ReadHit(ref r, null);
            if (local != null) local.TakeHit(info);
        }

        // ================================================================== effects
        public void SendFx(FXType type, Vector3 pos, Quaternion rot, float scale)
        {
            if (!ready) return;
            w.Reset();
            w.Byte((byte)type);
            w.Vec3(pos);
            w.Quat(rot);
            w.Half(scale);
            cs.Send(Msg.Fx, w.ToArray(), false);
        }

        public void SendSfx(Sfx id, Vector3 pos, float volume, float pitch)
        {
            if (!ready) return;
            w.Reset();
            w.Byte((byte)id);
            w.Vec3(pos);
            w.Unit(Mathf.Clamp01(volume));
            w.Half(pitch);
            cs.Send(Msg.Sfx, w.ToArray(), false);
        }

        public void SendBlast(Vector3 center, float radius, float fxScale)
        {
            if (!ready) return;
            w.Reset();
            w.Vec3(center);
            w.Half(radius);
            w.Half(fxScale);
            cs.Send(Msg.Blast, w.ToArray(), false);
        }

        void PlayFx(ref NetReader r)
        {
            var type = (FXType)r.Byte();
            var pos = r.Vec3();
            var rot = r.Quat();
            float scale = r.Half();
            Coop.ApplyingRemote = true;
            try { FXManager.Spawn(type, pos, rot, scale); }
            finally { Coop.ApplyingRemote = false; }
        }

        void PlaySfx(ref NetReader r)
        {
            var id = (Sfx)r.Byte();
            var pos = r.Vec3();
            float vol = r.Unit(), pitch = r.Half();
            Coop.ApplyingRemote = true;
            try { AudioManager.Play(id, pos, vol, pitch); }
            finally { Coop.ApplyingRemote = false; }
        }

        void ShakeForBlast(ref NetReader r)
        {
            var c = r.Vec3();
            float radius = r.Half(), fx = r.Half();
            if (local == null) return;
            float d = Vector3.Distance(local.transform.position, c);
            float shake = Mathf.Clamp01(1.2f - d / (radius * 6f)) * 0.9f * fx;
            if (shake > 0.02f) CameraDirector.Instance?.Shake(shake, Vector3.down);
        }

        // ================================================================== dispatch
        public void Receive(Msg kind, int from, ref NetReader r)
        {
            if (!ready) return;
            switch (kind)
            {
                case Msg.PlayerState: ReadPlayerState(from, ref r); break;
                case Msg.PlayerPose:
                    if (remotes.TryGetValue(from, out var rm) && rm.pose != null) rm.pose.Read(ref r);
                    break;
                case Msg.BossPose: ReadBossPose(ref r); break;
                case Msg.BossState: if (!Coop.IsHost) ReadBossState(ref r); break;
                case Msg.BossEvent: if (!Coop.IsHost) ReadBossEvent(ref r); break;
                case Msg.HitBoss: ReadHitBoss(from, ref r); break;
                case Msg.HitHazard: ReadHitHazard(from, ref r); break;
                case Msg.HitPlayer: ReadHitPlayer(ref r); break;
                case Msg.Fx: PlayFx(ref r); break;
                case Msg.Sfx: PlaySfx(ref r); break;
                case Msg.Blast: ShakeForBlast(ref r); break;
            }
        }
    }
}
