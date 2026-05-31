using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Enables ActionActive for Self-targeting action entities each frame.
// SingleTarget actions remain disabled until Phase 3 wires fan-out per AwarenessTarget.
// Runs first in UtilityDecisionSystemGroup so ConsiderationScoringSystem only sees valid candidates.
[BurstCompile]
[UpdateInGroup(typeof(UtilityDecisionSystemGroup), OrderFirst = true)]
public partial struct ActionInstancingSystem : ISystem
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

        state.Dependency = new ActionInstancingJob
        {
            aiConfig = brainLibrary.blob,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct ActionInstancingJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<AIConfigBlob> aiConfig;

    public void Execute(in UtilityAction utilityAction, EnabledRefRW<ActionActive> actionActiveEnabled)
    {
        ref ActionDefBlob actionDef = ref aiConfig.Value.actionDefs[utilityAction.actionDefIndex];
        actionActiveEnabled.ValueRW = (actionDef.targeting == TargetingMode.Self);
    }
}
