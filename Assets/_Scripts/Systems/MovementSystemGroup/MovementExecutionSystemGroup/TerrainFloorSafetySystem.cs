using DotsAnimationToolkit;
using DotsMovementToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// A physics ray can miss the ground; the terrain height data cannot.
// Only corrects a sink deeper than SINK_TOLERANCE_METRES, so normal gravity and resting height are untouched.
[BurstCompile]
[UpdateInGroup(typeof(MovementExecutionSystemGroup))]
[UpdateAfter(typeof(CaravanDriveSystem))]
public partial struct TerrainFloorSafetySystem : ISystem
{
    private EntityQuery loadedTileHeightFieldQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        loadedTileHeightFieldQuery = SystemAPI.QueryBuilder().WithAll<TerrainTileHeightField>().Build();
        state.RequireForUpdate<TerrainTileHeightField>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        NativeArray<TerrainTileHeightField> loadedTiles =
            loadedTileHeightFieldQuery.ToComponentDataArray<TerrainTileHeightField>(state.WorldUpdateAllocator);

        CaravanFloorSafetyJob caravanJob = new CaravanFloorSafetyJob { loadedTiles = loadedTiles };
        state.Dependency = caravanJob.ScheduleParallel(state.Dependency);

        UnitFloorSafetyJob unitJob = new UnitFloorSafetyJob { loadedTiles = loadedTiles };
        state.Dependency = unitJob.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(Caravan))]
[WithNone(typeof(Parent))]
public partial struct CaravanFloorSafetyJob : IJobEntity
{
    public const float SINK_TOLERANCE_METRES = 0.5f;

    [ReadOnly] public NativeArray<TerrainTileHeightField> loadedTiles;

    public void Execute(ref LocalTransform transform)
    {
        if (!TerrainHeightSampler.TrySampleGroundHeight(in loadedTiles, transform.Position, out float groundHeight)) return;
        if (transform.Position.y >= groundHeight - SINK_TOLERANCE_METRES) return;
        transform.Position.y = groundHeight;
    }
}

[BurstCompile]
[WithNone(typeof(Parent), typeof(Caravan))]
public partial struct UnitFloorSafetyJob : IJobEntity
{
    [ReadOnly] public NativeArray<TerrainTileHeightField> loadedTiles;

    public void Execute(ref LocalTransform transform, ref Gravity gravity)
    {
        if (!TerrainHeightSampler.TrySampleGroundHeight(in loadedTiles, transform.Position, out float groundHeight)) return;
        if (transform.Position.y >= groundHeight - CaravanFloorSafetyJob.SINK_TOLERANCE_METRES) return;
        transform.Position.y = groundHeight;
        gravity.verticalVelocity = 0f;
    }
}
