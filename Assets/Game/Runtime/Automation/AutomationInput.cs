using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CK.Runtime.Automation
{
    /// <summary>Desired device state for one automation step.</summary>
    public struct AutomationKeys
    {
        public bool W, A, S, D;
        public bool Space;
        public bool Q, E, R, F;
        public bool Tab, Map, Inventory;
        public bool Digit1, Digit2, Digit3, Digit4;
        public bool LeftShift;
        public bool LeftMouse;
        public bool RightMouse;

        public static AutomationKeys None => default;

        public static AutomationKeys MoveW => new AutomationKeys { W = true };
        public static AutomationKeys MoveD => new AutomationKeys { D = true };
    }

    /// <summary>
    /// Injects input through the Input System event queue (the same path real
    /// keyboard/mouse input takes). This keeps Play Mode automation honest:
    /// the game reads devices, not an automation-only shortcut.
    /// </summary>
    public sealed class AutomationInput
    {
        private Keyboard _keyboard;
        private Mouse _mouse;

        public string DeviceDescription { get; private set; } = "uninitialized";
        public int Injections { get; private set; }
        public string LastError { get; private set; } = string.Empty;

        public AutomationInput()
        {
            EnsureDevices();
        }

        private void EnsureDevices()
        {
            _keyboard = Keyboard.current;
            if (_keyboard == null)
            {
                _keyboard = InputSystem.AddDevice<Keyboard>();
            }
            _mouse = Mouse.current;
            if (_mouse == null)
            {
                _mouse = InputSystem.AddDevice<Mouse>();
            }
            DeviceDescription = $"keyboard={_keyboard.deviceId}/{_keyboard.layout} mouse={_mouse.deviceId}/{_mouse.layout}";
            Debug.Log("[CK] automation input devices: " + DeviceDescription);
        }

        public void Apply(AutomationKeys keys)
        {
            try
            {
                if (_keyboard == null || _mouse == null)
                {
                    EnsureDevices();
                }
                var pressed = new List<Key>(16);
                if (keys.W) pressed.Add(Key.W);
                if (keys.A) pressed.Add(Key.A);
                if (keys.S) pressed.Add(Key.S);
                if (keys.D) pressed.Add(Key.D);
                if (keys.Space) pressed.Add(Key.Space);
                if (keys.Q) pressed.Add(Key.Q);
                if (keys.E) pressed.Add(Key.E);
                if (keys.R) pressed.Add(Key.R);
                if (keys.F) pressed.Add(Key.F);
                if (keys.Tab) pressed.Add(Key.Tab);
                if (keys.Map) pressed.Add(Key.M);
                if (keys.Inventory) pressed.Add(Key.B);
                if (keys.Digit1) pressed.Add(Key.Digit1);
                if (keys.Digit2) pressed.Add(Key.Digit2);
                if (keys.Digit3) pressed.Add(Key.Digit3);
                if (keys.Digit4) pressed.Add(Key.Digit4);
                if (keys.LeftShift) pressed.Add(Key.LeftShift);

                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(pressed.ToArray()));
                ushort buttons = 0;
                if (keys.LeftMouse) buttons |= 1;
                if (keys.RightMouse) buttons |= 2;
                InputSystem.QueueStateEvent(_mouse, new MouseState
                {
                    position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),
                    buttons = buttons
                });
                InputSystem.Update();
                Injections++;
                if (Injections <= 3 || Injections % 600 == 0)
                {
                    Debug.Log($"[CK] automation input #{Injections} keys={Describe(keys)} buttons={buttons}");
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Debug.LogError("[CK] automation input injection failed: " + ex);
            }
        }

        public void Release()
        {
            Apply(default);
        }

        private static string Describe(AutomationKeys keys)
        {
            var parts = new List<string>(8);
            if (keys.W) parts.Add("W");
            if (keys.A) parts.Add("A");
            if (keys.S) parts.Add("S");
            if (keys.D) parts.Add("D");
            if (keys.Space) parts.Add("Space");
            if (keys.Q) parts.Add("Q");
            if (keys.E) parts.Add("E");
            if (keys.R) parts.Add("R");
            if (keys.F) parts.Add("F");
            if (keys.Tab) parts.Add("Tab");
            if (keys.Map) parts.Add("M");
            if (keys.Inventory) parts.Add("B");
            if (keys.Digit1) parts.Add("1");
            if (keys.Digit2) parts.Add("2");
            if (keys.Digit3) parts.Add("3");
            if (keys.Digit4) parts.Add("4");
            if (keys.LeftShift) parts.Add("LShift");
            if (keys.LeftMouse) parts.Add("LMB");
            if (keys.RightMouse) parts.Add("RMB");
            return parts.Count == 0 ? "<none>" : string.Join("+", parts);
        }
    }
}
