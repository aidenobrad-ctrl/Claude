using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Pulsebreak.Client.Cosmetics;
using Pulsebreak.Shared.Cosmetics;
using Pulsebreak.Shared.Net;

namespace Pulsebreak.Tests
{
    /// <summary>The cosmetic catalog, the rules for a look, how a look is sent, and where it is saved.</summary>
    public class CosmeticsTests
    {
        [Test]
        public void EverySlot_HasItems_WithUniqueIds_AndTheDefaultLookIsMadeOfRealItems()
        {
            foreach (CosmeticSlot slot in CosmeticCatalog.Slots)
            {
                IReadOnlyList<CosmeticItem> items = CosmeticCatalog.InSlot(slot);
                Assert.That(items.Count, Is.InRange(2, 255), slot + ": items are sent as one byte");
                var ids = new HashSet<string>();
                for (int i = 0; i < items.Count; i++)
                {
                    Assert.That(ids.Add(items[i].Id), Is.True, slot + " has two items called " + items[i].Id);
                    Assert.That(items[i].Index, Is.EqualTo(i));
                    Assert.That(items[i].Name, Is.Not.Empty);
                    Assert.That(CosmeticCatalog.At(slot, i), Is.SameAs(items[i]));
                }
            }

            var look = new CharacterLook();
            foreach (CosmeticSlot slot in CosmeticCatalog.Slots)
                Assert.That(CosmeticCatalog.Find(slot, look.Get(slot)), Is.Not.Null, slot + " default '" + look.Get(slot) + "' is not in the catalog");
            Assert.That(CosmeticCatalog.Default(CosmeticSlot.WeaponSkin).Factory, Is.True, "weapons start in their own colours");
            Assert.That(CosmeticCatalog.Default(CosmeticSlot.Headgear).Shape, Is.EqualTo((byte)HeadgearKind.None));
        }

        [Test]
        public void ALook_IsCleanedUp_WhateverItSays()
        {
            var messy = new CharacterLook
            {
                suitColor = "not-a-colour",
                pattern = "tiger",
                headgear = null,
                weaponSkin = "gilded",
                weaponSkins = new[]
                {
                    new WeaponSkinChoice { weaponId = "ar_hadron", skinId = "carbon" },
                    new WeaponSkinChoice { weaponId = "ar_hadron", skinId = "magma" },        // a second skin for the same weapon
                    new WeaponSkinChoice { weaponId = "smg_sprocket", skinId = "gilded" },    // the same as every weapon already has
                    new WeaponSkinChoice { weaponId = "sg_breaker", skinId = "no-such-skin" },
                    new WeaponSkinChoice { weaponId = new string('x', 80), skinId = "carbon" },
                    null,
                },
            };

            CharacterLook clean = LookRules.Sanitize(messy);
            Assert.That(clean.suitColor, Is.EqualTo(CosmeticCatalog.Default(CosmeticSlot.SuitColor).Id));
            Assert.That(clean.pattern, Is.EqualTo("tiger"));
            Assert.That(clean.headgear, Is.EqualTo("none"));
            Assert.That(clean.weaponSkins.Length, Is.EqualTo(1));
            Assert.That(clean.SkinFor("ar_hadron").Id, Is.EqualTo("carbon"));
            Assert.That(clean.SkinFor("smg_sprocket").Id, Is.EqualTo("gilded"));
            Assert.That(clean.SkinFor("anything_else").Id, Is.EqualTo("gilded"));
            Assert.That(LookRules.Sanitize(null), Is.Not.Null);

            var crowded = new CharacterLook { weaponSkins = new WeaponSkinChoice[100] };
            for (int i = 0; i < crowded.weaponSkins.Length; i++)
                crowded.weaponSkins[i] = new WeaponSkinChoice { weaponId = "weapon_" + i, skinId = "carbon" };
            Assert.That(LookRules.Sanitize(crowded).weaponSkins.Length, Is.EqualTo(LookRules.MaxWeaponSkins));
        }

        [Test]
        public void WeaponSkins_CanBeSetOneAtATime_OrForEveryWeapon()
        {
            var look = new CharacterLook();
            look.SetSkin("ar_hadron", "tiger");
            look.SetSkin("pistol_notch", "neon_grid");
            Assert.That(look.SkinFor("ar_hadron").Id, Is.EqualTo("tiger"));
            Assert.That(look.SkinFor("pistol_notch").Id, Is.EqualTo("neon_grid"));
            Assert.That(look.SkinFor("sg_breaker").Factory, Is.True);

            look.SetSkin("ar_hadron", "factory");
            Assert.That(look.weaponSkins.Length, Is.EqualTo(1), "choosing the skin every weapon has removes the weapon's own");

            look.SetSkinForAll("arctic");
            Assert.That(look.weaponSkins, Is.Empty);
            Assert.That(look.SkinFor("pistol_notch").Id, Is.EqualTo("arctic"));
        }

        [Test]
        public void ALook_SurvivesTheTrip_InUnderAKilobyte()
        {
            var look = new CharacterLook
            {
                suitColor = "cobalt", trimColor = "saffron", pattern = "circuit", headgear = "horned_helm", visor = "magenta",
                back = "long_scarf", weaponSkin = "carbon",
            };
            for (int i = 0; i < LookRules.MaxWeaponSkins; i++) look.SetSkin("weapon_number_" + i.ToString("00"), i % 2 == 0 ? "tiger" : "nebula");

            var writer = new NetWriter(64);
            NetMessages.WriteLook(writer, look);
            Assert.That(writer.Length, Is.LessThan(1100), "a look must fit in a player's info with room to spare");

            var reader = new NetReader();
            reader.Reset(writer.Data, 0, writer.Length);
            CharacterLook read = NetMessages.ReadLook(reader);
            Assert.That(reader.Failed, Is.False);
            Assert.That(reader.Remaining, Is.EqualTo(0));
            foreach (CosmeticSlot slot in CosmeticCatalog.Slots) Assert.That(read.Get(slot), Is.EqualTo(look.Get(slot)), slot.ToString());
            Assert.That(read.weaponSkins.Length, Is.EqualTo(LookRules.MaxWeaponSkins));
            Assert.That(read.SkinFor("weapon_number_03").Id, Is.EqualTo("nebula"));

            // Cut short anywhere, it fails cleanly and gives a usable look.
            for (int length = 0; length < writer.Length; length++)
            {
                reader.Reset(writer.Data, 0, length);
                CharacterLook partial = NetMessages.ReadLook(reader);
                Assert.That(reader.Failed, Is.True, "cut to " + length);
                Assert.That(partial, Is.Not.Null);
            }

            // An item this copy of the game does not have (a newer version's) becomes the default.
            var odd = new NetWriter(16);
            for (int i = 0; i < CosmeticCatalog.Slots.Length; i++) odd.WriteByte(250);
            odd.WriteByte(0);
            reader.Reset(odd.Data, 0, odd.Length);
            CharacterLook unknown = NetMessages.ReadLook(reader);
            Assert.That(reader.Failed, Is.False);
            Assert.That(unknown.headgear, Is.EqualTo("none"));
        }

        [Test]
        public void RandomLooks_AreRealItems_AndRepeatForTheSameSeed()
        {
            var seen = new HashSet<string>();
            for (uint seed = 1; seed < 60; seed++)
            {
                CharacterLook look = LookRules.Random(seed);
                Assert.That(LookRules.Sanitize(look).suitColor, Is.EqualTo(look.suitColor));
                Assert.That(look.trimColor, Is.Not.EqualTo(look.suitColor));
                Assert.That(LookRules.Random(seed).headgear, Is.EqualTo(look.headgear));
                seen.Add(look.headgear);
            }
            Assert.That(seen.Count, Is.GreaterThan(3), "bots should not all look alike");
        }

        [Test]
        public void TheLook_IsSaved_AndComesBack_AndABadFileIsKeptAside()
        {
            string folder = Path.Combine(Path.GetTempPath(), "pb-look-" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            string file = Path.Combine(folder, CosmeticsLibrary.FileName);
            try
            {
                long now = 1000;
                var library = new CosmeticsLibrary(file, () => now);
                Assert.That(library.Look.suitColor, Is.EqualTo(new CharacterLook().suitColor), "nothing saved yet: the default");
                int changes = 0;
                library.Changed += () => changes++;

                library.Edit(look => look.Set(CosmeticSlot.Headgear, "halo"));
                library.Edit(look => look.SetSkin("ar_hadron", "gilded"));
                Assert.That(changes, Is.EqualTo(2));
                Assert.That(library.Look.updatedAt, Is.GreaterThanOrEqualTo(now));

                var again = new CosmeticsLibrary(file, () => now);
                Assert.That(again.Look.headgear, Is.EqualTo("halo"));
                Assert.That(again.Look.SkinFor("ar_hadron").Id, Is.EqualTo("gilded"));

                File.WriteAllText(file, "{ this is not json");
                var broken = new CosmeticsLibrary(file, () => now);
                Assert.That(broken.Look.headgear, Is.EqualTo("none"));
                Assert.That(File.Exists(file + ".unreadable"), Is.True);
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
