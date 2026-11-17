using System.Linq;
using NUnit.Framework;
using Pulsebreak.Client.Loadouts;
using Pulsebreak.Shared.Loadouts;

namespace Pulsebreak.Tests
{
    public class LoadoutSyncTests
    {
        static LoadoutData L(string id, long updatedAt, string name = null) =>
            new LoadoutData { id = id, name = name ?? id, updatedAt = updatedAt };

        static LoadoutCollection C(params LoadoutData[] loadouts) => new LoadoutCollection { loadouts = loadouts };

        static string[] Ids(LoadoutCollection c) => c.loadouts.Select(l => l.id).ToArray();

        [Test]
        public void TheNewerCopyOfEachLoadout_Wins()
        {
            LoadoutCollection merged = LoadoutSync.Merge(
                C(L("a", 10, "a-old"), L("b", 30, "b-new")),
                C(L("a", 20, "a-new"), L("b", 25, "b-old"), L("c", 5)), now: 100);

            Assert.That(Ids(merged), Is.EqualTo(new[] { "a", "b", "c" }), "the first copy's order, then anything new");
            Assert.That(merged.loadouts[0].name, Is.EqualTo("a-new"));
            Assert.That(merged.loadouts[1].name, Is.EqualTo("b-new"));
            Assert.That(merged.version, Is.EqualTo(LoadoutCollection.CurrentVersion));
        }

        [Test]
        public void OnAnExactTie_TheFirstCopyWins()
        {
            LoadoutCollection merged = LoadoutSync.Merge(C(L("a", 10, "first")), C(L("a", 10, "second")), 100);
            Assert.That(merged.loadouts.Single().name, Is.EqualTo("first"));
        }

        [Test]
        public void ADeletion_BeatsOlderCopies_AndLosesToNewerEdits()
        {
            var deleted = new LoadoutCollection { deleted = new[] { new DeletedLoadout { id = "a", deletedAt = 50 } } };

            LoadoutCollection stale = LoadoutSync.Merge(deleted, C(L("a", 40)), 100);
            Assert.That(stale.loadouts, Is.Empty, "an old copy on another device does not bring it back");
            Assert.That(stale.deleted.Single().id, Is.EqualTo("a"));

            LoadoutCollection revived = LoadoutSync.Merge(deleted, C(L("a", 60, "edited later")), 100);
            Assert.That(revived.loadouts.Single().name, Is.EqualTo("edited later"));
            Assert.That(revived.deleted, Is.Empty);

            LoadoutCollection sameMoment = LoadoutSync.Merge(C(L("a", 50)), deleted, 100);
            Assert.That(sameMoment.loadouts, Is.Empty, "deleting at the same moment as an edit: the deletion was the last word");
        }

        [Test]
        public void Merging_IsSafeToRepeat_AndTheOrderOfCopiesDoesNotChangeWhatSurvives()
        {
            LoadoutCollection x = C(L("a", 10), L("b", 40), L("c", 15));
            x.deleted = new[] { new DeletedLoadout { id = "d", deletedAt = 30 } };
            LoadoutCollection y = C(L("b", 20), L("d", 25), L("e", 5));
            y.deleted = new[] { new DeletedLoadout { id = "c", deletedAt = 20 } };

            LoadoutCollection xy = LoadoutSync.Merge(x, y, 100), yx = LoadoutSync.Merge(y, x, 100);
            Assert.That(Ids(xy).OrderBy(i => i), Is.EqualTo(new[] { "a", "b", "e" }));
            Assert.That(Ids(yx).OrderBy(i => i), Is.EqualTo(Ids(xy).OrderBy(i => i)));
            Assert.That(xy.deleted.Select(d => d.id).OrderBy(i => i), Is.EqualTo(new[] { "c", "d" }));

            LoadoutCollection again = LoadoutSync.Merge(xy, xy, 100);
            Assert.That(Ids(again), Is.EqualTo(Ids(xy)));
            Assert.That(LoadoutSync.Merge(xy, y, 100).loadouts.Select(l => l.updatedAt), Is.EqualTo(xy.loadouts.Select(l => l.updatedAt)));
        }

        [Test]
        public void OverTheLimit_TheLeastRecentlyChangedGo_AndAreRecordedAsDeleted()
        {
            LoadoutData[] many = Enumerable.Range(0, LoadoutSync.MaxLoadouts + 3).Select(i => L("l" + i, 100 + i)).ToArray();
            LoadoutCollection merged = LoadoutSync.Merge(C(many), null, now: 500);
            Assert.That(merged.loadouts, Has.Length.EqualTo(LoadoutSync.MaxLoadouts));
            Assert.That(merged.deleted.Select(d => d.id).OrderBy(i => i), Is.EqualTo(new[] { "l0", "l1", "l2" }));
            Assert.That(merged.deleted.All(d => d.deletedAt == 500));
        }

        [Test]
        public void TheEquippedChoice_FollowsTheLaterOne_AndNothingIsNull()
        {
            var a = new LoadoutCollection { equippedId = "x", equippedUpdatedAt = 10 };
            var b = new LoadoutCollection { equippedId = "y", equippedUpdatedAt = 20 };
            Assert.That(LoadoutSync.Merge(a, b, 100).equippedId, Is.EqualTo("y"));
            Assert.That(LoadoutSync.Merge(b, a, 100).equippedId, Is.EqualTo("y"));

            LoadoutCollection empty = LoadoutSync.Merge(null, new LoadoutCollection { loadouts = null, deleted = null, equippedId = null }, 100);
            Assert.That(empty.loadouts, Is.Empty);
            Assert.That(empty.deleted, Is.Empty);
            Assert.That(empty.equippedId, Is.EqualTo(""));
            Assert.That(LoadoutSync.Merge(C(null, L("", 3), L("ok", 4)), null, 100).loadouts.Single().id, Is.EqualTo("ok"), "unusable entries are skipped");
        }

        [Test]
        public void OnlyTheNewestDeletionsAreRemembered()
        {
            var many = new LoadoutCollection
            {
                deleted = Enumerable.Range(0, LoadoutSync.MaxDeleted + 10).Select(i => new DeletedLoadout { id = "d" + i, deletedAt = i }).ToArray(),
            };
            LoadoutCollection merged = LoadoutSync.Merge(many, null, 1000);
            Assert.That(merged.deleted, Has.Length.EqualTo(LoadoutSync.MaxDeleted));
            Assert.That(merged.deleted[0].deletedAt, Is.EqualTo(LoadoutSync.MaxDeleted + 9), "newest first");
        }
    }

    public class LoadoutLibrarySyncTests
    {
        long _now = 1000;

        LoadoutLibrary Library(out MemoryLoadoutStore store)
        {
            store = new MemoryLoadoutStore();
            return new LoadoutLibrary(LoadoutTestKit.MakeCatalog(), store, () => _now);
        }

        [Test]
        public void DeletingALoadout_LeavesARecordThatIsSaved()
        {
            LoadoutLibrary library = Library(out MemoryLoadoutStore store);
            LoadoutData made = library.CreateFrom(library.Presets[0].id);
            _now = 1010;
            library.Delete(made.id);

            LoadoutCollection saved = store.Saved;
            Assert.That(saved.loadouts, Is.Empty);
            Assert.That(saved.deleted.Single().id, Is.EqualTo(made.id));
            Assert.That(saved.deleted.Single().deletedAt, Is.EqualTo(1010));
            Assert.That(saved.version, Is.EqualTo(LoadoutCollection.CurrentVersion));
        }

        [Test]
        public void EveryChange_IsStampedLaterThanTheOneBefore_EvenIfTheClockStandsStill()
        {
            LoadoutLibrary library = Library(out _);
            LoadoutData made = library.CreateFrom(library.Presets[0].id);
            long first = library.Find(made.id).updatedAt;
            library.Rename(made.id, "One");
            library.Rename(made.id, "Two");
            Assert.That(library.Find(made.id).updatedAt, Is.EqualTo(first + 2));

            long equipped = library.Snapshot().equippedUpdatedAt;
            library.Equip(made.id);
            Assert.That(library.Snapshot().equippedUpdatedAt, Is.GreaterThan(equipped));
        }

        [Test]
        public void MergingACopyFromElsewhere_KeepsWhicheverChangeIsNewer_AndTellsListeners()
        {
            LoadoutLibrary library = Library(out MemoryLoadoutStore store);
            LoadoutData mine = library.CreateFrom(library.Presets[0].id);
            library.Rename(mine.id, "Mine, newer");

            LoadoutData theirsOfMine = mine.Clone();
            theirsOfMine.name = "Theirs, older";
            theirsOfMine.updatedAt = 1;
            LoadoutData theirs = library.Presets[1].Clone();
            theirs.id = "from-elsewhere";
            theirs.name = "From elsewhere";
            theirs.updatedAt = 2000;

            int changed = 0, equipped = 0;
            library.Changed += () => changed++;
            library.EquippedChanged += () => equipped++;
            bool any = library.MergeFrom(new LoadoutCollection
            {
                loadouts = new[] { theirsOfMine, theirs },
                equippedId = "from-elsewhere",
                equippedUpdatedAt = 5000,
            });

            Assert.That(any, Is.True);
            Assert.That(library.Find(mine.id).name, Is.EqualTo("Mine, newer"));
            Assert.That(library.Find("from-elsewhere").name, Is.EqualTo("From elsewhere"));
            Assert.That(library.EquippedId, Is.EqualTo("from-elsewhere"));
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(equipped, Is.EqualTo(1));
            Assert.That(store.Saved.loadouts, Has.Length.EqualTo(2), "the merged result is saved");

            Assert.That(library.MergeFrom(library.Snapshot()), Is.False, "merging what it already has changes nothing");
        }

        [Test]
        public void ReplacingWithAnotherAccountsCopy_DiscardsWhatWasHeld()
        {
            LoadoutLibrary library = Library(out _);
            LoadoutData mine = library.CreateFrom(library.Presets[0].id);
            library.Equip(mine.id);

            LoadoutData theirs = library.Presets[1].Clone();
            theirs.id = "theirs";
            theirs.updatedAt = 1;
            library.MergeFrom(new LoadoutCollection { loadouts = new[] { theirs } }, replace: true);

            Assert.That(library.Custom.Select(l => l.id), Is.EqualTo(new[] { "theirs" }));
            Assert.That(library.IsPreset(library.EquippedId), Is.True, "what was equipped is gone, so a preset is");
        }

        [Test]
        public void IllegalContentArrivingFromElsewhere_IsRepairedLikeASave()
        {
            LoadoutLibrary library = Library(out _);
            var junk = new LoadoutData
            {
                id = "junk", name = "Junk", updatedAt = 5000,
                primary = new WeaponBuildData { weaponId = "does_not_exist" },
            };
            library.MergeFrom(new LoadoutCollection { loadouts = new[] { junk } });
            Assert.That(library.Find("junk").primary.weaponId, Is.Not.EqualTo("does_not_exist"));
        }
    }
}
