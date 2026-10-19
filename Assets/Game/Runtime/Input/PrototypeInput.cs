using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CK.Runtime.Input
{
    /// <summary>Raw device state; the only layer that knows about the Input System.</summary>
    public struct RawInputState
    {
        public bool W, A, S, D;
        public bool Space;
        public bool LeftMouse;
        public bool RightMouse;
        public bool LeftShift;
        public bool Q, E, R, F;
        public bool Tab, Map, Inventory;
        public bool Digit1, Digit2, Digit3, Digit4;
    }

    public interface IPrototypeInputBackend
    {
        string Name { get; }
        bool Poll(out RawInputState state);
    }

    /// <summary>
    /// Deterministic state source used by gate automation. Automated Play Mode runs
    /// inject Input System events, but the editor's input update clears synthetic
    /// device state; the harness overrides the polled state so the exact same
    /// PrototypeInput -> command buffer -> native tick path stays deterministic.
    /// </summary>
    public static class AutomationInputState
    {
        public static bool Active { get; private set; }
        public static RawInputState State { get; private set; }

        public static void Set(RawInputState state)
        {
            Active = true;
            State = state;
        }

        public static void Clear()
        {
            Active = false;
            State = default;
        }
    }

    /// <summary>
    /// Reads the new Input System devices directly. Works for real keyboard/mouse
    /// input and for automation that injects Input System state events.
    /// </summary>
    public sealed class DeviceInputBackend : IPrototypeInputBackend
    {
        /// <summary>
        /// When gate automation runs it installs dedicated virtual devices here so
        /// its injected state cannot be overwritten by the OS device state.
        /// </summary>
        public static Keyboard KeyboardOverride;
        public static Mouse MouseOverride;

        public string Name => AutomationInputState.Active
            ? "InputSystem(Keyboard/Mouse)+AutomationHarness"
            : "InputSystem(Keyboard/Mouse)";

        public bool Poll(out RawInputState state)
        {
            state = default;
            Keyboard keyboard = KeyboardOverride != null ? KeyboardOverride : Keyboard.current;
            Mouse mouse = MouseOverride != null ? MouseOverride : Mouse.current;
            if (keyboard == null)
            {
                if (AutomationInputState.Active)
                {
                    state = AutomationInputState.State;
                    return true;
                }
                return false;
            }
            state.W = keyboard.wKey.isPressed;
            state.A = keyboard.aKey.isPressed;
            state.S = keyboard.sKey.isPressed;
            state.D = keyboard.dKey.isPressed;
            state.Space = keyboard.spaceKey.isPressed;
            state.Q = keyboard.qKey.isPressed;
            state.E = keyboard.eKey.isPressed;
            state.R = keyboard.rKey.isPressed;
            state.F = keyboard.fKey.isPressed;
            state.Tab = keyboard.tabKey.isPressed;
            state.Map = keyboard.mKey.isPressed;
            state.Inventory = keyboard.bKey.isPressed;
            state.Digit1 = keyboard.digit1Key.isPressed;
            state.Digit2 = keyboard.digit2Key.isPressed;
            state.Digit3 = keyboard.digit3Key.isPressed;
            state.Digit4 = keyboard.digit4Key.isPressed;
            state.LeftShift = keyboard.leftShiftKey.isPressed;
            if (mouse != null)
            {
                state.LeftMouse = mouse.leftButton.isPressed;
                state.RightMouse = mouse.rightButton.isPressed;
            }
            if (AutomationInputState.Active)
            {
                state = AutomationInputState.State;
            }
            return true;
        }
    }

    public struct InputSnapshot
    {
        public float MoveX;
        public float MoveZ;
        public bool PrimaryHeld;
        public bool PrimaryPressed;
        public bool PrimaryReleased;
        public bool DodgePressed;
        public bool BlockHeld;
        public bool BlockPressed;
        public bool BlockReleased;
        public bool SkillQ;
        public bool SkillE;
        public bool SkillR;
        public bool SkillF;
        public bool TabPressed;
        public bool MapPressed;
        public bool InventoryPressed;
        public bool Item1;
        public bool Item2;
        public bool Item3;
        public bool Item4;

        public bool HasMoveInput => Mathf.Abs(MoveX) > 0.001f || Mathf.Abs(MoveZ) > 0.001f;
    }

    /// <summary>
    /// Edge detection and buffering-friendly conversion from raw devices to
    /// high level intents. Sampled once per fixed tick so logic stays 60 Hz.
    /// </summary>
    public sealed class PrototypeInput
    {
        private readonly IPrototypeInputBackend _backend;
        private RawInputState _previous;
        private RawInputState _current;
        private bool _hasPrevious;
        private int _sampleCount;

        public PrototypeInput(IPrototypeInputBackend backend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        public string BackendName => _backend != null ? _backend.Name : "none";
        public bool LastPollSucceeded { get; private set; }
        public int SampleCount => _sampleCount;

        public InputSnapshot Sample()
        {
            LastPollSucceeded = _backend.Poll(out _current);
            if (!LastPollSucceeded)
            {
                _current = default;
            }
            if (!_hasPrevious)
            {
                _previous = _current;
                _hasPrevious = true;
            }

            var snapshot = new InputSnapshot
            {
                MoveX = (_current.D ? 1f : 0f) - (_current.A ? 1f : 0f),
                MoveZ = (_current.W ? 1f : 0f) - (_current.S ? 1f : 0f),
                PrimaryHeld = _current.LeftMouse,
                PrimaryPressed = _current.LeftMouse && !_previous.LeftMouse,
                PrimaryReleased = !_current.LeftMouse && _previous.LeftMouse,
                DodgePressed = (_current.RightMouse && !_previous.RightMouse) ||
                               (_current.LeftShift && !_previous.LeftShift),
                BlockHeld = _current.Space,
                BlockPressed = _current.Space && !_previous.Space,
                BlockReleased = !_current.Space && _previous.Space,
                SkillQ = _current.Q && !_previous.Q,
                SkillE = _current.E && !_previous.E,
                SkillR = _current.R && !_previous.R,
                SkillF = _current.F && !_previous.F,
                TabPressed = _current.Tab && !_previous.Tab,
                MapPressed = _current.Map && !_previous.Map,
                InventoryPressed = _current.Inventory && !_previous.Inventory,
                Item1 = _current.Digit1 && !_previous.Digit1,
                Item2 = _current.Digit2 && !_previous.Digit2,
                Item3 = _current.Digit3 && !_previous.Digit3,
                Item4 = _current.Digit4 && !_previous.Digit4
            };

            _previous = _current;
            _sampleCount++;
            return snapshot;
        }
    }
}
