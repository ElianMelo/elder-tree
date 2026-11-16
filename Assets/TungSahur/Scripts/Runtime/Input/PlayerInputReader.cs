using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TungSahur.InputHandling
{
    public enum InputDeviceKind { KeyboardMouse, Gamepad }

    /// <summary>
    /// Souls-style input layer: buffered presses, tap-vs-hold on the dodge button (tap = dodge on
    /// release, hold = sprint), modifier chords for keyboard (Shift+LMB heavy, Shift+RMB skill),
    /// target switching by stick flick / mouse flick / scroll, and last-used-device tracking.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class PlayerInputReader : MonoBehaviour
    {
        [SerializeField] InputActionAsset actions;

        [Header("Look")]
        public float mouseSensitivity = 1.0f;     // degrees per (scaled) mouse unit
        public float stickYawSpeed = 200f;        // deg/s at full deflection
        public float stickPitchSpeed = 120f;
        public bool invertY;

        [Header("Buffers")]
        public float bufferWindow = 0.4f;
        public float sprintHoldThreshold = 0.22f;

        public static PlayerInputReader Instance { get; private set; }
        public InputDeviceKind Device { get; private set; } = InputDeviceKind.KeyboardMouse;
        public event System.Action<InputDeviceKind> DeviceChanged;

        InputActionMap gameplay, ui;
        InputAction move, look, lightAct, heavy, modifier, guard, skill, dodge, jump, lockOn, switchTarget, walk, pause, help, interact, useItem;
        InputAction navigate, submit, cancel, tabLeft, tabRight;

        float lightAt = -99, heavyAt = -99, skillAt = -99, dodgeAt = -99, jumpAt = -99;
        float dodgePressedTime = -99;
        bool dodgeHeldSprint;
        bool heavyFromModifier;
        bool walkToggle;
        float stickSwitchCooldown;
        float mouseSwitchAccum, mouseSwitchTimer;
        int pendingSwitch;
        bool lockOnPressed, pausePressed, helpPressed;
        Coroutine rumbleRoutine;
        Vector2 lastNav;
        float navRepeatAt;

        public bool GameplayEnabled => gameplay != null && gameplay.enabled;

        void Awake()
        {
            Instance = this;
            gameplay = actions.FindActionMap("Gameplay", true);
            ui = actions.FindActionMap("UI", true);
            move = gameplay["Move"]; look = gameplay["Look"]; lightAct = gameplay["Light"]; heavy = gameplay["Heavy"];
            modifier = gameplay["Modifier"]; guard = gameplay["Guard"]; skill = gameplay["Skill"]; dodge = gameplay["Dodge"];
            jump = gameplay["Jump"]; lockOn = gameplay["LockOn"]; switchTarget = gameplay["SwitchTarget"]; walk = gameplay["Walk"];
            pause = gameplay["Pause"]; help = gameplay["Help"]; interact = gameplay["Interact"]; useItem = gameplay["UseItem"];
            navigate = ui["Navigate"]; submit = ui["Submit"]; cancel = ui["Cancel"]; tabLeft = ui["TabLeft"]; tabRight = ui["TabRight"];

            lightAct.performed += _ =>
            {
                if (modifier.IsPressed()) { heavyAt = Time.unscaledTime; heavyFromModifier = true; }
                else lightAt = Time.unscaledTime;
            };
            heavy.performed += _ => { heavyAt = Time.unscaledTime; heavyFromModifier = false; };
            guard.performed += _ => { if (modifier.IsPressed()) skillAt = Time.unscaledTime; };
            skill.performed += _ => skillAt = Time.unscaledTime;
            dodge.started += _ => { dodgePressedTime = Time.unscaledTime; dodgeHeldSprint = false; };
            dodge.canceled += _ =>
            {
                if (!dodgeHeldSprint && Time.unscaledTime - dodgePressedTime < sprintHoldThreshold + 0.08f) dodgeAt = Time.unscaledTime;
                dodgeHeldSprint = false;
            };
            jump.performed += _ => jumpAt = Time.unscaledTime;
            lockOn.performed += _ => lockOnPressed = true;
            pause.performed += _ => pausePressed = true;
            help.performed += _ => helpPressed = true;
            walk.performed += _ => walkToggle = !walkToggle;
            switchTarget.performed += ctx =>
            {
                // mouse scroll arrives here as discrete y steps
                if (ctx.control.device is Mouse)
                {
                    var v = ctx.ReadValue<Vector2>();
                    if (Mathf.Abs(v.y) > 0.01f) pendingSwitch = v.y > 0 ? -1 : 1;
                }
            };

            foreach (var a in actions)
                a.performed += ctx => NoteDevice(ctx.control.device);
        }

        void OnEnable()
        {
            gameplay.Enable();
            ui.Enable();
        }

        void OnDisable()
        {
            gameplay?.Disable();
            ui?.Disable();
            StopRumble();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            StopRumble();
        }

        void NoteDevice(InputDevice d)
        {
            var kind = d is Gamepad ? InputDeviceKind.Gamepad : InputDeviceKind.KeyboardMouse;
            if (d is Mouse && kind == InputDeviceKind.KeyboardMouse && Device == InputDeviceKind.Gamepad)
            {
                // ignore tiny mouse jitter so a resting mouse doesn't steal focus from the pad
                if (Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude < 4f) return;
            }
            if (kind != Device)
            {
                Device = kind;
                DeviceChanged?.Invoke(kind);
            }
        }

        void Update()
        {
            // Sprint: dodge held past the threshold
            if (dodge.IsPressed() && Time.unscaledTime - dodgePressedTime > sprintHoldThreshold) dodgeHeldSprint = true;

            // Stick flick target switch
            stickSwitchCooldown -= Time.unscaledDeltaTime;
            if (Device == InputDeviceKind.Gamepad && Gamepad.current != null)
            {
                float x = Gamepad.current.rightStick.ReadValue().x;
                if (stickSwitchCooldown <= 0f && Mathf.Abs(x) > 0.75f)
                {
                    pendingSwitch = x > 0 ? 1 : -1;
                    stickSwitchCooldown = 0.35f;
                }
                if (Mathf.Abs(x) < 0.3f) stickSwitchCooldown = Mathf.Min(stickSwitchCooldown, 0f);
            }
            // Mouse flick target switch
            if (Mouse.current != null)
            {
                float dx = Mouse.current.delta.ReadValue().x;
                mouseSwitchTimer -= Time.unscaledDeltaTime;
                // leaky accumulator: only a quick, deliberate flick crosses the threshold
                mouseSwitchAccum = mouseSwitchAccum * Mathf.Exp(-10f * Time.unscaledDeltaTime) + dx;
                if (mouseSwitchTimer <= 0f && Mathf.Abs(mouseSwitchAccum) > 160f)
                {
                    pendingSwitch = mouseSwitchAccum > 0 ? 1 : -1;
                    mouseSwitchAccum = 0f;
                    mouseSwitchTimer = 0.35f;
                }
            }
        }

        // ------------------------------------------------------------------ queries
        public Vector2 Move
        {
            get
            {
                var v = move.ReadValue<Vector2>();
                return Vector2.ClampMagnitude(v, 1f);
            }
        }

        /// <summary>Walk if toggled, or for partial stick deflection.</summary>
        public bool WalkModifier => walkToggle;

        /// <summary>Camera rotation delta in degrees (x = yaw, y = pitch).</summary>
        public Vector2 LookDelta(float unscaledDt)
        {
            var v = look.ReadValue<Vector2>();
            Vector2 d;
            if (look.activeControl != null && look.activeControl.device is Gamepad)
            {
                // response curve: fine aim near centre, fast at the edge
                v = v.normalized * Mathf.Pow(v.magnitude, 1.6f);
                d = new Vector2(v.x * stickYawSpeed, v.y * stickPitchSpeed) * unscaledDt;
            }
            else d = v * mouseSensitivity * 2.2f;
            if (invertY) d.y = -d.y;
            return d;
        }

        public bool SprintHeld => dodgeHeldSprint && dodge.IsPressed();
        public bool GuardHeld => guard.IsPressed() && !modifier.IsPressed();
        public bool HeavyHeld => heavyFromModifier ? lightAct.IsPressed() : heavy.IsPressed();

        bool Consume(ref float t, float window)
        {
            if (Time.unscaledTime - t <= window) { t = -99; return true; }
            return false;
        }

        bool Peek(float t, float window) => Time.unscaledTime - t <= window;

        public bool ConsumeLight() => Consume(ref lightAt, bufferWindow);
        public bool ConsumeHeavy() => Consume(ref heavyAt, bufferWindow);
        public bool ConsumeSkill() => Consume(ref skillAt, bufferWindow);
        public bool ConsumeDodge() => Consume(ref dodgeAt, bufferWindow * 0.75f);
        public bool ConsumeJump() => Consume(ref jumpAt, bufferWindow * 0.6f);
        public bool HasBufferedLight => Peek(lightAt, bufferWindow);
        public bool HasBufferedHeavy => Peek(heavyAt, bufferWindow);
        public bool HasBufferedSkill => Peek(skillAt, bufferWindow);
        public bool HasBufferedDodge => Peek(dodgeAt, bufferWindow * 0.75f);
        public bool HasBufferedJump => Peek(jumpAt, bufferWindow * 0.6f);
        public void ClearAttackBuffers() { lightAt = heavyAt = skillAt = -99; }
        public void ClearAll() { lightAt = heavyAt = skillAt = dodgeAt = jumpAt = -99; lockOnPressed = false; pendingSwitch = 0; }

        public bool ConsumeLockOn() { bool v = lockOnPressed; lockOnPressed = false; return v; }
        public bool ConsumePause() { bool v = pausePressed; pausePressed = false; return v; }
        public bool ConsumeHelp() { bool v = helpPressed; helpPressed = false; return v; }
        public int ConsumeSwitchTarget() { int v = pendingSwitch; pendingSwitch = 0; return v; }

        // ------------------------------------------------------------------ UI
        /// <summary>Discrete menu navigation with key-repeat (unscaled).</summary>
        public Vector2Int ConsumeNavigate()
        {
            var v = navigate.ReadValue<Vector2>();
            Vector2Int dir = Vector2Int.zero;
            if (Mathf.Abs(v.y) > 0.5f) dir.y = v.y > 0 ? 1 : -1;
            else if (Mathf.Abs(v.x) > 0.5f) dir.x = v.x > 0 ? 1 : -1;
            if (dir == Vector2Int.zero) { lastNav = Vector2.zero; return dir; }
            bool fresh = lastNav == Vector2.zero;
            lastNav = v;
            if (fresh) { navRepeatAt = Time.unscaledTime + 0.38f; return dir; }
            if (Time.unscaledTime >= navRepeatAt) { navRepeatAt = Time.unscaledTime + 0.11f; return dir; }
            return Vector2Int.zero;
        }

        public bool SubmitPressed => submit.WasPressedThisFrame();
        public bool CancelPressed => cancel.WasPressedThisFrame();
        /// <summary>Works even while the gameplay map is disabled (title screen).</summary>
        public bool AnyGameplayButtonPressed
        {
            get
            {
                if (submit.WasPressedThisFrame() || cancel.WasPressedThisFrame()) return true;
                if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) { NoteDevice(Keyboard.current); return true; }
                if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
                var pad = Gamepad.current;
                if (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame ||
                                    pad.buttonWest.wasPressedThisFrame || pad.startButton.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame ||
                                    pad.leftShoulder.wasPressedThisFrame))
                {
                    NoteDevice(pad);
                    return true;
                }
                return false;
            }
        }

        public void SetGameplayEnabled(bool on)
        {
            if (on) gameplay.Enable(); else gameplay.Disable();
            if (!on) StopRumble();
        }

        // ------------------------------------------------------------------ rumble
        public void Rumble(float low, float high, float duration)
        {
            if (!Settings.Rumble) return;
            if (Device != InputDeviceKind.Gamepad || Gamepad.current == null) return;
            if (rumbleRoutine != null) StopCoroutine(rumbleRoutine);
            rumbleRoutine = StartCoroutine(RumbleRoutine(low, high, duration));
        }

        IEnumerator RumbleRoutine(float low, float high, float duration)
        {
            var pad = Gamepad.current;
            pad.SetMotorSpeeds(low, high);
            yield return new WaitForSecondsRealtime(duration);
            pad.SetMotorSpeeds(0, 0);
            rumbleRoutine = null;
        }

        void StopRumble()
        {
            if (Gamepad.current != null) Gamepad.current.SetMotorSpeeds(0, 0);
        }
    }
}
