using UnityEngine;
using Pulsebreak.Client.Player;
using Pulsebreak.Client.Settings;
using Pulsebreak.Client.UI;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Client.FiringRange
{
    /// <summary>
    /// Entry point of the firing range scene. Put this on one object in an otherwise empty
    /// scene and press Play: it builds the range, spawns the player with the chosen weapon,
    /// and puts up the HUD.
    ///
    /// The two assets below are the tuning surface for Phase 1. Swap in a different weapon
    /// definition or movement config to try other values.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class FiringRangeBootstrap : MonoBehaviour
    {
        [SerializeField] MovementConfig movement;
        [SerializeField] WeaponDefinition weapon;
        [Tooltip("Turn off if the scene already has its own directional light.")]
        [SerializeField] bool createSunLight = true;

        PlayerRig _rig;
        RangeStats _stats;
        HudController _hud;

        void Awake()
        {
            if (movement == null)
            {
                Debug.LogWarning("FiringRangeBootstrap: no Movement Config assigned, using built-in defaults.", this);
                movement = ScriptableObject.CreateInstance<MovementConfig>();
            }
            if (weapon == null)
            {
                Debug.LogWarning("FiringRangeBootstrap: no Weapon Definition assigned, using a default rifle.", this);
                weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
                weapon.weaponId = "fallback_rifle";
                weapon.displayName = "Fallback Rifle";
            }

            GameSettings.Load();
            GameLayers.ConfigureCollisionMatrix();
            RangeEnvironment.Apply(createSunLight);

            RangeLayout layout = RangeLayout.Build(transform);

            _rig = PlayerRigFactory.Create(layout.PlayerSpawn, layout.SpawnYaw, movement, weapon);
            _rig.Player.Weapon.InfiniteReserve = true;
            RangeEnvironment.ConfigureCamera(_rig.Camera.Camera);

            _stats = new RangeStats();
            _rig.Player.ShotFired += _ => _stats.RecordShot();
            _rig.Player.HitConfirmed += hit => _stats.RecordHit(hit);

            _hud = HudController.Create();
            _hud.Bind(_rig.Player, _rig.Camera.Camera, _stats);
            _hud.ResetStatsRequested += _stats.Reset;
            _hud.SetPaused(false);
        }
    }
}
