using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct BrainLibraryBakingSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BrainLibrary>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // Collect every assigned brain from the bake-only handoff buffer(s).
        List<BrainSO> brainSOs = new List<BrainSO>();
        foreach (DynamicBuffer<BrainLibraryEntry> buffer in SystemAPI.Query<DynamicBuffer<BrainLibraryEntry>>())
        {
            foreach (BrainLibraryEntry entry in buffer)
            {
                BrainSO brainSO = entry.config.Value;
                if (brainSO != null) brainSOs.Add(brainSO);
            }
        }

        if (brainSOs.Count == 0) return;

        // De-duplicate actions across all brains into one shared action-def table.
        Dictionary<UtilityActionSO, int> actionDefIndex = new Dictionary<UtilityActionSO, int>();
        List<UtilityActionSO> actionDefList = new List<UtilityActionSO>();
        foreach (BrainSO brainSO in brainSOs)
        {
            if (brainSO.actions == null) continue;
            foreach (UtilityActionSO action in brainSO.actions)
            {
                if (action == null || actionDefIndex.ContainsKey(action)) continue;
                actionDefIndex[action] = actionDefList.Count;
                actionDefList.Add(action);
            }
        }

        Dictionary<int, BrainSO> brainBySlot =
            BlobLibraryUtils.BuildEnumLookup(brainSOs, brain => (int)brain.unitType);
        int unitTypeCount = BlobLibraryUtils.EnumCount<UnitType>();
        int resolution = ConstGameData.SCORING_CURVE_RESOLUTION;

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref BrainLibraryBlob root = ref builder.ConstructRoot<BrainLibraryBlob>();

        // --- Action defs (+ nested considerations + sampled curves) ---
        BlobBuilderArray<ActionDefBlob> actionDefsBuilder =
            builder.Allocate(ref root.actionDefs, actionDefList.Count);

        for (int defIndex = 0; defIndex < actionDefList.Count; defIndex++)
        {
            UtilityActionSO action = actionDefList[defIndex];
            actionDefsBuilder[defIndex].actionType     = action.actionType;
            actionDefsBuilder[defIndex].priority       = action.priority;
            actionDefsBuilder[defIndex].targeting      = action.targeting;
            actionDefsBuilder[defIndex].behavior       =
                action.behavior != null ? action.behavior.behaviorType : BehaviorType.None;
            actionDefsBuilder[defIndex].maxCandidates  = math.max(1, action.maxCandidates);

            int considerationCount = action.considerations != null ? action.considerations.Count : 0;
            BlobBuilderArray<ConsiderationBlob> considerationsBuilder =
                builder.Allocate(ref actionDefsBuilder[defIndex].considerations, considerationCount);

            for (int considerationIndex = 0; considerationIndex < considerationCount; considerationIndex++)
            {
                ConsiderationAuthoring authored = action.considerations[considerationIndex];
                considerationsBuilder[considerationIndex].input      = authored.input;
                considerationsBuilder[considerationIndex].needType   = authored.needType;
                considerationsBuilder[considerationIndex].traitType  = authored.traitType;
                considerationsBuilder[considerationIndex].weight     = authored.weight;
                considerationsBuilder[considerationIndex].resolution = resolution;

                BlobBuilderArray<float> samplesBuilder =
                    builder.Allocate(ref considerationsBuilder[considerationIndex].samples, resolution);

                for (int sampleIndex = 0; sampleIndex < resolution; sampleIndex++)
                {
                    float t = (float)sampleIndex / (resolution - 1);
                    samplesBuilder[sampleIndex] = authored.curve != null ? authored.curve.Evaluate(t) : 0f;
                }
            }
        }

        // --- Brains (indexed by UnitType, pointing into actionDefs) ---
        BlobBuilderArray<BrainBlob> brainsBuilder = builder.Allocate(ref root.brains, unitTypeCount);

        for (int unitIndex = 0; unitIndex < unitTypeCount; unitIndex++)
        {
            brainsBuilder[unitIndex].unitType = (UnitType)unitIndex;

            List<int> indices = new List<int>();
            if (brainBySlot.TryGetValue(unitIndex, out BrainSO brainSO) && brainSO.actions != null)
            {
                foreach (UtilityActionSO action in brainSO.actions)
                {
                    if (action != null && actionDefIndex.TryGetValue(action, out int resolvedIndex))
                        indices.Add(resolvedIndex);
                }
            }

            BlobBuilderArray<int> indicesBuilder =
                builder.Allocate(ref brainsBuilder[unitIndex].actionDefIndices, indices.Count);
            for (int slotIndex = 0; slotIndex < indices.Count; slotIndex++)
                indicesBuilder[slotIndex] = indices[slotIndex];
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
