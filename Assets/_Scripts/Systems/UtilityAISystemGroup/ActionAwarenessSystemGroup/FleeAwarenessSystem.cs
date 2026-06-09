using Unity.Burst;
using Unity.Entities;
using Unity.Collections;

[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
[UpdateAfter(typeof(SelfDefenceAwarenessSystem))]
public partial struct FleeAwarenessSystem : ISystem
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
        state.Dependency = new FleeAwarenessJob
        {
            aiConfig = brainLibrary.blob,
        }.ScheduleParallel(state.Dependency);
    }
}

// Offers Flee when health is critical and the unit is under threat.
// Bravery/cowardice is handled via ConsiderationType.Trait in the Flee ActionDefBlob consideration curves.
[BurstCompile]
[WithAll(typeof(UtilityBrain), typeof(ActionRequest))]
[WithDisabled(typeof(Dead))]
public partial struct FleeAwarenessJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob> aiConfig;

    public void Execute(
        in UtilityBrain                 brain,
        in Health                       health,
        ref DynamicBuffer<Motivation>   motivations,
        ref DynamicBuffer<UtilityActions> options,
        in DynamicBuffer<ThreatEntry>   threats)
    {
        if (threats.Length == 0)
            return;

        float healthRatio = health.healthAmountMax > 0
            ? (float)health.healthAmount / health.healthAmountMax
            : 1f;

        if (healthRatio >= 0.3f)
            return;

        // SelfPreservation drives the scoring curve — mirrors EnemyAwareness setting BloodLust.
        AIUtils.SetMotivationValue(ref motivations, NeedType.SelfPreservation, 100f);

        int defIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, ActionType.Flee);
        if (defIndex < 0)
            return;

        // Find the top aggressor so BehaviorExecutionSystem can flee away from them.
        Entity topAggressor = Entity.Null;
        float  topThreat    = 0f;
        for (int i = 0; i < threats.Length; i++)
        {
            if (threats[i].threatScore > topThreat)
            {
                topThreat    = threats[i].threatScore;
                topAggressor = threats[i].attackerEntity;
            }
        }

        options.Add(new UtilityActions
        {
            actionType      = ActionType.Flee,
            actionDefIndex  = defIndex,
            targetEntity    = topAggressor,
            needsValidation = false,
        });
    }
}
