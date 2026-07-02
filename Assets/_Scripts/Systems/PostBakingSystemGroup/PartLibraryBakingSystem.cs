using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Bakes the PartLibrarySO into an enum-indexed PartLibraryBlob (Data-Blob-Pointer pattern), one
// PartDef slot per PartDefId. Mirrors ItemLibraryBakingSystem; the nested BlobArrays (ranges, zones)
// are hand-built. Tag/group strings are copied into FixedString32Bytes (Burst-safe). Every slot gets
// a safe default so a missing SO can never index out of range at runtime.
[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct PartLibraryBakingSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PartLibraryReference>();
    }

    public void OnUpdate(ref SystemState state)
    {
        PartLibrarySO librarySO = null;
        foreach (RefRO<PartLibraryReference> reference in SystemAPI.Query<RefRO<PartLibraryReference>>())
        {
            librarySO = reference.ValueRO.library.Value;
            break;
        }

        if (librarySO == null) return;

        int partCount = BlobLibraryUtils.EnumCount<PartDefId>();

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref PartLibraryBlob root = ref builder.ConstructRoot<PartLibraryBlob>();
        BlobBuilderArray<PartDef> partsBuilder = builder.Allocate(ref root.parts, partCount);

        // Seed every slot with a safe default (no design ranges, no ragdoll zones).
        for (int index = 0; index < partCount; index++)
        {
            ref PartDef def = ref partsBuilder[index];
            def.id                 = (PartDefId)index;
            def.group              = default;
            def.defaultSettleSpeed = 8f;
            builder.Allocate(ref def.ranges, 0);
            builder.Allocate(ref def.zones, 0);
        }

        // Overwrite the slots that have an authored SO.
        foreach (PartDefinitionSO partSO in librarySO.parts)
        {
            if (partSO == null) continue;

            int slot = (int)partSO.id;
            if (slot < 0 || slot >= partCount) continue;

            ref PartDef def = ref partsBuilder[slot];
            def.id                 = partSO.id;
            def.group              = ToFixed(partSO.group);
            def.defaultSettleSpeed = partSO.defaultSettleSpeed > 0f ? partSO.defaultSettleSpeed : 8f;

            // Fill ranges immediately (don't hold the BlobBuilderArray across the next Allocate).
            int rangeCount = partSO.ranges != null ? partSO.ranges.Count : 0;
            BlobBuilderArray<PartTagRange> rangesBuilder = builder.Allocate(ref def.ranges, rangeCount);
            for (int rangeIndex = 0; rangeIndex < rangeCount; rangeIndex++)
            {
                PartRange source = partSO.ranges[rangeIndex];
                rangesBuilder[rangeIndex] = new PartTagRange
                {
                    tag          = ToFixed(source.tag),
                    min          = source.min,
                    max          = source.max,
                    step         = source.step > 0 ? source.step : 1,
                    randomizable = source.randomizable,
                };
            }

            int zoneCount = partSO.zones != null ? partSO.zones.Count : 0;
            BlobBuilderArray<float2> zoneBuilder = builder.Allocate(ref def.zones, zoneCount);
            for (int zoneIndex = 0; zoneIndex < zoneCount; zoneIndex++)
                zoneBuilder[zoneIndex] = new float2(partSO.zones[zoneIndex].min, partSO.zones[zoneIndex].max);
        }

        BlobAssetReference<PartLibraryBlob> blobRef =
            builder.CreateBlobAssetReference<PartLibraryBlob>(Allocator.Persistent);

        foreach (RefRW<PartLibrary> holder in SystemAPI.Query<RefRW<PartLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();

            holder.ValueRW.library = blobRef;
        }
    }

    public void OnDestroy(ref SystemState state)
    {
        foreach (RefRW<PartLibrary> holder in SystemAPI.Query<RefRW<PartLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();
        }
    }

    // Managed-side string → FixedString32Bytes (baking is not Burst). Truncates silently if too long.
    private static FixedString32Bytes ToFixed(string value)
    {
        FixedString32Bytes result = default;
        if (!string.IsNullOrEmpty(value))
            result.CopyFromTruncated(value);
        return result;
    }
}
