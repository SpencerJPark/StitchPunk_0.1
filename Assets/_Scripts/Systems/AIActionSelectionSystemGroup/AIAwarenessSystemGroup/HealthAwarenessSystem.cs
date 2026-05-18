using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
[UpdateAfter(typeof(SelfDefenceAwarenessSystem))]
public partial struct HealthAwarenessSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<Health>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new HealthAwarenessJob().ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(AIBrain))]
[WithDisabled(typeof(Dead))]
[WithPresent(typeof(FleeAction), typeof(ActionInterruptRequest))]
public partial struct HealthAwarenessJob : IJobEntity
{
    public void Execute(
        in  Health                           health,
        in  CurrentAction                    currentAction,
        in  Personality                      personality,
        ref DynamicBuffer<ActionOption>      options,
        EnabledRefRW<ActionInterruptRequest> interruptRequest)
    {
        if (currentAction.actionType == ActionType.Flee)
            return;

        float healthRatio = (float)health.healthAmount / health.healthAmountMax;

        if (healthRatio < 0.3f)
        {
            float fleeUtility = (1f - healthRatio) * (1f - personality.bravery);
            options.Add(new ActionOption
            {
                actionType     = ActionType.Flee,
                motivationType = MotivationType.SelfPreservation,
                priority       = 3,
                utilityScore   = fleeUtility,
            });
            interruptRequest.ValueRW = true;
            return;
        }
        if (healthRatio < 0.6f)
        {
            float fleeUtility = (0.6f - healthRatio) * (1f - personality.bravery);
            options.Add(new ActionOption
            {
                actionType     = ActionType.Flee,
                motivationType = MotivationType.SelfPreservation,
                priority       = 2,
                utilityScore   = fleeUtility,
            });
            interruptRequest.ValueRW = true;
        }
    }
}
