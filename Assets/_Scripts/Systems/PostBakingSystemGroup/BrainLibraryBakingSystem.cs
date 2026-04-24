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
            entriesBuilder[i].randomMotivationAmount = brainSO.amount;

            int behaviourCount = brainSO.behaviours != null ? brainSO.behaviours.Length : 0;
            BlobBuilderArray<MotivationType> behavioursBuilder = builder.Allocate(ref entriesBuilder[i].motivation, behaviourCount);
            for (int j = 0; j < behaviourCount; j++)
                behavioursBuilder[j] = brainSO.behaviours[j];

            int randomCount = brainSO.randomBehaviours != null ? brainSO.randomBehaviours.Length : 0;
            BlobBuilderArray<MotivationType> randomBuilder = builder.Allocate(ref entriesBuilder[i].randomMotivations, randomCount);
            for (int j = 0; j < randomCount; j++)
                randomBuilder[j] = brainSO.randomBehaviours[j];

            int attackFactionsLength = brainSO.attackFactions != null ? brainSO.attackFactions.Length : 0;
            BlobBuilderArray<FactionType> attackFactionBuilder = builder.Allocate(ref entriesBuilder[i].attackFactions, attackFactionsLength);
            for (int j = 0; j < attackFactionsLength; j++)
                attackFactionBuilder[j] = brainSO.attackFactions[j];
            
            int attacksLength = brainSO.attacks != null ? brainSO.attacks.Length : 0;
            BlobBuilderArray<AttackType> attackBuilder = builder.Allocate(ref entriesBuilder[i].attacks, attacksLength);
            for (int j = 0; j < attacksLength; j++)
                attackBuilder[j] = brainSO.attacks[j];
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
