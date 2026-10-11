// Upgrade parts. Each category has a few levels (0 = stock); a car's parts
// fold into the physics' Upgrades layer (power, mass, tyre compound and
// width, brake torque, aero kit), so every part changes the car through the
// same documented formulas as the rest of the physics. Suspension,
// transmission, differential, brake and aero parts also unlock tuning
// sliders, as in a Forza-style tuning menu.
using System;
using System.Collections.Generic;

namespace Halcyon.Core
{
    public enum PartCategory { Engine, Weight, Tires, TireWidth, Brakes, Aero, Transmission, Differential, Suspension }

    /// <summary>Groups of tuning sliders; parts unlock them.</summary>
    [Flags]
    public enum TuneGroup
    {
        None = 0,
        Pressure = 1,       // always available
        Gearing = 2,        // final drive (sport transmission); per-gear ratios need race
        GearRatios = 4,
        Alignment = 8,      // camber, toe, caster (race suspension)
        AntiRoll = 16,      // anti-roll bars (sport suspension)
        Springs = 32,       // springs and ride height (sport suspension)
        Damping = 64,       // bump and rebound (race suspension)
        Aero = 128,         // front and rear downforce (aero kit)
        Brakes = 256,       // balance and pressure (race brakes)
        Differential = 512, // locking and AWD split (sport differential)
    }

    public sealed class Part
    {
        public PartCategory Category;
        public int Level;
        public string Name;
        /// <summary>What it does, for the menu ("+13% power").</summary>
        public string Effect;
        /// <summary>Price as a share of the car's price, and a minimum, credits.</summary>
        public double PriceShare, PriceMin;
        public TuneGroup Unlocks;
        /// <summary>Applies the part to an Upgrades layer.</summary>
        internal Action<Upgrades, CarSpec> Apply;
    }

    public static class Parts
    {
        static Part P(PartCategory c, int level, string name, string effect, double share, double min, TuneGroup unlocks, Action<Upgrades, CarSpec> apply) =>
            new Part { Category = c, Level = level, Name = name, Effect = effect, PriceShare = share, PriceMin = min, Unlocks = unlocks, Apply = apply };

        static readonly Compound[] TireLadder = { Compound.Eco, Compound.Street, Compound.Sport, Compound.SemiSlick, Compound.Slick };

        /// <summary>Every part, by category, level 0 (stock) first.</summary>
        public static readonly Dictionary<PartCategory, Part[]> Catalog = new Dictionary<PartCategory, Part[]>
        {
            [PartCategory.Engine] = new[]
            {
                P(PartCategory.Engine, 0, "Stock engine", "", 0, 0, TuneGroup.None, (u, s) => { }),
                P(PartCategory.Engine, 1, "Street intake and exhaust", "+6% power", 0.05, 1500, TuneGroup.None, (u, s) => u.powerScale *= 1.06),
                P(PartCategory.Engine, 2, "Sport cams and engine map", "+13% power", 0.12, 4000, TuneGroup.None, (u, s) => u.powerScale *= 1.13),
                P(PartCategory.Engine, 3, "Race internals", "+22% power", 0.25, 9000, TuneGroup.None, (u, s) => u.powerScale *= 1.22),
            },
            [PartCategory.Weight] = new[]
            {
                P(PartCategory.Weight, 0, "Stock weight", "", 0, 0, TuneGroup.None, (u, s) => { }),
                P(PartCategory.Weight, 1, "Street weight reduction", "-3% mass", 0.04, 1200, TuneGroup.None, (u, s) => u.massScale *= 0.97),
                P(PartCategory.Weight, 2, "Sport weight reduction", "-6% mass", 0.09, 3000, TuneGroup.None, (u, s) => u.massScale *= 0.94),
                P(PartCategory.Weight, 3, "Race weight reduction", "-10% mass", 0.18, 7000, TuneGroup.None, (u, s) => u.massScale *= 0.90),
            },
            [PartCategory.Tires] = new[]
            {
                P(PartCategory.Tires, 0, "Stock tyres", "", 0, 0, TuneGroup.None, (u, s) => { }),
                P(PartCategory.Tires, 1, "Sport tyres", "compound one step grippier", 0.03, 1200, TuneGroup.None, (u, s) => u.compound = Step(s.tires.compound, 1)),
                P(PartCategory.Tires, 2, "Semi-slick tyres", "two steps grippier", 0.06, 2500, TuneGroup.None, (u, s) => u.compound = Step(s.tires.compound, 2)),
                P(PartCategory.Tires, 3, "Slick tyres", "racing slicks", 0.10, 4500, TuneGroup.None, (u, s) => u.compound = Compound.Slick),
                P(PartCategory.Tires, 4, "Rally tyres", "grip on gravel and dirt", 0.04, 1500, TuneGroup.None, (u, s) => u.compound = Compound.Rally),
                P(PartCategory.Tires, 5, "Off-road tyres", "grip in mud and sand", 0.04, 1500, TuneGroup.None, (u, s) => u.compound = Compound.OffRoad),
            },
            [PartCategory.TireWidth] = new[]
            {
                P(PartCategory.TireWidth, 0, "Stock width", "", 0, 0, TuneGroup.None, (u, s) => { }),
                P(PartCategory.TireWidth, 1, "Wider tyres", "+15 mm", 0.02, 800, TuneGroup.None, (u, s) => u.tireWidthAdd += 15),
                P(PartCategory.TireWidth, 2, "Much wider tyres", "+30 mm", 0.04, 1600, TuneGroup.None, (u, s) => u.tireWidthAdd += 30),
            },
            [PartCategory.Brakes] = new[]
            {
                P(PartCategory.Brakes, 0, "Stock brakes", "", 0, 0, TuneGroup.None, (u, s) => { }),
                P(PartCategory.Brakes, 1, "Sport brakes", "+20% brake torque", 0.03, 1200, TuneGroup.None, (u, s) => u.brakeScale *= 1.20),
                P(PartCategory.Brakes, 2, "Race brakes", "+45% torque, adjustable balance", 0.07, 3500, TuneGroup.Brakes, (u, s) => u.brakeScale *= 1.45),
            },
            [PartCategory.Aero] = new[]
            {
                P(PartCategory.Aero, 0, "Stock aero", "", 0, 0, TuneGroup.None, (u, s) => { }),
                P(PartCategory.Aero, 1, "Sport aero kit", "+0.25 m² downforce area, adjustable", 0.05, 2500, TuneGroup.Aero, (u, s) => { u.aeroKit = Math.Max(u.aeroKit, 0.5); u.clAAdd += 0.25; }),
                P(PartCategory.Aero, 2, "Race aero kit", "+0.6 m² downforce area, adjustable", 0.10, 6000, TuneGroup.Aero, (u, s) => { u.aeroKit = Math.Max(u.aeroKit, 1); u.clAAdd += 0.6; }),
            },
            [PartCategory.Transmission] = new[]
            {
                P(PartCategory.Transmission, 0, "Stock transmission", "", 0, 0, TuneGroup.None, (u, s) => { }),
                P(PartCategory.Transmission, 1, "Sport transmission", "adjustable final drive", 0.04, 2000, TuneGroup.Gearing, (u, s) => { }),
                P(PartCategory.Transmission, 2, "Race transmission", "every ratio adjustable", 0.09, 5000, TuneGroup.Gearing | TuneGroup.GearRatios, (u, s) => { }),
            },
            [PartCategory.Differential] = new[]
            {
                P(PartCategory.Differential, 0, "Stock differential", "", 0, 0, TuneGroup.None, (u, s) => { }),
                P(PartCategory.Differential, 1, "Sport differential", "adjustable locking", 0.04, 2000, TuneGroup.Differential, (u, s) => { }),
            },
            [PartCategory.Suspension] = new[]
            {
                P(PartCategory.Suspension, 0, "Stock suspension", "", 0, 0, TuneGroup.None, (u, s) => { }),
                P(PartCategory.Suspension, 1, "Sport suspension", "adjustable springs, ride height, anti-roll bars", 0.05, 2500, TuneGroup.Springs | TuneGroup.AntiRoll, (u, s) => { }),
                P(PartCategory.Suspension, 2, "Race suspension", "also damping and alignment", 0.10, 6000, TuneGroup.Springs | TuneGroup.AntiRoll | TuneGroup.Damping | TuneGroup.Alignment, (u, s) => { }),
            },
        };

        /// <summary>A compound n steps up the road-tyre ladder from the stock one.</summary>
        static Compound Step(Compound stock, int n)
        {
            int i = Array.IndexOf(TireLadder, stock);
            if (i < 0) i = 1; // rally, off-road and snow tyres step onto the road ladder from Street
            return TireLadder[Math.Min(TireLadder.Length - 1, i + n)];
        }

        public static Part Get(PartCategory c, int level)
        {
            var list = Catalog[c];
            return list[Math.Max(0, Math.Min(list.Length - 1, level))];
        }

        /// <summary>The Upgrades layer for a car with these parts (level per category; missing = stock).</summary>
        public static Upgrades Build(CarSpec spec, IReadOnlyDictionary<PartCategory, int> parts)
        {
            var up = Upgrades.None();
            if (parts == null) return up;
            foreach (var kv in parts)
            {
                if (kv.Value <= 0) continue;
                Get(kv.Key, kv.Value).Apply(up, spec);
            }
            return up;
        }

        /// <summary>The tuning sliders these parts unlock (tyre pressures are always open).</summary>
        public static TuneGroup Unlocked(IReadOnlyDictionary<PartCategory, int> parts)
        {
            var g = TuneGroup.Pressure;
            if (parts == null) return g;
            foreach (var kv in parts) g |= Get(kv.Key, kv.Value).Unlocks;
            return g;
        }

        /// <summary>A part's price on a car worth carPrice, rounded to 100 credits.</summary>
        public static int Price(Part part, int carPrice) =>
            part.Level == 0 ? 0 : (int)(Math.Round(Math.Max(part.PriceMin, part.PriceShare * carPrice) / 100) * 100);
    }
}
