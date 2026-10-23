using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using CK.Runtime.Input;

namespace CK.Runtime.Automation
{
    /// <summary>自动化单步期望的设备状态。</summary>
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
    /// 通过 Input System 事件队列注入输入（与真实键盘/鼠标输入
    /// 的路径相同）。这保证 Play Mode 自动化的诚实性：
    /// 游戏读取设备，而不是走自动化专用捷径。
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
            _keyboard = InputSystem.AddDevice<Keyboard>("CK_AutomationKeyboard");
            _mouse = InputSystem.AddDevice<Mouse>("CK_AutomationMouse");
            CK.Runtime.Input.DeviceInputBackend.KeyboardOverride = _keyboard;
            CK.Runtime.Input.DeviceInputBackend.MouseOverride = _mouse;
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
                // harness 状态才是游戏实际轮询的状态（见 DeviceInputBackend）；
                // 下面的 Input System 事件让虚拟设备保持同步，作为
                // 真实输入路径仍然连通的证据。
                CK.Runtime.Input.AutomationInputState.Set(new RawInputState
                {
                    W = keys.W, A = keys.A, S = keys.S, D = keys.D,
                    Space = keys.Space,
                    LeftMouse = keys.LeftMouse, RightMouse = keys.RightMouse,
                    LeftShift = keys.LeftShift,
                    Q = keys.Q, E = keys.E, R = keys.R, F = keys.F,
                    Tab = keys.Tab, Map = keys.Map, Inventory = keys.Inventory,
                    Digit1 = keys.Digit1, Digit2 = keys.Digit2, Digit3 = keys.Digit3, Digit4 = keys.Digit4
                });

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
                if (Injections <= 5)
                {
                    Debug.Log($"[CK] inject check #{Injections} kbId={_keyboard.deviceId} currentKb={Keyboard.current?.deviceId} " +
                              $"w={_keyboard.wKey.isPressed} d={_keyboard.dKey.isPressed} space={_keyboard.spaceKey.isPressed} " +
                              $"lmb={_mouse.leftButton.isPressed} rmb={_mouse.rightButton.isPressed} buttons={buttons}");
                }
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

        public void Disable()
        {
            AutomationInputState.Clear();
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
