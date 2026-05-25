using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Executes the Wander action. On the first frame (fresh activation from action selection), paths
// to the action-selected waypoint (random point within its radius) or a random fallback position.
// On arrival, records the waypoint in RecentWaypoint and immediately exits back to action selection
// with no idle delay — the next decision starts the same frame the unit arrives.
[BurstCompile]
[UpdateInGroup(typeof(ActionExecutionSystemGroup))]
public partial struct WanderActionSystem : ISystem
{
    private ComponentLookup<LocalTransform>     transformLookup;
    private ComponentLookup<NavigationWaypoint> waypointLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        waypointLookup  = state.GetComponentLookup<NavigationWaypoint>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        waypointLookup.Update(ref state);

        state.Dependency = new WanderJob
        {
            time            = (float)SystemAPI.Time.ElapsedTime,
            transformLookup = transformLookup,
            waypointLookup  = waypointLookup,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(AIBrain), typeof(WanderAction))]
[WithDisabled(typeof(ActionRequest), typeof(ActionInterruptRequest))]
[WithPresent(typeof(PathRequest), typeof(DStarLiteFollower))]
public partial struct WanderJob : IJobEntity
{
    private const float ARRIVE_RANGE  = 1.0f;
    private const float STOPPING_DIST = 0.5f;

    public float time;

    [ReadOnly] public ComponentLookup<LocalTransform>     transformLookup;
    [ReadOnly] public ComponentLookup<NavigationWaypoint> waypointLookup;

    public void Execute(
        [EntityIndexInQuery] int          entityIndex,
        in LocalTransform                 transform,
        in Awareness                      awareness,
        in CurrentAction                  currentAction,
        ref PathRequest                   pathRequest,
        ref DynamicBuffer<RecentWaypoint> recentWaypoints,
        EnabledRefRW<WanderAction>        wanderActionEnabled,
        EnabledRefRW<ActionRequest>       actionRequestEnabled,
        EnabledRefRW<PathRequest>         pathRequestEnabled,
        EnabledRefRO<DStarLiteFollower>   dstarFollowerEnabled)
    {
        // Arrival: record waypoint and return to action selection immediately (no idle)
        if (math.distancesq(transform.Position, pathRequest.targetPosition) <= ARRIVE_RANGE * ARRIVE_RANGE)
        {
            if (currentAction.targetEntity != Entity.Null)
                PushRecent(ref recentWaypoints, currentAction.targetEntity);

            AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);
            wanderActionEnabled.ValueRW  = false;
            actionRequestEnabled.ValueRW = true;
            return;
        }

        // Fresh activation: PathRequest not in flight and no active navigation follower
        if (!pathRequestEnabled.ValueRO && !dstarFollowerEnabled.ValueRO)
        {
            float3 target;
            if (currentAction.targetEntity != Entity.Null
                && transformLookup.HasComponent(currentAction.targetEntity))
            {
                float3 waypointPos = transformLookup[currentAction.targetEntity].Position;
                float  radius      = waypointLookup.HasComponent(currentAction.targetEntity)
                    ? waypointLookup[currentAction.targetEntity].radius
                    : 0f;

                if (radius > 0f)
                {
                    Unity.Mathematics.Random rng = new Unity.Mathematics.Random(
                        (uint)(entityIndex + 1) * (uint)(time * 1000f + 1f));
                    float angle  = rng.NextFloat(0f, math.PI * 2f);
                    float offset = rng.NextFloat(0f, radius);
                    target = waypointPos + new float3(math.cos(angle) * offset, 0f, math.sin(angle) * offset);
                }
                else
                {
                    target = waypointPos;
                }
            }
            else
            {
                Unity.Mathematics.Random rng = new Unity.Mathematics.Random(
                    (uint)(entityIndex + 1) * (uint)(time * 1000f + 1f));
                float angle = rng.NextFloat(0f, math.PI * 2f);
                float dist  = rng.NextFloat(awareness.range * 0.3f, awareness.range);
                target = transform.Position + new float3(
                    math.cos(angle) * dist,
                    0f,
                    math.sin(angle) * dist);
            }

            AIUtils.BeginPathRequest(ref pathRequest, pathRequestEnabled, target, STOPPING_DIST);
        }
    }

    private static void PushRecent(ref DynamicBuffer<RecentWaypoint> buf, Entity entity)
    {
        if (buf.Length >= 4) buf.RemoveAt(0);
        buf.Add(new RecentWaypoint { entity = entity });
    }
}
