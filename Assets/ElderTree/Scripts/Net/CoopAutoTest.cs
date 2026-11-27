using System;
using System.Collections;
using System.IO;
using ElderTree.Combat;
using ElderTree.Enemies;
using ElderTree.Masks;
using ElderTree.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ElderTree.Net
{
    /// <summary>
    /// Plays a co-op session by itself, for testing two (or more) players on one machine. Only runs when the game is
    /// started with <c>-coopTest host|join</c>:
    /// <code>
    /// ElderTree.exe -coopTest host -coopProfile p1 -coopPlayers 2 -coopStage 1 -coopShots D:\shots
    /// ElderTree.exe -coopTest join -coopProfile p2 -coopStage 1 -coopShots D:\shots
    /// </code>
    /// The host opens a party and starts the vote once <c>-coopPlayers</c> are in; everyone votes for stage
    /// <c>-coopStage</c> (index in the stage catalog). In the stage, the joiner strikes the boss now and then (sent to
    /// the host), the host fells it after a while, each player walks to their own mask, and the party returns to the
    /// vote. Progress goes to the log as "[CoopTest]"; with <c>-coopShots</c>, screenshots are saved along the way.
    /// <c>-coopProfile</c> gives each instance its own anonymous sign-in. Add <c>-coopUdp</c> to skip DTLS.
    /// </summary>
    public class CoopAutoTest : MonoBehaviour
    {
        string role, shots;
        int players = 2, stage = 1, shotN;
        int rounds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Arg("-coopTest") == null) return;
            var go = new GameObject("Coop Auto Test");
            DontDestroyOnLoad(go);
            go.AddComponent<CoopAutoTest>();
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        static void Log(string m) => Debug.Log("[CoopTest] " + m);

        void Shot(string tag)
        {
            if (string.IsNullOrEmpty(shots)) return;
            Directory.CreateDirectory(shots);
            var path = Path.Combine(shots, $"{role}_{shotN++:00}_{tag}.png");
            ScreenCapture.CaptureScreenshot(path);
            Log("shot " + path);
        }

        IEnumerator Start()
        {
            role = Arg("-coopTest");
            shots = Arg("-coopShots");
            int.TryParse(Arg("-coopPlayers") ?? "2", out players);
            int.TryParse(Arg("-coopStage") ?? "1", out stage);
            Log($"role={role} players={players} stage={stage} udp={CoopSession.UseUdpRelay}");
            Application.runInBackground = true;
            yield return new WaitForSeconds(2f);
            var s = CoopSession.Ensure();
            FindAnyObjectByType<UI.MainMenuScreen>()?.OpenCoop();
            s.Changed += () => Log($"changed: working={s.Working} err={s.Error} phase={s.CurrentPhase} party={Coop.Party.Count} code={s.Code} pick={s.VotePick}");

            if (role == "host")
            {
                s.Host();
                while (!s.Connected) { if (!string.IsNullOrEmpty(s.Error)) { Log("FAILED " + s.Error); yield break; } yield return null; }
                Log("hosting, code " + s.Code);
                while (Coop.Party.Count < players) yield return null;
                yield return new WaitForSeconds(1.5f);
                Shot("party");
                s.StartVote();
            }
            else
            {
                while (!s.Connected)
                {
                    var found = s.Browse();
                    while (!found.IsCompleted) yield return null;
                    if (found.Result.Count > 0)
                    {
                        Log("joining " + found.Result[0].Name);
                        s.JoinById(found.Result[0].Id);
                        float t = 0f;
                        while (!s.Connected && t < 25f) { t += Time.unscaledDeltaTime; yield return null; }
                    }
                    else yield return new WaitForSeconds(3f);
                }
                Log("joined, party of " + Coop.Party.Count);
            }

            while (true)
            {
                // vote
                while (s.CurrentPhase != CoopSession.Phase.Vote) yield return null;
                yield return new WaitForSeconds(1f);
                FindAnyObjectByType<UI.MainMenuScreen>()?.OpenCoop();
                yield return new WaitForSeconds(role == "host" ? 1f : 2.5f);
                Shot("vote_before");
                s.CastVote(stage);
                Log("voted " + stage);
                yield return new WaitForSeconds(1f);
                Shot("vote_after");
                // stage
                while (!Coop.InStage || NetStage.Instance == null) yield return null;
                Log("in stage " + SceneManager.GetActiveScene().name);
                yield return Fight();
                rounds++;
                if (rounds >= 2) { Log("DONE two rounds"); yield break; }
            }
        }

        IEnumerator Fight()
        {
            yield return new WaitForSeconds(6f);
            Shot("stage_start");
            var enc = BossEncounter.Instance;
            BossBase boss = null;
            float wait = 0f;
            while (wait < 30f)
            {
                if (enc != null) foreach (var b in enc.Roster) if (b != null && b.gameObject.activeInHierarchy) boss = b;
                if (boss != null && enc.Active) break;
                wait += Time.deltaTime;
                yield return null;
            }
            if (boss == null) { Log("no boss came (training stage?)"); yield return new WaitForSeconds(5f); Coop.StageDone(); yield break; }
            Log($"boss {boss.DisplayName} active hp={boss.Health:F0}/{boss.MaxHealth:F0} mirror={(boss.Mirror != null)} players={PlayerController.All.Count}");
            var me = PlayerController.Instance;
            // wear a mask (host: the first owned, joiner: the last) so each side can see the other's form
            var ms = MaskSystem.Instance;
            if (ms != null)
            {
                int n = ms.slots.Length;
                for (int k = 0; k < n; k++)
                {
                    var m = ms.slots[role == "host" ? k : n - 1 - k];
                    if (m != null && ms.Owns(m) && ms.TryWear(m)) { Log("wearing " + m.name); break; }
                }
            }
            yield return new WaitForSeconds(4f);
            Shot("fight");

            // the joiner lands a few blows (they go to the host); the host watches its boss lose health
            float t0 = Time.time;
            float before = boss.Health;
            while (Time.time - t0 < 14f)
            {
                if (role != "host" && me != null && !me.IsDead && boss.IsAlive)
                {
                    boss.ReceiveHit(new DamageInfo { damage = 120f, poiseDamage = 10f, point = boss.transform.position + Vector3.up * 2f,
                        direction = (boss.transform.position - me.transform.position).normalized, weight = ImpactWeight.Heavy, source = me.gameObject });
                }
                Log($"hp={boss.Health:F0}/{boss.MaxHealth:F0} move={boss.CurrentMove} aggro={(boss.Mirror != null ? boss.Mirror.aggroId : (boss.Aggro != null ? boss.Aggro.NetId : -1))} meHp={(me != null ? me.stats.Health : 0):F0} dead={(me != null && me.IsDead)}");
                yield return new WaitForSeconds(2f);
            }
            Log($"after blows: {before:F0} -> {boss.Health:F0}");
            Shot("fight_late");

            // -coopWipe (first round): every tree falls; the party should fade back to the start and the boss return
            if (rounds == 0 && Array.IndexOf(Environment.GetCommandLineArgs(), "-coopWipe") >= 0 && me != null)
            {
                me.TakeHazardDamage(99999f, Vector3.forward);
                Log("fell on purpose, dead=" + me.IsDead);
                float w = 0f;
                while (me.IsDead && w < 30f) { w += Time.deltaTime; if (Mathf.Abs(w - 6f) < Time.deltaTime) Shot("down"); yield return null; }
                Log($"stood up again after {w:F1}s at {me.transform.position}");
                w = 0f;
                while (!(boss.gameObject.activeInHierarchy && boss.IsAlive && enc.Active) && w < 30f) { w += Time.deltaTime; yield return null; }
                Log($"rematch: boss active={boss.gameObject.activeInHierarchy} alive={boss.IsAlive} hp={boss.Health:F0}/{boss.MaxHealth:F0} after {w:F1}s");
                yield return new WaitForSeconds(4f);
                Shot("rematch");
            }

            // the host fells it
            if (role == "host" && boss.IsAlive)
            {
                boss.ReceiveHit(new DamageInfo { damage = boss.Health + 10f, poiseDamage = 0f, point = boss.transform.position + Vector3.up * 2f,
                    direction = Vector3.forward, weight = ImpactWeight.Heavy, source = me != null ? me.gameObject : null });
                Log("host fells the boss");
            }
            float w2 = 0f;
            while (!boss.IsDead && w2 < 20f) { w2 += Time.deltaTime; yield return null; }
            Log("boss dead=" + boss.IsDead);
            yield return new WaitForSeconds(7f);
            Shot("victory");

            // walk to my own mask (if it dropped for me)
            var pickup = FindAnyObjectByType<MaskPickup>();
            Log("mask pickup here: " + (pickup != null ? pickup.mask.name : "none (already owned)"));
            if (pickup != null && me != null)
            {
                me.motor.Teleport(pickup.transform.position + Vector3.back * 0.3f, me.transform.rotation);
                float w3 = 0f;
                while (pickup != null && w3 < 10f) { w3 += Time.deltaTime; yield return null; }
                Log("mask taken=" + (pickup == null));
                var cine = FindAnyObjectByType<MaskCinematic>();
                yield return new WaitForSeconds(1f);
                while (cine != null && cine.Playing) yield return null;
            }
            // the arena tells the host we are done; wait for the party to go back to the vote
            float w4 = 0f;
            while (Coop.InStage && w4 < 150f) { w4 += Time.deltaTime; yield return null; }
            Log("left the stage, phase " + CoopSession.Instance?.CurrentPhase);
        }
    }
}
