using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Pulsebreak.Client.Loadouts;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Tests
{
    /// <summary>A small catalog built in memory, so these tests do not depend on the real content.</summary>
    static class LoadoutTestKit
    {
        public static LoadoutCatalog MakeCatalog()
        {
            AttachmentDefinition sight = Attachment("sight", AttachmentSlot.Sight);
            AttachmentDefinition holo = Attachment("holo", AttachmentSlot.Sight);
            AttachmentDefinition quickMag = Attachment("quick_mag", AttachmentSlot.Magazine,
                new StatModifier(WeaponStat.ReloadTime, ModifierOp.Multiply, 0.8f));
            AttachmentDefinition smgStock = Attachment("smg_stock", AttachmentSlot.Stock);

            WeaponDefinition rifle = Weapon("rifle", WeaponSlotType.Primary, sight, holo, quickMag);
            WeaponDefinition smg = Weapon("smg", WeaponSlotType.Primary, sight, smgStock);
            WeaponDefinition pistol = Weapon("pistol", WeaponSlotType.Secondary, sight);
            WeaponDefinition knife = Weapon("knife", WeaponSlotType.Melee);
            knife.stats.ballistics = BallisticsType.Melee;
            knife.stats.magazineSize = 0;

            var weapons = ScriptableObject.CreateInstance<WeaponCatalog>();
            weapons.weapons = new[] { rifle, smg, pistol, knife };
            weapons.attachments = new[] { sight, holo, quickMag, smgStock };

            UtilityDefinition frag = Utility("frag", charges: 2, throwTime: 0.25f, cooldown: 0.5f, recharge: 4f);
            UtilityDefinition impact = Utility("impact", charges: 1, throwTime: 0.2f, cooldown: 0.5f, recharge: 4f);
            UtilityDefinition disc = Utility("disc", charges: 3, throwTime: 0.1f, cooldown: 0.2f, recharge: 0f);

            PerkSetDefinition plain = PerkSet("plain");
            PerkSetDefinition mover = PerkSet("mover");
            mover.moveSpeedMultiplier = 1.05f;
            mover.bonusUtilityCharges = 1;
            mover.utilityRechargeMultiplier = 2f;
            mover.weaponModifiers = new[] { new StatModifier(WeaponStat.ReloadTime, ModifierOp.Multiply, 0.9f) };

            var alpha = ScriptableObject.CreateInstance<LoadoutPreset>();
            alpha.presetId = "alpha";
            alpha.displayName = "Alpha";
            alpha.primary = rifle;
            alpha.primaryAttachments = new[] { sight };
            alpha.secondary = pistol;
            alpha.melee = knife;
            alpha.utility1 = frag;
            alpha.utility2 = impact;
            alpha.perkSet = plain;

            var bravo = ScriptableObject.CreateInstance<LoadoutPreset>();
            bravo.presetId = "bravo";
            bravo.displayName = "Bravo";
            bravo.primary = smg;
            bravo.secondary = pistol;
            bravo.melee = knife;
            bravo.utility1 = disc;
            bravo.utility2 = frag;
            bravo.perkSet = mover;

            var catalog = ScriptableObject.CreateInstance<LoadoutCatalog>();
            catalog.weapons = weapons;
            catalog.utilities = new[] { frag, impact, disc };
            catalog.perkSets = new[] { plain, mover };
            catalog.presets = new[] { alpha, bravo };
            return catalog;
        }

        public static LoadoutData ValidData(string id = "custom")
        {
            return new LoadoutData
            {
                id = id,
                name = "Mine",
                primary = new WeaponBuildData { weaponId = "rifle", attachments = new[] { "holo", "quick_mag" } },
                secondary = new WeaponBuildData { weaponId = "pistol" },
                melee = new WeaponBuildData { weaponId = "knife" },
                utility1 = "disc",
                utility2 = "impact",
                perkSet = "mover",
            };
        }

        static AttachmentDefinition Attachment(string id, AttachmentSlot slot, params StatModifier[] modifiers)
        {
            var attachment = ScriptableObject.CreateInstance<AttachmentDefinition>();
            attachment.attachmentId = id;
            attachment.displayName = id;
            attachment.slot = slot;
            attachment.modifiers = modifiers;
            return attachment;
        }

        static WeaponDefinition Weapon(string id, WeaponSlotType slot, params AttachmentDefinition[] accepted)
        {
            var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            weapon.weaponId = id;
            weapon.displayName = id;
            weapon.slot = slot;
            weapon.attachments = accepted;
            return weapon;
        }

        static UtilityDefinition Utility(string id, int charges, float throwTime, float cooldown, float recharge)
        {
            var utility = ScriptableObject.CreateInstance<UtilityDefinition>();
            utility.utilityId = id;
            utility.displayName = id;
            utility.charges = charges;
            utility.throwTime = throwTime;
            utility.cooldown = cooldown;
            utility.rechargeTime = recharge;
            return utility;
        }

        static PerkSetDefinition PerkSet(string id)
        {
            var perkSet = ScriptableObject.CreateInstance<PerkSetDefinition>();
            perkSet.perkSetId = id;
            perkSet.displayName = id;
            return perkSet;
        }
    }

    /// <summary>A loadout store that keeps things in memory, standing in for the file or a backend.</summary>
    sealed class MemoryLoadoutStore : ILoadoutStore
    {
        public LoadoutCollection Saved;
        public int SaveCount;

        public LoadoutCollection Load() => Saved;

        public void Save(LoadoutCollection collection)
        {
            Saved = collection;
            SaveCount++;
        }
    }

    public class LoadoutRulesTests
    {
        [Test]
        public void AValidLoadout_ResolvesAsWritten_WithNothingToCorrect()
        {
            LoadoutCatalog catalog = LoadoutTestKit.MakeCatalog();
            var notes = new List<string>();

            Loadout loadout = LoadoutRules.Resolve(LoadoutTestKit.ValidData(), catalog, notes);

            Assert.That(notes, Is.Empty);
            Assert.That(loadout.Name, Is.EqualTo("Mine"));
            Assert.That(loadout.Weapons[0].Weapon.weaponId, Is.EqualTo("rifle"));
            Assert.That(loadout.Weapons[0].Get(AttachmentSlot.Sight).attachmentId, Is.EqualTo("holo"));
            Assert.That(loadout.Weapons[0].Get(AttachmentSlot.Magazine).attachmentId, Is.EqualTo("quick_mag"));
            Assert.That(loadout.Weapons[1].Weapon.weaponId, Is.EqualTo("pistol"));
            Assert.That(loadout.Weapons[2].Weapon.weaponId, Is.EqualTo("knife"));
            Assert.That(loadout.Utilities[0].utilityId, Is.EqualTo("disc"));
            Assert.That(loadout.Utilities[1].utilityId, Is.EqualTo("impact"));
            Assert.That(loadout.PerkSet.perkSetId, Is.EqualTo("mover"));
        }

        [Test]
        public void AnUnknownWeapon_IsReplacedByTheFirstThatFitsTheSlot()
        {
            LoadoutCatalog catalog = LoadoutTestKit.MakeCatalog();
            LoadoutData data = LoadoutTestKit.ValidData();
            data.primary.weaponId = "removed_in_a_patch";
            var notes = new List<string>();

            Loadout loadout = LoadoutRules.Resolve(data, catalog, notes);

            Assert.That(loadout.Weapons[0].Weapon.weaponId, Is.EqualTo("rifle"));
            Assert.That(loadout.Weapons[0].Get(AttachmentSlot.Sight), Is.Null, "the old weapon's attachments do not carry over");
            Assert.That(notes, Has.Count.EqualTo(1));
        }

        [Test]
        public void AWeaponInTheWrongSlot_IsReplaced()
        {
            LoadoutCatalog catalog = LoadoutTestKit.MakeCatalog();
            LoadoutData data = LoadoutTestKit.ValidData();
            data.secondary.weaponId = "rifle";
            data.melee.weaponId = "pistol";

            Loadout loadout = LoadoutRules.Resolve(data, catalog);

            Assert.That(loadout.Weapons[1].Weapon.weaponId, Is.EqualTo("pistol"));
            Assert.That(loadout.Weapons[2].Weapon.weaponId, Is.EqualTo("knife"));
        }

        [Test]
        public void AttachmentsThatDoNotFit_AreRemoved_AndASlotKeepsOnlyItsFirst()
        {
            LoadoutCatalog catalog = LoadoutTestKit.MakeCatalog();
            LoadoutData data = LoadoutTestKit.ValidData();
            data.primary.attachments = new[] { "smg_stock", "sight", "holo", "no_such_thing" };
            var notes = new List<string>();

            Loadout loadout = LoadoutRules.Resolve(data, catalog, notes);

            Assert.That(loadout.Weapons[0].Get(AttachmentSlot.Stock), Is.Null, "the rifle does not take the SMG stock");
            Assert.That(loadout.Weapons[0].Get(AttachmentSlot.Sight).attachmentId, Is.EqualTo("sight"));
            Assert.That(notes, Has.Count.EqualTo(3));
        }

        [Test]
        public void TheTwoUtilitySlots_AlwaysHoldDifferentItems()
        {
            LoadoutCatalog catalog = LoadoutTestKit.MakeCatalog();
            LoadoutData data = LoadoutTestKit.ValidData();
            data.utility1 = "frag";
            data.utility2 = "frag";

            Loadout loadout = LoadoutRules.Resolve(data, catalog);

            Assert.That(loadout.Utilities[0].utilityId, Is.EqualTo("frag"));
            Assert.That(loadout.Utilities[1].utilityId, Is.EqualTo("impact"));

            data.utility1 = "gone";
            data.utility2 = "also_gone";
            loadout = LoadoutRules.Resolve(data, catalog);
            Assert.That(loadout.Utilities[0], Is.Not.Null);
            Assert.That(loadout.Utilities[1], Is.Not.Null);
            Assert.That(loadout.Utilities[0], Is.Not.SameAs(loadout.Utilities[1]));
        }

        [Test]
        public void AnUnknownPerkSet_FallsBackToTheFirst()
        {
            LoadoutCatalog catalog = LoadoutTestKit.MakeCatalog();
            LoadoutData data = LoadoutTestKit.ValidData();
            data.perkSet = "";

            Assert.That(LoadoutRules.Resolve(data, catalog).PerkSet.perkSetId, Is.EqualTo("plain"));
        }

        [TestCase("  Close Quarters  ", "Close Quarters")]
        [TestCase("", "Loadout")]
        [TestCase("   ", "Loadout")]
        [TestCase(null, "Loadout")]
        [TestCase("line\nbreak\ttab", "linebreaktab")]
        [TestCase("An extremely long loadout name that goes on", "An extremely long loadou")]
        public void Names_AreCleanedUp(string typed, string expected)
        {
            Assert.That(LoadoutRules.CleanName(typed), Is.EqualTo(expected));
            Assert.That(LoadoutRules.CleanName(typed).Length, Is.LessThanOrEqualTo(LoadoutRules.MaxNameLength));
        }

        [Test]
        public void Sanitize_FixesBadData_AndLeavesGoodDataAlone()
        {
            LoadoutCatalog catalog = LoadoutTestKit.MakeCatalog();

            LoadoutData good = LoadoutTestKit.ValidData();
            good.updatedAt = 1234;
            LoadoutData same = LoadoutRules.Sanitize(good, catalog);
            Assert.That(same.id, Is.EqualTo(good.id));
            Assert.That(same.primary.attachments, Is.EqualTo(good.primary.attachments));
            Assert.That(same.utility1, Is.EqualTo(good.utility1));
            Assert.That(same.perkSet, Is.EqualTo(good.perkSet));
            Assert.That(same.updatedAt, Is.EqualTo(1234));

            var bad = new LoadoutData { id = "x", name = "  ", utility1 = "frag", utility2 = "frag" };
            LoadoutData fixedUp = LoadoutRules.Sanitize(bad, catalog);
            Assert.That(fixedUp.name, Is.EqualTo("Loadout"));
            Assert.That(fixedUp.primary.weaponId, Is.EqualTo("rifle"));
            Assert.That(fixedUp.secondary.weaponId, Is.EqualTo("pistol"));
            Assert.That(fixedUp.melee.weaponId, Is.EqualTo("knife"));
            Assert.That(fixedUp.utility2, Is.Not.EqualTo(fixedUp.utility1));

            // Once clean, cleaning again changes nothing.
            var notes = new List<string>();
            LoadoutRules.Sanitize(fixedUp, catalog, notes);
            Assert.That(notes, Is.Empty);
        }

        [Test]
        public void APerkSet_AppliesToEveryWeapon_OnTopOfAttachments()
        {
            LoadoutCatalog catalog = LoadoutTestKit.MakeCatalog();
            Loadout loadout = LoadoutRules.Resolve(LoadoutTestKit.ValidData(), catalog);
            float baseReload = catalog.weapons.FindWeapon("rifle").stats.reloadTime;

            // Quick mag x0.8, then the perk set x0.9.
            Assert.That(loadout.ResolveStats(0).reloadTime, Is.EqualTo(baseReload * 0.8f * 0.9f).Within(0.0001f));
            // The pistol has no attachment, only the perk.
            Assert.That(loadout.ResolveStats(1).reloadTime, Is.EqualTo(baseReload * 0.9f).Within(0.0001f));

            Assert.That(loadout.MoveSpeedMultiplier, Is.EqualTo(1.05f));
            Assert.That(loadout.UtilityCharges(0), Is.EqualTo(4), "three discs plus one from the perk");
            Assert.That(loadout.UtilityRechargeMultiplier, Is.EqualTo(2f));
        }

        [Test]
        public void APreset_ConvertsToDataAndBack()
        {
            LoadoutCatalog catalog = LoadoutTestKit.MakeCatalog();
            var notes = new List<string>();

            Loadout loadout = LoadoutRules.Resolve(catalog.FindPreset("alpha").ToData(), catalog, notes);

            Assert.That(notes, Is.Empty);
            Assert.That(loadout.Id, Is.EqualTo("alpha"));
            Assert.That(loadout.Name, Is.EqualTo("Alpha"));
            Assert.That(loadout.Weapons[0].Get(AttachmentSlot.Sight).attachmentId, Is.EqualTo("sight"));
            Assert.That(LoadoutValidator.Validate(catalog), Is.Empty);
        }
    }

    public class UtilityBeltTests
    {
        const float Dt = SimConstants.TickDelta;

        static UtilityBelt MakeBelt(out LoadoutCatalog catalog)
        {
            catalog = LoadoutTestKit.MakeCatalog();
            // Slot 0: frag (2 carried, 0.25 s wind-up, 0.5 s cooldown). Slot 1: impact (1 carried, 0.2 s wind-up).
            return new UtilityBelt(new[] { catalog.FindUtility("frag"), catalog.FindUtility("impact") }, new[] { 2, 1 });
        }

        /// <summary>Runs ticks and returns the slots released, in order.</summary>
        static List<int> Run(UtilityBelt belt, int ticks, InputButtons buttons, bool canUse = true)
        {
            var released = new List<int>();
            for (int i = 0; i < ticks; i++)
            {
                belt.Simulate(new PlayerInputCmd { Buttons = buttons }, canUse, Dt);
                if (belt.ReleasedSlot >= 0) released.Add(belt.ReleasedSlot);
            }
            return released;
        }

        [Test]
        public void AThrow_LeavesTheHandAfterItsWindUp_AndSpendsOne()
        {
            UtilityBelt belt = MakeBelt(out _);
            int started = 0;
            belt.ThrowStarted += _ => started++;

            Assert.That(Run(belt, 1, InputButtons.Utility1), Is.Empty);
            Assert.That(belt.IsThrowing, Is.True);
            Assert.That(started, Is.EqualTo(1));

            // 0.25 s is 15 ticks; one has gone by.
            Assert.That(Run(belt, 12, InputButtons.None), Is.Empty);
            Assert.That(Run(belt, 4, InputButtons.None), Is.EqualTo(new[] { 0 }));
            Assert.That(belt.IsThrowing, Is.False);
            Assert.That(belt.GetState(0).Charges, Is.EqualTo(1));
        }

        [Test]
        public void HoldingTheKey_ThrowsOnce_AndTheCooldownSpacesThrowsOut()
        {
            UtilityBelt belt = MakeBelt(out _);

            Assert.That(Run(belt, 120, InputButtons.Utility1), Has.Count.EqualTo(1), "held down");

            Run(belt, 2, InputButtons.None);
            Assert.That(Run(belt, 60, InputButtons.Utility1), Has.Count.EqualTo(1), "second press");
            Assert.That(belt.GetState(0).Charges, Is.EqualTo(0));

            Run(belt, 60, InputButtons.None);
            Assert.That(Run(belt, 60, InputButtons.Utility1), Is.Empty, "none left");
        }

        [Test]
        public void APressDuringTheCooldown_DoesNothing()
        {
            UtilityBelt belt = MakeBelt(out _);
            Run(belt, 1, InputButtons.Utility1);
            Run(belt, 20, InputButtons.None);
            Assert.That(belt.GetState(0).Charges, Is.EqualTo(1));

            // 0.5 s cooldown has only just started.
            Assert.That(Run(belt, 1, InputButtons.Utility1), Is.Empty);
            Assert.That(belt.IsThrowing, Is.False);
        }

        [Test]
        public void TheSecondKey_ThrowsTheSecondSlot()
        {
            UtilityBelt belt = MakeBelt(out _);

            List<int> released = Run(belt, 1, InputButtons.Utility2);
            released.AddRange(Run(belt, 30, InputButtons.None));

            Assert.That(released, Is.EqualTo(new[] { 1 }));
            Assert.That(belt.GetState(0).Charges, Is.EqualTo(2));
            Assert.That(belt.GetState(1).Charges, Is.EqualTo(0));
        }

        [Test]
        public void BeingInterrupted_CancelsTheThrow_WithoutSpendingIt()
        {
            UtilityBelt belt = MakeBelt(out _);
            Run(belt, 5, InputButtons.Utility1);
            Assert.That(belt.IsThrowing, Is.True);

            Assert.That(Run(belt, 30, InputButtons.None, canUse: false), Is.Empty);
            Assert.That(belt.IsThrowing, Is.False);
            Assert.That(belt.GetState(0).Charges, Is.EqualTo(2));

            Assert.That(Run(belt, 30, InputButtons.Utility1, canUse: false), Is.Empty, "cannot start one either");
        }

        [Test]
        public void Charges_ComeBackOnlyWhereRechargingIsOn_AndFasterWithAMultiplier()
        {
            UtilityBelt belt = MakeBelt(out _);
            Run(belt, 1, InputButtons.Utility1);
            Run(belt, 30, InputButtons.None);
            Assert.That(belt.GetState(0).Charges, Is.EqualTo(1));

            Run(belt, SimConstants.TickRate * 10, InputButtons.None);
            Assert.That(belt.GetState(0).Charges, Is.EqualTo(1), "recharging is off by default");

            // Frag takes 4 s to come back; at double speed that is 2 s.
            belt.Recharges = true;
            belt.RechargeMultiplier = 2f;
            Run(belt, SimConstants.TickRate * 2 - 5, InputButtons.None);
            Assert.That(belt.GetState(0).Charges, Is.EqualTo(1));
            Run(belt, 10, InputButtons.None);
            Assert.That(belt.GetState(0).Charges, Is.EqualTo(2));

            Run(belt, SimConstants.TickRate * 10, InputButtons.None);
            Assert.That(belt.GetState(0).Charges, Is.EqualTo(2), "never above what is carried");
        }
    }

    public class LoadoutLibraryTests
    {
        long _now = 1000;

        LoadoutLibrary MakeLibrary(MemoryLoadoutStore store, out LoadoutCatalog catalog)
        {
            catalog = LoadoutTestKit.MakeCatalog();
            return new LoadoutLibrary(catalog, store, () => _now);
        }

        [Test]
        public void WithNothingSaved_ThereAreOnlyPresets_AndTheFirstIsEquipped()
        {
            LoadoutLibrary library = MakeLibrary(new MemoryLoadoutStore(), out _);

            Assert.That(library.Presets.Count, Is.EqualTo(2));
            Assert.That(library.Custom, Is.Empty);
            Assert.That(library.EquippedId, Is.EqualTo("alpha"));
            Assert.That(library.ResolveEquipped().Weapons[0].Weapon.weaponId, Is.EqualTo("rifle"));
        }

        [Test]
        public void CreateFrom_MakesAnEditableCopy_WithItsOwnIdAndName_AndSavesIt()
        {
            var store = new MemoryLoadoutStore();
            LoadoutLibrary library = MakeLibrary(store, out _);

            LoadoutData copy = library.CreateFrom("alpha");
            LoadoutData another = library.CreateFrom("alpha");

            Assert.That(copy.id, Is.Not.EqualTo("alpha"));
            Assert.That(copy.id, Is.Not.EqualTo(another.id));
            Assert.That(copy.name, Is.EqualTo("Alpha 2"));
            Assert.That(another.name, Is.EqualTo("Alpha 3"));
            Assert.That(copy.primary.weaponId, Is.EqualTo("rifle"));
            Assert.That(copy.updatedAt, Is.EqualTo(1000));
            Assert.That(library.IsPreset(copy.id), Is.False);
            Assert.That(store.Saved.loadouts.Length, Is.EqualTo(2));
        }

        [Test]
        public void ThereIsALimitOnCustomLoadouts()
        {
            LoadoutLibrary library = MakeLibrary(new MemoryLoadoutStore(), out _);
            for (int i = 0; i < LoadoutLibrary.MaxCustomLoadouts; i++) Assert.That(library.CreateFrom("alpha"), Is.Not.Null);

            Assert.That(library.CanCreate, Is.False);
            Assert.That(library.CreateFrom("alpha"), Is.Null);
            Assert.That(library.Custom.Count, Is.EqualTo(LoadoutLibrary.MaxCustomLoadouts));
        }

        [Test]
        public void Rename_CleansTheName_AndOnlyWorksOnCustomLoadouts()
        {
            var store = new MemoryLoadoutStore();
            LoadoutLibrary library = MakeLibrary(store, out _);
            LoadoutData copy = library.CreateFrom("alpha");
            _now = 2000;

            library.Rename(copy.id, "   Night Shift   ");
            library.Rename("alpha", "Hacked");

            Assert.That(library.Find(copy.id).name, Is.EqualTo("Night Shift"));
            Assert.That(library.Find(copy.id).updatedAt, Is.EqualTo(2000));
            Assert.That(library.Find("alpha").name, Is.EqualTo("Alpha"));
            Assert.That(store.Saved.loadouts[0].name, Is.EqualTo("Night Shift"));
        }

        [Test]
        public void Replace_SavesAnEdit_CorrectsIt_AndTellsThePlayerOnlyIfItIsEquipped()
        {
            var store = new MemoryLoadoutStore();
            LoadoutLibrary library = MakeLibrary(store, out _);
            LoadoutData copy = library.CreateFrom("alpha");
            int equippedChanges = 0, changes = 0;
            library.EquippedChanged += () => equippedChanges++;
            library.Changed += () => changes++;

            LoadoutData edit = library.Find(copy.id).Clone();
            edit.primary.weaponId = "smg";
            edit.primary.attachments = new[] { "smg_stock", "quick_mag" };
            library.Replace(edit);

            LoadoutData saved = library.Find(copy.id);
            Assert.That(saved.primary.weaponId, Is.EqualTo("smg"));
            Assert.That(saved.primary.attachments, Is.EqualTo(new[] { "smg_stock" }), "the SMG does not take the quick mag");
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(equippedChanges, Is.EqualTo(0), "it is not the equipped loadout");

            library.Equip(copy.id);
            edit = library.Find(copy.id).Clone();
            edit.perkSet = "mover";
            library.Replace(edit);
            Assert.That(equippedChanges, Is.EqualTo(2), "once for equipping, once for the edit");
            Assert.That(library.ResolveEquipped().PerkSet.perkSetId, Is.EqualTo("mover"));
        }

        [Test]
        public void EditingTheReturnedData_DoesNotChangeTheLibrary_UntilReplaceIsCalled()
        {
            LoadoutLibrary library = MakeLibrary(new MemoryLoadoutStore(), out _);
            LoadoutData copy = library.CreateFrom("alpha");

            LoadoutData edit = library.Find(copy.id).Clone();
            edit.perkSet = "mover";

            Assert.That(library.Find(copy.id).perkSet, Is.EqualTo("plain"));
        }

        [Test]
        public void Presets_CannotBeReplacedOrDeleted()
        {
            LoadoutLibrary library = MakeLibrary(new MemoryLoadoutStore(), out _);

            LoadoutData edit = library.Find("alpha").Clone();
            edit.primary.weaponId = "smg";
            library.Replace(edit);

            Assert.That(library.Find("alpha").primary.weaponId, Is.EqualTo("rifle"));
            Assert.That(library.Delete("alpha"), Is.False);
            Assert.That(library.Presets.Count, Is.EqualTo(2));
        }

        [Test]
        public void DeletingTheEquippedLoadout_FallsBackToAPreset()
        {
            LoadoutLibrary library = MakeLibrary(new MemoryLoadoutStore(), out _);
            LoadoutData copy = library.CreateFrom("bravo");
            library.Equip(copy.id);
            int equippedChanges = 0;
            library.EquippedChanged += () => equippedChanges++;

            Assert.That(library.Delete(copy.id), Is.True);

            Assert.That(library.EquippedId, Is.EqualTo("alpha"));
            Assert.That(library.Custom, Is.Empty);
            Assert.That(equippedChanges, Is.EqualTo(1));
        }

        [Test]
        public void Equip_IgnoresUnknownIds_AndRemembersTheChoice()
        {
            var store = new MemoryLoadoutStore();
            LoadoutLibrary library = MakeLibrary(store, out _);

            library.Equip("no_such_loadout");
            Assert.That(library.EquippedId, Is.EqualTo("alpha"));

            library.Equip("bravo");
            Assert.That(library.EquippedId, Is.EqualTo("bravo"));
            Assert.That(store.Saved.equippedId, Is.EqualTo("bravo"));
        }

        [Test]
        public void EverythingSaved_IsThereAgainNextTime()
        {
            var store = new MemoryLoadoutStore();
            LoadoutLibrary first = MakeLibrary(store, out _);
            LoadoutData copy = first.CreateFrom("bravo");
            first.Rename(copy.id, "Keeper");
            first.Equip(copy.id);

            // A new library on the same store is the game starting up again.
            LoadoutLibrary second = MakeLibrary(store, out _);

            Assert.That(second.Custom.Count, Is.EqualTo(1));
            Assert.That(second.EquippedId, Is.EqualTo(copy.id));
            Assert.That(second.Equipped.name, Is.EqualTo("Keeper"));
            Assert.That(second.ResolveEquipped().Weapons[0].Weapon.weaponId, Is.EqualTo("smg"));
        }

        [Test]
        public void ADamagedSave_IsRepairedOnLoad()
        {
            var store = new MemoryLoadoutStore
            {
                Saved = new LoadoutCollection
                {
                    equippedId = "deleted_long_ago",
                    loadouts = new[]
                    {
                        new LoadoutData { id = "a", name = "Old", primary = new WeaponBuildData { weaponId = "removed_gun" } },
                        new LoadoutData { id = "a", name = "Same id again" },
                        new LoadoutData { id = "", name = "No id" },
                        new LoadoutData { id = "alpha", name = "Pretends to be a preset" },
                        null,
                    },
                },
            };

            LoadoutLibrary library = MakeLibrary(store, out _);

            Assert.That(library.Custom.Count, Is.EqualTo(1));
            Assert.That(library.Custom[0].name, Is.EqualTo("Old"));
            Assert.That(library.Custom[0].primary.weaponId, Is.EqualTo("rifle"), "the missing weapon was replaced");
            Assert.That(library.EquippedId, Is.EqualTo("alpha"));
            Assert.That(library.Find("alpha").name, Is.EqualTo("Alpha"));
        }
    }
}
