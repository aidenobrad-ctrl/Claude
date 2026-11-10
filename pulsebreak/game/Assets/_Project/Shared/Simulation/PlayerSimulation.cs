using System;
using UnityEngine;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Shared.Simulation
{
    /// <summary>
    /// Everything about one player that the simulation changes, in a form that can be saved,
    /// compared, sent over the network and put back.
    /// </summary>
    public sealed class PlayerSimState
    {
        public MotorState Motor;
        public int ActiveSlot;
        public int PendingSlot = -1;
        public readonly WeaponState[] Weapons = new WeaponState[Loadout.WeaponSlots];
        public readonly UtilitySlotState[] Utilities = new UtilitySlotState[Loadout.UtilitySlots];
        public int ThrowingSlot = -1;
        public float ThrowTimer;
        public InputButtons BeltButtons;

        /// <summary>
        /// True if a predicted state is close enough to what the server reported that there
        /// is nothing to correct.
        /// </summary>
        public bool Matches(PlayerSimState server) => Difference(server) == null;

        /// <summary>
        /// The first thing that differs between a predicted state and the server's, or null
        /// if nothing does. Counts must be exact; positions and timers get a little slack,
        /// because two machines do not always round a float the same way.
        /// </summary>
        public string Difference(PlayerSimState server)
        {
            const float Position = 0.002f, Speed = 0.02f, Time = 0.002f;

            if ((Motor.Position - server.Motor.Position).sqrMagnitude > Position * Position) return "position";
            if ((Motor.Velocity - server.Motor.Velocity).sqrMagnitude > Speed * Speed) return "velocity";
            if (Motor.Stance != server.Motor.Stance) return "stance";
            if (Motor.Grounded != server.Motor.Grounded) return "grounded";
            if (Motor.Mantling != server.Motor.Mantling) return "mantling";
            if (Mathf.Abs(Motor.EyeHeight - server.Motor.EyeHeight) > Position) return "eye height";
            if (Mathf.Abs(Motor.SlideTimer - server.Motor.SlideTimer) > Time) return "slide timer";
            if (Mathf.Abs(Motor.MantleTimer - server.Motor.MantleTimer) > Time) return "mantle timer";

            if (ActiveSlot != server.ActiveSlot || PendingSlot != server.PendingSlot) return "weapon in hand";
            for (int i = 0; i < Weapons.Length; i++)
            {
                WeaponState mine = Weapons[i], theirs = server.Weapons[i];
                if (mine.Phase != theirs.Phase) return "weapon phase";
                if (mine.AmmoInMagazine != theirs.AmmoInMagazine || mine.AmmoReserve != theirs.AmmoReserve) return "ammunition";
                if (mine.ShotCounter != theirs.ShotCounter || mine.BurstShotsRemaining != theirs.BurstShotsRemaining) return "shots fired";
                if (mine.MeleePending != theirs.MeleePending) return "melee swing";
                if (Mathf.Abs(mine.FireCooldown - theirs.FireCooldown) > Time) return "fire cooldown";
                if (Mathf.Abs(mine.PhaseTimer - theirs.PhaseTimer) > Time) return "weapon timer";
                if (Mathf.Abs(mine.ChargeAmount - theirs.ChargeAmount) > Time) return "charge";
            }

            if (ThrowingSlot != server.ThrowingSlot || Mathf.Abs(ThrowTimer - server.ThrowTimer) > Time) return "utility throw";
            for (int i = 0; i < Utilities.Length; i++)
            {
                if (Utilities[i].Charges != server.Utilities[i].Charges) return "utility charges";
                if (Mathf.Abs(Utilities[i].Cooldown - server.Utilities[i].Cooldown) > Time) return "utility cooldown";
            }
            return null;
        }

        public void CopyFrom(PlayerSimState other)
        {
            Motor = other.Motor;
            ActiveSlot = other.ActiveSlot;
            PendingSlot = other.PendingSlot;
            Array.Copy(other.Weapons, Weapons, Weapons.Length);
            Array.Copy(other.Utilities, Utilities, Utilities.Length);
            ThrowingSlot = other.ThrowingSlot;
            ThrowTimer = other.ThrowTimer;
            BeltButtons = other.BeltButtons;
        }
    }

    /// <summary>
    /// One player, simulated: the body, the weapons carried and the utility belt, advanced a
    /// tick at a time from input commands.
    ///
    /// The firing range, a client predicting its own player and the server all run this same
    /// class. That is what makes prediction possible: given the same state and the same
    /// commands, the client and the server arrive at the same place.
    /// </summary>
    public sealed class PlayerSimulation
    {
        readonly CharacterController _controller;
        uint _seed;
        WeaponSimulator[] _weapons = new WeaponSimulator[0];
        bool _infiniteReserve, _rechargeUtilities;

        public PlayerMotor Motor { get; }
        public Loadout Loadout { get; private set; }
        public WeaponInventory Inventory { get; private set; }
        public UtilityBelt Belt { get; private set; }

        /// <summary>Raised after <see cref="SetLoadout"/> has rebuilt the weapons and belt.</summary>
        public event Action LoadoutApplied;

        /// <summary>
        /// While set, none of the events below are raised. Prediction sets it while it replays
        /// commands after a correction, so a shot that was already heard is not heard again.
        /// </summary>
        public bool Muted { get; set; }

        // Presentation hooks. They survive a change of loadout, so listeners subscribe once.
        public event Action Jumped;
        public event Action<float> Landed;
        public event Action SlideStarted;
        public event Action MantleStarted;
        /// <summary>The trigger produced a shot (once per shot, however many pellets). Argument is the slot.</summary>
        public event Action<int> WeaponFired;
        public event Action ReloadStarted;
        public event Action RoundInserted;
        public event Action ReloadFinished;
        public event Action DryFired;
        /// <summary>The wind-up of a utility throw began. Argument is the utility slot.</summary>
        public event Action<int> ThrowStarted;
        /// <summary>A different weapon is now in hand. Argument is the slot.</summary>
        public event Action<int> ActiveChanged;

        /// <param name="seed">Seeds weapon spread. Client and server must use the same value for the same player.</param>
        public PlayerSimulation(CharacterController controller, MovementConfig movement, uint seed)
        {
            _controller = controller;
            _seed = seed;
            Motor = new PlayerMotor(controller, movement, GameLayers.WorldMask);
            Motor.Jumped += () => { if (!Muted) Jumped?.Invoke(); };
            Motor.Landed += impact => { if (!Muted) Landed?.Invoke(impact); };
            Motor.SlideStarted += () => { if (!Muted) SlideStarted?.Invoke(); };
            Motor.MantleStarted += () => { if (!Muted) MantleStarted?.Invoke(); };
        }

        /// <summary>
        /// The seed a player's weapons use in a match. The server and that player's own
        /// client both start from it, so both draw the same spread for the same shot.
        /// </summary>
        public static uint SeedFor(int playerId) => 0x5EED0000u + (uint)playerId * 16u;

        /// <summary>Takes effect the next time a loadout is set.</summary>
        public uint Seed
        {
            get => _seed;
            set => _seed = value;
        }

        public CharacterController Controller => _controller;
        public WeaponSimulator ActiveWeapon => Inventory.Active;
        public WeaponBuild ActiveBuild => Loadout.Weapons[Inventory.ActiveIndex];
        public int SlotCount => _weapons.Length;
        public WeaponSimulator GetWeapon(int slot) => _weapons[slot];

        /// <summary>The weapon simulated in the most recent step, and the build it came from. Read shots from here.</summary>
        public WeaponSimulator SteppedWeapon => Inventory.Simulated;
        public int SteppedSlot => Array.IndexOf(_weapons, Inventory.Simulated);
        public WeaponBuild SteppedBuild => Loadout.Weapons[SteppedSlot];

        /// <summary>Reloads never drain the reserve. Used by the firing range.</summary>
        public bool InfiniteReserve
        {
            get => _infiniteReserve;
            set
            {
                _infiniteReserve = value;
                foreach (WeaponSimulator weapon in _weapons)
                {
                    if (weapon != null) weapon.InfiniteReserve = value;
                }
            }
        }

        /// <summary>Used utility items come back over time. Used by the firing range.</summary>
        public bool RechargeUtilities
        {
            get => _rechargeUtilities;
            set
            {
                _rechargeUtilities = value;
                if (Belt != null) Belt.Recharges = value;
            }
        }

        /// <summary>Replaces everything carried. Weapons and utility start full.</summary>
        public void SetLoadout(Loadout loadout, int activeSlot)
        {
            Loadout = loadout;
            _weapons = new WeaponSimulator[loadout.Weapons.Length];
            for (int i = 0; i < _weapons.Length; i++)
            {
                if (loadout.Weapons[i] == null) continue;
                var weapon = new WeaponSimulator(loadout.ResolveStats(i), _seed + (uint)i) { InfiniteReserve = _infiniteReserve };
                int slot = i;
                weapon.Fired += () => { if (!Muted) WeaponFired?.Invoke(slot); };
                weapon.ReloadStarted += () => { if (!Muted) ReloadStarted?.Invoke(); };
                weapon.RoundInserted += () => { if (!Muted) RoundInserted?.Invoke(); };
                weapon.ReloadFinished += () => { if (!Muted) ReloadFinished?.Invoke(); };
                weapon.DryFired += () => { if (!Muted) DryFired?.Invoke(); };
                _weapons[i] = weapon;
            }
            Inventory = new WeaponInventory(_weapons, activeSlot);
            Inventory.ActiveChanged += slot => { if (!Muted) ActiveChanged?.Invoke(slot); };

            var charges = new int[loadout.Utilities.Length];
            for (int i = 0; i < charges.Length; i++) charges[i] = loadout.UtilityCharges(i);
            Belt = new UtilityBelt(loadout.Utilities, charges)
            {
                Recharges = _rechargeUtilities,
                RechargeMultiplier = loadout.UtilityRechargeMultiplier,
            };
            Belt.ThrowStarted += slot => { if (!Muted) ThrowStarted?.Invoke(slot); };

            LoadoutApplied?.Invoke();
        }

        /// <summary>Raises <see cref="ActiveChanged"/> for the weapon now in hand, for when it changed while muted.</summary>
        public void AnnounceActiveWeapon() => ActiveChanged?.Invoke(Inventory.ActiveIndex);

        /// <summary>
        /// One tick: movement, then the utility belt, then the weapon in hand. Afterwards,
        /// shots are in <see cref="SteppedWeapon"/> and a released throw in the belt; what
        /// they hit is for the caller to work out.
        /// </summary>
        public void Step(in PlayerInputCmd cmd, float dt)
        {
            WeaponSimulator inHand = Inventory.Active;
            var modifiers = new MoveModifiers
            {
                SpeedMultiplier = Loadout.MoveSpeedMultiplier * Mathf.Lerp(
                    inHand.Stats.hipMoveMultiplier, inHand.Stats.adsMoveMultiplier, inHand.State.AdsAmount),
            };
            Motor.Simulate(cmd, modifiers, dt);
            MotorState motor = Motor.State;

            // A throw winding up keeps the hands off the weapon.
            Belt.Simulate(cmd, !motor.Mantling, dt);

            var context = new WeaponContext
            {
                CanUseWeapon = !motor.Mantling && !Belt.IsThrowing,
                Sprinting = motor.Sprinting,
                TimeSinceSprint = motor.TimeSinceSprint,
                HorizontalSpeed = motor.HorizontalSpeed,
                Grounded = motor.Grounded,
                Crouched = motor.Stance != Stance.Standing,
            };
            Inventory.Simulate(cmd, context, dt);
        }

        public void Capture(PlayerSimState into)
        {
            into.Motor = Motor.State;
            into.ActiveSlot = Inventory.ActiveIndex;
            into.PendingSlot = Inventory.PendingIndex;
            for (int i = 0; i < into.Weapons.Length; i++)
                into.Weapons[i] = i < _weapons.Length && _weapons[i] != null ? _weapons[i].State : default;
            for (int i = 0; i < into.Utilities.Length; i++)
                into.Utilities[i] = i < Belt.SlotCount ? Belt.GetState(i) : default;
            into.ThrowingSlot = Belt.ThrowingSlot;
            into.ThrowTimer = Belt.ThrowTimer;
            into.BeltButtons = Belt.PreviousButtons;
        }

        public void Restore(PlayerSimState from)
        {
            Motor.SetState(from.Motor);
            for (int i = 0; i < _weapons.Length && i < from.Weapons.Length; i++)
            {
                if (_weapons[i] != null) _weapons[i].SetState(from.Weapons[i]);
            }
            Inventory.SetSlots(from.ActiveSlot, from.PendingSlot);
            Belt.SetState(from.Utilities, from.ThrowingSlot, from.ThrowTimer, from.BeltButtons);
        }
    }
}
