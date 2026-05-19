using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Executes the Wander action. If CurrentAction.targetEntity is set (a NavigationWaypoint),
// picks a random point within that waypoint's radius and paths there; otherwise falls back
// to a random point within awareness range. On arrival at a waypoint, records it in
// RecentWaypoint (last 2) so the awareness system won't offer it again next cycle.
//
// States (ActionTimer enabled state as flag):
//   Pathing  (WanderAction enabled, ActionTimer disabled) — path to destination
//   Idling   (WanderAction enabled, ActionTimer enabled)  — wait out ActionTimer
//   Complete — disable WanderAction, re-enable ActionRequest
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
[WithPresent(typeof(PathRequest), typeof(ActionTimer))]
public partial struct WanderJob : IJobEntity
{
    private const float IDLE_DURATION = 2.0f;
    private const float ARRIVE_RANGE  = 1.0f;
    private const float STOPPING_DIST = 0.5f;

    public float time;

    [ReadOnly] public ComponentLookup<LocalTransform>     transformLookup;
    [ReadOnly] public ComponentLookup<NavigationWaypoint> waypointLookup;

    public void Execute(
        [EntityIndexInQuery] int                  entityIndex,
        in LocalTransform                         transform,
        in Awareness                              awareness,
        in CurrentAction                          currentAction,
        ref PathRequest                           pathRequest,
        ref ActionTimer                           actionTimer,
        ref DynamicBuffer<RecentWaypoint>         recentWaypoints,
        EnabledRefRW<WanderAction>                wanderActionEnabled,
        EnabledRefRW<ActionRequest>               actionRequestEnabled,
        EnabledRefRW<ActionTimer>                 actionTimerEnabled,
        EnabledRefRW<PathRequest>                 pathRequestEnabled)
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
            if (currentAction.targetEntity != Entity.Null)
                PushRecent(ref recentWaypoints, currentAction.targetEntity);

            AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);
            actionTimerEnabled.ValueRW = true;
            actionTimer.time           = IDLE_DURATION;
            return;
        }

        // First frame: no path issued yet
        if (!pathRequestEnabled.ValueRO)
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
        if (buf.Length >= 2) buf.RemoveAt(0);
        buf.Add(new RecentWaypoint { entity = entity });
    }
}
