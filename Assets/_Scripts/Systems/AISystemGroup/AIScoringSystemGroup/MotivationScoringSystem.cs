using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Unified motivation scoring system. Iterates the Motivation buffer on each AI entity
/// and scores every entry using the same formula: EvaluateCurve(value) * contextMultiplier.
///
/// Two scoring modes:
///   Interaction — queries the spatial hash for nearby entities of this motivationType,
///                 writes an ActionOption per candidate (category = actionCategory, target = entity)
///   Action      — no spatial query; scores directly and writes one ActionOption
///                 (category = actionCategory, no target — execution systems use Target component)
///
/// Awareness systems (AIAwarenessSystemGroup) are responsible for updating motivation values
/// and contextMultipliers to reflect what the unit can currently act on. Scoring is pure.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup))]
public partial struct MotivationScoringSystem : ISystem
{

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<ScoringLibrary>();
        
        
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ScoringLibrary      scoringLibrary = SystemAPI.GetSingleton<ScoringLibrary>();

        state.Dependency = new MotivationScoringJob
        {
            scoringLibrary            = scoringLibrary.library,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActiveBrain), typeof(NeedsAction))]
public partial struct MotivationScoringJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<AIScoringLibraryBlob> scoringLibrary;

    public void Execute(
        ref DynamicBuffer<ActionOption> options,
        [ReadOnly] in DynamicBuffer<Motivation> motivations)
    {
        for (int i = 0; i < options.Length; i++)
        {
            ActionOption action = options[i];
            
            float calculatedScore = ScoreAction(action, motivations, scoringLibrary);
            
            action.utilityScore = math.max(0f, calculatedScore);
            
            options[i] = action;
        }
    }
    
    private static float ScoreAction(
        ActionOption action, 
        in DynamicBuffer<Motivation> motivations, 
        BlobAssetReference<AIScoringLibraryBlob> library)
    {
        for (int m = 0; m < motivations.Length; m++)
        {
            var motivation = motivations[m];

            if (action.motivationType == motivation.motivationType)
            {
                float motiveScore = AIUtils.EvaluateScoringCurve(library, motivation.motivationType, motivation.value);
                
                // Usually utility is multiplied to weight the motivation
                return motiveScore * action.utilityScore;
            }
        }
        return 0f;
    }
}
