using NUnit.Framework;
using Pulsebreak.Client.Loadouts;

namespace Pulsebreak.Tests
{
    /// <summary>The game lists only the player's own loadouts; the presets are starting points kept out of sight.</summary>
    public class OwnLoadoutTests
    {
        [Test]
        public void AFirstRun_StartsWithALoadoutOfItsOwn_AndAPresetLeftEquippedBecomesACopy()
        {
            var store = new MemoryLoadoutStore();
            var library = new LoadoutLibrary(LoadoutTestKit.MakeCatalog(), store);
            Assert.That(library.IsPreset(library.EquippedId), "a fresh library starts on the first preset");
            int changes = 0;
            bool sawPreset = false;
            void Watch()
            {
                changes++;
                sawPreset |= library.IsPreset(library.EquippedId);
            }
            library.Changed += Watch;

            Assert.That(library.EnsureOwnLoadout(), Is.True);
            Assert.That(library.Custom.Count, Is.EqualTo(1));
            Assert.That(library.EquippedId, Is.EqualTo(library.Custom[0].id));
            Assert.That(library.Custom[0].primary.weaponId, Is.EqualTo(library.Presets[0].primary.weaponId));
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(sawPreset, Is.False, "listeners never see it half done");
            Assert.That(library.EnsureOwnLoadout(), Is.False, "already has one");
            library.Changed -= Watch;

            // Equipping a preset (an older save) turns it into the player's own copy of that preset.
            library.Equip(library.Presets[1].id);
            Assert.That(library.EnsureOwnLoadout(), Is.True);
            Assert.That(library.Custom.Count, Is.EqualTo(2));
            Assert.That(library.Equipped.primary.weaponId, Is.EqualTo(library.Presets[1].primary.weaponId));
            Assert.That(library.IsPreset(library.EquippedId), Is.False);
            Assert.That(store.Saved.loadouts.Length, Is.EqualTo(2), "and it is saved");
        }
    }
}
