using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct SoundLibraryBakingSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SoundLibraryReference>();
    }

    public void OnUpdate(ref SystemState state)
    {
        SoundLibrarySO librarySO = null;
        foreach (RefRO<SoundLibraryReference> reference in SystemAPI.Query<RefRO<SoundLibraryReference>>())
        {
            librarySO = reference.ValueRO.library.Value;
            break;
        }

        if (librarySO == null) return;

        Dictionary<int, SoundSO> soByIndex =
            BlobLibraryUtils.BuildEnumLookup(librarySO.sounds, so => (int)so.type);
        int typeCount = BlobLibraryUtils.EnumCount<SoundType>();

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref SoundLibraryBlob root = ref builder.ConstructRoot<SoundLibraryBlob>();
        BlobBuilderArray<SoundBlob> soundsBuilder = builder.Allocate(ref root.sounds, typeCount);

        BlobLibraryUtils.FillWithLookup(soundsBuilder, typeCount, soByIndex, DefaultSound, MapSound);

        BlobAssetReference<SoundLibraryBlob> blobRef =
            builder.CreateBlobAssetReference<SoundLibraryBlob>(Allocator.Persistent);

        foreach (RefRW<SoundLibrary> holder in SystemAPI.Query<RefRW<SoundLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();

            holder.ValueRW.library = blobRef;
        }
    }

    public void OnDestroy(ref SystemState state)
    {
        foreach (RefRW<SoundLibrary> holder in SystemAPI.Query<RefRW<SoundLibrary>>())
        {
            if (holder.ValueRO.library.IsCreated)
                holder.ValueRW.library.Dispose();
        }
    }

    static SoundBlob DefaultSound(int i) => new SoundBlob
    {
        type           = (SoundType)i,
        bus            = SoundBus.SFX,
        variationCount = 0,
        volumeMin      = 1f,
        volumeMax      = 1f,
        pitchMin       = 1f,
        pitchMax       = 1f,
        loop           = false,
        priority       = 128,
        minDistance    = 1f,
        maxDistance    = 30f,
        maxConcurrent  = 8,
        dedupWindow    = 0.05f,
    };

    static SoundBlob MapSound(SoundSO so) => new SoundBlob
    {
        type           = so.type,
        bus            = so.bus,
        variationCount = so.clipVariations != null ? so.clipVariations.Length : 0,
        volumeMin      = so.volumeRange.x,
        volumeMax      = so.volumeRange.y,
        pitchMin       = so.pitchRange.x,
        pitchMax       = so.pitchRange.y,
        loop           = so.loop,
        priority       = (byte)so.priority,
        minDistance    = so.minDistance,
        maxDistance    = so.maxDistance,
        maxConcurrent  = so.maxConcurrent,
        dedupWindow    = so.dedupWindow,
    };
}
