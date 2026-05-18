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
        ref Health health,
        in CurrentAction currentAction,
        ref DynamicBuffer<ActionOption> options,
        EnabledRefRW<ActionInterruptRequest> interruptRequest)
    {
        if (currentAction.actionType == ActionType.Flee)
            return;

        if ((float)health.healthAmount / health.healthAmountMax < 0.3f)
        {
            options.Add(new ActionOption
            {
                actionType     = ActionType.Flee,
                motivationType = MotivationType.SelfPreservation,
                priority       = 3,
                utilityScore   = 1f,
            });
            interruptRequest.ValueRW = true;
            return;
        }
        if ((float)health.healthAmount / health.healthAmountMax < 0.6f)
        {
            options.Add(new ActionOption
            {
                actionType     = ActionType.Flee,
                motivationType = MotivationType.SelfPreservation,
                priority       = 2,
                utilityScore   = 0.6f,
            });
            interruptRequest.ValueRW = true;
        }
    }
}