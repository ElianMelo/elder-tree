using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ElderTree.Enemies;
using ElderTree.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ElderTree.Dev
{
    /// <summary>
    /// Development autopilot: drives a virtual gamepad from a tiny script so gameplay can be
    /// exercised and screenshotted without a human (used for automated playtests).
    /// Script: "stick x y; rstick x y; tap RB 0.1; hold RT 0.8; press B; release B; key F1; wait 0.5; shot name; log".
    /// Bosses: "boss [name]" spawns one (primary by default) and makes it the focus of "bossmove X",
    /// "bosshp dmg [poise]", "lockboss", "bossaway" and the log. "waitmove X [timeout]" waits until the focus boss's
    /// CurrentMove is X; "viewcam yaw dist height [lookY]" puts a review camera on the focus boss (yaw relative to its
    /// heading, 90 = its right side) that follows it, "viewcam off" removes it. "tpboss d" stands the player d metres in front of the focus boss.
    /// </summary>
    public class AutoPilot : MonoBehaviour
    {
        public static AutoPilot I { get; private set; }
        public static readonly List<string> Log = new();

        Gamepad pad;
        Vector2 left, right;
        readonly HashSet<GamepadButton> held = new();
        readonly Dictionary<GamepadButton, float> tapUntil = new();
        Coroutine running;
        public bool Busy => running != null;
        public string shotDir = "Temp/Shots";
        /// <summary>The boss the boss commands act on (last one spawned with "boss").</summary>
        public static BossBase Focus;
        static BossBase FocusBoss => Focus != null ? Focus : BossEncounter.Instance != null ? BossEncounter.Instance.boss : null;

        public static AutoPilot Ensure()
        {
            if (I != null) return I;
            var go = new GameObject("AutoPilot");
            DontDestroyOnLoad(go);
            return go.AddComponent<AutoPilot>();
        }

        void Awake()
        {
            I = this;
            pad = InputSystem.AddDevice<Gamepad>("AutoPad");
        }

        void OnDestroy()
        {
            if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
            if (I == this) I = null;
        }

        Camera viewCam;
        Vector4 viewCamParams;

        void LateUpdate()
        {
            var b = FocusBoss;
            if (viewCam == null || b == null) return;
            Vector3 c = b.transform.position;
            Quaternion r = b.transform.rotation * Quaternion.Euler(0f, viewCamParams.x, 0f);
            viewCam.transform.position = c + r * Vector3.forward * viewCamParams.y + Vector3.up * viewCamParams.z;
            viewCam.transform.LookAt(c + Vector3.up * viewCamParams.w);
        }

        void Update()
        {
            var s = new GamepadState { leftStick = left, rightStick = right };
            foreach (var b in held) s = Apply(s, b);
            var expired = new List<GamepadButton>();
            foreach (var kv in tapUntil)
            {
                if (Time.unscaledTime < kv.Value) s = Apply(s, kv.Key);
                else expired.Add(kv.Key);
            }
            foreach (var e in expired) tapUntil.Remove(e);
            InputSystem.QueueStateEvent(pad, s);
        }

        static GamepadState Apply(GamepadState s, GamepadButton b)
        {
            if (b == GamepadButton.RightTrigger) { s.rightTrigger = 1f; return s; }
            if (b == GamepadButton.LeftTrigger) { s.leftTrigger = 1f; return s; }
            return s.WithButton(b);
        }

        static GamepadButton Parse(string n) => n.ToUpperInvariant() switch
        {
            "A" => GamepadButton.South, "B" => GamepadButton.East, "X" => GamepadButton.West, "Y" => GamepadButton.North,
            "RB" => GamepadButton.RightShoulder, "LB" => GamepadButton.LeftShoulder,
            "RT" => GamepadButton.RightTrigger, "LT" => GamepadButton.LeftTrigger,
            "R3" => GamepadButton.RightStick, "L3" => GamepadButton.LeftStick,
            "START" => GamepadButton.Start, "SELECT" => GamepadButton.Select,
            "UP" => GamepadButton.DpadUp, "DOWN" => GamepadButton.DpadDown, "LEFT" => GamepadButton.DpadLeft, "RIGHT" => GamepadButton.DpadRight,
            _ => GamepadButton.South
        };

        public void Run(string script)
        {
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(Exec(script));
        }

        IEnumerator Exec(string script)
        {
            foreach (var raw in script.Split(';'))
            {
                var p = raw.Trim().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) continue;
                float F(int i, float d = 0f) => p.Length > i ? float.Parse(p[i], CultureInfo.InvariantCulture) : d;
                switch (p[0].ToLowerInvariant())
                {
                    case "stick": left = new Vector2(F(1), F(2)); break;
                    case "rstick": right = new Vector2(F(1), F(2)); break;
                    case "tap": tapUntil[Parse(p[1])] = Time.unscaledTime + F(2, 0.08f); break;
                    case "hold": { var b = Parse(p[1]); held.Add(b); yield return new WaitForSecondsRealtime(F(2, 0.5f)); held.Remove(b); break; }
                    case "press": held.Add(Parse(p[1])); break;
                    case "release": held.Remove(Parse(p[1])); break;
                    case "wait": yield return new WaitForSecondsRealtime(F(1, 0.1f)); break;
                    case "key":
                    {
                        // tap a keyboard key, e.g. "key F1" or "key Digit1"
                        var kb = Keyboard.current;
                        if (kb == null || !System.Enum.TryParse<Key>(p[1], true, out var k)) break;
                        InputSystem.QueueStateEvent(kb, new KeyboardState(k));
                        yield return new WaitForSecondsRealtime(F(2, 0.08f));
                        InputSystem.QueueStateEvent(kb, new KeyboardState());
                        yield return null;
                        break;
                    }
                    case "shot":
                        System.IO.Directory.CreateDirectory(shotDir);
                        ScreenCapture.CaptureScreenshot($"{shotDir}/{p[1]}.png");
                        yield return null;
                        break;
                    case "log": Log.Add(Report()); break;
                    case "boss":
                    {
                        var enc = BossEncounter.Instance;
                        if (enc == null) break;
                        Focus = enc.Find(p.Length > 1 ? p[1] : null);
                        if (Focus != null) enc.Spawn(Focus);
                        break;
                    }
                    case "bossaway": if (FocusBoss != null) BossEncounter.Instance?.Despawn(FocusBoss); break;
                    case "bossmove": FocusBoss?.ForceNextMove(p[1]); break;
                    case "tpboss":
                    {
                        // "tpboss d": stand d metres in front of the focus boss, facing it
                        var b = FocusBoss; var pc = PlayerController.Instance;
                        if (b == null || pc == null) break;
                        Vector3 at = b.transform.position + b.transform.forward * F(1, 4f);
                        pc.motor.Teleport(at, Quaternion.LookRotation(-b.transform.forward));
                        break;
                    }
                    case "waitmove":
                    {
                        float until = Time.unscaledTime + F(2, 8f);
                        while (Time.unscaledTime < until && FocusBoss != null && FocusBoss.CurrentMove != p[1]) yield return null;
                        break;
                    }
                    case "viewcam":
                    {
                        if (p.Length > 1 && p[1] == "off") { if (viewCam) Destroy(viewCam.gameObject); viewCam = null; break; }
                        if (viewCam == null)
                        {
                            var go = new GameObject("AutoPilot ViewCam");
                            go.transform.SetParent(transform, false);
                            viewCam = go.AddComponent<Camera>();
                            var main = Camera.main;
                            if (main != null) { viewCam.CopyFrom(main); viewCam.targetTexture = null; }
                            viewCam.depth = 50f;
                            viewCam.fieldOfView = 50f;
                        }
                        viewCamParams = new Vector4(F(1, 90f), F(2, 22f), F(3, 5f), F(4, 5f));
                        break;
                    }
                    case "bosshp":
                    {
                        var b = FocusBoss;
                        if (b != null) b.ReceiveHit(new Combat.DamageInfo { damage = F(1, 100), poiseDamage = F(2, 0), point = b.transform.position + Vector3.up * 2, direction = Vector3.forward });
                        break;
                    }
                    case "lockboss":
                    {
                        var b = FocusBoss;
                        if (b != null && PlayerController.Instance != null) PlayerController.Instance.lockOn.ForceLock(b.GetComponent<Combat.Targetable>());
                        break;
                    }
                    case "god": PlayerController.DevNoDamage = F(1, 1) > 0.5f; break;
                    case "wear":
                    {
                        // "wear shark" / "wear alligator" / "wear none": instant form swap (no ownership needed)
                        var ms = Masks.MaskSystem.Instance;
                        if (ms == null) break;
                        Equipment.MaskData m = null;
                        foreach (var sm in ms.slots) if (sm != null && p.Length > 1 && sm.id == p[1]) m = sm;
                        ms.SetWorn(m, true);
                        break;
                    }
                    case "charge": PlayerController.Instance?.stats.DevSetSpecialCharge(F(1, 2)); break;
                    case "spirit": PlayerController.Instance?.DevFillSpirit(p.Length > 1 ? (int)F(1, 0) : -1); break;
                    case "hurt":
                    {
                        var t = PlayerController.Instance != null ? PlayerController.Instance.lockOn.Current : null;
                        var d = t != null ? t.GetComponent<TrainingDummy>() : null;
                        if (d != null) d.ReceiveHit(new Combat.DamageInfo { damage = F(1, 100), poiseDamage = 0, point = d.transform.position + Vector3.up, direction = Vector3.forward });
                        break;
                    }
                }
            }
            left = right = Vector2.zero;
            held.Clear();
            running = null;
        }

        public static string Report()
        {
            var sb = new StringBuilder();
            var pc = PlayerController.Instance;
            if (pc != null)
            {
                sb.Append($"t={Time.time:F2} state={pc.State} pos={pc.transform.position.ToString("F2")} yaw={pc.transform.eulerAngles.y:F0} ");
                sb.Append($"vel={pc.motor.PlanarVelocity.magnitude:F2} grounded={pc.motor.Grounded} sprint={pc.IsSprinting} ");
                sb.Append($"stam={pc.stats.Stamina:F0} mana={pc.stats.Mana:F0} charge={pc.stats.SpecialCharge:F2} atk={(pc.CurrentAttack != null ? pc.CurrentAttack.id : "-")} ");
                sb.Append($"lock={(pc.lockOn.Current != null ? pc.lockOn.Current.name : "-")} ");
            }
            var boss = FocusBoss;
            if (boss != null && boss.gameObject.activeInHierarchy)
                sb.Append($"[BOSS {boss.name} {boss.StateLabel} move={boss.CurrentMove} hp={boss.Health:F0} poise={boss.Poise:F0} p2={boss.PhaseTwo} pos={boss.transform.position.ToString("F1")}] ");
            if (pc != null) sb.Append($"php={pc.stats.Health:F0} ");
            if (Enemies.AerialBomb.Live.Count > 0 && pc != null)
            {
                float best = 999f;
                foreach (var ab in Enemies.AerialBomb.Live) best = Mathf.Min(best, Vector3.Distance(ab.Target, pc.transform.position));
                sb.Append($"bombs={Enemies.AerialBomb.Live.Count} nearest={best:F1} ");
            }
            sb.Append($"walkers={Enemies.LivingBomb.Active.Count} ");
            foreach (var d in Object.FindObjectsByType<TrainingDummy>(FindObjectsSortMode.InstanceID))
                sb.Append($"[{d.name}: hp={d.Health:F0} alive={d.IsAlive}] ");
            return sb.ToString();
        }
    }
}
