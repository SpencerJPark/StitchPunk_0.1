using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(AIScoringSystemGroup), OrderLast = true)]
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
[WithAll(typeof(AIBrain), typeof(ActionRequest), typeof(Alive))]
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
            action.utilityScore = math.max(0f, ScoreAction(action, motivations, scoringLibrary));
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

            if (action.needType == motivation.needType)
            {
                float motiveScore = AIUtils.EvaluateScoringCurve(library, motivation.needType, motivation.value);
                
                // Usually utility is multiplied to weight the motivation
                return motiveScore * action.utilityScore;
            }
        }
        return 0f;
    }
}
