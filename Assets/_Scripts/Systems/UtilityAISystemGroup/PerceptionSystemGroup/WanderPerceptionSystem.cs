using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// v2 perception for Wander: mirrors NavigationAwarenessSystem but writes AwarenessTarget instead
// of ActionOption, and filters on UtilityBrainV2 so legacy AIBrain units are untouched.
[BurstCompile]
[UpdateInGroup(typeof(PerceptionSystemGroup))]
public partial struct WanderPerceptionSystem : ISystem
{
    private ComponentLookup<LocalTransform> _transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<SpatialHashRegistry>();
        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _transformLookup.Update(ref state);
        SpatialHashRegistry registry = SystemAPI.GetSingleton<SpatialHashRegistry>();

        state.Dependency = new WanderPerceptionJob
        {
            waypointCells   = registry.waypointCells,
            transformLookup = _transformLookup,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrainV2))]
public partial struct WanderPerceptionJob : IJobEntity
{
    [ReadOnly] public NativeParallelMultiHashMap<int2, Entity> waypointCells;
    [ReadOnly] public ComponentLookup<LocalTransform>          transformLookup;

    public void Execute(
        in LocalTransform                  transform,
        in Awareness                       awareness,
        ref DynamicBuffer<RecentWaypoint>  recentWaypoints,
        ref DynamicBuffer<AwarenessTarget> awarenessTargets)
    {
        awarenessTargets.Clear();

        float3 unitPos    = transform.Position;
        int2   centerCell = InteractionSpatialHashSystem.GetCell(unitPos);
        int    cellRange  = (int)math.ceil(awareness.range / InteractionSpatialHashSystem.CELL_SIZE);

        for (int x = -cellRange; x <= cellRange; x++)
        {
            for (int z = -cellRange; z <= cellRange; z++)
            {
                int2 targetCell = centerCell + new int2(x, z);
                bool hit = waypointCells.TryGetFirstValue(
                    targetCell, out Entity waypoint,
                    out NativeParallelMultiHashMapIterator<int2> it);

                if (!hit) continue;

                do
                {
                    if (IsRecent(waypoint, recentWaypoints)) continue;

                    if (!transformLookup.TryGetComponent(waypoint, out LocalTransform waypointTransform))
                        continue;

                    float dist = math.distance(unitPos, waypointTransform.Position);
                    if (dist > awareness.range) continue;

                    awarenessTargets.Add(new AwarenessTarget
                    {
                        targetEntity     = waypoint,
                        distanceToTarget = dist,
                        inLineOfSight    = true,
                    });
                }
                while (waypointCells.TryGetNextValue(out waypoint, ref it));
            }
        }

        // Fallback: all nearby waypoints were recently visited. Reset recent memory and re-scan
        // so the unit never stalls waiting for waypoints that will never become available.
        if (awarenessTargets.Length > 0) return;

        recentWaypoints.Clear();

        for (int x = -cellRange; x <= cellRange; x++)
        {
            for (int z = -cellRange; z <= cellRange; z++)
            {
                int2 targetCell = centerCell + new int2(x, z);
                bool hit = waypointCells.TryGetFirstValue(
                    targetCell, out Entity waypoint,
                    out NativeParallelMultiHashMapIterator<int2> it);

                if (!hit) continue;

                do
                {
                    if (!transformLookup.TryGetComponent(waypoint, out LocalTransform waypointTransform))
                        continue;

                    float dist = math.distance(unitPos, waypointTransform.Position);
                    if (dist > awareness.range) continue;

                    awarenessTargets.Add(new AwarenessTarget
                    {
                        targetEntity     = waypoint,
                        distanceToTarget = dist,
                        inLineOfSight    = true,
                    });
                }
                while (waypointCells.TryGetNextValue(out waypoint, ref it));
            }
        }
    }

    private static bool IsRecent(Entity entity, in DynamicBuffer<RecentWaypoint> recent)
    {
        for (int i = 0; i < recent.Length; i++)
            if (recent[i].entity == entity) return true;
        return false;
    }
}
