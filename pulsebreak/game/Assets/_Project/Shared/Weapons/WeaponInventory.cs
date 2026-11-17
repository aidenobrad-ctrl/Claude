using System;
using UnityEngine;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Shared.Weapons
{
    /// <summary>
    /// The weapons a player is carrying and which one is in their hands. Each slot keeps its
    /// own simulator, so ammo is remembered while a weapon is put away. Switching takes the
    /// old weapon's holster time followed by the new weapon's equip time.
    /// </summary>
    public sealed class WeaponInventory
    {
        readonly WeaponSimulator[] _slots;
        int _active;
        int _pending = -1;

        /// <summary>Presentation hook: the weapon in hand changed. Argument is the new slot index.</summary>
        public event Action<int> ActiveChanged;

        public int SlotCount => _slots.Length;
        public int ActiveIndex => _active;
        public WeaponSimulator Active => _slots[_active];
        public bool IsSwitching => _pending >= 0;

        /// <summary>The slot being switched to, or the active one when no switch is under way.</summary>
        public int TargetIndex => _pending >= 0 ? _pending : _active;

        /// <summary>
        /// The weapon that was simulated in the most recent tick. Read shots from this rather
        /// than <see cref="Active"/>, which may already have moved on to the next weapon.
        /// </summary>
        public WeaponSimulator Simulated { get; private set; }

        /// <param name="slots">One simulator per slot. A slot may be null if nothing is carried there.</param>
        public WeaponInventory(WeaponSimulator[] slots, int startIndex = 0)
        {
            _slots = slots;
            _active = Mathf.Clamp(startIndex, 0, slots.Length - 1);
            if (_slots[_active] == null)
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i] == null) continue;
                    _active = i;
                    break;
                }
            }

            Simulated = Active;
            Active.BeginEquip(Vector2.zero, Vector2.zero, InputButtons.None);
        }

        public WeaponSimulator Get(int index) => index >= 0 && index < _slots.Length ? _slots[index] : null;

        /// <summary>The slot being switched to, or -1.</summary>
        public int PendingIndex => _pending;

        /// <summary>Puts which weapon is in hand, and any switch under way, back to a saved state. Raises no event.</summary>
        public void SetSlots(int active, int pending)
        {
            if (active >= 0 && active < _slots.Length && _slots[active] != null) _active = active;
            _pending = pending >= 0 && pending < _slots.Length && _slots[pending] != null ? pending : -1;
            Simulated = Active;
        }

        /// <summary>Advances the weapon in hand by one tick, handling any request to switch.</summary>
        public void Simulate(in PlayerInputCmd cmd, in WeaponContext context, float dt)
        {
            int request = cmd.SelectSlot - 1;
            if (request >= 0 && request < _slots.Length && _slots[request] != null && request != TargetIndex)
            {
                if (request == _active)
                {
                    // Changed their mind halfway through putting it away: bring it back up.
                    _pending = -1;
                    WeaponState state = Active.State;
                    Active.BeginEquip(state.RecoilCurrent, state.RecoilTarget, cmd.Buttons);
                }
                else
                {
                    if (_pending < 0) Active.BeginHolster();
                    _pending = request;
                }
            }

            Simulated = Active;
            Active.Simulate(cmd, context, dt);

            if (_pending >= 0 && Active.HolsterComplete)
            {
                WeaponState previous = Active.State;
                _active = _pending;
                _pending = -1;
                Active.BeginEquip(previous.RecoilCurrent, previous.RecoilTarget, cmd.Buttons);
                ActiveChanged?.Invoke(_active);
            }
        }
    }
}
