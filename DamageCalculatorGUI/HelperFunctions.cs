﻿using System.Numerics;
using System.Security.AccessControl;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DamageCalculatorGUI
{
    internal class HelperFunctions
    {
        public static void GetAllControls(Control control, List<Control> controlIDs)
        {
            // Store the current control
            controlIDs.Add(control);
            
            // Check if the control has any children
            if (control.Controls.Count > 0)
            { // Get each child-control recursively
                foreach (Control childControl in control.Controls)
                { // Iterate over each child
                    GetAllControls(childControl, controlIDs);
                }
            }
        }
        public static T ClampAboveZero<T>(T value) where T : INumber<T>
        {
            return value <= T.Zero ? T.Zero : value;
        }
        public static T Mod<T>(T a, T b) where T : INumber<T>
        {
            return a - b * Floor(a / b);
        }
        public static T Floor<T>(T x) where T : INumber<T>, IComparable<T>
        {
            return (T)Convert.ChangeType(Math.Floor(Convert.ToDouble(x)), typeof(T));
        }
        public static T GetRandomEnum<T>() where T : Enum
        {
            return (T)(object)DamageCalculator.random.Next(upperBound: Enum.GetNames(typeof(T)).Length);
        }
        public static bool IsControlDown()
        {
            return (Control.ModifierKeys & Keys.Control) == Keys.Control;
        }
        public static Tuple<Tuple<int, int, int>, Tuple<int, int, int>> ParseDiceValues(string str)
        {
            string count_d_sides_regex = @"((?<!\()\d+[d]\d+)";
            string plus_damage_regex = @"((?<=\+)\d+(?!\)))";
            string count_d_sides_crit_regex = @"((?<=\()\d+[d]\d+)";
            string plus_damage_crit_regex = @"((?<=\+)\d+(?=\)))";

            RegexOptions options = RegexOptions.IgnoreCase;

            // Run regex for xDy on xDy+z (mDn+o)
            string xDy = Regex.Match(input: str, pattern: count_d_sides_regex, options: options).Value;

            // Run regex for z on xDy+z (mDn+o)
            string xDyPlus = Regex.Match(input: str, pattern: plus_damage_regex, options: options).Value;
            if (xDyPlus.Length == 0) // Catch and prevent failed regex parsing problems
                xDyPlus = "0";

            // Run regex for mDn on xDy+z (mDn+o)
            string xDyCrit = Regex.Match(input: str, pattern: count_d_sides_crit_regex, options: options).Value;
            if (xDyCrit.Length == 0) // Catch and prevent failed regex parsing problems
                xDyCrit = "0d0";

            // Run regex for o on xDy+z (mDn+o)
            string xDyCritPlus = Regex.Match(input: str, pattern: plus_damage_crit_regex, options: options).Value;
            if (xDyCritPlus.Length == 0) // Catch and prevent failed regex parsing problems
                xDyCritPlus = "0";

            Tuple<int, int, int> damage = new(int.Parse(xDy[..(str.IndexOf('d'))]), // Get count
                                        int.Parse(xDy[(str.IndexOf('d') + 1)..]),
                                        int.Parse(xDyPlus)); // Get faces
            Tuple<int, int, int> damageCrit = new(int.Parse(xDyCrit[..(str.IndexOf('d'))]), // Get count
                                        int.Parse(xDyCrit[(str.IndexOf('d') + 1)..]),
                                        int.Parse(xDyCritPlus)); // Get faces

            return new(damage, damageCrit);
        }
        public static Tuple<int, int, int> ComputePercentiles(Dictionary<int, int> damageBins)
        {
            // Declare & Initialize Base Percentiles
            int halfPercentileFinal = -1; // (Median)
            int quarterPercentileFinal = -1;
            int threeQuarterPercentileFinal = -1;

            // Compute the total for finding the target number weight
            int sum = 0;
            for (int index = 0; index < damageBins.Count; index++)
            { // Iterate through the dictionary, storing the weighted sum
                sum += damageBins[index]; // Damage val * number of damage values
            }

            // Compute Required Weights for each percentile bracket
            float quarterPercentileWeight = sum / 4;
            float halfPercentileWeight = sum / 2;
            float threeQuarterPercentileWeight = sum * 3 / 4;

            // Compute the percentiles
            for (int binIndex = 0; binIndex < damageBins.Count; binIndex++)
            {
                // 25th Percentile
                quarterPercentileWeight -= damageBins[binIndex]; // Reduce weight until...
                if (quarterPercentileWeight <= 0 && quarterPercentileFinal == -1) // At or below 0, then...
                    quarterPercentileFinal = binIndex; // Store the index of when we've hit the quarter percentile.

                // 50th Percentile
                halfPercentileWeight -= damageBins[binIndex];
                if (halfPercentileWeight <= 0 && halfPercentileFinal == -1)
                    halfPercentileFinal = binIndex;

                // 75th Percentile
                threeQuarterPercentileWeight -= damageBins[binIndex];
                if (threeQuarterPercentileWeight <= 0 && threeQuarterPercentileFinal == -1)
                    threeQuarterPercentileFinal = binIndex;
            }

            return new(quarterPercentileFinal, halfPercentileFinal, threeQuarterPercentileFinal);
        }
        public static Array GetSubArray(Array original_array, int[] index_extracted)
        {
            int numDims = original_array.Rank;
            int[] fullInd = new int[numDims];
            index_extracted.CopyTo(fullInd, 0);

            int extracted_array_size = original_array.GetLength(numDims - 1);

            Array extracted_array = Array.CreateInstance(elementType: original_array.GetType().GetElementType(), length: extracted_array_size);
            
            for (int i = 0; i < extracted_array_size; i++)
            {
                fullInd[^1] = i;
                extracted_array.SetValue(original_array.GetValue(fullInd), i);
            }

            return extracted_array;
        }
        public static Color GetContrastingBAWColor(Color input)
        {
            return (input.R + input.G + input.B) > 382
                ? Color.Black
                : Color.White;
        }
    }
}
