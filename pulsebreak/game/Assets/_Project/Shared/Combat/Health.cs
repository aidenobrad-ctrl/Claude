using System;
using UnityEngine;

namespace Pulsebreak.Shared.Combat
{
    /// <summary>Hit points for anything that can be shot.</summary>
    [DisallowMultipleComponent]
    public sealed class Health : MonoBehaviour, IDamageable
    {
        [Min(1f)] public float maxHealth = 100f;

        public float Current { get; private set; }
        public bool IsAlive => Current > 0f;
        public float Fraction => maxHealth > 0f ? Current / maxHealth : 0f;

        public event Action<DamageInfo, DamageResult> Damaged;
        public event Action<DamageInfo> Died;
        public event Action Revived;

        void Awake()
        {
            Current = maxHealth;
        }

        public DamageResult ApplyDamage(in DamageInfo info)
        {
            if (!IsAlive || info.Amount <= 0f) return default;

            float applied = Mathf.Min(info.Amount, Current);
            Current -= applied;

            var result = new DamageResult { Applied = applied, Killed = Current <= 0f };
            Damaged?.Invoke(info, result);
            if (result.Killed) Died?.Invoke(info);
            return result;
        }

        public void Revive()
        {
            Current = maxHealth;
            Revived?.Invoke();
        }
    }
}
