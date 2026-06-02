using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct BehaviorLibraryBakingSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BehaviorLibraryReference>();
    }

    public void OnUpdate(ref SystemState state)
    {
        BehaviorLibrarySO librarySO = null;
        foreach (RefRO<BehaviorLibraryReference> reference in SystemAPI.Query<RefRO<BehaviorLibraryReference>>())
        {
            librarySO = reference.ValueRO.library.Value;
            break;
        }

        if (librarySO == null) return;

        Dictionary<int, BehaviorSO> soByType =
            BlobLibraryUtils.BuildEnumLookup(librarySO.behaviors, behavior => (int)behavior.behaviorType);
        int typeCount = BlobLibraryUtils.EnumCount<BehaviorType>();

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref BehaviorLibraryBlob root = ref builder.ConstructRoot<BehaviorLibraryBlob>();
        BlobBuilderArray<BehaviorConfigBlob> behaviorsBuilder = builder.Allocate(ref root.behaviors, typeCount);

        for (int typeIndex = 0; typeIndex < typeCount; typeIndex++)
        {
            behaviorsBuilder[typeIndex].behaviorType = (BehaviorType)typeIndex;

            if (!soByType.TryGetValue(typeIndex, out BehaviorSO behaviorSO))
            {
                builder.Allocate(ref behaviorsBuilder[typeIndex].executionSequence, 0);
                builder.Allocate(ref behaviorsBuilder[typeIndex].interruptionCleanup, 0);
                continue;
            }

            // Execution sequence
            List<BehaviorCommandAuthoring> execSource = behaviorSO.executionSequence;
            int execCount = execSource != null ? execSource.Count : 0;
            BlobBuilderArray<BehaviorCommand> execBuilder =
                builder.Allocate(ref behaviorsBuilder[typeIndex].executionSequence, execCount);
            for (int commandIndex = 0; commandIndex < execCount; commandIndex++)
            {
                BehaviorCommandAuthoring authored = execSource[commandIndex];
                if (authored == null) continue;
                execBuilder[commandIndex] = new BehaviorCommand
                {
                    type       = authored.type,
                    IntParam   = authored.IntParam,
                    FloatParam = authored.FloatParam,
                    Duration   = authored.Duration,
                };
            }

            // Interruption cleanup sequence
            List<BehaviorCommandAuthoring> cleanupSource = behaviorSO.interruptionCleanup;
            int cleanupCount = cleanupSource != null ? cleanupSource.Count : 0;
            BlobBuilderArray<BehaviorCommand> cleanupBuilder =
                builder.Allocate(ref behaviorsBuilder[typeIndex].interruptionCleanup, cleanupCount);
            for (int commandIndex = 0; commandIndex < cleanupCount; commandIndex++)
            {
                BehaviorCommandAuthoring authored = cleanupSource[commandIndex];
                if (authored == null) continue;
                cleanupBuilder[commandIndex] = new BehaviorCommand
                {
                    type       = authored.type,
                    IntParam   = authored.IntParam,
                    FloatParam = authored.FloatParam,
                    Duration   = authored.Duration,
                };
            }
        }

        BlobAssetReference<BehaviorLibraryBlob> blobRef =
            builder.CreateBlobAssetReference<BehaviorLibraryBlob>(Allocator.Persistent);

        foreach (RefRW<BehaviorLibrary> holder in SystemAPI.Query<RefRW<BehaviorLibrary>>())
        {
            if (holder.ValueRO.blob.IsCreated)
                holder.ValueRW.blob.Dispose();

            holder.ValueRW.blob = blobRef;
        }
    }

    public void OnDestroy(ref SystemState state)
    {
        foreach (RefRW<BehaviorLibrary> holder in SystemAPI.Query<RefRW<BehaviorLibrary>>())
        {
            if (holder.ValueRO.blob.IsCreated)
                holder.ValueRW.blob.Dispose();
        }
    }
}
