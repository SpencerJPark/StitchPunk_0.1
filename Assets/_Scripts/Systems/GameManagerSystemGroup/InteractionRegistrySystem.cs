using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(GameManagerSystemGroup))]
public partial struct SpatialHashSystem : ISystem
{
    public const float CELL_SIZE = 20f;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.EntityManager.CreateSingleton(new SpatialHashRegistry
        {
            waypointCells    = new NativeParallelMultiHashMap<int2, Entity>(1024, Allocator.Persistent),
            interactionCells = new NativeParallelMultiHashMap<SpatialInteractionKey, Entity>(1024, Allocator.Persistent)
        });
    }

    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.TryGetSingleton<SpatialHashRegistry>(out SpatialHashRegistry singleton))
        {
            if (singleton.waypointCells.IsCreated)
                singleton.waypointCells.Dispose();
            if (singleton.interactionCells.IsCreated)
                singleton.interactionCells.Dispose();
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        RefRW<SpatialHashRegistry> singleton = SystemAPI.GetSingletonRW<SpatialHashRegistry>();

        singleton.ValueRW.waypointCells.Clear();
        singleton.ValueRW.interactionCells.Clear();

        // Register every active interaction provider under each motivation type it satisfies.
        // The key (cell, MotivationType) lets EnvironmentalAwarenessSystem query by NPC need.
        foreach ((RefRO<LocalTransform> transform,
                  DynamicBuffer<MotivationSatisfaction> satisfaction,
                  Entity entity) in
            SystemAPI.Query<RefRO<LocalTransform>, DynamicBuffer<MotivationSatisfaction>>()
                .WithAll<InteractionProvider>()
                .WithEntityAccess())
        {
            int2 cell = GetCell(transform.ValueRO.Position);

            singleton.ValueRW.waypointCells.Add(cell, entity);

            for (int i = 0; i < satisfaction.Length; i++)
            {
                MotivationSatisfaction entry = satisfaction[i];
                if (entry.motivationType == MotivationType.None)
                    continue;

                singleton.ValueRW.interactionCells.Add(
                    new SpatialInteractionKey(cell, entry.motivationType),
                    entity);
            }
        }
    }

    public static int2 GetCell(float3 position)
    {
        return new int2(
            (int)math.floor(position.x / CELL_SIZE),
            (int)math.floor(position.z / CELL_SIZE));
    }
}
