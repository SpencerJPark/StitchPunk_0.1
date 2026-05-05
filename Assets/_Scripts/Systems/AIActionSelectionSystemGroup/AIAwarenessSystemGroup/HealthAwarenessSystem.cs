using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
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

// Applies healing to entities with Heal enabled, clamps to max, then disables the component
[BurstCompile]
[WithAll(typeof(AIBrain), typeof(ActionRequest))]
[WithDisabled(typeof(Dead))]
public partial struct HealthAwarenessJob : IJobEntity
{
    public void Execute(ref Health health, ref DynamicBuffer<ActionOption> options)
    {
        if (health.healthAmount / health.healthAmountMax < 0.3f)
        {
            options.Add(new ActionOption
            {
                actionType = ActionType.Flee,
                motivationType = MotivationType.SelfPreservation,
                priority = 3,
                utilityScore = 1f,
            });
            return;
        }
        if (health.healthAmount / health.healthAmountMax < 0.6f)
        {
            options.Add(new ActionOption
            {
                actionType = ActionType.Flee,
                motivationType = MotivationType.SelfPreservation,
                priority = 2,
                utilityScore = 0.6f,
            });
            return;
        }
        if (health.healthAmount / health.healthAmountMax < 0.9f)
        {
            options.Add(new ActionOption
            {
                actionType = ActionType.Flee,
                motivationType = MotivationType.SelfPreservation,
                priority = 2,
                utilityScore = 0.4f,
            });
            return;
        }
    }
}