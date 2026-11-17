using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Tests
{
    /// <summary>Shared helpers for driving a weapon tick by tick.</summary>
    static class WeaponTestKit
    {
        public const float Dt = SimConstants.TickDelta;

        public static readonly WeaponContext Standing = new WeaponContext
        {
            CanUseWeapon = true,
            Grounded = true,
            TimeSinceSprint = 10f,
        };

        /// <summary>A plain test weapon: no draw time, no recoil, no spread unless a test asks for it.</summary>
        public static WeaponStats MakeStats(FireMode mode = FireMode.Automatic, float rpm = 720f, int magazine = 30)
        {
            return new WeaponStats
            {
                fireMode = mode,
                roundsPerMinute = rpm,
                magazineSize = magazine,
                reserveAmmo = 90,
                equipTime = 0f,
                holsterTime = 0f,
                reloadTime = 1f,
                emptyReloadTime = 1.5f,
                sprintToFireTime = 0.15f,
                recoilRandomness = 0f,
                recoilPattern = new[] { Vector2.zero },
            };
        }

        /// <summary>Runs ticks with the given buttons held and returns every shot fired.</summary>
        public static List<ShotEvent> Run(WeaponSimulator weapon, int ticks, InputButtons buttons, WeaponContext context, ref int tick)
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
    }

    public class WeaponSimulatorTests
    {
        const float Dt = WeaponTestKit.Dt;
        static WeaponContext Standing => WeaponTestKit.Standing;
        static WeaponStats MakeStats(FireMode mode = FireMode.Automatic, float rpm = 720f, int magazine = 30) =>
            WeaponTestKit.MakeStats(mode, rpm, magazine);
        static List<ShotEvent> Run(WeaponSimulator weapon, int ticks, InputButtons buttons, WeaponContext context, ref int tick) =>
            WeaponTestKit.Run(weapon, ticks, buttons, context, ref tick);

        [Test]
        public void Automatic_FiresAtTheConfiguredRate()
        {
            var weapon = new WeaponSimulator(MakeStats(rpm: 720f), 1);
            int tick = 0;

            // 720 rpm is 12 shots a second: at t = 0, 1/12, 2/12 ... 11/12.
            List<ShotEvent> shots = Run(weapon, SimConstants.TickRate, InputButtons.Fire, Standing, ref tick);

            Assert.That(shots.Count, Is.EqualTo(12));
        }

        [Test]
        public void Automatic_KeepsTheAverageRate_WhenTheIntervalIsNotAWholeNumberOfTicks()
        {
            // 800 rpm is one shot every 4.5 ticks. Rounding each interval up to 5 ticks would give 36 shots in 3 s.
            var weapon = new WeaponSimulator(MakeStats(rpm: 800f, magazine: 100), 1);
            int tick = 0;

            List<ShotEvent> shots = Run(weapon, SimConstants.TickRate * 3, InputButtons.Fire, Standing, ref tick);

            Assert.That(shots.Count, Is.EqualTo(40));
        }

        [Test]
        public void SemiAuto_FiresOncePerTriggerPull()
        {
            var weapon = new WeaponSimulator(MakeStats(FireMode.SemiAuto, rpm: 300f), 1);
            int tick = 0;

            Assert.That(Run(weapon, 30, InputButtons.Fire, Standing, ref tick).Count, Is.EqualTo(1), "holding the trigger");
            Assert.That(Run(weapon, 5, InputButtons.None, Standing, ref tick).Count, Is.EqualTo(0), "released");
            Assert.That(Run(weapon, 30, InputButtons.Fire, Standing, ref tick).Count, Is.EqualTo(1), "second pull");
        }

        [Test]
        public void SemiAuto_CannotExceedTheFireRateByClickingFaster()
        {
            var weapon = new WeaponSimulator(MakeStats(FireMode.SemiAuto, rpm: 300f), 1);
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
            WeaponStats stats = MakeStats(FireMode.Burst, rpm: 900f);
            stats.burstCount = 3;
            stats.burstCooldown = 0.25f;
            var weapon = new WeaponSimulator(stats, 1);
            int tick = 0;

            int fired = Run(weapon, 1, InputButtons.Fire, Standing, ref tick).Count;
            fired += Run(weapon, 20, InputButtons.None, Standing, ref tick).Count;

            Assert.That(fired, Is.EqualTo(3));
            Assert.That(weapon.State.AmmoInMagazine, Is.EqualTo(27));
        }

        [Test]
        public void Shotgun_FiresEveryPelletFromOneShell()
        {
            WeaponStats stats = MakeStats(FireMode.SemiAuto, rpm: 80f, magazine: 6);
            stats.pellets = 8;
            stats.hipSpread = 3f;
            var weapon = new WeaponSimulator(stats, 5);
            int fires = 0;
            weapon.Fired += () => fires++;
            int tick = 0;

            List<ShotEvent> shots = Run(weapon, 1, InputButtons.Fire, Standing, ref tick);

            Assert.That(shots.Count, Is.EqualTo(8));
            Assert.That(fires, Is.EqualTo(1), "one blast, however many pellets");
            Assert.That(weapon.State.AmmoInMagazine, Is.EqualTo(5), "one shell used");

            bool spreadOut = false;
            for (int i = 0; i < shots.Count; i++)
            {
                Assert.That(shots[i].ShotNumber, Is.EqualTo(shots[0].ShotNumber));
                Assert.That(shots[i].Pellet, Is.EqualTo(i));
                Assert.That(shots[i].AimOffset.magnitude, Is.LessThanOrEqualTo(shots[i].Spread + 0.0001f));
                spreadOut |= shots[i].AimOffset.x != shots[0].AimOffset.x;
            }
            Assert.That(spreadOut, Is.True, "pellets must not all go the same way");
        }

        [Test]
        public void Charge_BuildsWhileHeld_AndFiresOnRelease()
        {
            WeaponStats stats = MakeStats(FireMode.Charge, rpm: 120f);
            stats.chargeTime = 0.5f;
            stats.minChargeDamage = 0.4f;
            var weapon = new WeaponSimulator(stats, 1);
            int tick = 0;

            // Half charged.
            Assert.That(Run(weapon, 15, InputButtons.Fire, Standing, ref tick).Count, Is.EqualTo(0));
            Assert.That(weapon.State.ChargeAmount, Is.EqualTo(0.5f).Within(0.01f));
            List<ShotEvent> half = Run(weapon, 1, InputButtons.None, Standing, ref tick);
            Assert.That(half.Count, Is.EqualTo(1));
            Assert.That(half[0].DamageScale, Is.EqualTo(0.7f).Within(0.01f));

            // Fully charged, and held past full: it waits for the release.
            Run(weapon, 30, InputButtons.None, Standing, ref tick);
            Assert.That(Run(weapon, 90, InputButtons.Fire, Standing, ref tick).Count, Is.EqualTo(0));
            Assert.That(weapon.State.ChargeAmount, Is.EqualTo(1f));
            List<ShotEvent> full = Run(weapon, 1, InputButtons.None, Standing, ref tick);
            Assert.That(full.Count, Is.EqualTo(1));
            Assert.That(full[0].DamageScale, Is.EqualTo(1f));
        }

        [Test]
        public void EmptyMagazine_ReloadsAutomatically_AndRefillsFromReserve()
        {
            WeaponStats stats = MakeStats(rpm: 720f, magazine: 5);
            var weapon = new WeaponSimulator(stats, 1);
            int tick = 0;

            List<ShotEvent> shots = Run(weapon, 40, InputButtons.Fire, Standing, ref tick);
            Assert.That(shots.Count, Is.EqualTo(5));
            Assert.That(weapon.State.Phase, Is.EqualTo(WeaponPhase.Reloading));

            // Empty reload takes 1.5 s; nothing can fire during it.
            int ticksToFinish = Mathf.CeilToInt(stats.emptyReloadTime / Dt) + 2;
            Assert.That(Run(weapon, ticksToFinish, InputButtons.None, Standing, ref tick).Count, Is.EqualTo(0));

            Assert.That(weapon.State.Phase, Is.EqualTo(WeaponPhase.Ready));
            Assert.That(weapon.State.AmmoInMagazine, Is.EqualTo(5));
            Assert.That(weapon.State.AmmoReserve, Is.EqualTo(85));
        }

        [Test]
        public void Reload_CannotFire_UntilItCompletes()
        {
            WeaponStats stats = MakeStats(magazine: 10);
            var weapon = new WeaponSimulator(stats, 1);
            int tick = 0;

            Run(weapon, 6, InputButtons.Fire, Standing, ref tick);
            Run(weapon, 2, InputButtons.None, Standing, ref tick);
            Run(weapon, 1, InputButtons.Reload, Standing, ref tick);
            Assert.That(weapon.State.Phase, Is.EqualTo(WeaponPhase.Reloading));

            int almostDone = Mathf.FloorToInt(stats.reloadTime / Dt) - 3;
            Assert.That(Run(weapon, almostDone, InputButtons.Fire, Standing, ref tick).Count, Is.EqualTo(0));
            Assert.That(Run(weapon, 10, InputButtons.Fire, Standing, ref tick).Count, Is.GreaterThan(0));
        }

        [Test]
        public void PerRoundReload_LoadsOneAtATime_AndFiringCutsItShort()
        {
            WeaponStats stats = MakeStats(FireMode.SemiAuto, rpm: 120f, magazine: 6);
            stats.reloadStyle = ReloadStyle.PerRound;
            stats.reloadIntroTime = 0.2f;
            stats.reloadTime = 0.5f;
            var weapon = new WeaponSimulator(stats, 1);
            int rounds = 0;
            weapon.RoundInserted += () => rounds++;
            int tick = 0;

            // Fire four of the six shells (120 rpm is a shot every 30 ticks).
            for (int i = 0; i < 4; i++)
            {
                Run(weapon, 1, InputButtons.Fire, Standing, ref tick);
                Run(weapon, 30, InputButtons.None, Standing, ref tick);
            }
            Assert.That(weapon.State.AmmoInMagazine, Is.EqualTo(2));

            Run(weapon, 1, InputButtons.Reload, Standing, ref tick);
            Assert.That(weapon.State.Phase, Is.EqualTo(WeaponPhase.Reloading));

            // 0.2 s lead-in plus 0.5 s for the first shell, then 0.5 s for the second.
            Run(weapon, 45, InputButtons.None, Standing, ref tick);
            Assert.That(weapon.State.AmmoInMagazine, Is.EqualTo(3), "first shell in");
            Run(weapon, 30, InputButtons.None, Standing, ref tick);
            Assert.That(weapon.State.AmmoInMagazine, Is.EqualTo(4), "second shell in");
            Assert.That(rounds, Is.EqualTo(2));

            // Pulling the trigger stops the reload and fires what is loaded.
            List<ShotEvent> shots = Run(weapon, 2, InputButtons.Fire, Standing, ref tick);
            Assert.That(shots.Count, Is.EqualTo(1));
            Assert.That(weapon.State.Phase, Is.EqualTo(WeaponPhase.Ready));
            Assert.That(weapon.State.AmmoInMagazine, Is.EqualTo(3));
        }

        [Test]
        public void PerRoundReload_StopsWhenFull()
        {
            WeaponStats stats = MakeStats(FireMode.SemiAuto, rpm: 600f, magazine: 4);
            stats.reloadStyle = ReloadStyle.PerRound;
            stats.reloadTime = 0.25f;
            var weapon = new WeaponSimulator(stats, 1);
            int tick = 0;

            Run(weapon, 1, InputButtons.Fire, Standing, ref tick);
            Run(weapon, 10, InputButtons.None, Standing, ref tick);
            Run(weapon, 1, InputButtons.Reload, Standing, ref tick);
            Run(weapon, 60, InputButtons.None, Standing, ref tick);

            Assert.That(weapon.State.Phase, Is.EqualTo(WeaponPhase.Ready));
            Assert.That(weapon.State.AmmoInMagazine, Is.EqualTo(4));
            Assert.That(weapon.State.AmmoReserve, Is.EqualTo(89));
        }

        [Test]
        public void InfiniteReserve_NeverDrains()
        {
            var weapon = new WeaponSimulator(MakeStats(magazine: 5), 1) { InfiniteReserve = true };
            int tick = 0;

            Run(weapon, SimConstants.TickRate * 6, InputButtons.Fire, Standing, ref tick);

            Assert.That(weapon.State.AmmoReserve, Is.EqualTo(90));
        }

        [Test]
        public void Melee_ConnectsAfterItsWindUp_AndUsesNoAmmo()
        {
            WeaponStats stats = MakeStats(FireMode.SemiAuto, rpm: 100f, magazine: 0);
            stats.ballistics = BallisticsType.Melee;
            stats.meleeHitDelay = 0.1f;
            var weapon = new WeaponSimulator(stats, 1);
            int swings = 0;
            weapon.Fired += () => swings++;
            int tick = 0;

            Assert.That(Run(weapon, 5, InputButtons.Fire, Standing, ref tick).Count, Is.EqualTo(0), "still winding up");
            Assert.That(swings, Is.EqualTo(1));

            List<ShotEvent> strike = Run(weapon, 3, InputButtons.None, Standing, ref tick);
            Assert.That(strike.Count, Is.EqualTo(1));
            Assert.That(strike[0].Kind, Is.EqualTo(ShotKind.Melee));

            // Keep swinging, once the first swing has recovered: it never runs dry and never reloads.
            Run(weapon, 40, InputButtons.None, Standing, ref tick);
            for (int i = 0; i < 10; i++)
            {
                Run(weapon, 1, InputButtons.Fire, Standing, ref tick);
                Run(weapon, 40, InputButtons.None, Standing, ref tick);
                Assert.That(weapon.State.Phase, Is.EqualTo(WeaponPhase.Ready));
            }
            Assert.That(swings, Is.EqualTo(11));
        }

        [Test]
        public void Projectile_ShotsAreMarkedAsProjectiles()
        {
            WeaponStats stats = MakeStats(FireMode.SemiAuto, rpm: 60f, magazine: 1);
            stats.ballistics = BallisticsType.Projectile;
            var weapon = new WeaponSimulator(stats, 1);
            int tick = 0;

            List<ShotEvent> shots = Run(weapon, 1, InputButtons.Fire, Standing, ref tick);

            Assert.That(shots.Count, Is.EqualTo(1));
            Assert.That(shots[0].Kind, Is.EqualTo(ShotKind.Projectile));
        }

        [Test]
        public void Sprinting_BlocksFiring_UntilTheSprintOutDelayHasPassed()
        {
            WeaponStats stats = MakeStats();
            var weapon = new WeaponSimulator(stats, 1);
            int tick = 0;

            WeaponContext sprinting = Standing;
            sprinting.Sprinting = true;
            sprinting.TimeSinceSprint = 0f;
            Assert.That(Run(weapon, 20, InputButtons.Fire, sprinting, ref tick).Count, Is.EqualTo(0));

            WeaponContext justStopped = Standing;
            justStopped.TimeSinceSprint = stats.sprintToFireTime * 0.5f;
            Assert.That(Run(weapon, 1, InputButtons.Fire, justStopped, ref tick).Count, Is.EqualTo(0));

            Assert.That(Run(weapon, 1, InputButtons.Fire, Standing, ref tick).Count, Is.EqualTo(1));
        }

        [Test]
        public void Mantling_BlocksFiring()
        {
            var weapon = new WeaponSimulator(MakeStats(), 1);
            int tick = 0;
            WeaponContext busy = Standing;
            busy.CanUseWeapon = false;

            Assert.That(Run(weapon, 30, InputButtons.Fire, busy, ref tick).Count, Is.EqualTo(0));
        }

        [Test]
        public void Shots_AreIdentical_ForTheSameSeed_AndDifferent_ForAnother()
        {
            WeaponStats stats = MakeStats();
            stats.hipSpread = 2f;
            int tickA = 0, tickB = 0, tickC = 0;

            List<ShotEvent> a = Run(new WeaponSimulator(stats, 42), 60, InputButtons.Fire, Standing, ref tickA);
            List<ShotEvent> b = Run(new WeaponSimulator(stats, 42), 60, InputButtons.Fire, Standing, ref tickB);
            List<ShotEvent> c = Run(new WeaponSimulator(stats, 43), 60, InputButtons.Fire, Standing, ref tickC);

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
            WeaponStats stats = MakeStats(magazine: 200);
            stats.hipSpread = 1.5f;
            stats.bloomPerShot = 0.2f;
            stats.maxBloom = 2f;
            var weapon = new WeaponSimulator(stats, 7);
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
            WeaponStats stats = MakeStats();
            var weapon = new WeaponSimulator(stats, 1);
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
            Assert.That(aimed, Is.EqualTo(stats.adsSpread).Within(0.0001f));
        }

        [Test]
        public void Ads_TakesTheConfiguredTime()
        {
            WeaponStats stats = MakeStats();
            stats.adsTime = 0.2f;
            var weapon = new WeaponSimulator(stats, 1);
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
            WeaponStats stats = MakeStats(rpm: 600f);
            stats.recoilPattern = new[] { new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.25f) };
            stats.recoilLoopStart = 1;
            var weapon = new WeaponSimulator(stats, 1);
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
        public void RecoilPattern_LoopsFromLoopStart_AndHonoursTheScales()
        {
            WeaponStats stats = MakeStats();
            stats.recoilPattern = new[] { new Vector2(1f, 1f), new Vector2(1f, 2f), new Vector2(1f, 3f) };
            stats.recoilLoopStart = 1;

            Assert.That(stats.GetRecoilKick(0).y, Is.EqualTo(1f));
            Assert.That(stats.GetRecoilKick(2).y, Is.EqualTo(3f));
            Assert.That(stats.GetRecoilKick(3).y, Is.EqualTo(2f));
            Assert.That(stats.GetRecoilKick(4).y, Is.EqualTo(3f));
            Assert.That(stats.GetRecoilKick(5).y, Is.EqualTo(2f));

            stats.recoilVerticalScale = 0.5f;
            stats.recoilHorizontalScale = 2f;
            Assert.That(stats.GetRecoilKick(2).y, Is.EqualTo(1.5f));
            Assert.That(stats.GetRecoilKick(2).x, Is.EqualTo(2f));
        }
    }

    public class WeaponInventoryTests
    {
        const float Dt = WeaponTestKit.Dt;
        static WeaponContext Standing => WeaponTestKit.Standing;

        static WeaponStats Rifle()
        {
            WeaponStats stats = WeaponTestKit.MakeStats(FireMode.Automatic, 600f, 30);
            stats.holsterTime = 0.2f;
            stats.equipTime = 0.3f;
            return stats;
        }

        static WeaponStats Pistol()
        {
            WeaponStats stats = WeaponTestKit.MakeStats(FireMode.SemiAuto, 300f, 12);
            stats.holsterTime = 0.1f;
            stats.equipTime = 0.2f;
            return stats;
        }

        /// <summary>A rifle in slot 1 and a pistol in slot 2, with the rifle already up.</summary>
        static WeaponInventory MakeInventory(out int tick)
        {
            var inventory = new WeaponInventory(new[] { new WeaponSimulator(Rifle(), 1), new WeaponSimulator(Pistol(), 2) });
            tick = 0;
            Run(inventory, 30, InputButtons.None, 0, ref tick);
            return inventory;
        }

        static int Run(WeaponInventory inventory, int ticks, InputButtons buttons, byte selectSlot, ref int tick)
        {
            int shots = 0;
            for (int i = 0; i < ticks; i++)
            {
                var cmd = new PlayerInputCmd { Tick = tick++, Buttons = buttons, SelectSlot = i == 0 ? selectSlot : (byte)0 };
                inventory.Simulate(cmd, Standing, Dt);
                shots += inventory.Simulated.Shots.Count;
            }
            return shots;
        }

        [Test]
        public void Switching_TakesTheOldWeaponsHolsterTime_ThenTheNewWeaponsEquipTime()
        {
            WeaponInventory inventory = MakeInventory(out int tick);
            int changes = 0;
            inventory.ActiveChanged += _ => changes++;

            // 0.2 s to put the rifle away.
            Run(inventory, 11, InputButtons.None, 2, ref tick);
            Assert.That(inventory.ActiveIndex, Is.EqualTo(0));
            Assert.That(inventory.IsSwitching, Is.True);

            Run(inventory, 2, InputButtons.None, 0, ref tick);
            Assert.That(inventory.ActiveIndex, Is.EqualTo(1));
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(inventory.Active.State.Phase, Is.EqualTo(WeaponPhase.Equipping));

            // 0.2 s to bring the pistol up.
            Run(inventory, 13, InputButtons.None, 0, ref tick);
            Assert.That(inventory.Active.State.Phase, Is.EqualTo(WeaponPhase.Ready));
        }

        [Test]
        public void NothingFires_WhileSwitching()
        {
            WeaponInventory inventory = MakeInventory(out int tick);

            int during = Run(inventory, 20, InputButtons.Fire, 2, ref tick);

            Assert.That(during, Is.EqualTo(0));
        }

        [Test]
        public void ATriggerHeldThroughTheSwitch_DoesNotFireASemiAuto()
        {
            WeaponInventory inventory = MakeInventory(out int tick);

            // Hold fire from before the switch until well after the pistol is up.
            int held = Run(inventory, 60, InputButtons.Fire, 2, ref tick);
            Assert.That(inventory.ActiveIndex, Is.EqualTo(1));
            Assert.That(held, Is.EqualTo(0), "the rifle is being put away, and the pistol needs a fresh pull");

            Run(inventory, 2, InputButtons.None, 0, ref tick);
            Assert.That(Run(inventory, 2, InputButtons.Fire, 0, ref tick), Is.EqualTo(1));
        }

        [Test]
        public void Ammo_IsRememberedForEachSlot()
        {
            WeaponInventory inventory = MakeInventory(out int tick);

            Run(inventory, 30, InputButtons.Fire, 0, ref tick);
            int rifleAmmo = inventory.Get(0).State.AmmoInMagazine;
            Assert.That(rifleAmmo, Is.LessThan(30));

            Run(inventory, 60, InputButtons.None, 2, ref tick);
            Run(inventory, 60, InputButtons.None, 1, ref tick);

            Assert.That(inventory.ActiveIndex, Is.EqualTo(0));
            Assert.That(inventory.Active.State.AmmoInMagazine, Is.EqualTo(rifleAmmo));
        }

        [Test]
        public void ChangingYourMind_BringsTheSameWeaponBackUp()
        {
            WeaponInventory inventory = MakeInventory(out int tick);

            Run(inventory, 5, InputButtons.None, 2, ref tick);
            Assert.That(inventory.IsSwitching, Is.True);

            Run(inventory, 30, InputButtons.None, 1, ref tick);
            Assert.That(inventory.IsSwitching, Is.False);
            Assert.That(inventory.ActiveIndex, Is.EqualTo(0));
            Assert.That(inventory.Active.State.Phase, Is.EqualTo(WeaponPhase.Ready));
        }

        [Test]
        public void SelectingTheWeaponInHand_OrAnEmptySlot_DoesNothing()
        {
            var inventory = new WeaponInventory(new[] { new WeaponSimulator(Rifle(), 1), null, new WeaponSimulator(Pistol(), 2) });
            int tick = 0;
            Run(inventory, 30, InputButtons.None, 0, ref tick);

            Run(inventory, 5, InputButtons.None, 1, ref tick);
            Assert.That(inventory.IsSwitching, Is.False, "already holding slot 1");

            Run(inventory, 5, InputButtons.None, 2, ref tick);
            Assert.That(inventory.IsSwitching, Is.False, "slot 2 is empty");

            Run(inventory, 5, InputButtons.None, 9, ref tick);
            Assert.That(inventory.IsSwitching, Is.False, "there is no slot 9");
        }

        [Test]
        public void Recoil_CarriesOverToTheNextWeapon_SoTheViewDoesNotSnap()
        {
            WeaponStats rifle = Rifle();
            rifle.recoilPattern = new[] { new Vector2(0f, 1f) };
            rifle.recoilRecovery = 2f;
            var inventory = new WeaponInventory(new[] { new WeaponSimulator(rifle, 1), new WeaponSimulator(Pistol(), 2) });
            int tick = 0;
            Run(inventory, 30, InputButtons.None, 0, ref tick);

            Run(inventory, 40, InputButtons.Fire, 0, ref tick);
            Assert.That(inventory.Active.State.RecoilCurrent.y, Is.GreaterThan(2f));

            // Step through the switch one tick at a time; the view offset must never jump.
            float last = inventory.Active.State.RecoilCurrent.y;
            Run(inventory, 1, InputButtons.None, 2, ref tick);
            for (int i = 0; i < 30; i++)
            {
                Run(inventory, 1, InputButtons.None, 0, ref tick);
                float now = inventory.Active.State.RecoilCurrent.y;
                Assert.That(Mathf.Abs(now - last), Is.LessThan(0.3f));
                last = now;
            }
            Assert.That(inventory.ActiveIndex, Is.EqualTo(1));
        }
    }
}
