using System;
using System.Collections.Generic;
using Unity.Entities;

// Shared helpers for PostBakingSystemGroup library bakers.
// SystemAPI.Query calls must stay inside the ISystem struct itself (source-generator constraint),
// so these helpers cover only the pure-C# patterns: dictionary building and array filling.
public static class BlobLibraryUtils
{
    // Build an enum-indexed lookup from a list of SOs. Skips null entries.
    public static Dictionary<int, TSO> BuildEnumLookup<TSO>(
        IEnumerable<TSO> items,
        Func<TSO, int> getKey
    )
    {
        Dictionary<int, TSO> lookup = new Dictionary<int, TSO>();
        foreach (TSO item in items)
        {
            if (item == null) continue;
            lookup[getKey(item)] = item;
        }
        return lookup;
    }

    // Returns the number of declared values in an enum type.
    public static int EnumCount<TEnum>() where TEnum : Enum
        => Enum.GetValues(typeof(TEnum)).Length;

    // Fill a pre-allocated flat BlobBuilderArray using the pre-fill-then-overwrite pattern.
    // Every slot is seeded by defaultFactory(i); soList entries then overwrite their keyed slot.
    // Use for libraries where all slots get a safe default (e.g. AttackLibrary).
    public static void FillWithPreFill<TSO, TItem>(
        BlobBuilderArray<TItem> array,
        int count,
        Func<int, TItem> defaultFactory,
        IEnumerable<TSO> soList,
        Func<TSO, int> getIndex,
        Func<TSO, TItem> mapper
    ) where TItem : struct
    {
        for (int i = 0; i < count; i++)
            array[i] = defaultFactory(i);

        foreach (TSO so in soList)
        {
            if (so == null) continue;
            array[getIndex(so)] = mapper(so);
        }
    }

    // Fill a pre-allocated flat BlobBuilderArray using a Dictionary lookup.
    // Each slot calls mapper when found, defaultFactory(i) when absent.
    // Use for sparse libraries where found vs. absent produce different struct layouts (e.g. ItemLibrary).
    public static void FillWithLookup<TSO, TItem>(
        BlobBuilderArray<TItem> array,
        int count,
        Dictionary<int, TSO> lookup,
        Func<int, TItem> defaultFactory,
        Func<TSO, TItem> mapper
    ) where TItem : struct
    {
        for (int i = 0; i < count; i++)
            array[i] = lookup.TryGetValue(i, out TSO so) ? mapper(so) : defaultFactory(i);
    }
}
