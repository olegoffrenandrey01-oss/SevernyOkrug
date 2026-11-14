// СЕВЕРНЫЙ ОКРУГ — prototype. Input wrapper: works with the new Input System package
// (ENABLE_INPUT_SYSTEM) and with the legacy Input Manager. No input assets required.
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Severny
{
    public enum GKey
    {
        Forward, Back, Left, Right, Jump, Crouch, Sprint, Walk, LeanLeft, LeanRight,
        Reload, FireMode, CheckMag, Interact, Inventory, Exits, Pause, Heal,
        Weapon1, Weapon2, DebugOverlay, Rotate, QuickMove, NavTest, Benchmark, Flashlight, Shots
    }

    public static class GameInput
    {
        /// <summary>When false (UI open) gameplay queries return nothing.</summary>
        public static bool GameplayEnabled = true;

#if ENABLE_INPUT_SYSTEM
        static Key Map(GKey k)
        {
            switch (k)
            {
                case GKey.Forward: return Key.W;
                case GKey.Back: return Key.S;
                case GKey.Left: return Key.A;
                case GKey.Right: return Key.D;
                case GKey.Jump: return Key.Space;
                case GKey.Crouch: return Key.C;
                case GKey.Sprint: return Key.LeftShift;
                case GKey.Walk: return Key.LeftAlt;
                case GKey.LeanLeft: return Key.Q;
                case GKey.LeanRight: return Key.E;
                case GKey.Reload: return Key.R;
                case GKey.FireMode: return Key.B;
                case GKey.CheckMag: return Key.T;
                case GKey.Interact: return Key.F;
                case GKey.Inventory: return Key.Tab;
                case GKey.Exits: return Key.O;
                case GKey.Pause: return Key.Escape;
                case GKey.Heal: return Key.H;
                case GKey.Weapon1: return Key.Digit1;
                case GKey.Weapon2: return Key.Digit2;
                case GKey.DebugOverlay: return Key.F3;
                case GKey.Rotate: return Key.R;
                case GKey.QuickMove: return Key.LeftCtrl;
                case GKey.NavTest: return Key.F9;
                case GKey.Benchmark: return Key.F8;
                case GKey.Shots: return Key.F7;
                case GKey.Flashlight: return Key.L;
            }
            return Key.None;
        }
        static UnityEngine.InputSystem.Controls.KeyControl KC(GKey k)
        {
            var kb = Keyboard.current;
            if (kb == null) return null;
            return kb[Map(k)];
        }
        public static bool Held(GKey k) { var c = KC(k); return c != null && c.isPressed; }
        public static bool Down(GKey k) { var c = KC(k); return c != null && c.wasPressedThisFrame; }
        public static bool Up(GKey k) { var c = KC(k); return c != null && c.wasReleasedThisFrame; }
        public static Vector2 MouseDelta() { var m = Mouse.current; return m == null ? Vector2.zero : m.delta.ReadValue(); }
        public static bool FireHeld() { var m = Mouse.current; return m != null && m.leftButton.isPressed; }
        public static bool FireDown() { var m = Mouse.current; return m != null && m.leftButton.wasPressedThisFrame; }
        public static bool AimHeld() { var m = Mouse.current; return m != null && m.rightButton.isPressed; }
        public static float Scroll() { var m = Mouse.current; return m == null ? 0f : m.scroll.ReadValue().y / 120f; }
        public static Vector2 MousePosition() { var m = Mouse.current; return m == null ? Vector2.zero : m.position.ReadValue(); }
        static UnityEngine.InputSystem.Controls.ButtonControl MB(int b) { var m = Mouse.current; return m == null ? null : b == 0 ? m.leftButton : b == 1 ? m.rightButton : m.middleButton; }
        public static bool MouseHeld(int b) { var c = MB(b); return c != null && c.isPressed; }
        public static bool MouseDown(int b) { var c = MB(b); return c != null && c.wasPressedThisFrame; }
        public static bool MouseUp(int b) { var c = MB(b); return c != null && c.wasReleasedThisFrame; }
        public static bool ShiftHeld() { var kb = Keyboard.current; return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed); }
        public static bool CtrlHeld() { var kb = Keyboard.current; return kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed); }
#else
        static KeyCode Map(GKey k)
        {
            switch (k)
            {
                case GKey.Forward: return KeyCode.W;
                case GKey.Back: return KeyCode.S;
                case GKey.Left: return KeyCode.A;
                case GKey.Right: return KeyCode.D;
                case GKey.Jump: return KeyCode.Space;
                case GKey.Crouch: return KeyCode.C;
                case GKey.Sprint: return KeyCode.LeftShift;
                case GKey.Walk: return KeyCode.LeftAlt;
                case GKey.LeanLeft: return KeyCode.Q;
                case GKey.LeanRight: return KeyCode.E;
                case GKey.Reload: return KeyCode.R;
                case GKey.FireMode: return KeyCode.B;
                case GKey.CheckMag: return KeyCode.T;
                case GKey.Interact: return KeyCode.F;
                case GKey.Inventory: return KeyCode.Tab;
                case GKey.Exits: return KeyCode.O;
                case GKey.Pause: return KeyCode.Escape;
                case GKey.Heal: return KeyCode.H;
                case GKey.Weapon1: return KeyCode.Alpha1;
                case GKey.Weapon2: return KeyCode.Alpha2;
                case GKey.DebugOverlay: return KeyCode.F3;
                case GKey.Rotate: return KeyCode.R;
                case GKey.QuickMove: return KeyCode.LeftControl;
                case GKey.NavTest: return KeyCode.F9;
                case GKey.Benchmark: return KeyCode.F8;
                case GKey.Shots: return KeyCode.F7;
                case GKey.Flashlight: return KeyCode.L;
            }
            return KeyCode.None;
        }
        public static bool Held(GKey k) => Input.GetKey(Map(k));
        public static bool Down(GKey k) => Input.GetKeyDown(Map(k));
        public static bool Up(GKey k) => Input.GetKeyUp(Map(k));
        // Legacy axes are ~1/10 of the pixel delta at default sensitivity; scale to pixels.
        public static Vector2 MouseDelta() => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
        public static bool FireHeld() => Input.GetMouseButton(0);
        public static bool FireDown() => Input.GetMouseButtonDown(0);
        public static bool AimHeld() => Input.GetMouseButton(1);
        public static float Scroll() => Input.mouseScrollDelta.y;
        public static Vector2 MousePosition() => Input.mousePosition;
        public static bool MouseHeld(int b) => Input.GetMouseButton(b);
        public static bool MouseDown(int b) => Input.GetMouseButtonDown(b);
        public static bool MouseUp(int b) => Input.GetMouseButtonUp(b);
        public static bool ShiftHeld() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        public static bool CtrlHeld() => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
#endif
        // Gameplay-gated helpers (return nothing while a UI screen owns the input)
        public static bool GHeld(GKey k) => GameplayEnabled && Held(k);
        public static bool GDown(GKey k) => GameplayEnabled && Down(k);
        public static bool GFireHeld() => GameplayEnabled && FireHeld();
        public static bool GFireDown() => GameplayEnabled && FireDown();
        public static bool GAimHeld() => GameplayEnabled && AimHeld();
        public static Vector2 GMouseDelta() => GameplayEnabled ? MouseDelta() : Vector2.zero;

        public static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
