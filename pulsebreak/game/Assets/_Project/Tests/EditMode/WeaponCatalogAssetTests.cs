#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Tests
{
    /// <summary>
    /// Checks the real weapon and attachment assets, not test doubles. This is the test that
    /// fails when a new weapon is added with a typo in its numbers.
    /// </summary>
    public class WeaponCatalogAssetTests
    {
        const string CatalogPath = "Assets/_Project/Content/Weapons/WeaponCatalog.asset";

        static WeaponCatalog LoadCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<WeaponCatalog>(CatalogPath);
            Assert.That(catalog, Is.Not.Null, "No weapon catalog at " + CatalogPath);
            return catalog;
        }

        [Test]
        public void EveryWeaponAndAttachment_PassesValidation()
        {
            List<string> problems = WeaponValidator.Validate(LoadCatalog());
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void EveryLoadoutSlot_HasAtLeastOneWeapon()
        {
            WeaponCatalog catalog = LoadCatalog();
            Assert.That(catalog.FirstForSlot(WeaponSlotType.Primary), Is.Not.Null);
            Assert.That(catalog.FirstForSlot(WeaponSlotType.Secondary), Is.Not.Null);
            Assert.That(catalog.FirstForSlot(WeaponSlotType.Melee), Is.Not.Null);
        }

        [Test]
        public void EveryWeapon_StaysPlayable_WithEachOfItsAttachments()
        {
            foreach (WeaponDefinition weapon in LoadCatalog().weapons)
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

            foreach (WeaponDefinition weapon in LoadCatalog().weapons)
            {
                WeaponStats stats = new WeaponBuild(weapon).ResolveStats();
                if (!stats.UsesAmmo) continue;

                var simulator = new WeaponSimulator(stats, 1);
                int fired = 0;
                simulator.Fired += () => fired++;

                // Tap the trigger on and off for a minute of game time; every fire mode responds to that.
                for (int tick = 0; tick < SimConstants.TickRate * 60 && simulator.State.Phase != WeaponPhase.Reloading; tick++)
                {
                    bool held = stats.fireMode == FireMode.Automatic || (tick / 4) % 2 == 0;
                    var cmd = new PlayerInputCmd { Tick = tick, Buttons = held ? InputButtons.Fire : InputButtons.None };
                    simulator.Simulate(cmd, context, SimConstants.TickDelta);
                }

                Assert.That(fired, Is.EqualTo(stats.magazineSize), weapon.weaponId + " should fire exactly one magazine before reloading");
            }
        }
    }
}
#endif
