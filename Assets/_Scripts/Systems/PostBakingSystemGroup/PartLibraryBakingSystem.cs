using Unity.Collections;
using Unity.Entities;

// Bakes the PartLibrarySO into an enum-indexed PartLibraryBlob (Data-Blob-Pointer pattern), one
// PartDef slot per UnitPartId. Mirrors ItemLibraryBakingSystem; the nested design BlobArrays are
// hand-built. Tag/group strings are copied into FixedString32Bytes (Burst-safe). Every slot gets a
// safe default so a missing SO can never index out of range at runtime. DESIGN only — ragdoll
// config bakes through RagdollJointAuthoring on the joint empties, never through this blob.
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

        int partCount = BlobLibraryUtils.EnumCount<UnitPartId>();

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref PartLibraryBlob root = ref builder.ConstructRoot<PartLibraryBlob>();
        BlobBuilderArray<PartDef> partsBuilder = builder.Allocate(ref root.parts, partCount);

        // Seed every slot with a safe default (no designs).
        for (int index = 0; index < partCount; index++)
        {
            ref PartDef def = ref partsBuilder[index];
            def.id    = (UnitPartId)index;
            def.group = default;
            builder.Allocate(ref def.designs, 0);
        }

        // Overwrite the slots that have an authored SO.
        bool[] slotAuthored = new bool[partCount];
        foreach (UnitPartSO partSO in librarySO.parts)
        {
            if (partSO == null) continue;

            int slot = (int)partSO.id;
            if (slot < 0 || slot >= partCount) continue;

            if (slotAuthored[slot])
                UnityEngine.Debug.LogWarning(
                    $"[PartLibraryBaking] Duplicate UnitPartId {partSO.id} — '{partSO.name}' overwrites an " +
                    "earlier SO with the same id (last-one-wins). Fix the library so each id is authored once.");
            slotAuthored[slot] = true;

            ref PartDef def = ref partsBuilder[slot];
            def.id    = partSO.id;
            def.group = ToFixed(partSO.group, partSO.name, "group");

            int designCount = partSO.designs != null ? partSO.designs.Count : 0;
            BlobBuilderArray<PartDesignDef> designsBuilder = builder.Allocate(ref def.designs, designCount);
            for (int designIndex = 0; designIndex < designCount; designIndex++)
            {
                PartDesign source = partSO.designs[designIndex];
                if (source == null)
                {
                    // A default struct would be a phantom 1-slice empty-tag design (slice 0 in every
                    // pool) — bake an inverted span instead so RangeCount resolves to 0.
                    designsBuilder[designIndex] = new PartDesignDef
                    {
                        tag             = default,
                        minTextureIndex = 0,
                        maxTextureIndex = -1,
                        step            = 1,
                    };
                    continue;
                }

                designsBuilder[designIndex] = new PartDesignDef
                {
                    tag             = ToFixed(source.tag, partSO.name, "design tag"),
                    minTextureIndex = source.minTextureIndex,
                    maxTextureIndex = source.maxTextureIndex,
                    step            = source.step > 0 ? source.step : 1,
                    primaryColor    = ToPaletteSlot(source.primaryColor),
                    secondaryColor  = ToPaletteSlot(source.secondaryColor),
                    tertiaryColor   = ToPaletteSlot(source.tertiaryColor),
                };
            }
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

    // Managed PaletteSlot (SO class) → blittable PartPaletteSlot. Null-safe: an unset inspector
    // slot bakes as unused (palette None). Indices clamp into short range, never negative, and the
    // window end never precedes its start.
    private static PartPaletteSlot ToPaletteSlot(PaletteSlot source)
    {
        if (source == null) return default;

        int minIndex = source.minColorIndex;
        if (minIndex < 0) minIndex = 0;
        if (minIndex > short.MaxValue) minIndex = short.MaxValue;

        int maxIndex = source.maxColorIndex;
        if (maxIndex < minIndex) maxIndex = minIndex;
        if (maxIndex > short.MaxValue) maxIndex = short.MaxValue;

        return new PartPaletteSlot
        {
            palette           = source.palette,
            minColorIndex     = (short)minIndex,
            maxColorIndex     = (short)maxIndex,
            useAlternateColor = source.useAlternateColor,
        };
    }

    // Managed-side string → FixedString32Bytes (baking is not Burst). Warns on truncation —
    // two tags longer than the ~29-byte capacity would otherwise silently collide into one tag.
    private static FixedString32Bytes ToFixed(string value, string assetName, string fieldDescription)
    {
        FixedString32Bytes result = default;
        if (string.IsNullOrEmpty(value)) return result;

        if (System.Text.Encoding.UTF8.GetByteCount(value) > FixedString32Bytes.UTF8MaxLengthInBytes)
            UnityEngine.Debug.LogWarning(
                $"[PartLibraryBaking] '{assetName}' {fieldDescription} '{value}' exceeds " +
                $"{FixedString32Bytes.UTF8MaxLengthInBytes} UTF-8 bytes and will be TRUNCATED — " +
                "long tags can silently collide. Shorten the string.");

        result.CopyFromTruncated(value);
        return result;
    }
}
