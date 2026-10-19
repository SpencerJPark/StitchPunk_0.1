using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

// Awareness systems gate on ActionRequest, so this throttles ThinkRate units: busy units think every interval, idle units
// on the frame they become idle and then every IDLE_THINK_INTERVAL. interval <= 0 thinks every frame.
[BurstCompile]
[UpdateInGroup(typeof(UtilityMotivationSystemGroup), OrderFirst = true)]
public partial struct ThinkScheduleSystem : ISystem
{
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
            thinkRate.wasIdle = idle;
            actionRequestEnabled.ValueRW = true;
            return;
        }

        if (thinkRate.timeUntilNextThink < 0f)
        {
            // Negative is the baker's uninitialised marker; golden-ratio spread staggers units across the interval.
            float fraction = (entity.Index * 0.618034f) % 1f;
            thinkRate.timeUntilNextThink = math.max(thinkRate.interval * fraction, 0.0001f);
        }
        else
        {
            thinkRate.timeUntilNextThink -= deltaTime;
        }

        bool becameIdle = idle && !thinkRate.wasIdle;
        thinkRate.wasIdle = idle;
        float effectiveInterval = idle ? math.min(thinkRate.interval, ThinkRate.IDLE_THINK_INTERVAL) : thinkRate.interval;
        bool timerDue = thinkRate.timeUntilNextThink <= 0f;

        if (timerDue)
            thinkRate.timeUntilNextThink = math.max(thinkRate.timeUntilNextThink + effectiveInterval, 0.0001f);
        else if (becameIdle)
            thinkRate.timeUntilNextThink = effectiveInterval;

        if (idle)
            thinkRate.timeUntilNextThink = math.min(thinkRate.timeUntilNextThink, effectiveInterval);

        actionRequestEnabled.ValueRW = becameIdle || timerDue;
    }
}
