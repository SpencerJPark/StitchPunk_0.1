using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AISystemGroup))]
[UpdateBefore(typeof(AIAwarenessSystemGroup))]
public partial struct SpatialHashSystem : ISystem
{
    public const float CELL_SIZE = 10f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SpatialHashSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Get or create singleton
        if (!SystemAPI.TryGetSingletonRW<SpatialHashSingleton>(out var singleton))
            return;

        // Clear previous frame
        singleton.ValueRW.npcCells.Clear();
        singleton.ValueRW.waypointCells.Clear();

        // Hash NPCs
        state.Dependency = new HashNPCsJob
        {
            cellSize = CELL_SIZE,
            npcCells = singleton.ValueRW.npcCells.AsParallelWriter()
        }.ScheduleParallel(state.Dependency);

        // Hash Waypoints
        state.Dependency = new HashWaypointsJob
        {
            cellSize = CELL_SIZE,
            waypointCells = singleton.ValueRW.waypointCells.AsParallelWriter()
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct HashNPCsJob : IJobEntity
{
    public float cellSize;
    public NativeParallelMultiHashMap<int2, Entity>.ParallelWriter npcCells;

    public void Execute(in LocalTransform transform, in BodyBrain bodyBrain, Entity entity)
    {
        int2 cell = GetCell(transform.Position, cellSize);
        npcCells.Add(cell, bodyBrain.brain);
    }

    private int2 GetCell(float3 pos, float size)
    {
        return new int2(
            (int)math.floor(pos.x / size),
            (int)math.floor(pos.z / size)
        );
    }
}

[BurstCompile]
public partial struct HashWaypointsJob : IJobEntity
{
    public float cellSize;
    public NativeParallelMultiHashMap<int2, Entity>.ParallelWriter waypointCells;

    public void Execute(in LocalTransform transform, in Waypoint waypoint, Entity entity)
    {
        int2 cell = GetCell(transform.Position, cellSize);
        waypointCells.Add(cell, entity);
    }

    private int2 GetCell(float3 pos, float size)
    {
        return new int2(
            (int)math.floor(pos.x / size),
            (int)math.floor(pos.z / size)
        );
    }
}

public struct SpatialHashSingleton : IComponentData
{
    public NativeParallelMultiHashMap<int2, Entity> npcCells;
    public NativeParallelMultiHashMap<int2, Entity> waypointCells;
}