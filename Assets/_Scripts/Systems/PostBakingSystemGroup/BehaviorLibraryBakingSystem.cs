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

                if (!BehaviorCommandCatalog.IsImplemented(authored.type))
                    UnityEngine.Debug.LogWarning(
                        $"[BehaviorLibraryBaking] '{behaviorSO.name}' executionSequence[{commandIndex}] is " +
                        $"{authored.type} — no interpreter arm exists (see BehaviorCommandCatalog); " +
                        "it will no-op at runtime.");

                execBuilder[commandIndex] = BakeCommand(authored, commandIndex, behaviorSO.name);
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

                if (!BehaviorCommandCatalog.IsImplemented(authored.type))
                    UnityEngine.Debug.LogWarning(
                        $"[BehaviorLibraryBaking] '{behaviorSO.name}' interruptionCleanup[{commandIndex}] is " +
                        $"{authored.type} — no interpreter arm exists (see BehaviorCommandCatalog); " +
                        "it will no-op at runtime.");

                if (BehaviorCommandCatalog.IsBlocking(authored.type))
                    UnityEngine.Debug.LogError(
                        $"[BehaviorLibraryBaking] '{behaviorSO.name}' interruptionCleanup[{commandIndex}] is " +
                        $"{authored.type} — blocking commands are not allowed in cleanup (it runs in one frame) " +
                        "and will be skipped at runtime.");

                cleanupBuilder[commandIndex] = BakeCommand(authored, commandIndex, behaviorSO.name);
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

    // Copies one authored command into blob form. Invalid LoopUntil jump targets degrade to a
    // no-op (immediate-timeout loop) so a bad asset skips the loop instead of hanging a unit.
    private static BehaviorCommand BakeCommand(BehaviorCommandAuthoring authored, int commandIndex, string assetName)
    {
        BehaviorCommand command = new BehaviorCommand
        {
            type                = authored.type,
            IntParam            = authored.IntParam,
            FloatParam          = authored.FloatParam,
            Duration            = authored.Duration,
            Qualifier           = authored.Qualifier,
            QualifierIntParam   = authored.QualifierIntParam,
            QualifierFloatParam = authored.QualifierFloatParam,
            Looping             = authored.Looping,
        };

        if (authored.type == BehaviorCommandType.LoopUntil
            && (authored.IntParam < 0 || authored.IntParam >= commandIndex))
        {
            UnityEngine.Debug.LogError(
                $"[BehaviorLibraryBaking] '{assetName}' command [{commandIndex}] LoopUntil jump index " +
                $"{authored.IntParam} must be >= 0 and < {commandIndex}. Baking as no-op (exits immediately).");
            command.Qualifier = LoopQualifier.None;
            command.Duration  = 0.01f;
            command.IntParam  = commandIndex; // self-target is never taken because the timeout fires first
        }

        return command;
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
