using System;
using UnityEngine;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Shared.Loadouts
{
    /// <summary>One utility slot's state.</summary>
    public struct UtilitySlotState
    {
        public int Charges;
        /// <summary>Time until this slot can be thrown again.</summary>
        public float Cooldown;
        /// <summary>Progress towards getting a charge back, in seconds.</summary>
        public float RechargeTimer;
    }

    /// <summary>
    /// Runs the two utility slots: charges, the wind-up of a throw, cooldowns and recharging.
    /// Like the weapon simulator it only decides when a throw is released; the caller turns
    /// that into a projectile.
    /// </summary>
    public sealed class UtilityBelt
    {
        readonly UtilityDefinition[] _items;
        readonly int[] _maxCharges;
        readonly UtilitySlotState[] _slots;
        int _throwing = -1;
        float _throwTimer;
        InputButtons _previousButtons;

        /// <summary>Presentation hook: a throw has started. Argument is the slot.</summary>
        public event Action<int> ThrowStarted;

        public int SlotCount => _items.Length;

        /// <summary>A throw is winding up. The weapon in hand cannot be used meanwhile.</summary>
        public bool IsThrowing => _throwing >= 0;

        /// <summary>The slot whose item left the hand in the most recent tick, or -1.</summary>
        public int ReleasedSlot { get; private set; } = -1;

        /// <summary>Used charges come back over time. On in the firing range, off in matches.</summary>
        public bool Recharges { get; set; }
        public float RechargeMultiplier { get; set; } = 1f;

        /// <param name="items">One item per slot. A slot may be null if nothing is carried there.</param>
        /// <param name="charges">How many of each are carried.</param>
        public UtilityBelt(UtilityDefinition[] items, int[] charges)
        {
            _items = items;
            _maxCharges = charges;
            _slots = new UtilitySlotState[items.Length];
            for (int i = 0; i < items.Length; i++) _slots[i].Charges = charges[i];
        }

        public UtilityDefinition GetItem(int slot) => _items[slot];
        public UtilitySlotState GetState(int slot) => _slots[slot];
        public int MaxCharges(int slot) => _maxCharges[slot];

        /// <summary>Advances the belt by one tick.</summary>
        /// <param name="canUse">False while the hands are busy with something else, such as a mantle.</param>
        public void Simulate(in PlayerInputCmd cmd, bool canUse, float dt)
        {
            ReleasedSlot = -1;
            InputButtons pressed = cmd.Buttons & ~_previousButtons;
            _previousButtons = cmd.Buttons;

            for (int i = 0; i < _slots.Length; i++)
            {
                UtilityDefinition item = _items[i];
                if (item == null) continue;

                _slots[i].Cooldown = Mathf.Max(0f, _slots[i].Cooldown - dt);

                if (!Recharges || item.rechargeTime <= 0f || _slots[i].Charges >= _maxCharges[i]) continue;
                _slots[i].RechargeTimer += dt * RechargeMultiplier;
                if (_slots[i].RechargeTimer >= item.rechargeTime)
                {
                    _slots[i].RechargeTimer = 0f;
                    _slots[i].Charges++;
                }
            }

            if (_throwing >= 0)
            {
                // Being interrupted (a mantle) cancels the throw without spending the charge.
                if (!canUse)
                {
                    _throwing = -1;
                    return;
                }

                _throwTimer -= dt;
                if (_throwTimer > 0f) return;

                _slots[_throwing].Charges--;
                _slots[_throwing].Cooldown = _items[_throwing].cooldown;
                ReleasedSlot = _throwing;
                _throwing = -1;
                return;
            }

            if (!canUse) return;
            int wanted = (pressed & InputButtons.Utility1) != 0 ? 0 : (pressed & InputButtons.Utility2) != 0 ? 1 : -1;
            if (wanted < 0 || wanted >= _slots.Length || _items[wanted] == null) return;
            if (_slots[wanted].Charges <= 0 || _slots[wanted].Cooldown > 0f) return;

            _throwing = wanted;
            _throwTimer = _items[wanted].throwTime;
            ThrowStarted?.Invoke(wanted);
        }
    }
}
