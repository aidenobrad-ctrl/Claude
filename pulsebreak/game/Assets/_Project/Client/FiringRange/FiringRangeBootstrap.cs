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
    /// scene and press Play: it builds the range, spawns the player with a starting loadout,
    /// and puts up the HUD.
    ///
    /// The catalog decides which weapons the armory offers. Open the pause menu in play to
    /// change weapons and attachments.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class FiringRangeBootstrap : MonoBehaviour
    {
        [SerializeField] MovementConfig movement;
        [SerializeField] WeaponCatalog catalog;
        [Header("Starting loadout (weapon ids)")]
        [SerializeField] string primaryWeaponId = "ar_hadron";
        [SerializeField] string secondaryWeaponId = "pistol_notch";
        [SerializeField] string meleeWeaponId = "melee_field_knife";
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
            if (catalog == null || catalog.weapons == null || catalog.weapons.Length == 0)
            {
                Debug.LogWarning("FiringRangeBootstrap: no Weapon Catalog assigned, using three built-in stand-in weapons.", this);
                catalog = BuildFallbackCatalog();
            }

            GameSettings.Load();
            GameLayers.ConfigureCollisionMatrix();
            RangeEnvironment.Apply(createSunLight);

            RangeLayout layout = RangeLayout.Build(transform);

            var loadout = new[]
            {
                Choose(primaryWeaponId, WeaponSlotType.Primary),
                Choose(secondaryWeaponId, WeaponSlotType.Secondary),
                Choose(meleeWeaponId, WeaponSlotType.Melee),
            };

            _rig = PlayerRigFactory.Create(layout.PlayerSpawn, layout.SpawnYaw, movement, loadout, new ProjectileSystem());
            _rig.Player.InfiniteReserve = true;
            RangeEnvironment.ConfigureCamera(_rig.Camera.Camera);

            _stats = new RangeStats();
            _rig.Player.WeaponFired += _ => _stats.RecordShot();
            _rig.Player.ShotLanded += _stats.RecordShotLanded;
            _rig.Player.HitConfirmed += hit => _stats.RecordHit(hit);

            _hud = HudController.Create();
            _hud.Bind(_rig.Player, _rig.Camera.Camera, _stats, catalog);
            _hud.ResetStatsRequested += _stats.Reset;
            _hud.SetPaused(false);
        }

        /// <summary>The named weapon if the catalog has it, otherwise the first one that fits the slot.</summary>
        WeaponBuild Choose(string weaponId, WeaponSlotType slot)
        {
            WeaponDefinition weapon = catalog.FindWeapon(weaponId);
            if (weapon == null || weapon.slot != slot) weapon = catalog.FirstForSlot(slot);
            if (weapon == null)
            {
                Debug.LogWarning("FiringRangeBootstrap: the catalog has no weapon for the " + slot + " slot.", this);
                return null;
            }
            return new WeaponBuild(weapon);
        }

        /// <summary>Keeps the range playable when the catalog asset is missing.</summary>
        static WeaponCatalog BuildFallbackCatalog()
        {
            var rifle = ScriptableObject.CreateInstance<WeaponDefinition>();
            rifle.weaponId = "fallback_rifle";
            rifle.displayName = "Fallback Rifle";

            var pistol = ScriptableObject.CreateInstance<WeaponDefinition>();
            pistol.weaponId = "fallback_pistol";
            pistol.displayName = "Fallback Pistol";
            pistol.weaponClass = WeaponClass.Pistol;
            pistol.slot = WeaponSlotType.Secondary;
            pistol.stats.fireMode = FireMode.SemiAuto;
            pistol.stats.roundsPerMinute = 400f;
            pistol.stats.magazineSize = 12;
            pistol.view.bodyLength = 0.18f;
            pistol.view.barrelLength = 0.04f;
            pistol.view.stockLength = 0f;
            pistol.view.bulk = 0.75f;

            var knife = ScriptableObject.CreateInstance<WeaponDefinition>();
            knife.weaponId = "fallback_knife";
            knife.displayName = "Fallback Knife";
            knife.weaponClass = WeaponClass.Melee;
            knife.slot = WeaponSlotType.Melee;
            knife.stats.ballistics = BallisticsType.Melee;
            knife.stats.fireMode = FireMode.SemiAuto;
            knife.stats.roundsPerMinute = 100f;
            knife.stats.damage = 55f;
            knife.stats.magazineSize = 0;
            knife.view.kind = ViewModelKind.Blade;
            knife.view.bodyLength = 0.2f;

            var fallback = ScriptableObject.CreateInstance<WeaponCatalog>();
            fallback.weapons = new[] { rifle, pistol, knife };
            return fallback;
        }
    }
}
