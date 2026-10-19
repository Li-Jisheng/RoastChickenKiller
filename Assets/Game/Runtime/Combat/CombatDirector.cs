using System;
using System.Collections.Generic;
using UnityEngine;
using CK.Domain.Audio;
using CK.Domain.Combat;
using CK.Domain.Config;
using CK.Native;
using CK.Runtime.Actors;
using CK.Runtime.Audio;
using CK.Runtime.Input;
using CK.Runtime.View;

namespace CK.Runtime.Combat
{
    /// <summary>
    /// Unity host for the authoritative native combat core.
    /// Samples input at 60 Hz, submits commands/volumes, then renders the returned
    /// events and snapshot. No combat rule is evaluated here.
    /// </summary>
    public sealed class CombatDirector : MonoBehaviour
    {
        public const int PlayerActorId = 0;
        public const int EnemyActorId = 1;

        public CombatConfig Config { get; private set; }
        public NativeCombatBridge Bridge { get; private set; }
        public ActorView Player { get; private set; }
        public ActorView Enemy { get; private set; }
        public ActorSnapshot PlayerSnapshot { get; private set; }
        public ActorSnapshot EnemySnapshot { get; private set; }
        public IAudioService Audio { get; private set; }
        public PrototypeInput Input { get; private set; }
        public ScriptedEnemyDriver EnemyDriver { get; private set; }

        public bool Initialized { get; private set; }
        public bool HasError { get; private set; }
        public string ErrorText { get; private set; } = string.Empty;

        public int Tick => Initialized && Bridge != null ? (int)Bridge.TickIndex : 0;
        public int WeaponSlotIndex { get; private set; }
        public WeaponConfig CurrentWeapon { get; private set; }
        public int LastItemSlot { get; private set; }
        public string LastInputFeedback { get; private set; } = string.Empty;
        public string LastEventText { get; private set; } = "none";
        public int LastDamage { get; private set; }
        public bool LastOverlap { get; private set; }
        public CombatEventType LastEventType { get; private set; }
        public int BlockedCount { get; private set; }
        public int DodgedCount { get; private set; }
        public int HitTakenCount { get; private set; }
        public int HitDealtCount { get; private set; }
        public bool InventoryVisible { get; set; }
        public bool MapVisible { get; set; }
        public int WeaponSwitches { get; private set; }
        public int WeaponSwitchRejections { get; private set; }
        public float PlayerPlanarSpeed { get; private set; }
        public bool PlayerTouchingEnemy { get; private set; }
        public float DistanceToEnemy { get; private set; }
        public readonly List<CombatEvent> RecentEvents = new List<CombatEvent>();
        public readonly List<int> EnemySwingTicks = new List<int>();
        public readonly List<int> ItemSlotHistory = new List<int>();

        public event Action<CombatEvent> EventReceived;
        public event Action<int> WeaponSlotChanged;
        public event Action<int> ItemSlotTriggered;

        private readonly List<ActorFrameVolumes> _frameVolumes = new List<ActorFrameVolumes>(2);
        private readonly List<CombatEvent> _drained = new List<CombatEvent>(16);

        private Vector3 _playerSpawn;
        private float _playerYaw;
        private float _groundLimit;
        private float _verticalSpeed;
        private float _lastPlanarSpeed;
        private float _primaryPressTime;
        private bool _primaryHeld;
        private bool _heavyTriggered;
        private Vector3 _dodgeDirection;
        private int _dodgeAbilityIndex = -1;
        private AbilityConfig _dodgeAbility;
        private int _weaponSwitchRejectLogTick = -100;
        private uint _commandSequence;
        private bool _initialFocusLogged;

        public void Initialize(CombatConfig config, IAudioService audio, IPrototypeInputBackend backend,
            ActorView player, ActorView enemy, out string error)
        {
            Config = config;
            Audio = audio ?? new NullAudioService();
            Player = player;
            Enemy = enemy;
            error = string.Empty;
            HasError = false;

            _playerSpawn = player.transform.position;
            _playerYaw = player.transform.eulerAngles.y;
            _groundLimit = Mathf.Max(1f, config.World.GroundSize * 0.5f - player.BodyVolume.Radius);

            Bridge = new NativeCombatBridge();
            if (!Bridge.Initialize(config, out string bridgeError))
            {
                HasError = true;
                ErrorText = bridgeError;
                error = bridgeError;
                Debug.LogError("[CK] NativeCombatBridge failed: " + bridgeError);
                return;
            }

            Input = new PrototypeInput(backend ?? new DeviceInputBackend());
            WeaponSlotIndex = 0;
            ApplyWeaponSlot(0, false);
            if (Config.TryGetAbilityBySlot(AbilitySlot.Dodge, out AbilityConfig dodgeAbility))
            {
                _dodgeAbility = dodgeAbility;
                _dodgeAbilityIndex = Config.FindAbilityIndex(dodgeAbility.Id);
            }

            PlayerSnapshot = Bridge.GetSnapshot(PlayerActorId);
            EnemySnapshot = Bridge.GetSnapshot(EnemyActorId);

            EnemyDriver = gameObject.GetComponent<ScriptedEnemyDriver>();
            if (EnemyDriver == null)
            {
                EnemyDriver = gameObject.AddComponent<ScriptedEnemyDriver>();
            }
            if (!EnemyDriver.Initialize(this, config, out string driverError))
            {
                HasError = true;
                ErrorText = driverError;
                error = driverError;
                return;
            }

            Initialized = true;
            Debug.Log($"[CK] combat initialized: {config} nativeHash={Bridge.NativeConfigHash:X8} abi={NativeCombatBridge.GetAbiVersion()} actors={Bridge.ActorCount} tickRate={config.TickRate}");
            Debug.Log($"[CK] input backend: {Input.BackendName}");
        }

        private void FixedUpdate()
        {
            if (!Initialized || HasError)
            {
                return;
            }

            InputSnapshot input = Input.Sample();
            uint tick = Bridge.TickIndex;
            if (input.PrimaryPressed || input.PrimaryReleased || input.BlockPressed || input.BlockReleased ||
                input.DodgePressed || input.SkillQ || input.SkillE || input.SkillR || input.SkillF ||
                input.TabPressed || input.MapPressed || input.InventoryPressed ||
                input.Item1 || input.Item2 || input.Item3 || input.Item4 || tick % 60u == 0u)
            {
                Debug.Log($"[CK] input t{tick} move=({input.MoveX:0},{input.MoveZ:0}) block={input.BlockHeld} " +
                          $"primary={input.PrimaryPressed}/{input.PrimaryHeld}/{input.PrimaryReleased} " +
                          $"backend={Input.BackendName} polled={Input.LastPollSucceeded} samples={Input.SampleCount}");
            }

            // 1. Weapon / panel / item intents (host side, no combat rules).
            HandleHostInput(input);

            // 2. Player movement (uses the snapshot returned by the previous tick).
            UpdatePlayerMovement(input);

            // 3. Pose from the current snapshot, then derive the exact hit volumes.
            AbilityConfig playerAbility = GetAbility(PlayerSnapshot != null ? PlayerSnapshot.AbilityIndex : -1);
            AbilityConfig enemyAbility = GetAbility(EnemySnapshot != null ? EnemySnapshot.AbilityIndex : -1);
            Player.ApplySwordPose(playerAbility, PlayerSnapshot, PlayerSnapshot != null && PlayerSnapshot.Blocking);
            Enemy.ApplySwordPose(enemyAbility, EnemySnapshot, false);

            _frameVolumes.Clear();
            _frameVolumes.Add(new ActorFrameVolumes
            {
                ActorId = PlayerActorId,
                Body = Player.GetBodyCapsule(),
                Weapon = Player.GetWeaponCapsule(out bool playerWeaponActive),
                WeaponActiveHint = playerWeaponActive ? 1 : 0
            });
            _frameVolumes.Add(new ActorFrameVolumes
            {
                ActorId = EnemyActorId,
                Body = Enemy.GetBodyCapsule(),
                Weapon = Enemy.GetWeaponCapsule(out bool enemyWeaponActive),
                WeaponActiveHint = enemyWeaponActive ? 1 : 0
            });

            // 4. Combat commands (tap/hold, dodge, skills, block) and enemy script.
            SubmitInputCommands(input, tick);
            EnemyDriver.SubmitIfDue(tick);

            // 5. Authoritative tick.
            Bridge.Tick(_frameVolumes);

            // 6. Consume results.
            _drained.Clear();
            Bridge.DrainEvents(_drained);
            if (_drained.Count > 0)
            {
                for (int i = 0; i < _drained.Count; i++)
                {
                    HandleEvent(_drained[i]);
                }
            }
            PlayerSnapshot = Bridge.GetSnapshot(PlayerActorId);
            EnemySnapshot = Bridge.GetSnapshot(EnemyActorId);
            if (PlayerSnapshot.Busy && PlayerSnapshot.AbilityIndex == _dodgeAbilityIndex &&
                PlayerSnapshot.AbilityFrame <= Config.Combat.DodgeInvulnFrames + 2)
            {
                Debug.Log($"[CK] dodge state t={PlayerSnapshot.AbilityFrame} inv={PlayerSnapshot.Invulnerable} " +
                          $"phase={PlayerSnapshot.Phase} enemyFrame={EnemySnapshot.AbilityFrame} tick={Bridge.TickIndex}");
            }
            UpdateDistances();
        }

        private void Update()
        {
            if (!Initialized || HasError)
            {
                return;
            }
            Player.TickVisual();
            Enemy.TickVisual();
            if (!_initialFocusLogged)
            {
                _initialFocusLogged = true;
                Debug.Log($"[CK] device={SystemInfo.graphicsDeviceType} gfx={SystemInfo.graphicsDeviceName} resolution={Screen.width}x{Screen.height} scene=CombatPrototype");
            }
        }

        private void HandleHostInput(InputSnapshot input)
        {
            if (input.SkillQ || input.SkillE || input.SkillR || input.SkillF)
            {
                if (input.SkillQ)
                {
                    LastInputFeedback = "Q";
                }
                else if (input.SkillE)
                {
                    LastInputFeedback = "E";
                }
                else if (input.SkillR)
                {
                    LastInputFeedback = "R";
                }
                else
                {
                    LastInputFeedback = "F";
                }
            }
            if (input.DodgePressed)
            {
                LastInputFeedback = "Dodge(RMB/Shift)";
            }
            if (input.BlockPressed)
            {
                LastInputFeedback = "Block down";
            }
            if (input.BlockReleased)
            {
                LastInputFeedback = "Block up";
            }

            if (input.TabPressed)
            {
                TrySwitchWeapon();
            }
            if (input.MapPressed)
            {
                MapVisible = !MapVisible;
                if (MapVisible)
                {
                    InventoryVisible = false;
                }
                LastInputFeedback = MapVisible ? "M: map open" : "M: map closed";
                Debug.Log($"[CK] map panel {(MapVisible ? "opened" : "closed")} at tick {Tick}");
            }
            if (input.InventoryPressed)
            {
                InventoryVisible = !InventoryVisible;
                if (InventoryVisible)
                {
                    MapVisible = false;
                }
                LastInputFeedback = InventoryVisible ? "B: inventory open" : "B: inventory closed";
                Debug.Log($"[CK] inventory panel {(InventoryVisible ? "opened" : "closed")} at tick {Tick}");
            }

            int item = 0;
            if (input.Item1)
            {
                item = 1;
            }
            else if (input.Item2)
            {
                item = 2;
            }
            else if (input.Item3)
            {
                item = 3;
            }
            else if (input.Item4)
            {
                item = 4;
            }
            if (item > 0)
            {
                LastItemSlot = item;
                ItemSlotHistory.Add(item);
                LastInputFeedback = $"Item {item}" + (item <= Config.Loadout.ItemBarSlots ? " (empty)" : " (out of range)");
                ItemSlotTriggered?.Invoke(item);
                Debug.Log($"[CK] item bar {item}/{Config.Loadout.ItemBarSlots} at tick {Tick} (empty slot)");
            }
        }

        private void UpdatePlayerMovement(InputSnapshot input)
        {
            Camera camera = Camera.main;
            Vector3 forward = camera != null ? Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 right = camera != null ? Vector3.ProjectOnPlane(camera.transform.right, Vector3.up).normalized : Vector3.right;
            Vector3 move = (right * input.MoveX + forward * input.MoveZ);
            if (move.sqrMagnitude > 1f)
            {
                move.Normalize();
            }

            bool dodging = PlayerSnapshot != null && PlayerSnapshot.Busy && PlayerSnapshot.AbilityIndex == _dodgeAbilityIndex;
            Vector3 displacement;
            if (dodging && _dodgeAbility != null)
            {
                float dodgeSpeed = Config.Combat.DodgeDistance * Config.TickRate / Mathf.Max(1, _dodgeAbility.TotalFrames);
                displacement = _dodgeDirection * (dodgeSpeed * Time.fixedDeltaTime);
                _lastPlanarSpeed = dodgeSpeed;
            }
            else
            {
                float speed = Config.Actors.Player.MoveSpeed;
                if (PlayerSnapshot != null && PlayerSnapshot.Blocking)
                {
                    speed *= Config.Combat.BlockMoveMultiplier;
                }
                displacement = move * (speed * Time.fixedDeltaTime);
                _lastPlanarSpeed = displacement.magnitude / Mathf.Max(0.0001f, Time.fixedDeltaTime);
                if (move.sqrMagnitude > 0.001f)
                {
                    Player.FaceDirection(move);
                }
            }

            bool wasGrounded = Player.Controller != null && Player.Controller.isGrounded;
            _verticalSpeed += Config.World.Gravity * Time.fixedDeltaTime;
            if (wasGrounded && _verticalSpeed < 0f)
            {
                _verticalSpeed = 0f;
            }
            displacement.y = _verticalSpeed * Time.fixedDeltaTime;
            Player.Move(displacement);

            Vector3 position = Player.transform.position;
            float clampedX = Mathf.Clamp(position.x, -_groundLimit, _groundLimit);
            float clampedZ = Mathf.Clamp(position.z, -_groundLimit, _groundLimit);
            if (Mathf.Abs(position.x - clampedX) > 0.0001f || Mathf.Abs(position.z - clampedZ) > 0.0001f)
            {
                Player.Teleport(new Vector3(clampedX, position.y, clampedZ));
                LastInputFeedback = "Ground boundary";
            }
            PlayerPlanarSpeed = _lastPlanarSpeed;
        }

        private void SubmitInputCommands(InputSnapshot input, uint tick)
        {
            // Tap / hold classification: release before threshold = primary,
            // crossing the threshold while held = heavy (exactly once).
            if (input.PrimaryPressed)
            {
                _primaryHeld = true;
                _heavyTriggered = false;
                _primaryPressTime = Time.time;
            }
            if (_primaryHeld && !_heavyTriggered && Time.time - _primaryPressTime >= Config.Combat.HeavyHoldSeconds)
            {
                _heavyTriggered = true;
                Submit(CombatCommandKind.Heavy, tick);
                LastInputFeedback = "LMB hold -> Heavy";
            }
            if (input.PrimaryReleased && _primaryHeld)
            {
                _primaryHeld = false;
                if (!_heavyTriggered)
                {
                    Submit(CombatCommandKind.Primary, tick);
                    LastInputFeedback = "LMB tap -> Primary";
                }
                _heavyTriggered = false;
            }

            if (input.DodgePressed)
            {
                Vector3 direction = GetInputDirection(input);
                _dodgeDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : -Player.transform.forward;
                if (_dodgeAbilityIndex < 0 && Config.TryGetAbilityBySlot(AbilitySlot.Dodge, out AbilityConfig dodge))
                {
                    _dodgeAbilityIndex = Config.FindAbilityIndex(dodge.Id);
                    _dodgeAbility = dodge;
                }
                Submit(CombatCommandKind.Dodge, tick);
            }
            if (input.SkillQ)
            {
                Submit(CombatCommandKind.Skill1, tick);
            }
            if (input.SkillE)
            {
                Submit(CombatCommandKind.Skill2, tick);
            }
            if (input.SkillR)
            {
                Submit(CombatCommandKind.Skill3, tick);
            }
            if (input.SkillF)
            {
                Submit(CombatCommandKind.Skill4, tick);
            }
            if (input.BlockPressed)
            {
                Submit(CombatCommandKind.BlockPress, tick);
            }
            if (input.BlockReleased)
            {
                Submit(CombatCommandKind.BlockRelease, tick);
            }
        }

        private Vector3 GetInputDirection(InputSnapshot input)
        {
            Camera camera = Camera.main;
            Vector3 forward = camera != null ? Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 right = camera != null ? Vector3.ProjectOnPlane(camera.transform.right, Vector3.up).normalized : Vector3.right;
            return right * input.MoveX + forward * input.MoveZ;
        }

        private void Submit(CombatCommandKind kind, uint tick)
        {
            _commandSequence++;
            Bridge.SubmitCommand(PlayerActorId, kind, tick, _commandSequence);
            Debug.Log($"[CK] cmd t{tick} #{_commandSequence} {kind}");
        }

        private void HandleEvent(CombatEvent evt)
        {
            RecentEvents.Add(evt);
            if (RecentEvents.Count > 32)
            {
                RecentEvents.RemoveAt(0);
            }
            EventReceived?.Invoke(evt);

            LastEventType = evt.Type;
            LastOverlap = evt.Overlap;
            if (evt.Type == CombatEventType.Hit)
            {
                LastDamage = Mathf.RoundToInt(evt.Amount);
                if (evt.TargetActor == PlayerActorId)
                {
                    HitTakenCount++;
                    Player.Flash(new Color(1f, 0.35f, 0.35f), 0.25f);
                }
                else
                {
                    HitDealtCount++;
                    Enemy.Flash(Color.white, 0.2f);
                }
            }
            else if (evt.Type == CombatEventType.Blocked)
            {
                LastDamage = 0;
                BlockedCount++;
                Player.Flash(new Color(0.5f, 0.8f, 1f), 0.2f);
            }
            else if (evt.Type == CombatEventType.Dodged)
            {
                LastDamage = 0;
                DodgedCount++;
                Player.Flash(new Color(0.5f, 1f, 0.6f), 0.2f);
            }

            if (evt.Type == CombatEventType.AbilityStarted)
            {
                AbilityConfig ability = GetAbility(evt.AbilityIndex);
                if (ability != null)
                {
                    if (evt.SourceActor == EnemyActorId && ability.Slot == AbilitySlot.EnemyAttack)
                    {
                        EnemySwingTicks.Add((int)evt.Tick);
                        Debug.Log($"[CK] enemy swing started tick={evt.Tick} intervalLog={FormatSwingIntervals()}");
                    }
                    if (ability.Slot == AbilitySlot.Skill4)
                    {
                        Player.Flash(Color.white, 0.35f);
                        Player.Sword?.Flash(Color.white, 0.35f);
                    }
                }
            }

            switch (evt.Type)
            {
                case CombatEventType.Hit:
                    Audio.PlayCue(AudioCues.Hit);
                    break;
                case CombatEventType.Blocked:
                    Audio.PlayCue(AudioCues.Blocked);
                    break;
                case CombatEventType.Dodged:
                    Audio.PlayCue(AudioCues.Dodged);
                    break;
                case CombatEventType.BuffApplied:
                    Audio.PlayCue(AudioCues.BuffApplied);
                    break;
                case CombatEventType.BuffExpired:
                    Audio.PlayCue(AudioCues.BuffExpired);
                    break;
                case CombatEventType.DodgeStarted:
                    Audio.PlayCue(AudioCues.Dodge);
                    break;
            }

            string target = evt.TargetActor == PlayerActorId ? "player" : evt.TargetActor == EnemyActorId ? "enemy" : "-";
            LastEventText =
                $"t{evt.Tick} {evt.Type} src={evt.SourceActor}->{target} dmg={evt.Amount:0} overlap={(evt.Overlap ? 1 : 0)} def={evt.DefenseBefore}->{evt.DefenseAfter} hp={evt.HealthAfter}";
            Debug.Log($"[CK] event {LastEventText} reason={evt.Reason} buff={evt.BuffIndex}");
        }

        public string FormatSwingIntervals()
        {
            if (EnemySwingTicks.Count < 2)
            {
                return EnemySwingTicks.Count == 1 ? EnemySwingTicks[0].ToString() : "none";
            }
            var parts = new List<string>();
            int start = Math.Max(1, EnemySwingTicks.Count - 4);
            for (int i = start; i < EnemySwingTicks.Count; i++)
            {
                parts.Add($"{EnemySwingTicks[i]} (+{EnemySwingTicks[i] - EnemySwingTicks[i - 1]})");
            }
            return string.Join(", ", parts);
        }

        private void TrySwitchWeapon()
        {
            if (PlayerSnapshot == null)
            {
                return;
            }
            if (PlayerSnapshot.Busy || PlayerSnapshot.Blocking)
            {
                WeaponSwitchRejections++;
                LastInputFeedback = "Tab: switch locked (action)";
                if (Tick - _weaponSwitchRejectLogTick > 15)
                {
                    _weaponSwitchRejectLogTick = Tick;
                    Debug.Log($"[CK] weapon switch rejected at tick {Tick} (busy={PlayerSnapshot.Busy} blocking={PlayerSnapshot.Blocking})");
                }
                return;
            }
            int next = (WeaponSlotIndex + 1) % Mathf.Max(1, Config.Loadout.WeaponSlots);
            ApplyWeaponSlot(next, true);
        }

        private void ApplyWeaponSlot(int slot, bool log)
        {
            WeaponSlotIndex = slot;
            CurrentWeapon = slot >= 0 && slot < Config.Weapons.Count ? Config.Weapons[slot] : null;
            if (Player != null)
            {
                Player.SetWeapon(CurrentWeapon);
            }
            if (log)
            {
                WeaponSwitches++;
                Audio.PlayCue(AudioCues.WeaponSwitch);
                LastInputFeedback = CurrentWeapon != null
                    ? $"Tab: weapon slot {slot + 1}/{Config.Loadout.WeaponSlots} {CurrentWeapon.DisplayName} len={CurrentWeapon.Length:0.##}"
                    : $"Tab: weapon slot {slot + 1}/{Config.Loadout.WeaponSlots} EMPTY";
                Debug.Log($"[CK] weapon slot -> {slot + 1}/{Config.Loadout.WeaponSlots} " +
                          (CurrentWeapon != null ? $"{CurrentWeapon.Id} len={CurrentWeapon.Length} r={CurrentWeapon.Radius}" : "<empty>"));
            }
            WeaponSlotChanged?.Invoke(slot);
        }

        public AbilityConfig GetAbility(int index)
        {
            if (Config == null || index < 0 || index >= Config.Abilities.Count)
            {
                return null;
            }
            return Config.Abilities[index];
        }

        public void UpdateDistances()
        {
            Vector3 a = Player.transform.position;
            Vector3 b = Enemy.transform.position;
            Vector3 delta = b - a;
            delta.y = 0f;
            DistanceToEnemy = delta.magnitude;
            float contactRadius = Player.BodyVolume.Radius + Enemy.BodyVolume.Radius;
            float skin = Player.Controller != null ? Mathf.Max(0f, Player.Controller.skinWidth) : 0f;
            PlayerTouchingEnemy = DistanceToEnemy <= contactRadius + skin + 0.12f;
        }

        /// <summary>Resets native state and returns both actors to their spawn transform.</summary>
        public void ResetCombat()
        {
            if (!Initialized)
            {
                return;
            }
            Bridge.Reset();
            Player.Teleport(_playerSpawn);
            Player.transform.rotation = Quaternion.Euler(0f, _playerYaw, 0f);
            Enemy.transform.position = new Vector3(Config.World.EnemySpawn.X, Config.World.EnemySpawn.Y, Config.World.EnemySpawn.Z);
            Enemy.transform.rotation = Quaternion.Euler(0f, Config.World.EnemyYaw, 0f);
            _verticalSpeed = 0f;
            _primaryHeld = false;
            _heavyTriggered = false;
            _commandSequence = 0;
            PlayerSnapshot = Bridge.GetSnapshot(PlayerActorId);
            EnemySnapshot = Bridge.GetSnapshot(EnemyActorId);
            RecentEvents.Clear();
            EnemySwingTicks.Clear();
            if (EnemyDriver != null)
            {
                EnemyDriver.ResetSchedule();
            }
            LastDamage = 0;
            LastOverlap = false;
            LastEventType = CombatEventType.None;
            LastEventText = "reset";
            BlockedCount = 0;
            DodgedCount = 0;
            HitTakenCount = 0;
            HitDealtCount = 0;
            InventoryVisible = false;
            MapVisible = false;
            Debug.Log($"[CK] combat reset at tick {Bridge.TickIndex}");
        }

        public void SetPlayerPosition(Vector3 position)
        {
            Player.Teleport(position);
            _verticalSpeed = 0f;
            UpdateDistances();
        }
    }
}
