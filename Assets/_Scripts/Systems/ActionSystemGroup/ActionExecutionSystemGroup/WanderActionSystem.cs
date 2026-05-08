using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Executes the Wander action. Picks a random point within awareness range,
// paths there, idles briefly, then returns to ActionRequest.
//
// States (ActionTimer enabled state as flag):
//   Pathing  (WanderAction enabled, ActionTimer disabled) — path to random point
//   Idling   (WanderAction enabled, ActionTimer enabled)  — wait out ActionTimer
//   Complete — disable WanderAction, re-enable ActionRequest
[BurstCompile]
[UpdateInGroup(typeof(ActionExecutionSystemGroup))]
public partial struct WanderActionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float time      = (float)SystemAPI.Time.ElapsedTime;
        float deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new WanderJob
        {
            time      = time,
            deltaTime = deltaTime,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(AIBrain), typeof(WanderAction))]
[WithDisabled(typeof(ActionRequest), typeof(ActionInterruptRequest))]
[WithPresent(typeof(PathRequest), typeof(ActionTimer))]
public partial struct WanderJob : IJobEntity
{
    private const float IDLE_DURATION = 2.0f;
    private const float ARRIVE_RANGE  = 1.0f;
    private const float STOPPING_DIST = 0.5f;

    public float time;
    public float deltaTime;

    public void Execute(
        [EntityIndexInQuery] int entityIndex,
        in LocalTransform          transform,
        in Awareness               awareness,
        ref PathRequest            pathRequest,
        ref ActionTimer            actionTimer,
        EnabledRefRW<WanderAction>  wanderActionEnabled,
        EnabledRefRW<ActionRequest> actionRequestEnabled,
        EnabledRefRW<ActionTimer>   actionTimerEnabled,
        EnabledRefRW<PathRequest>   pathRequestEnabled)
    {
        if (actionTimerEnabled.ValueRO)
        {
            // Idling — count down the idle timer (ticked by ActionTimerSystem)
            if (actionTimer.time <= 0f)
            {
                actionTimerEnabled.ValueRW   = false;
                wanderActionEnabled.ValueRW  = false;
                actionRequestEnabled.ValueRW = true;
            }
            return;
        }

        // Arrival check
        if (math.distancesq(transform.Position, pathRequest.targetPosition) <= ARRIVE_RANGE * ARRIVE_RANGE)
        {
            AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);
            actionTimerEnabled.ValueRW = true;
            actionTimer.time           = IDLE_DURATION;
            return;
        }

        // First frame: no path issued yet
        if (!pathRequestEnabled.ValueRO)
        {
            Unity.Mathematics.Random random = new Unity.Mathematics.Random(
                (uint)(entityIndex + 1) * (uint)(time * 1000f + 1f));

            float  angle        = random.NextFloat(0f, math.PI * 2f);
            float  dist         = random.NextFloat(awareness.range * 0.3f, awareness.range);
            float3 wanderTarget = transform.Position + new float3(
                math.cos(angle) * dist,
                0f,
                math.sin(angle) * dist);

            AIUtils.BeginPathRequest(ref pathRequest, pathRequestEnabled, wanderTarget, STOPPING_DIST);
        }
    }
}
