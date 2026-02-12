using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// -------------------------------------------------------
// SPATIAL HASH INIT (runs once)
// -------------------------------------------------------

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct SpatialHashInitSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        Entity entity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(entity, new SpatialHashSingleton
        {
            npcCells = new NativeParallelMultiHashMap<int2, Entity>(1000, Allocator.Persistent),
            waypointCells = new NativeParallelMultiHashMap<int2, Entity>(500, Allocator.Persistent)
        });
    }

    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.TryGetSingletonRW<SpatialHashSingleton>(out RefRW<SpatialHashSingleton> singleton))
        {
            singleton.ValueRW.npcCells.Dispose();
            singleton.ValueRW.waypointCells.Dispose();
        }
    }

    public void OnUpdate(ref SystemState state)
    {
        state.Enabled = false;
    }
}

// -------------------------------------------------------
// SPATIAL HASH REBUILD (runs every frame before awareness)
// -------------------------------------------------------

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
        if (!SystemAPI.TryGetSingletonRW<SpatialHashSingleton>(out RefRW<SpatialHashSingleton> singleton))
            return;

        singleton.ValueRW.npcCells.Clear();
        singleton.ValueRW.waypointCells.Clear();

        state.Dependency = new HashNPCsJob
        {
            cellSize = CELL_SIZE,
            npcCells = singleton.ValueRW.npcCells.AsParallelWriter()
        }.ScheduleParallel(state.Dependency);

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
        int2 cell = new int2(
            (int)math.floor(transform.Position.x / cellSize),
            (int)math.floor(transform.Position.z / cellSize)
        );
        npcCells.Add(cell, bodyBrain.brain);
    }
}

[BurstCompile]
public partial struct HashWaypointsJob : IJobEntity
{
    public float cellSize;
    public NativeParallelMultiHashMap<int2, Entity>.ParallelWriter waypointCells;

    public void Execute(in LocalTransform transform, in InteractionProvider interactionProvider, Entity entity)
    {
        int2 cell = new int2(
            (int)math.floor(transform.Position.x / cellSize),
            (int)math.floor(transform.Position.z / cellSize)
        );
        waypointCells.Add(cell, entity);
    }
}