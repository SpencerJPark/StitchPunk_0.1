using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct SpatialHashSystem : ISystem
{
    public const float CELL_SIZE = 20f;

    private ComponentLookup<BladderInteraction> bladderLookup;
    private ComponentLookup<HungerInteraction> hungerLookup;
    private ComponentLookup<EnergyInteraction> energyLookup;
    private ComponentLookup<FunInteraction> funLookup;
    private ComponentLookup<SocialInteraction> socialLookup;
    private ComponentLookup<ComfortInteraction> comfortLookup;
    private ComponentLookup<SafetyInteraction> safetyLookup;
    private ComponentLookup<MovementInteraction> movementLookup;

    public void OnCreate(ref SystemState state)
    {
        state.EntityManager.CreateSingleton(new SpatialHashRegistry
        {
            waypointCells = new NativeParallelMultiHashMap<int2, Entity>(1024, Allocator.Persistent),
            interactionCells = new NativeParallelMultiHashMap<SpatialInteractionKey, Entity>(1024, Allocator.Persistent)
        });

        bladderLookup = state.GetComponentLookup<BladderInteraction>(true);
        hungerLookup = state.GetComponentLookup<HungerInteraction>(true);
        energyLookup = state.GetComponentLookup<EnergyInteraction>(true);
        funLookup = state.GetComponentLookup<FunInteraction>(true);
        socialLookup = state.GetComponentLookup<SocialInteraction>(true);
        comfortLookup = state.GetComponentLookup<ComfortInteraction>(true);
        safetyLookup = state.GetComponentLookup<SafetyInteraction>(true);
        movementLookup = state.GetComponentLookup<MovementInteraction>(true);
    }

    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.TryGetSingleton<SpatialHashRegistry>(out var singleton))
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
        bladderLookup.Update(ref state);
        hungerLookup.Update(ref state);
        energyLookup.Update(ref state);
        funLookup.Update(ref state);
        socialLookup.Update(ref state);
        comfortLookup.Update(ref state);
        safetyLookup.Update(ref state);
        movementLookup.Update(ref state);

        var singleton = SystemAPI.GetSingletonRW<SpatialHashRegistry>();

        singleton.ValueRW.waypointCells.Clear();
        singleton.ValueRW.interactionCells.Clear();

        foreach (var (transform, provider, entity) in
            SystemAPI.Query<RefRO<LocalTransform>, RefRO<InteractionProvider>>()
                .WithEntityAccess())
        {
            int2 cell = GetCell(transform.ValueRO.Position);

            // Add to general waypoint hash (for backwards compatibility or general queries)
            singleton.ValueRW.waypointCells.Add(cell, entity);

            // Add to interaction-specific hash for each motivation type this interaction provides
            if (bladderLookup.HasComponent(entity))
                singleton.ValueRW.interactionCells.Add(new SpatialInteractionKey(cell, MotivationType.Bladder), entity);

            if (hungerLookup.HasComponent(entity))
                singleton.ValueRW.interactionCells.Add(new SpatialInteractionKey(cell, MotivationType.Hunger), entity);

            if (energyLookup.HasComponent(entity))
                singleton.ValueRW.interactionCells.Add(new SpatialInteractionKey(cell, MotivationType.Energy), entity);

            if (funLookup.HasComponent(entity))
                singleton.ValueRW.interactionCells.Add(new SpatialInteractionKey(cell, MotivationType.Fun), entity);

            if (socialLookup.HasComponent(entity))
                singleton.ValueRW.interactionCells.Add(new SpatialInteractionKey(cell, MotivationType.Social), entity);

            if (comfortLookup.HasComponent(entity))
                singleton.ValueRW.interactionCells.Add(new SpatialInteractionKey(cell, MotivationType.Comfort), entity);

            if (safetyLookup.HasComponent(entity))
                singleton.ValueRW.interactionCells.Add(new SpatialInteractionKey(cell, MotivationType.Safety), entity);

            if (movementLookup.HasComponent(entity))
                singleton.ValueRW.interactionCells.Add(new SpatialInteractionKey(cell, MotivationType.Movement), entity);
        }
    }

    public static int2 GetCell(float3 position)
    {
        return new int2(
            (int)math.floor(position.x / CELL_SIZE),
            (int)math.floor(position.z / CELL_SIZE));
    }
}