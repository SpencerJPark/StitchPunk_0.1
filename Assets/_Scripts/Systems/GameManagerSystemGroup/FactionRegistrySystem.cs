using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// to be deleted

/// <summary>
/// Rebuilds the FactionRegistry singleton every frame.
/// Mirrors SpatialHashSystem but for body entities keyed by FactionType.
///
/// Runs in UtilityAwarenessSystemGroup so the registry is ready before
/// AIScoringSystemGroup and AIExecutionSystemGroup run their targeting queries.
///
/// Only alive entities (Dead disabled or no Dead component) are indexed.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(GameManagerSystemGroup))]
public partial struct FactionRegistrySystem : ISystem
{
    private ComponentLookup<Dead> deadLookup;

    public void OnCreate(ref SystemState state)
    {
        state.EntityManager.CreateSingleton(new FactionRegistry
        {
            entities = new NativeParallelMultiHashMap<byte, Entity>(256, Allocator.Persistent)
        });

        deadLookup = state.GetComponentLookup<Dead>(true);

        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<Faction>();
    }

    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.TryGetSingleton<FactionRegistry>(out FactionRegistry registry))
        {
            if (registry.entities.IsCreated)
                registry.entities.Dispose();
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        deadLookup.Update(ref state);

        RefRW<FactionRegistry> registry = SystemAPI.GetSingletonRW<FactionRegistry>();
        registry.ValueRW.entities.Clear();

        foreach ((RefRO<Faction> faction, Entity entity) in
            SystemAPI.Query<RefRO<Faction>>().WithEntityAccess())
        {
            // Skip entities that are dead (if they have the Dead component and it is enabled)
            if (deadLookup.HasComponent(entity) && deadLookup.IsComponentEnabled(entity))
                continue;

            registry.ValueRW.entities.Add((byte)faction.ValueRO.factionType, entity);
        }
    }
}
