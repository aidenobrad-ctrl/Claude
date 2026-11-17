using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Tests
{
    /// <summary>The parts of the game modes that client and server share: maps, rules, playlists and restricted loadouts.</summary>
    public class MatchRulesTests
    {
        [Test]
        public void MaskedSlots_AreEmptied_AndTheUtilityGoesWithItsBit()
        {
            LoadoutCatalog catalog = LoadoutTestKit.MakeCatalog();
            Loadout all = MatchLoadouts.Apply(LoadoutRules.Resolve(LoadoutTestKit.ValidData(), catalog), MatchLoadouts.AllSlots);
            Assert.That(all.Weapons, Has.None.Null);
            Assert.That(all.Utilities[0], Is.Not.Null);

            Loadout secondaryOnly = MatchLoadouts.Apply(LoadoutRules.Resolve(LoadoutTestKit.ValidData(), catalog), 1 << 1);
            Assert.That(secondaryOnly.Weapons[0], Is.Null);
            Assert.That(secondaryOnly.Weapons[1], Is.Not.Null);
            Assert.That(secondaryOnly.Weapons[2], Is.Null);
            Assert.That(secondaryOnly.Utilities[0], Is.Null);
            Assert.That(MatchLoadouts.Allows(1 << 1, 1), Is.True);
            Assert.That(MatchLoadouts.Allows(1 << 1, 0), Is.False);

            // A mask that leaves no weapon at all would leave a player empty-handed: it is ignored for weapons.
            Loadout noWeapons = MatchLoadouts.Apply(LoadoutRules.Resolve(LoadoutTestKit.ValidData(), catalog), MatchLoadouts.UtilityBit);
            Assert.That(noWeapons.Weapons, Has.None.Null);
            Assert.That(noWeapons.Utilities[0], Is.Not.Null);
        }

        [Test]
        public void GunGameLoadout_IsOneWeaponAndAKnife()
        {
            LoadoutCatalog catalog = LoadoutTestKit.MakeCatalog();
            for (int level = 0; level < GunGameLadder.Length; level++)
            {
                LoadoutData data = MatchLoadouts.GunGame(level, catalog, out byte mask, out int active);
                Loadout loadout = MatchLoadouts.Apply(LoadoutRules.Resolve(data, catalog), mask);
                int carried = 0;
                foreach (WeaponBuild build in loadout.Weapons)
                {
                    if (build != null) carried++;
                }
                Assert.That(loadout.Weapons[active], Is.Not.Null, "level " + level);
                Assert.That(loadout.Weapons[(int)WeaponSlotType.Melee], Is.Not.Null, "a knife at every level");
                Assert.That(carried, Is.LessThanOrEqualTo(2));
                Assert.That(loadout.Utilities[0], Is.Null);
            }
        }

        [Test]
        public void EveryMode_HasAPlaylist_ThatAgreesWithItsRules()
        {
            var ids = new HashSet<string>();
            foreach (GameModeKind mode in System.Enum.GetValues(typeof(GameModeKind)))
            {
                Playlist playlist = Playlists.For(mode);
                Assert.That(playlist.Mode, Is.EqualTo(mode), "no playlist for " + mode);
                Assert.That(ids.Add(playlist.Id), Is.True, "two playlists share the id " + playlist.Id);
                Assert.That(Playlists.Find(playlist.Id), Is.SameAs(playlist));

                MatchSettings rules = MatchSettings.For(mode);
                if (playlist.TeamSize > 0) rules.teamSize = playlist.TeamSize;
                else rules.maxPlayers = playlist.Players;
                Assert.That(rules.Capacity, Is.EqualTo(playlist.Players), playlist.Id);
                Assert.That(rules.TeamBased, Is.EqualTo(playlist.TeamSize > 0), playlist.Id);
                Assert.That(playlist.MinPlayers, Is.InRange(1, playlist.Players), playlist.Id);
                Assert.That(ArenaLayout.Find(rules.arenaId).Supports(rules), Is.True, playlist.Id + " plays on a map that cannot hold it");
            }
            Assert.That(Playlists.Find("nonsense"), Is.Null);
        }

        [Test]
        public void EveryModesDefaultMap_HasWhatTheModeNeeds()
        {
            foreach (GameModeKind mode in System.Enum.GetValues(typeof(GameModeKind)))
            {
                MatchSettings rules = MatchSettings.For(mode);
                ArenaLayout arena = ArenaLayout.Find(rules.arenaId);
                Assert.That(arena.Id, Is.EqualTo(rules.arenaId), mode + " names a map that does not exist");
                Assert.That(arena.Supports(rules), Is.True, mode.ToString());
                if (rules.TeamBased)
                {
                    foreach (ArenaSpawn[] side in arena.Sides)
                        Assert.That(side.Length, Is.GreaterThanOrEqualTo(rules.teamSize), mode + ": a spot for everyone on a team");
                }
            }
        }

        [Test]
        public void EverySpawnAndZone_IsOnTheFloor_AndNotInsideAnything()
        {
            foreach (string id in ArenaLayout.Ids)
            {
                ArenaLayout arena = ArenaLayout.Find(id);
                var spots = new List<Vector3>();
                foreach (ArenaSpawn[] side in arena.Sides)
                {
                    foreach (ArenaSpawn spawn in side) spots.Add(spawn.Position);
                }
                foreach (ArenaSpawn spawn in arena.FreeSpawns) spots.Add(spawn.Position);
                foreach (ArenaZone zone in arena.Zones) spots.Add(zone.Center);
                foreach (ArenaZone zone in arena.Hardpoints) spots.Add(zone.Center);

                foreach (Vector3 spot in spots)
                {
                    bool standing = false;
                    foreach (ArenaBox box in arena.Boxes)
                    {
                        Vector3 min = box.Center - box.Size * 0.5f, max = box.Center + box.Size * 0.5f;
                        bool over = spot.x > min.x && spot.x < max.x && spot.z > min.z && spot.z < max.z;
                        if (!over) continue;
                        if (Mathf.Abs(spot.y - max.y) < 0.05f) standing = true;
                        // Nothing solid where a body would be.
                        Assert.That(max.y <= spot.y + 0.05f || min.y >= spot.y + 1.8f, Is.True, id + ": " + spot + " is inside a block at " + box.Center);
                    }
                    Assert.That(standing, Is.True, id + ": " + spot + " is not standing on anything");
                }
            }
        }

        [Test]
        public void AFreshMatchState_HasNobodyWinning_AndNoZone()
        {
            MatchState state = MatchState.Empty;
            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Waiting));
            Assert.That(state.RoundWinner, Is.EqualTo(MatchState.Nobody));
            Assert.That(state.MatchWinner, Is.EqualTo(MatchState.Nobody));
            Assert.That(state.ActiveZone, Is.EqualTo(-1));

            state.AddScore(1, 3);
            state.AddScore(MatchState.Nobody, 5);
            state.AddScore(Teams.None, 5);
            Assert.That(state.Score(0), Is.EqualTo(0));
            Assert.That(state.Score(1), Is.EqualTo(3));
        }
    }
}
