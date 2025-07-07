using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class EnumUtils
{
    /// <summary>
    /// Picks a random enum value of type T, where each value is weighted by the corresponding entry in weights.
    /// </summary>
    public static T GetWeightedRandom<T>(IList<int> weights) where T : Enum
    {
        // 1) Grab all enum values in declaration order
        var values = Enum.GetValues(typeof(T)).Cast<T>().ToArray();

        // 2) Validate
        if (weights == null)
            throw new ArgumentNullException(nameof(weights));
        if (weights.Count != values.Length)
            throw new ArgumentException(
                $"Weights length ({weights.Count}) must match number of enum values ({values.Length}).");

        // 3) Sum weights (in an int)
        int total = weights.Sum();
        if (total <= 0)
            throw new ArgumentException("At least one weight must be positive.");

        // 4) Pick a random integer in [0, total)
        int r = UnityEngine.Random.Range(0, total);

        // 5) Walk cumulative buckets
        int cum = 0;
        for (int i = 0; i < values.Length; i++)
        {
            cum += weights[i];
            if (r < cum)
                return values[i];
        }

        // 6) Fallback: return last enum value
        return values[values.Length - 1];
    }
}


// Example usage:
// enum LootTier { Common, Uncommon, Rare, Epic }
// var weights = new List<int> { 50, 30, 15, 5 };
// LootTier tier = EnumUtils.GetWeightedRandom<LootTier>(weights);
