using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Executes the Wander action. Picks a random point within awareness range,
/// paths there, idles briefly, then returns to NeedsAction.
///
/// States:
///   Pathing  (WanderAction enabled, ArrivedAtTarget disabled) — path to random point
///   Idling   (WanderAction enabled, ArrivedAtTarget enabled)  — wait out ActionTimer
///   Complete — disable WanderAction, re-enable NeedsAction
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup))]
public partial struct WanderExecutionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float time = (float)SystemAPI.Time.ElapsedTime;
        float deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new WanderJob
        {
            time = time,
            deltaTime = deltaTime,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActiveBrain), typeof(WanderAction))]
[WithDisabled(typeof(NeedsAction))]
public partial struct WanderJob : IJobEntity
{
    private const float IDLE_DURATION    = 2.0f;
    private const float ARRIVE_RANGE     = 1.0f;
    private const float STOPPING_DIST    = 0.5f;

    public float time;
    public float deltaTime;

    public void Execute(
        Entity entity,
        [EntityIndexInQuery] int entityIndex,
        in LocalTransform transform,
        in Awareness awareness,
        ref PathRequest pathRequest,
        ref PathfindingAgent agent,
        ref Movement movement,
        ref ActionTimer actionTimer,
        EnabledRefRW<WanderAction> wanderActionEnabled,
        EnabledRefRW<NeedsAction> needsActionEnabled,
        EnabledRefRW<ArrivedAtTarget> arrivedEnabled,
        EnabledRefRW<PathRequest> pathRequestEnabled,
        EnabledRefRW<DStarLiteFollower> dstarEnabled,
        EnabledRefRW<FlowFieldFollower> flowEnabled)
    {
        if (arrivedEnabled.ValueRO)
        {
            // Idling — count down the idle timer
            actionTimer.time -= deltaTime;
            if (actionTimer.time <= 0f)
            {
                arrivedEnabled.ValueRW = false;
                wanderActionEnabled.ValueRW = false;
                needsActionEnabled.ValueRW = true;
            }
            return;
        }

        // Check if we've arrived this frame
        if (AIUtils.IsTargetInRange(transform, ref arrivedEnabled, pathRequest.targetPosition, ARRIVE_RANGE))
        {
            AIUtils.HaltPathing(ref agent, pathRequestEnabled, dstarEnabled, flowEnabled);
            actionTimer.time = IDLE_DURATION;
            return;
        }

        // First frame: no path issued yet (targetPosition default is zero)
        if (!pathRequestEnabled.ValueRO)
        {
            Unity.Mathematics.Random random = new Unity.Mathematics.Random(
                (uint)(entityIndex + 1) * (uint)(time * 1000f + 1f));

            float angle = random.NextFloat(0f, math.PI * 2f);
            float dist  = random.NextFloat(awareness.range * 0.3f, awareness.range);
            float3 wanderTarget = transform.Position + new float3(
                math.cos(angle) * dist,
                0f,
                math.sin(angle) * dist);

            AIUtils.BeginPathRequest(
                ref pathRequest, pathRequestEnabled,
                ref agent, ref movement,
                wanderTarget, STOPPING_DIST);
        }
    }
}
