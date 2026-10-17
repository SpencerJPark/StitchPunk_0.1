using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

// Awareness systems gate on ActionRequest, so this throttles ThinkRate units to a few re-thinks per
// second mid-behaviour while idle units still choose immediately. Units without ThinkRate keep ActionRequest as baked.
[BurstCompile]
[UpdateInGroup(typeof(UtilityMotivationSystemGroup), OrderFirst = true)]
public partial struct ThinkScheduleSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new ThinkScheduleJob
        {
            deltaTime = SystemAPI.Time.DeltaTime
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrain))]
[WithPresent(typeof(ActionRequest))]
[WithDisabled(typeof(Dead))]
public partial struct ThinkScheduleJob : IJobEntity
{
    public float deltaTime;

    public void Execute(Entity entity, ref ThinkRate thinkRate, in StateMachine stateMachine, EnabledRefRW<ActionRequest> actionRequestEnabled)
    {
        bool idle = stateMachine.activeBehavior == BehaviorType.None;

        if (thinkRate.interval <= 0f)
        {
            actionRequestEnabled.ValueRW = true;
            return;
        }

        bool due = false;
        if (thinkRate.timeUntilNextThink < 0f)
        {
            // Negative is the baker's uninitialised marker; golden-ratio spread staggers units across the interval.
            float fraction = (entity.Index * 0.618034f) % 1f;
            thinkRate.timeUntilNextThink = math.max(thinkRate.interval * fraction, 0.0001f);
        }
        else
        {
            thinkRate.timeUntilNextThink -= deltaTime;
            if (thinkRate.timeUntilNextThink <= 0f)
            {
                due = true;
                thinkRate.timeUntilNextThink = math.max(thinkRate.timeUntilNextThink + thinkRate.interval, 0.0001f);
            }
        }

        actionRequestEnabled.ValueRW = idle || due;
    }
}
