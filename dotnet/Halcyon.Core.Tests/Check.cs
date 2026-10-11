// Comparison statistics for one named check: how many numbers were compared,
// how many matched bit for bit, the largest absolute and relative
// differences, and the first few mismatches.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Halcyon.Core.Tests
{
    sealed class Check
    {
        public readonly string Group;
        public readonly string Name;
        public long Values;
        public long Exact;
        public double MaxAbs;
        public double MaxRel;
        public int ProblemCount;
        public readonly List<string> Problems = new List<string>();
        public double Seconds;

        public Check(string group, string name)
        {
            Group = group;
            Name = name;
        }

        public bool Pass => ProblemCount == 0 && Exact == Values;

        /// <summary>Bit-identical, treating every NaN as equal to every other NaN.</summary>
        public static bool Same(double e, double a) =>
            BitConverter.DoubleToInt64Bits(e) == BitConverter.DoubleToInt64Bits(a) || (double.IsNaN(e) && double.IsNaN(a));

        public static string R(double x) => x.ToString("R", CultureInfo.InvariantCulture);

        public void Num(double expected, double actual, string where)
        {
            if (Record(expected, actual)) return;
            Problem($"{where}: expected {R(expected)}, got {R(actual)}{(expected == actual ? " (sign of zero)" : "")}");
        }

        /// <summary>Like Num, but builds the location text only on a mismatch.</summary>
        public void Num(double expected, double actual, Func<string> where)
        {
            if (Record(expected, actual)) return;
            Problem($"{where()}: expected {R(expected)}, got {R(actual)}{(expected == actual ? " (sign of zero)" : "")}");
        }

        bool Record(double e, double a)
        {
            Values++;
            if (Same(e, a))
            {
                Exact++;
                return true;
            }
            double abs, rel;
            if (double.IsNaN(e) || double.IsNaN(a) || double.IsInfinity(e) || double.IsInfinity(a))
            {
                abs = rel = double.PositiveInfinity;
            }
            else
            {
                abs = Math.Abs(e - a);
                double m = Math.Max(Math.Abs(e), Math.Abs(a));
                rel = m == 0 ? 0 : abs / m;
            }
            if (abs > MaxAbs) MaxAbs = abs;
            if (rel > MaxRel) MaxRel = rel;
            return false;
        }

        public void Str(string expected, string actual, string where)
        {
            Values++;
            if (expected == actual)
            {
                Exact++;
                return;
            }
            Problem($"{where}: expected \"{expected}\", got \"{actual}\"");
        }

        public void Bool(bool expected, bool actual, string where)
        {
            Values++;
            if (expected == actual)
            {
                Exact++;
                return;
            }
            Problem($"{where}: expected {expected}, got {actual}");
        }

        /// <summary>A structural problem (missing field, length mismatch, exception).</summary>
        public void Fail(string message) => Problem(message);

        void Problem(string message)
        {
            ProblemCount++;
            if (Problems.Count < 4) Problems.Add(message);
        }
    }
}
