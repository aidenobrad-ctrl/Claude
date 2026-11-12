using System.Collections.Generic;
using NUnit.Framework;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Progression;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Tests
{
    /// <summary>Levels, match XP, ranks and unlocks: the rules the backend pays out by and the game shows.</summary>
    public class ProgressionTests
    {
        [Test]
        public void Levels_TakeMoreXpEachTime_AndCountUpConsistently()
        {
            for (int level = 1; level < Levels.MaxLevel - 1; level++)
                Assert.That(Levels.XpToNext(level + 1), Is.GreaterThan(Levels.XpToNext(level)));
            Assert.That(Levels.XpToNext(Levels.MaxLevel), Is.EqualTo(0));

            for (int level = 1; level <= Levels.MaxLevel; level++)
            {
                long start = Levels.XpAtLevel(level);
                Assert.That(Levels.LevelFor(start), Is.EqualTo(level));
                if (level > 1) Assert.That(Levels.LevelFor(start - 1), Is.EqualTo(level - 1));
            }
            Assert.That(Levels.LevelFor(long.MaxValue / 2), Is.EqualTo(Levels.MaxLevel));
            Assert.That(Levels.LevelFor(400, out long into, out long needed), Is.EqualTo(2));
            Assert.That(into, Is.EqualTo(200));
            Assert.That(needed, Is.EqualTo(Levels.XpToNext(2)));
            Assert.That(Levels.WeaponLevelFor(0), Is.EqualTo(1));
            Assert.That(Levels.WeaponLevelFor(long.MaxValue / 2), Is.EqualTo(Levels.MaxWeaponLevel));
        }

        [Test]
        public void EverythingInTheGame_UnlocksWithinTheLevelCap_InAboutFiftyGoodMatches()
        {
            // A typical good casual match: finished, won, eight kills, two headshots.
            var typical = new PlayerReport { completed = true, outcome = MatchOutcome.Won, kills = 8, headshots = 2 };
            int perMatch = MatchXp.For(typical, ranked: false);
            long toLevel30 = Levels.XpAtLevel(30);
            Assert.That(toLevel30 / perMatch, Is.InRange(30, 80), "about " + toLevel30 / perMatch + " matches to unlock every weapon");
        }

        [Test]
        public void MatchXp_RewardsFinishingAndWinning_AndRankedPaysMore()
        {
            var stayed = new PlayerReport { completed = true, outcome = MatchOutcome.Won, kills = 4, headshots = 1, objective = 400, roundsWon = 2 };
            int expected = 4 * MatchXp.PerKill + MatchXp.PerHeadshot + 400 / MatchXp.ObjectiveDivisor + 2 * MatchXp.PerRoundWon + MatchXp.Finish + MatchXp.Win;
            Assert.That(MatchXp.For(stayed, false), Is.EqualTo(expected));
            Assert.That(MatchXp.For(stayed, true), Is.EqualTo(expected * 5 / 4));

            var left = new PlayerReport { completed = false, outcome = MatchOutcome.Won, kills = 4 };
            Assert.That(MatchXp.For(left, false), Is.EqualTo(4 * MatchXp.PerKill), "leaving forfeits finishing and winning");
            var drew = new PlayerReport { completed = true, outcome = MatchOutcome.Draw };
            Assert.That(MatchXp.For(drew, false), Is.EqualTo(MatchXp.Finish + MatchXp.Draw));
            Assert.That(MatchXp.For(new PlayerReport { completed = true, kills = 100000 }, false), Is.EqualTo(MatchXp.Cap));
            Assert.That(MatchXp.ForWeapon(new WeaponReport { kills = 3, headshots = 1 }), Is.EqualTo(3 * MatchXp.WeaponPerKill + MatchXp.WeaponPerHeadshot));
        }

        [Test]
        public void Ranks_HaveTiersAndDivisions_AndPlacementHidesThem()
        {
            Assert.That(Ranks.TierFor(Ranks.StartRating), Is.EqualTo(RankTier.Pulse));
            Assert.That(Ranks.TierFor(0), Is.EqualTo(RankTier.Spark));
            Assert.That(Ranks.TierFor(2500), Is.EqualTo(RankTier.Zenith));
            Assert.That(Ranks.Name(2500, 40), Is.EqualTo("Zenith"));
            Assert.That(Ranks.Name(1100, 40), Is.EqualTo("Surge I"));
            Assert.That(Ranks.Name(1299, 40), Is.EqualTo("Surge III"));
            Assert.That(Ranks.Name(1500, 2), Is.EqualTo("Placement 2/" + Ranks.PlacementMatches));

            // Even players: a win gains what a loss costs. An upset is worth more than a win expected.
            int win = Ranks.Change(1000, 1000, 1.0, 40), loss = Ranks.Change(1000, 1000, 0.0, 40);
            Assert.That(win, Is.GreaterThan(0));
            Assert.That(win, Is.EqualTo(-loss));
            Assert.That(Ranks.Change(1000, 1400, 1.0, 40), Is.GreaterThan(win));
            Assert.That(Ranks.Change(1400, 1000, 1.0, 40), Is.LessThan(win));
            Assert.That(Ranks.Change(1000, 1000, 1.0, 0), Is.GreaterThan(win), "placement moves faster");
            Assert.That(Ranks.Change(1000, 1000, 0.5, 40), Is.EqualTo(0));
        }

        [Test]
        public void RankedPlaylists_AreDuelAndElimination_AndStartOnlyWhenFull()
        {
            Assert.That(Playlists.HasRanked(GameModeKind.Duel), Is.True);
            Assert.That(Playlists.HasRanked(GameModeKind.Elimination), Is.True);
            Assert.That(Playlists.HasRanked(GameModeKind.TeamDeathmatch), Is.False);
            Assert.That(Playlists.For(GameModeKind.Duel).Ranked, Is.False, "the casual playlist comes first");
            foreach (Playlist playlist in Playlists.All)
            {
                if (playlist.Ranked) Assert.That(playlist.MinPlayers, Is.EqualTo(playlist.Players), playlist.Id + ": no bots in ranked");
            }
        }

        [Test]
        public void UnlockRules_SwapWhatIsLocked_ForTheStarter_AndLeaveTheRest()
        {
            LoadoutCatalog catalog = LoadoutTestKit.MakeCatalog();
            WeaponDefinition rifle = catalog.weapons.FindWeapon("rifle");
            rifle.unlockLevel = 5;
            catalog.weapons.FindAttachment("holo").unlockLevel = 3;
            catalog.FindUtility("disc").unlockLevel = 7;
            catalog.FindPerkSet("mover").unlockLevel = 4;
            LoadoutData wanted = LoadoutTestKit.ValidData();    // rifle with holo and quick mag, disc, mover

            var notes = new List<string>();
            LoadoutData starter = UnlockRules.Apply(wanted, catalog, PlayerUnlocks.Starter, notes);
            Assert.That(starter.primary.weaponId, Is.EqualTo("smg"), "the open primary with the lowest unlock level");
            Assert.That(starter.primary.attachments, Is.Empty);
            Assert.That(starter.utility, Is.Not.EqualTo("disc"));
            Assert.That(starter.perkSet, Is.Not.EqualTo("mover"));
            Assert.That(starter.secondary.weaponId, Is.EqualTo("pistol"), "what is open stays");
            Assert.That(notes.Count, Is.EqualTo(3));

            var levelled = new PlayerUnlocks { Level = 10 };
            levelled.SetWeaponLevel("rifle", 2);
            LoadoutData partway = UnlockRules.Apply(wanted, catalog, levelled);
            Assert.That(partway.primary.weaponId, Is.EqualTo("rifle"));
            Assert.That(partway.primary.attachments, Is.EqualTo(new[] { "quick_mag" }), "the holo sight needs rifle level 3");

            LoadoutData everything = UnlockRules.Apply(wanted, catalog, PlayerUnlocks.Everything);
            Assert.That(everything.primary.attachments, Is.EquivalentTo(new[] { "holo", "quick_mag" }));
            Assert.That(everything.utility, Is.EqualTo("disc"));
        }
    }
}
