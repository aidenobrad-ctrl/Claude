using NUnit.Framework;
using UnityEngine;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Tests
{
    public class DamageCalculatorTests
    {
        [Test]
        public void DealtDamage_IsAlwaysAMultipleOfFive_AndNeverRoundsAHitAway()
        {
            Assert.That(DamageCalculator.Standardize(25f), Is.EqualTo(25f));
            Assert.That(DamageCalculator.Standardize(44f), Is.EqualTo(45f), "the example of an untidy number");
            Assert.That(DamageCalculator.Standardize(41.2f), Is.EqualTo(40f));
            Assert.That(DamageCalculator.Standardize(1f), Is.EqualTo(5f), "a hit that lands always counts");
            Assert.That(DamageCalculator.Standardize(0f), Is.EqualTo(0f));
            Assert.That(DamageCalculator.Standardize(-3f), Is.EqualTo(0f));
            for (float amount = 0.5f; amount < 300f; amount += 0.37f)
            {
                float dealt = DamageCalculator.Standardize(amount);
                Assert.That(dealt % 5f, Is.EqualTo(0f), amount.ToString());
                Assert.That(Mathf.Abs(dealt - amount), Is.LessThanOrEqualTo(4.5f), amount.ToString());
            }
        }

        static WeaponStats MakeStats()
        {
            return new WeaponStats
            {
                damage = 30f,
                headMultiplier = 1.5f,
                limbMultiplier = 0.8f,
                falloffStart = 20f,
                falloffEnd = 40f,
                minDamageMultiplier = 0.5f,
            };
        }

        [TestCase(0f, 30f)]
        [TestCase(20f, 30f)]
        [TestCase(30f, 22.5f)]
        [TestCase(40f, 15f)]
        [TestCase(200f, 15f)]
        public void BodyDamage_FallsOffWithDistance(float distance, float expected)
        {
            Assert.That(DamageCalculator.Compute(MakeStats(), distance, HitZone.Body), Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void HitZones_ScaleDamage()
        {
            WeaponStats stats = MakeStats();
            Assert.That(DamageCalculator.Compute(stats, 5f, HitZone.Head), Is.EqualTo(45f).Within(0.001f));
            Assert.That(DamageCalculator.Compute(stats, 5f, HitZone.Limb), Is.EqualTo(24f).Within(0.001f));
        }

        [Test]
        public void Falloff_IsIgnored_WhenTheRangeIsNotConfigured()
        {
            WeaponStats stats = MakeStats();
            stats.falloffEnd = stats.falloffStart;
            Assert.That(DamageCalculator.FalloffMultiplier(stats, 500f), Is.EqualTo(1f));
        }

        [TestCase(0f, 100f)]
        [TestCase(2f, 70f)]
        [TestCase(4f, 40f)]
        [TestCase(4.1f, 0f)]
        public void ExplosionDamage_FallsFromTheCentreToTheEdge_ThenStops(float distance, float expected)
        {
            var stats = new WeaponStats { damage = 100f, explosionRadius = 4f, explosionEdgeDamage = 0.4f };
            Assert.That(DamageCalculator.ExplosionDamage(stats, distance), Is.EqualTo(expected).Within(0.001f));
        }
    }

    public class MoveMathTests
    {
        const float Dt = SimConstants.TickDelta;

        [Test]
        public void Accelerate_ReachesWishSpeed_AndNeverExceedsIt()
        {
            Vector3 velocity = Vector3.zero;
            for (int i = 0; i < 120; i++)
            {
                velocity = MoveMath.ApplyFriction(velocity, 8f, 2.5f, Dt);
                velocity = MoveMath.Accelerate(velocity, Vector3.forward, 5.6f, 14f, Dt);
                Assert.That(velocity.magnitude, Is.LessThanOrEqualTo(5.6f + 0.0001f));
            }
            Assert.That(velocity.magnitude, Is.EqualTo(5.6f).Within(0.01f));
        }

        [Test]
        public void Friction_BringsYouToAFullStop()
        {
            Vector3 velocity = Vector3.forward * 7.8f;
            for (int i = 0; i < 60; i++) velocity = MoveMath.ApplyFriction(velocity, 8f, 2.5f, Dt);
            Assert.That(velocity.magnitude, Is.EqualTo(0f));
        }

        [Test]
        public void AirSteering_NeverAddsSpeed_EvenWhenStrafing()
        {
            // The classic exploit: hold a strafe key and sweep the aim to gain speed every tick.
            Vector3 velocity = Vector3.forward * 7.8f;
            float yaw = 0f;
            for (int i = 0; i < 600; i++)
            {
                yaw += 2f;
                velocity = MoveMath.Accelerate(velocity, AimMath.YawRight(yaw), 7.8f, 3.5f, Dt);
                Assert.That(velocity.magnitude, Is.LessThanOrEqualTo(7.8f + 0.001f));
            }
        }

        [Test]
        public void AirSteering_KeepsMomentumAboveWishSpeed()
        {
            // Leaving a slide at 10.5 m/s: steering must not clamp that down to run speed.
            Vector3 velocity = Vector3.forward * 10.5f;
            for (int i = 0; i < 30; i++)
                velocity = MoveMath.Accelerate(velocity, Vector3.forward, 5.6f, 3.5f, Dt);
            Assert.That(velocity.magnitude, Is.EqualTo(10.5f).Within(0.001f));
        }

        [Test]
        public void RunningDiagonallyIntoAWall_DoesNotBeatRunSpeed()
        {
            // The wall removes the part of the velocity pointing into it every tick. Without a
            // cap, input keeps feeding that lost part back in and the player outruns run speed.
            Vector3 wish = new Vector3(1f, 0f, 1f).normalized;
            Vector3 velocity = Vector3.zero;
            for (int i = 0; i < 240; i++)
            {
                velocity = MoveMath.ApplyFriction(velocity, 8f, 2.5f, Dt);
                velocity = MoveMath.Accelerate(velocity, wish, 5.6f, 14f, Dt);
                velocity.z = 0f;
                Assert.That(velocity.magnitude, Is.LessThanOrEqualTo(5.6f + 0.001f));
            }
            Assert.That(velocity.x, Is.GreaterThan(3f), "still slides along the wall");
        }

        [Test]
        public void RotateYaw_PositiveTurnsRight()
        {
            Vector3 turned = MoveMath.RotateYaw(Vector3.forward, 90f);
            Assert.That(turned.x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(turned.z, Is.EqualTo(0f).Within(0.0001f));
        }
    }

    public class AimMathTests
    {
        [Test]
        public void Direction_MatchesUnityConventions()
        {
            AssertVector(AimMath.Direction(0f, 0f), 0f, 0f, 1f);
            AssertVector(AimMath.Direction(90f, 0f), 1f, 0f, 0f);
            AssertVector(AimMath.Direction(0f, 90f), 0f, -1f, 0f);
            AssertVector(AimMath.Direction(0f, -90f), 0f, 1f, 0f);
        }

        [Test]
        public void YawBasis_IsForwardAndRight()
        {
            AssertVector(AimMath.YawForward(0f), 0f, 0f, 1f);
            AssertVector(AimMath.YawRight(0f), 1f, 0f, 0f);
            AssertVector(AimMath.YawForward(90f), 1f, 0f, 0f);
            AssertVector(AimMath.YawRight(90f), 0f, 0f, -1f);
        }

        static void AssertVector(Vector3 actual, float x, float y, float z)
        {
            Assert.That(actual.x, Is.EqualTo(x).Within(0.0001f));
            Assert.That(actual.y, Is.EqualTo(y).Within(0.0001f));
            Assert.That(actual.z, Is.EqualTo(z).Within(0.0001f));
        }
    }

    public class DeterministicRandomTests
    {
        [Test]
        public void SameSeed_GivesTheSameSequence()
        {
            var a = new DeterministicRandom(123);
            var b = new DeterministicRandom(123);
            for (int i = 0; i < 100; i++) Assert.That(a.NextUInt(), Is.EqualTo(b.NextUInt()));
        }

        [Test]
        public void NextFloat_IsUniformInZeroToOne()
        {
            var random = new DeterministicRandom(99);
            double sum = 0;
            const int count = 20000;
            for (int i = 0; i < count; i++)
            {
                float value = random.NextFloat();
                Assert.That(value, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
                sum += value;
            }
            Assert.That(sum / count, Is.EqualTo(0.5).Within(0.01));
        }

        [Test]
        public void NeighbouringShotSeeds_AreUnrelated()
        {
            // Consecutive shot numbers must not produce visibly correlated spread.
            float first = new DeterministicRandom(DeterministicRandom.Combine(5, 0)).NextFloat();
            float second = new DeterministicRandom(DeterministicRandom.Combine(5, 1)).NextFloat();
            Assert.That(Mathf.Abs(first - second), Is.GreaterThan(0.0001f));
        }
    }
}
