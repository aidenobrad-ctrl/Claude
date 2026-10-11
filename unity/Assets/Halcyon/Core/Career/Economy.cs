// Prices, payouts and levels.
//
// A car's price comes from its PI: ln(price) rises 0.006 per PI point from
// 25,000 credits at PI 450, and steeply past PI 850 so hypercars cost
// millions:
//   price = exp(10.1 + 0.006 (PI - 450) + 0.000273 max(0, PI - 850)^2)
// rounded to 500 credits (Pico ~25k, Aster GT ~110k, Vanta ~2.3M).
//
// Race payouts scale with the race's length, the class and the rivals'
// difficulty, and fall off down the order. Levels need 1000 n^1.3 XP each.
using System;

namespace Halcyon.Core
{
    public static class Economy
    {
        public const int StartingCredits = 50000;

        public static int CarPrice(int pi)
        {
            double over = Math.Max(0, pi - 850);
            double p = Math.Exp(10.1 + 0.006 * (pi - 450) + 0.000273 * over * over);
            return (int)(Math.Round(p / 500) * 500);
        }

        /// <summary>Selling a car returns 60% of its price.</summary>
        public static int SellPrice(int pi) => (int)(Math.Round(CarPrice(pi) * 0.6 / 100) * 100);

        static double ClassFactor(PiClass c) =>
            c == PiClass.D ? 1.0 : c == PiClass.C ? 1.15 : c == PiClass.B ? 1.3 : c == PiClass.A ? 1.5 : c == PiClass.S1 ? 1.8 : c == PiClass.S2 ? 2.2 : 2.6;

        static double DifficultyFactor(Difficulty d) =>
            d == Difficulty.Novice ? 0.7 : d == Difficulty.Average ? 1.0 : d == Difficulty.Pro ? 1.3 : 1.7;

        static readonly double[] PositionShare = { 1.0, 0.7, 0.5, 0.35, 0.25 };

        /// <summary>Credits for finishing a race of this length (m) in this position (1 = winner) of a field.</summary>
        public static int Payout(double distance, int position, PiClass cls, Difficulty d)
        {
            double share = position >= 1 && position <= PositionShare.Length ? PositionShare[position - 1] : 0.15;
            double baseCredits = 2500 * Math.Sqrt(Math.Max(0.5, distance / 5000));
            return (int)(Math.Round(baseCredits * share * ClassFactor(cls) * DifficultyFactor(d) / 50) * 50);
        }

        /// <summary>XP for a race: 100 per km plus a bonus for a podium.</summary>
        public static int RaceXp(double distance, int position) =>
            (int)Math.Round(100 * distance / 1000) + (position == 1 ? 300 : position == 2 ? 200 : position == 3 ? 100 : 0);

        /// <summary>XP needed to go from level n to n + 1.</summary>
        public static int XpForLevel(int n) => (int)Math.Round(1000 * Math.Pow(Math.Max(1, n), 1.3));

        /// <summary>Level (1-based) and the XP into it, for a total.</summary>
        public static (int level, int into, int need) Level(int totalXp)
        {
            int level = 1, left = totalXp;
            while (left >= XpForLevel(level))
            {
                left -= XpForLevel(level);
                level++;
            }
            return (level, left, XpForLevel(level));
        }
    }
}
