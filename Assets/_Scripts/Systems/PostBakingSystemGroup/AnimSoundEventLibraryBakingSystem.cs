using Unity.Collections;
using Unity.Entities;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct AnimSoundEventLibraryBakingSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<AnimSoundEventLibraryReference>();
    }

    public void OnUpdate(ref SystemState state)
    {
        AnimSoundEventMappingSO librarySO = null;
        foreach (RefRO<AnimSoundEventLibraryReference> reference in
            SystemAPI.Query<RefRO<AnimSoundEventLibraryReference>>())
        {
            librarySO = reference.ValueRO.library.Value;
            break;
        }

        if (librarySO == null) return;

        int entryCount = librarySO.entries?.Count ?? 0;

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref AnimSoundEventMappingBlob root = ref builder.ConstructRoot<AnimSoundEventMappingBlob>();
        BlobBuilderArray<AnimSoundEventEntryBlob> entriesBuilder = builder.Allocate(ref root.entries, entryCount);

        for (int i = 0; i < entryCount; i++)
        {
            entriesBuilder[i].eventKey = librarySO.entries[i].eventKey;
            entriesBuilder[i].sound    = librarySO.entries[i].sound;
        }

        BlobAssetReference<AnimSoundEventMappingBlob> blobRef =
            builder.CreateBlobAssetReference<AnimSoundEventMappingBlob>(Allocator.Persistent);

        foreach (RefRW<AnimSoundEventLibrary> holder in SystemAPI.Query<RefRW<AnimSoundEventLibrary>>())
        {
            if (holder.ValueRO.blob.IsCreated)
                holder.ValueRW.blob.Dispose();

            holder.ValueRW.blob = blobRef;
        }
    }

    public void OnDestroy(ref SystemState state)
    {
        foreach (RefRW<AnimSoundEventLibrary> holder in SystemAPI.Query<RefRW<AnimSoundEventLibrary>>())
        {
            if (holder.ValueRO.blob.IsCreated)
                holder.ValueRW.blob.Dispose();
        }
    }
}
