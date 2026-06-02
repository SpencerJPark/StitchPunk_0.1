using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Evaluates all considerations for each active action entity and writes totalUtility.
// Uses pre-sampled ConsiderationBlob curves. Applies a compensation factor so adding more
// considerations doesn't collapse the overall score toward zero (Dave Mark's modification factor).
[BurstCompile]
[UpdateInGroup(typeof(UtilityDecisionSystemGroup))]
[UpdateAfter(typeof(ContextAssemblySystem))]
public partial struct ConsiderationScoringSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<BrainLibrary>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BrainLibrary brainLibrary = SystemAPI.GetSingleton<BrainLibrary>();

        state.Dependency = new ConsiderationScoringJob
        {
            aiConfig = brainLibrary.blob,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActionActive))]
public partial struct ConsiderationScoringJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob> aiConfig;

    public void Execute(ref UtilityAction utilityAction)
    {
        ref ActionDefBlob actionDef = ref aiConfig.Value.actionDefs[utilityAction.actionDefIndex];

        int considerationCount = actionDef.considerations.Length;
        if (considerationCount == 0)
        {
            utilityAction.totalUtility = 1f;
            return;
        }

        float product = 1f;
        for (int i = 0; i < considerationCount; i++)
        {
            ref ConsiderationBlob consideration = ref actionDef.considerations[i];
            float inputValue  = ReadInput(consideration.input, utilityAction.contextData);
            float curveScore  = consideration.Evaluate(inputValue);
            product          *= curveScore * consideration.weight;
        }

        // Modification factor: compensates for score collapse as consideration count grows.
        float modFactor         = 1f - (1f / considerationCount);
        utilityAction.totalUtility = product + (1f - product) * modFactor * product;
    }

    private static float ReadInput(ConsiderationType inputType, in ActionContextData context)
    {
        switch (inputType)
        {
            case ConsiderationType.Health:           return context.ownerHealthRatio;
            case ConsiderationType.Motivation:       return context.ownerNeedRatio;
            case ConsiderationType.DistanceToTarget: return math.saturate(context.distanceToTarget / 50f);
            default:                                 return 0f;
        }
    }
}
