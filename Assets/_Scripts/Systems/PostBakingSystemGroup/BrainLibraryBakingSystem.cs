using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial struct BrainLibraryBakingSystem : ISystem
{
    private readonly struct ActionDefVariant : System.IEquatable<ActionDefVariant>
    {
        public readonly UtilityActionSO action;
        public readonly int priority;

        public ActionDefVariant(UtilityActionSO action, int priority)
        {
            this.action = action;
            this.priority = priority;
        }

        public bool Equals(ActionDefVariant other) => action == other.action && priority == other.priority;
        public override bool Equals(object obj) => obj is ActionDefVariant other && Equals(other);
        public override int GetHashCode() => (action != null ? action.GetHashCode() : 0) * 397 ^ priority;
    }

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
        // Keyed by (action, effective priority) so brains overriding a tier get their own def.
        Dictionary<ActionDefVariant, int> actionDefIndex = new Dictionary<ActionDefVariant, int>();
        List<ActionDefVariant> actionDefList = new List<ActionDefVariant>();
        // Flatten base brain + modules once per brain; later passes reuse this so the blob matches what was validated.
        Dictionary<BrainSO, List<UtilityActionSO>> flattenedActionsByBrain = new Dictionary<BrainSO, List<UtilityActionSO>>();
        HashSet<string> loggedProblems = new HashSet<string>();
        foreach (BrainSO brainSO in brainSOs)
        {
            if (flattenedActionsByBrain.ContainsKey(brainSO)) continue;
            List<UtilityActionSO> flattenedActions = new List<UtilityActionSO>();
            List<string> problems = new List<string>();
            brainSO.CollectActions(flattenedActions, problems);
            flattenedActionsByBrain[brainSO] = flattenedActions;
            foreach (string problem in problems)
            {
                if (loggedProblems.Add(problem))
                    UnityEngine.Debug.LogWarning("[BrainLibraryBaking] " + problem);
            }
        }

        Dictionary<BrainSO, List<int>> actionDefIndicesByBrain = new Dictionary<BrainSO, List<int>>();
        foreach (BrainSO brainSO in brainSOs)
        {
            if (actionDefIndicesByBrain.ContainsKey(brainSO)) continue;
            List<int> brainActionDefIndices = new List<int>();
            actionDefIndicesByBrain[brainSO] = brainActionDefIndices;
            foreach (UtilityActionSO action in flattenedActionsByBrain[brainSO])
            {
                if (action == null) continue;
                int effectivePriority = brainSO.TryGetPriorityOverride(action.actionType, out int overridePriority)
                    ? overridePriority
                    : action.priority;
                ActionDefVariant variant = new ActionDefVariant(action, effectivePriority);
                if (!actionDefIndex.TryGetValue(variant, out int variantIndex))
                {
                    variantIndex = actionDefList.Count;
                    actionDefIndex[variant] = variantIndex;
                    actionDefList.Add(variant);
                }
                brainActionDefIndices.Add(variantIndex);
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
            ActionDefVariant variant = actionDefList[defIndex];
            UtilityActionSO action = variant.action;
            actionDefsBuilder[defIndex].actionType     = action.actionType;
            actionDefsBuilder[defIndex].priority       = variant.priority;
            actionDefsBuilder[defIndex].behavior       =
                action.behavior != null ? action.behavior.behaviorType : BehaviorType.None;

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
                    float normalizedTime = (float)sampleIndex / (resolution - 1);
                    samplesBuilder[sampleIndex] = authored.curve != null ? authored.curve.Evaluate(normalizedTime) : 0f;
                }
            }
        }

        // --- Brains (indexed by UnitType, pointing into actionDefs) ---
        BlobBuilderArray<BrainBlob> brainsBuilder = builder.Allocate(ref root.brains, unitTypeCount);

        for (int unitIndex = 0; unitIndex < unitTypeCount; unitIndex++)
        {
            brainsBuilder[unitIndex].unitType = (UnitType)unitIndex;

            List<int> indices = new List<int>();
            if (brainBySlot.TryGetValue(unitIndex, out BrainSO brainSO))
            {
                indices.AddRange(actionDefIndicesByBrain[brainSO]);
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
