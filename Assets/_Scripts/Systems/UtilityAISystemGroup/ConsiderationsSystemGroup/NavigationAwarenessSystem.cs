using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AIAwarenessSystemGroup))]
public partial struct NavigationAwarenessSystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<SpatialHashRegistry>();

        transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);

        SpatialHashRegistry registry = SystemAPI.GetSingleton<SpatialHashRegistry>();

        state.Dependency = new NavigationAwarenessJob
        {
            waypointCells   = registry.waypointCells,
            transformLookup = transformLookup,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(AIBrain), typeof(ActionRequest))]
[WithPresent(typeof(WanderAction))]
public partial struct NavigationAwarenessJob : IJobEntity
{
    [ReadOnly] public NativeParallelMultiHashMap<int2, Entity> waypointCells;
    [ReadOnly] public ComponentLookup<LocalTransform>          transformLookup;

    public void Execute(
        in LocalTransform                    transform,
        in Awareness                         awareness,
        in DynamicBuffer<RecentWaypoint>     recentWaypoints,
        ref DynamicBuffer<ActionOption>      options)
    {
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

                    float utilityScore = 1f - math.saturate(dist / awareness.range);

                    options.Add(new ActionOption
                    {
                        actionType      = ActionType.Wander,
                        needType        = NeedType.Movement,
                        priority        = 0,
                        utilityScore    = utilityScore,
                        needsValidation = false,
                        targetEntity    = waypoint,
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
