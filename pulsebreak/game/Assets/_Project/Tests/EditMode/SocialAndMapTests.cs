using NUnit.Framework;
using UnityEngine;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Social;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Tests
{
    public class SocialAndMapTests
    {
        [Test]
        public void ChatText_IsCleaned_AndStrongLanguageHidden_WithoutCatchingOrdinaryWords()
        {
            Assert.That(ChatText.Clean("  push ​  left\n"), Is.EqualTo("push left"));
            Assert.That(ChatText.Clean(" \t "), Is.Null);
            Assert.That(ChatText.Clean(new string('a', 400)).Length, Is.EqualTo(ChatText.MaxLength));
            Assert.That(ChatText.Filter("sh1t, FUUUCK"), Is.EqualTo("****, ******"));
            foreach (string fine in new[] { "good night", "classy pass", "Scunthorpe assessment", "nice shot", "shoot the pillar" })
                Assert.That(ChatText.HasStrongLanguage(fine), Is.False, fine);
        }

        [Test]
        public void WeaponRules_LeaveTheRightSlots()
        {
            Assert.That(WeaponRules.Mask(WeaponRule.Any), Is.EqualTo(MatchLoadouts.AllSlots));
            Assert.That(MatchLoadouts.Allows(WeaponRules.Mask(WeaponRule.SidearmsOnly), (int)WeaponSlotType.Primary), Is.False);
            Assert.That(MatchLoadouts.Allows(WeaponRules.Mask(WeaponRule.SidearmsOnly), (int)WeaponSlotType.Secondary), Is.True);
            Assert.That(MatchLoadouts.Allows(WeaponRules.Mask(WeaponRule.MeleeOnly), (int)WeaponSlotType.Secondary), Is.False);
            Assert.That(WeaponRules.FirstSlot(WeaponRule.MeleeOnly), Is.EqualTo((int)WeaponSlotType.Melee));
            Assert.That(WeaponRules.All.Length, Is.EqualTo(6));
        }

        [Test]
        public void Maps_NamePlaces_AndEveryModeHasSomewhereToPlay()
        {
            ArenaLayout saltworks = ArenaLayout.Find(ArenaLayout.SaltworksId);
            Assert.That(saltworks.PlaceName(new Vector3(-14f, 0f, 0f)), Is.EqualTo("on A"));
            Assert.That(ArenaLayout.Find(ArenaLayout.KilnId).PlaceName(new Vector3(0f, 0f, -10f)), Is.EqualTo("south"));
            foreach (GameModeKind mode in new[] { GameModeKind.Duel, GameModeKind.TeamDeathmatch, GameModeKind.FreeForAll, GameModeKind.Elimination,
                         GameModeKind.Domination, GameModeKind.Hardpoint, GameModeKind.GunGame })
            {
                Assert.That(ArenaLayout.SupportingIds(MatchSettings.For(mode)).Count, Is.GreaterThanOrEqualTo(2), mode + " has a choice of maps");
            }
            Assert.That(ArenaLayout.Ids.Length, Is.EqualTo(5));
        }
    }
}
