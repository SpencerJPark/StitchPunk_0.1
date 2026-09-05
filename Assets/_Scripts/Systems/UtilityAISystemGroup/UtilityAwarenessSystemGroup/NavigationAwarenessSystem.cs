using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(UtilityAwarenessSystemGroup))]
public partial struct NavigationAwarenessSystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<SpatialHashRegistry>();
        state.RequireForUpdate<BrainLibrary>();

        transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);

        SpatialHashRegistry registry    = SystemAPI.GetSingleton<SpatialHashRegistry>();
        BrainLibrary        brainLibrary = SystemAPI.GetSingleton<BrainLibrary>();

        state.Dependency = new NavigationAwarenessJob
        {
            waypointCells   = registry.waypointCells,
            transformLookup = transformLookup,
            aiConfig        = brainLibrary.blob,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrain), typeof(ActionRequest))]
[WithDisabled(typeof(CutsceneActor))]
public partial struct NavigationAwarenessJob : IJobEntity
{
    [ReadOnly] public NativeParallelMultiHashMap<int2, Entity> waypointCells;
    [ReadOnly] public ComponentLookup<LocalTransform>          transformLookup;
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob>     aiConfig;

    public void Execute(
        in UtilityBrain                      brain,
        in LocalTransform                    transform,
        in Awareness                         awareness,
        in DynamicBuffer<RecentWaypoint>     recentWaypoints,
        ref DynamicBuffer<UtilityActions>    options)
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

                    int defIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, ActionType.Wander);
                    if (defIndex < 0) continue;

                    options.Add(new UtilityActions
                    {
                        actionType      = ActionType.Wander,
                        actionDefIndex  = defIndex,
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
