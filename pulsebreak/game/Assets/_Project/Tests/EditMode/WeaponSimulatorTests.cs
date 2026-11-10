using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Tests
{
    public class WeaponSimulatorTests
    {
        const float Dt = SimConstants.TickDelta;

        static readonly WeaponContext Standing = new WeaponContext
        {
            CanUseWeapon = true,
            Grounded = true,
            TimeSinceSprint = 10f,
        };

        /// <summary>A plain test weapon: no draw time, no recoil, no spread unless a test asks for it.</summary>
        static WeaponDefinition MakeWeapon(FireMode mode = FireMode.Automatic, float rpm = 720f, int magazine = 30)
        {
            var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            weapon.fireMode = mode;
            weapon.roundsPerMinute = rpm;
            weapon.magazineSize = magazine;
            weapon.reserveAmmo = 90;
            weapon.equipTime = 0f;
            weapon.reloadTime = 1f;
            weapon.emptyReloadTime = 1.5f;
            weapon.sprintToFireTime = 0.15f;
            weapon.recoilRandomness = 0f;
            weapon.recoilPattern = new[] { Vector2.zero };
            return weapon;
        }

        /// <summary>Runs ticks with the given buttons held and returns every shot fired.</summary>
        static List<ShotEvent> Run(WeaponSimulator weapon, int ticks, InputButtons buttons, WeaponContext context, ref int tick)
        {
            var shots = new List<ShotEvent>();
            for (int i = 0; i < ticks; i++)
            {
                var cmd = new PlayerInputCmd { Tick = tick++, Buttons = buttons };
                weapon.Simulate(cmd, context, Dt);
                shots.AddRange(weapon.Shots);
            }
            return shots;
        }

        [Test]
        public void Automatic_FiresAtTheConfiguredRate()
        {
            var weapon = new WeaponSimulator(MakeWeapon(rpm: 720f), 1);
            int tick = 0;

            // 720 rpm is 12 shots a second: at t = 0, 1/12, 2/12 ... 11/12.
            List<ShotEvent> shots = Run(weapon, SimConstants.TickRate, InputButtons.Fire, Standing, ref tick);

            Assert.That(shots.Count, Is.EqualTo(12));
        }

        [Test]
        public void Automatic_KeepsTheAverageRate_WhenTheIntervalIsNotAWholeNumberOfTicks()
        {
            // 800 rpm is one shot every 4.5 ticks. Rounding each interval up to 5 ticks would give 36 shots in 3 s.
            var weapon = new WeaponSimulator(MakeWeapon(rpm: 800f, magazine: 100), 1);
            int tick = 0;

            List<ShotEvent> shots = Run(weapon, SimConstants.TickRate * 3, InputButtons.Fire, Standing, ref tick);

            Assert.That(shots.Count, Is.EqualTo(40));
        }

        [Test]
        public void SemiAuto_FiresOncePerTriggerPull()
        {
            var weapon = new WeaponSimulator(MakeWeapon(FireMode.SemiAuto, rpm: 300f), 1);
            int tick = 0;

            Assert.That(Run(weapon, 30, InputButtons.Fire, Standing, ref tick).Count, Is.EqualTo(1), "holding the trigger");
            Assert.That(Run(weapon, 5, InputButtons.None, Standing, ref tick).Count, Is.EqualTo(0), "released");
            Assert.That(Run(weapon, 30, InputButtons.Fire, Standing, ref tick).Count, Is.EqualTo(1), "second pull");
        }

        [Test]
        public void SemiAuto_CannotExceedTheFireRateByClickingFaster()
        {
            var weapon = new WeaponSimulator(MakeWeapon(FireMode.SemiAuto, rpm: 300f), 1);
            int tick = 0;
            int fired = 0;

            // Click every other tick for one second. 300 rpm allows five shots.
            for (int i = 0; i < SimConstants.TickRate / 2; i++)
            {
                fired += Run(weapon, 1, InputButtons.Fire, Standing, ref tick).Count;
                fired += Run(weapon, 1, InputButtons.None, Standing, ref tick).Count;
            }

            Assert.That(fired, Is.EqualTo(5));
        }

        [Test]
        public void Burst_FiresTheWholeBurstFromOneTap_ThenWaits()
        {
            WeaponDefinition definition = MakeWeapon(FireMode.Burst, rpm: 900f);
            definition.burstCount = 3;
            definition.burstCooldown = 0.25f;
            var weapon = new WeaponSimulator(definition, 1);
            int tick = 0;

            int fired = Run(weapon, 1, InputButtons.Fire, Standing, ref tick).Count;
            fired += Run(weapon, 20, InputButtons.None, Standing, ref tick).Count;

            Assert.That(fired, Is.EqualTo(3));
            Assert.That(weapon.State.AmmoInMagazine, Is.EqualTo(27));
        }

        [Test]
        public void EmptyMagazine_ReloadsAutomatically_AndRefillsFromReserve()
        {
            WeaponDefinition definition = MakeWeapon(rpm: 720f, magazine: 5);
            var weapon = new WeaponSimulator(definition, 1);
            int tick = 0;

            List<ShotEvent> shots = Run(weapon, 40, InputButtons.Fire, Standing, ref tick);
            Assert.That(shots.Count, Is.EqualTo(5));
            Assert.That(weapon.State.Phase, Is.EqualTo(WeaponPhase.Reloading));

            // Empty reload takes 1.5 s; nothing can fire during it.
            int ticksToFinish = Mathf.CeilToInt(definition.emptyReloadTime / Dt) + 2;
            Assert.That(Run(weapon, ticksToFinish, InputButtons.None, Standing, ref tick).Count, Is.EqualTo(0));

            Assert.That(weapon.State.Phase, Is.EqualTo(WeaponPhase.Ready));
            Assert.That(weapon.State.AmmoInMagazine, Is.EqualTo(5));
            Assert.That(weapon.State.AmmoReserve, Is.EqualTo(85));
        }

        [Test]
        public void Reload_CannotFire_UntilItCompletes()
        {
            WeaponDefinition definition = MakeWeapon(magazine: 10);
            var weapon = new WeaponSimulator(definition, 1);
            int tick = 0;

            Run(weapon, 6, InputButtons.Fire, Standing, ref tick);
            Run(weapon, 2, InputButtons.None, Standing, ref tick);
            Run(weapon, 1, InputButtons.Reload, Standing, ref tick);
            Assert.That(weapon.State.Phase, Is.EqualTo(WeaponPhase.Reloading));

            int almostDone = Mathf.FloorToInt(definition.reloadTime / Dt) - 3;
            Assert.That(Run(weapon, almostDone, InputButtons.Fire, Standing, ref tick).Count, Is.EqualTo(0));
            Assert.That(Run(weapon, 10, InputButtons.Fire, Standing, ref tick).Count, Is.GreaterThan(0));
        }

        [Test]
        public void InfiniteReserve_NeverDrains()
        {
            var weapon = new WeaponSimulator(MakeWeapon(magazine: 5), 1) { InfiniteReserve = true };
            int tick = 0;

            Run(weapon, SimConstants.TickRate * 6, InputButtons.Fire, Standing, ref tick);

            Assert.That(weapon.State.AmmoReserve, Is.EqualTo(90));
        }

        [Test]
        public void Sprinting_BlocksFiring_UntilTheSprintOutDelayHasPassed()
        {
            WeaponDefinition definition = MakeWeapon();
            var weapon = new WeaponSimulator(definition, 1);
            int tick = 0;

            WeaponContext sprinting = Standing;
            sprinting.Sprinting = true;
            sprinting.TimeSinceSprint = 0f;
            Assert.That(Run(weapon, 20, InputButtons.Fire, sprinting, ref tick).Count, Is.EqualTo(0));

            WeaponContext justStopped = Standing;
            justStopped.TimeSinceSprint = definition.sprintToFireTime * 0.5f;
            Assert.That(Run(weapon, 1, InputButtons.Fire, justStopped, ref tick).Count, Is.EqualTo(0));

            Assert.That(Run(weapon, 1, InputButtons.Fire, Standing, ref tick).Count, Is.EqualTo(1));
        }

        [Test]
        public void Mantling_BlocksFiring()
        {
            var weapon = new WeaponSimulator(MakeWeapon(), 1);
            int tick = 0;
            WeaponContext busy = Standing;
            busy.CanUseWeapon = false;

            Assert.That(Run(weapon, 30, InputButtons.Fire, busy, ref tick).Count, Is.EqualTo(0));
        }

        [Test]
        public void Shots_AreIdentical_ForTheSameSeed_AndDifferent_ForAnother()
        {
            WeaponDefinition definition = MakeWeapon();
            definition.hipSpread = 2f;
            int tickA = 0, tickB = 0, tickC = 0;

            List<ShotEvent> a = Run(new WeaponSimulator(definition, 42), 60, InputButtons.Fire, Standing, ref tickA);
            List<ShotEvent> b = Run(new WeaponSimulator(definition, 42), 60, InputButtons.Fire, Standing, ref tickB);
            List<ShotEvent> c = Run(new WeaponSimulator(definition, 43), 60, InputButtons.Fire, Standing, ref tickC);

            Assert.That(a.Count, Is.EqualTo(b.Count));
            bool anyDifferent = false;
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(a[i].AimOffset.x, Is.EqualTo(b[i].AimOffset.x));
                Assert.That(a[i].AimOffset.y, Is.EqualTo(b[i].AimOffset.y));
                anyDifferent |= a[i].AimOffset.x != c[i].AimOffset.x;
            }
            Assert.That(anyDifferent, Is.True);
        }

        [Test]
        public void Spread_StaysInsideTheReportedCone()
        {
            WeaponDefinition definition = MakeWeapon(magazine: 200);
            definition.hipSpread = 1.5f;
            definition.bloomPerShot = 0.2f;
            definition.maxBloom = 2f;
            var weapon = new WeaponSimulator(definition, 7);
            int tick = 0;

            // With no recoil the whole aim offset is spread.
            List<ShotEvent> shots = Run(weapon, 600, InputButtons.Fire, Standing, ref tick);

            Assert.That(shots.Count, Is.GreaterThan(100));
            foreach (ShotEvent shot in shots)
                Assert.That(shot.AimOffset.magnitude, Is.LessThanOrEqualTo(shot.Spread + 0.0001f));
        }

        [Test]
        public void Spread_Tightens_WhenAiming_AndWidens_InTheAir()
        {
            WeaponDefinition definition = MakeWeapon();
            var weapon = new WeaponSimulator(definition, 1);
            int tick = 0;

            Run(weapon, 1, InputButtons.None, Standing, ref tick);
            float hip = weapon.CurrentSpread;

            WeaponContext airborne = Standing;
            airborne.Grounded = false;
            Run(weapon, 1, InputButtons.None, airborne, ref tick);
            float air = weapon.CurrentSpread;

            Run(weapon, 60, InputButtons.Ads, Standing, ref tick);
            float aimed = weapon.CurrentSpread;

            Assert.That(air, Is.GreaterThan(hip));
            Assert.That(aimed, Is.LessThan(hip));
            Assert.That(aimed, Is.EqualTo(definition.adsSpread).Within(0.0001f));
        }

        [Test]
        public void Ads_TakesTheConfiguredTime()
        {
            WeaponDefinition definition = MakeWeapon();
            definition.adsTime = 0.2f;
            var weapon = new WeaponSimulator(definition, 1);
            int tick = 0;

            Run(weapon, 6, InputButtons.Ads, Standing, ref tick);
            Assert.That(weapon.State.AdsAmount, Is.EqualTo(0.5f).Within(0.01f));

            Run(weapon, 7, InputButtons.Ads, Standing, ref tick);
            Assert.That(weapon.State.AdsAmount, Is.EqualTo(1f));

            Run(weapon, 13, InputButtons.None, Standing, ref tick);
            Assert.That(weapon.State.AdsAmount, Is.EqualTo(0f));
        }

        [Test]
        public void Recoil_FollowsThePattern_ThenRecoversToZero()
        {
            WeaponDefinition definition = MakeWeapon(rpm: 600f);
            definition.recoilPattern = new[] { new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.25f) };
            definition.recoilLoopStart = 1;
            var weapon = new WeaponSimulator(definition, 1);
            int tick = 0;

            // 600 rpm is a shot every 6 ticks, so 19 ticks is four shots: pattern 0, 1, 2, then loop back to 1.
            List<ShotEvent> shots = Run(weapon, 19, InputButtons.Fire, Standing, ref tick);
            Assert.That(shots.Count, Is.EqualTo(4));
            Assert.That(weapon.State.RecoilTarget.x, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(weapon.State.RecoilTarget.y, Is.EqualTo(2.25f).Within(0.0001f));

            Run(weapon, SimConstants.TickRate * 2, InputButtons.None, Standing, ref tick);
            Assert.That(weapon.State.RecoilTarget.magnitude, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(weapon.State.RecoilCurrent.magnitude, Is.LessThan(0.01f));
            Assert.That(weapon.State.SprayIndex, Is.EqualTo(0));
        }

        [Test]
        public void RecoilPattern_LoopsFromLoopStart()
        {
            WeaponDefinition definition = MakeWeapon();
            definition.recoilPattern = new[] { new Vector2(0f, 1f), new Vector2(0f, 2f), new Vector2(0f, 3f) };
            definition.recoilLoopStart = 1;

            Assert.That(definition.GetRecoilKick(0).y, Is.EqualTo(1f));
            Assert.That(definition.GetRecoilKick(2).y, Is.EqualTo(3f));
            Assert.That(definition.GetRecoilKick(3).y, Is.EqualTo(2f));
            Assert.That(definition.GetRecoilKick(4).y, Is.EqualTo(3f));
            Assert.That(definition.GetRecoilKick(5).y, Is.EqualTo(2f));
        }
    }
}
