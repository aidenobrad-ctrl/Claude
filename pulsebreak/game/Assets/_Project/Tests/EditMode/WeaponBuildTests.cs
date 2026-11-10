using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Tests
{
    public class WeaponBuildTests
    {
        static AttachmentDefinition Attachment(string id, AttachmentSlot slot, params StatModifier[] modifiers)
        {
            var attachment = ScriptableObject.CreateInstance<AttachmentDefinition>();
            attachment.attachmentId = id;
            attachment.displayName = id;
            attachment.slot = slot;
            attachment.modifiers = modifiers;
            return attachment;
        }

        static WeaponDefinition Weapon(params AttachmentDefinition[] accepted)
        {
            var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            weapon.weaponId = "test_rifle";
            weapon.displayName = "Test Rifle";
            weapon.stats = new WeaponStats { damage = 20f, magazineSize = 30, reloadTime = 2f, emptyReloadTime = 2.5f, adsTime = 0.2f, adsZoom = 1.25f };
            weapon.attachments = accepted;
            return weapon;
        }

        static StatModifier Times(WeaponStat stat, float value) => new StatModifier(stat, ModifierOp.Multiply, value);
        static StatModifier Plus(WeaponStat stat, float value) => new StatModifier(stat, ModifierOp.Add, value);

        [Test]
        public void NoAttachments_GivesTheBaseStats()
        {
            WeaponDefinition weapon = Weapon();
            WeaponStats stats = new WeaponBuild(weapon).ResolveStats();

            Assert.That(stats.damage, Is.EqualTo(20f));
            Assert.That(stats.magazineSize, Is.EqualTo(30));
            Assert.That(stats, Is.Not.SameAs(weapon.stats), "the base stats must never be handed out to be modified");
        }

        [Test]
        public void Attachments_ChangeTheStatsTheyName()
        {
            AttachmentDefinition magazine = Attachment("ext_mag", AttachmentSlot.Magazine,
                Times(WeaponStat.MagazineSize, 1.5f), Times(WeaponStat.ReloadTime, 1.1f));
            WeaponDefinition weapon = Weapon(magazine);
            var build = new WeaponBuild(weapon);

            Assert.That(build.Fit(magazine), Is.True);
            WeaponStats stats = build.ResolveStats();

            Assert.That(stats.magazineSize, Is.EqualTo(45));
            Assert.That(stats.reloadTime, Is.EqualTo(2.2f).Within(0.0001f));
            Assert.That(stats.emptyReloadTime, Is.EqualTo(2.75f).Within(0.0001f), "reload time covers both reloads");
            Assert.That(weapon.stats.magazineSize, Is.EqualTo(30), "the weapon asset itself is untouched");
        }

        [Test]
        public void Additions_ApplyBeforeMultipliers_WhateverOrderTheyAreFittedIn()
        {
            AttachmentDefinition barrel = Attachment("barrel", AttachmentSlot.Barrel, Times(WeaponStat.Damage, 1.5f));
            AttachmentDefinition grip = Attachment("grip", AttachmentSlot.Grip, Plus(WeaponStat.Damage, 10f));
            WeaponDefinition weapon = Weapon(barrel, grip);

            float oneWay = new WeaponBuild(weapon, new[] { barrel, grip }).ResolveStats().damage;
            float otherWay = new WeaponBuild(weapon, new[] { grip, barrel }).ResolveStats().damage;

            // (20 + 10) x 1.5
            Assert.That(oneWay, Is.EqualTo(45f).Within(0.0001f));
            Assert.That(otherWay, Is.EqualTo(oneWay));
        }

        [Test]
        public void AWeaponOnlyTakesAttachmentsItLists()
        {
            AttachmentDefinition listed = Attachment("listed", AttachmentSlot.Grip, Times(WeaponStat.AdsTime, 0.9f));
            AttachmentDefinition stranger = Attachment("stranger", AttachmentSlot.Grip, Times(WeaponStat.Damage, 5f));
            var build = new WeaponBuild(Weapon(listed));

            Assert.That(build.Fit(stranger), Is.False);
            Assert.That(build.Get(AttachmentSlot.Grip), Is.Null);
            Assert.That(build.ResolveStats().damage, Is.EqualTo(20f));
        }

        [Test]
        public void EachSlotHoldsOneAttachment()
        {
            AttachmentDefinition first = Attachment("first", AttachmentSlot.Stock, Times(WeaponStat.AdsTime, 0.5f));
            AttachmentDefinition second = Attachment("second", AttachmentSlot.Stock, Times(WeaponStat.AdsTime, 2f));
            var build = new WeaponBuild(Weapon(first, second));

            build.Fit(first);
            build.Fit(second);
            Assert.That(build.Get(AttachmentSlot.Stock), Is.SameAs(second));
            Assert.That(build.ResolveStats().adsTime, Is.EqualTo(0.4f).Within(0.0001f));

            build.Clear(AttachmentSlot.Stock);
            Assert.That(build.ResolveStats().adsTime, Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void OptionsFor_ListsOnlyThatSlot()
        {
            AttachmentDefinition sightA = Attachment("a", AttachmentSlot.Sight);
            AttachmentDefinition sightB = Attachment("b", AttachmentSlot.Sight);
            AttachmentDefinition laser = Attachment("c", AttachmentSlot.Laser);
            var build = new WeaponBuild(Weapon(sightA, laser, sightB));

            List<AttachmentDefinition> sights = build.OptionsFor(AttachmentSlot.Sight);

            Assert.That(sights, Is.EqualTo(new[] { sightA, sightB }));
            Assert.That(build.OptionsFor(AttachmentSlot.Barrel), Is.Empty);
        }

        [Test]
        public void ASight_SetsTheZoomAndTheReticle()
        {
            AttachmentDefinition scope = Attachment("scope", AttachmentSlot.Sight);
            scope.sightZoom = 3f;
            scope.reticle = ReticleStyle.Chevron;
            var build = new WeaponBuild(Weapon(scope));

            Assert.That(build.Reticle, Is.EqualTo(ReticleStyle.None), "iron sights by default");
            build.Fit(scope);

            Assert.That(build.Reticle, Is.EqualTo(ReticleStyle.Chevron));
            Assert.That(build.ResolveStats().adsZoom, Is.EqualTo(3f));
        }

        [Test]
        public void ASuppressor_MarksTheBuildSuppressed()
        {
            AttachmentDefinition suppressor = Attachment("suppressor", AttachmentSlot.Barrel);
            suppressor.suppressesSound = true;
            var build = new WeaponBuild(Weapon(suppressor));

            Assert.That(build.Suppressed, Is.False);
            build.Fit(suppressor);
            Assert.That(build.Suppressed, Is.True);
        }

        [Test]
        public void ExtremeModifiers_AreClampedToSomethingPlayable()
        {
            AttachmentDefinition broken = Attachment("broken", AttachmentSlot.Barrel,
                Times(WeaponStat.MagazineSize, 0.001f), Plus(WeaponStat.AdsTime, -5f), Plus(WeaponStat.FireRate, -99999f));
            var build = new WeaponBuild(Weapon(broken));
            build.Fit(broken);

            WeaponStats stats = build.ResolveStats();

            Assert.That(stats.magazineSize, Is.GreaterThanOrEqualTo(0));
            Assert.That(stats.adsTime, Is.GreaterThan(0f));
            Assert.That(stats.roundsPerMinute, Is.GreaterThanOrEqualTo(1f));
        }

        [Test]
        public void RecoilAttachments_ScaleTheKick_WithoutTouchingThePattern()
        {
            AttachmentDefinition grip = Attachment("grip", AttachmentSlot.Grip, Times(WeaponStat.RecoilVertical, 0.8f));
            WeaponDefinition weapon = Weapon(grip);
            weapon.stats.recoilPattern = new[] { new Vector2(0.5f, 1f) };
            var build = new WeaponBuild(weapon);
            build.Fit(grip);

            WeaponStats stats = build.ResolveStats();

            Assert.That(stats.GetRecoilKick(0).y, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(stats.GetRecoilKick(0).x, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(weapon.stats.recoilPattern[0].y, Is.EqualTo(1f));
        }
    }

    public class WeaponRatingsTests
    {
        [Test]
        public void TimeToKill_CountsTheGapsBetweenShots()
        {
            // 26 damage needs four body shots: three gaps at 720 rpm.
            var stats = new WeaponStats { damage = 26f, roundsPerMinute = 720f };
            WeaponRatings ratings = WeaponRatings.From(stats);

            Assert.That(ratings.ShotsToKill, Is.EqualTo(4));
            Assert.That(ratings.TimeToKill, Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(ratings.DamagePerSecond, Is.EqualTo(312f).Within(0.5f));
        }

        [Test]
        public void AShotgun_AddsUpItsPellets()
        {
            var stats = new WeaponStats { damage = 14f, pellets = 8, roundsPerMinute = 70f };
            WeaponRatings ratings = WeaponRatings.From(stats);

            Assert.That(ratings.DamagePerShot, Is.EqualTo(112f));
            Assert.That(ratings.ShotsToKill, Is.EqualTo(1));
            Assert.That(ratings.TimeToKill, Is.EqualTo(0f));
        }

        [Test]
        public void ABurstWeapon_PaysForThePauseBetweenBursts()
        {
            var stats = new WeaponStats
            {
                fireMode = FireMode.Burst, damage = 30f, roundsPerMinute = 900f, burstCount = 3, burstCooldown = 0.2f,
            };
            WeaponRatings ratings = WeaponRatings.From(stats);

            // Four shots: one full burst, the pause, then the first shot of the next.
            Assert.That(ratings.ShotsToKill, Is.EqualTo(4));
            Assert.That(ratings.TimeToKill, Is.EqualTo(3f * (60f / 900f) + 0.2f).Within(0.001f));
            Assert.That(ratings.EffectiveRpm, Is.LessThan(900f));
        }

        [Test]
        public void Ratings_StayBetweenZeroAndOne()
        {
            var extreme = new WeaponStats
            {
                damage = 5000f, roundsPerMinute = 6000f, falloffStart = 900f, falloffEnd = 1000f,
                hipSpread = 50f, adsSpread = 20f, adsTime = 9f, hipMoveMultiplier = 0.1f,
            };
            WeaponRatings r = WeaponRatings.From(extreme);

            foreach (float value in new[] { r.Damage, r.FireRate, r.Range, r.Accuracy, r.Control, r.Mobility, r.Handling })
                Assert.That(value, Is.InRange(0f, 1f));
        }
    }

    public class WeaponValidatorTests
    {
        static WeaponDefinition Valid()
        {
            var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            weapon.weaponId = "valid_rifle";
            weapon.displayName = "Valid Rifle";
            return weapon;
        }

        [Test]
        public void AFreshWeaponWithAnId_IsValid()
        {
            Assert.That(WeaponValidator.Validate(Valid()), Is.Empty);
        }

        [Test]
        public void BadIds_AreReported()
        {
            WeaponDefinition weapon = Valid();
            weapon.weaponId = "Has Spaces";
            Assert.That(WeaponValidator.Validate(weapon), Is.Not.Empty);
        }

        [Test]
        public void MistakesInTheNumbers_AreReported()
        {
            WeaponDefinition burst = Valid();
            burst.stats.fireMode = FireMode.Burst;
            burst.stats.burstCount = 1;
            Assert.That(WeaponValidator.Validate(burst), Has.Count.EqualTo(1));

            WeaponDefinition loop = Valid();
            loop.stats.recoilLoopStart = 5;
            Assert.That(WeaponValidator.Validate(loop), Has.Count.EqualTo(1));

            WeaponDefinition falloff = Valid();
            falloff.stats.falloffStart = 50f;
            falloff.stats.falloffEnd = 20f;
            Assert.That(WeaponValidator.Validate(falloff), Has.Count.EqualTo(1));
        }

        [Test]
        public void AMeleeWeapon_MustBeInTheMeleeSlot_WithNoAmmo()
        {
            WeaponDefinition blade = Valid();
            blade.stats.ballistics = BallisticsType.Melee;
            Assert.That(WeaponValidator.Validate(blade), Has.Count.EqualTo(2));

            blade.slot = WeaponSlotType.Melee;
            blade.stats.magazineSize = 0;
            Assert.That(WeaponValidator.Validate(blade), Is.Empty);
        }

        [Test]
        public void ACatalog_ReportsDuplicateIds_AndAttachmentsItDoesNotKnow()
        {
            var known = ScriptableObject.CreateInstance<AttachmentDefinition>();
            known.attachmentId = "known";
            known.displayName = "Known";
            var unknown = ScriptableObject.CreateInstance<AttachmentDefinition>();
            unknown.attachmentId = "unknown";
            unknown.displayName = "Unknown";

            WeaponDefinition first = Valid();
            first.attachments = new[] { known, unknown };
            WeaponDefinition second = Valid();

            var catalog = ScriptableObject.CreateInstance<WeaponCatalog>();
            catalog.weapons = new[] { first, second };
            catalog.attachments = new[] { known };

            List<string> problems = WeaponValidator.Validate(catalog);

            Assert.That(problems, Has.Count.EqualTo(2));
            Assert.That(problems, Has.Some.Contains("more than once"));
            Assert.That(problems, Has.Some.Contains("not in the catalog"));
        }

        [Test]
        public void AnAttachmentThatZeroesAStat_IsReported()
        {
            var attachment = ScriptableObject.CreateInstance<AttachmentDefinition>();
            attachment.attachmentId = "zero";
            attachment.displayName = "Zero";
            attachment.modifiers = new[] { new StatModifier(WeaponStat.Damage, ModifierOp.Multiply, 0f) };

            Assert.That(WeaponValidator.Validate(attachment), Has.Count.EqualTo(1));
        }
    }
}
