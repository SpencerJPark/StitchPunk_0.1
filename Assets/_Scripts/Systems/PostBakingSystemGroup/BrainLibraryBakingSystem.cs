using Unity.Collections;
using Unity.Entities;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct BrainLibraryBakingSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BrainLibraryReference>();
    }

    public void OnUpdate(ref SystemState state)
    {
        BrainLibrarySO librarySO = null;
        foreach (RefRO<BrainLibraryReference> reference in SystemAPI.Query<RefRO<BrainLibraryReference>>())
        {
            librarySO = reference.ValueRO.library.Value;
            break;
        }

        if (librarySO == null || librarySO.brains == null || librarySO.brains.Count == 0) return;

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref BrainLibraryBlob root = ref builder.ConstructRoot<BrainLibraryBlob>();
        BlobBuilderArray<BrainEntryBlob> entriesBuilder = builder.Allocate(ref root.entries, librarySO.brains.Count);

        for (int i = 0; i < librarySO.brains.Count; i++)
        {
            BrainSO brainSO = librarySO.brains[i];
            if (brainSO == null) continue;

            entriesBuilder[i].brainType             = brainSO.brainType;
            entriesBuilder[i].factionType           = brainSO.factionType;
            entriesBuilder[i].canBePlayerControlled = brainSO.canBePlayerControlled;
            entriesBuilder[i].awarenessRange        = brainSO.awarenessRange;
            entriesBuilder[i].randomBehaviourAmount = brainSO.amount;

            int behaviourCount = brainSO.behaviours != null ? brainSO.behaviours.Length : 0;
            BlobBuilderArray<BehaviourType> behavioursBuilder = builder.Allocate(ref entriesBuilder[i].behaviours, behaviourCount);
            for (int j = 0; j < behaviourCount; j++)
                behavioursBuilder[j] = brainSO.behaviours[j];

            int randomCount = brainSO.randomBehaviours != null ? brainSO.randomBehaviours.Length : 0;
            BlobBuilderArray<BehaviourType> randomBuilder = builder.Allocate(ref entriesBuilder[i].randomBehaviours, randomCount);
            for (int j = 0; j < randomCount; j++)
                randomBuilder[j] = brainSO.randomBehaviours[j];

            int attackCount = brainSO.attackFactions != null ? brainSO.attackFactions.Length : 0;
            BlobBuilderArray<FactionType> attackBuilder = builder.Allocate(ref entriesBuilder[i].attackFactions, attackCount);
            for (int j = 0; j < attackCount; j++)
                attackBuilder[j] = brainSO.attackFactions[j];
        }

        BlobAssetReference<BrainLibraryBlob> blobRef =
            builder.CreateBlobAssetReference<BrainLibraryBlob>(Allocator.Persistent);

        foreach (RefRW<BrainLibrary> holder in SystemAPI.Query<RefRW<BrainLibrary>>())
        {
            if (holder.ValueRO.blob.IsCreated)
                holder.ValueRW.blob.Dispose();

            holder.ValueRW.blob = blobRef;
        }
    }

    public void OnDestroy(ref SystemState state)
    {
        foreach (RefRW<BrainLibrary> holder in SystemAPI.Query<RefRW<BrainLibrary>>())
        {
            if (holder.ValueRO.blob.IsCreated)
                holder.ValueRW.blob.Dispose();
        }
    }
}
