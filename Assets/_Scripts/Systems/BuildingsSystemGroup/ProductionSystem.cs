using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(BuildingsSystemGroup))]
public partial struct ProductionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FactoryLibrary>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BlobAssetReference<FactoryLibraryBlob> libraryBlob =
            SystemAPI.GetSingleton<FactoryLibrary>().blob;

        float deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new StartProductionJob
        {
            libraryBlob = libraryBlob,
        }.Schedule(state.Dependency);

        state.Dependency = new TickProductionJob
        {
            libraryBlob = libraryBlob,
            deltaTime   = deltaTime,
        }.Schedule(state.Dependency);
    }
}

// Checks idle stations for available inputs and workers, then starts a production cycle.
[BurstCompile]
[WithDisabled(typeof(ProductionProgress))]
public partial struct StartProductionJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<FactoryLibraryBlob> libraryBlob;

    public void Execute(
        in FactoryStation station,
        ref DynamicBuffer<StationInputSlot> inputs,
        in DynamicBuffer<StationWorkerSlot> workers,
        ref ProductionProgress progress,
        EnabledRefRW<ProductionProgress> progressEnabled)
    {
        ref FactoryLibraryBlob blob = ref libraryBlob.Value;
        int recipeIndex = blob.FindRecipeIndex(station.structureType);
        if (recipeIndex < 0) return;

        ref RecipeBlob recipe = ref blob.recipes[recipeIndex];

        if (!HasRequiredWorkers(workers, recipe.workersRequired)) return;
        if (!HasRequiredInputs(inputs, ref recipe.inputs)) return;

        ConsumeInputs(ref inputs, ref recipe.inputs);

        progress.elapsed         = 0f;
        progress.recipeBlobIndex = recipeIndex;
        progressEnabled.ValueRW  = true;
    }

    private static bool HasRequiredWorkers(in DynamicBuffer<StationWorkerSlot> workers, int required)
    {
        if (required == 0) return true;
        int filled = 0;
        for (int i = 0; i < workers.Length; i++)
        {
            if (workers[i].workerEntity != Entity.Null)
                filled++;
        }
        return filled >= required;
    }

    private static bool HasRequiredInputs(
        in DynamicBuffer<StationInputSlot> inputs,
        ref BlobArray<FactoryItemType> required)
    {
        for (int r = 0; r < required.Length; r++)
        {
            FactoryItemType needed = required[r];
            bool found = false;
            for (int i = 0; i < inputs.Length; i++)
            {
                if (inputs[i].itemType == needed)
                {
                    found = true;
                    break;
                }
            }
            if (!found) return false;
        }
        return true;
    }

    private static void ConsumeInputs(
        ref DynamicBuffer<StationInputSlot> inputs,
        ref BlobArray<FactoryItemType> required)
    {
        for (int r = 0; r < required.Length; r++)
        {
            FactoryItemType needed = required[r];
            for (int i = 0; i < inputs.Length; i++)
            {
                if (inputs[i].itemType == needed)
                {
                    inputs.RemoveAtSwapBack(i);
                    break;
                }
            }
        }
    }
}

// Advances the timer on running production cycles and writes outputs on completion.
[BurstCompile]
public partial struct TickProductionJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<FactoryLibraryBlob> libraryBlob;
    public float deltaTime;

    public void Execute(
        ref DynamicBuffer<StationOutputSlot> outputs,
        ref ProductionProgress progress,
        EnabledRefRW<ProductionProgress> progressEnabled)
    {
        ref FactoryLibraryBlob blob = ref libraryBlob.Value;
        ref RecipeBlob recipe       = ref blob.recipes[progress.recipeBlobIndex];

        progress.elapsed += deltaTime;

        if (progress.elapsed < recipe.durationSeconds) return;

        for (int i = 0; i < recipe.outputs.Length; i++)
            outputs.Add(new StationOutputSlot { itemType = recipe.outputs[i] });

        progressEnabled.ValueRW = false;
    }
}
