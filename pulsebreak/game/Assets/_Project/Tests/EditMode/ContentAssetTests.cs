#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Tests
{
    /// <summary>
    /// Checks the real content assets, not test doubles. This is the test that fails when a
    /// new weapon, attachment, utility item, perk set or preset is added with a mistake in it.
    /// </summary>
    public class ContentAssetTests
    {
        const string CatalogPath = "Assets/_Project/Content/Loadouts/LoadoutCatalog.asset";

        static LoadoutCatalog LoadCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LoadoutCatalog>(CatalogPath);
            Assert.That(catalog, Is.Not.Null, "No loadout catalog at " + CatalogPath);
            Assert.That(catalog.weapons, Is.Not.Null, "The loadout catalog has no weapon catalog assigned");
            return catalog;
        }

        [Test]
        public void AllContent_PassesValidation()
        {
            List<string> problems = LoadoutValidator.Validate(LoadCatalog());
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void EveryLoadoutSlot_HasAtLeastOneWeapon()
        {
            WeaponCatalog weapons = LoadCatalog().weapons;
            Assert.That(weapons.FirstForSlot(WeaponSlotType.Primary), Is.Not.Null);
            Assert.That(weapons.FirstForSlot(WeaponSlotType.Secondary), Is.Not.Null);
            Assert.That(weapons.FirstForSlot(WeaponSlotType.Melee), Is.Not.Null);
        }

        [Test]
        public void EveryPreset_ResolvesExactlyAsAuthored()
        {
            LoadoutCatalog catalog = LoadCatalog();
            foreach (LoadoutPreset preset in catalog.presets)
            {
                var notes = new List<string>();
                Loadout loadout = LoadoutRules.Resolve(preset.ToData(), catalog, notes);

                Assert.That(notes, Is.Empty, preset.presetId + ": " + string.Join("; ", notes));
                for (int slot = 0; slot < Loadout.WeaponSlots; slot++)
                    Assert.That(loadout.ResolveStats(slot), Is.Not.Null, preset.presetId + " weapon slot " + slot);
            }
        }

        [Test]
        public void EveryWeapon_StaysPlayable_WithEachOfItsAttachments()
        {
            foreach (WeaponDefinition weapon in LoadCatalog().weapons.weapons)
            {
                foreach (AttachmentDefinition attachment in weapon.attachments)
                {
                    var build = new WeaponBuild(weapon);
                    Assert.That(build.Fit(attachment), Is.True);
                    WeaponStats stats = build.ResolveStats();

                    string label = weapon.weaponId + " + " + attachment.attachmentId;
                    Assert.That(stats.roundsPerMinute, Is.GreaterThan(0f), label);
                    Assert.That(stats.damage, Is.GreaterThan(0f), label);
                    Assert.That(stats.adsTime, Is.GreaterThan(0f), label);
                    if (weapon.stats.UsesAmmo) Assert.That(stats.magazineSize, Is.GreaterThan(0), label);
                }
            }
        }

        [Test]
        public void EveryWeapon_CanEmptyItsMagazine()
        {
            var context = new WeaponContext { CanUseWeapon = true, Grounded = true, TimeSinceSprint = 10f };

            foreach (WeaponDefinition weapon in LoadCatalog().weapons.weapons)
            {
                WeaponStats stats = new WeaponBuild(weapon).ResolveStats();
                if (!stats.UsesAmmo) continue;

                var simulator = new WeaponSimulator(stats, 1);
                int fired = 0;
                simulator.Fired += () => fired++;

                // Tap the trigger on and off; charge weapons need long enough holds to charge.
                int period = stats.fireMode == FireMode.Charge ? 50 : 4;
                for (int tick = 0; tick < SimConstants.TickRate * 90 && simulator.State.Phase != WeaponPhase.Reloading; tick++)
                {
                    bool held = stats.fireMode == FireMode.Automatic || (tick / period) % 2 == 0;
                    var cmd = new PlayerInputCmd { Tick = tick, Buttons = held ? InputButtons.Fire : InputButtons.None };
                    simulator.Simulate(cmd, context, SimConstants.TickDelta);
                }

                Assert.That(fired, Is.EqualTo(stats.magazineSize), weapon.weaponId + " should fire exactly one magazine before reloading");
            }
        }
    }
}
#endif
