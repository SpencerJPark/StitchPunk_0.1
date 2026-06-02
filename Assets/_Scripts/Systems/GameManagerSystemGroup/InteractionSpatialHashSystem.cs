using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(GameManagerSystemGroup))]
public partial struct InteractionSpatialHashSystem : ISystem
{
    public const float CELL_SIZE = 20f;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<InteractionLibrary>();
        state.EntityManager.CreateSingleton(new SpatialHashRegistry
        {
            waypointCells    = new NativeParallelMultiHashMap<int2, Entity>(1024, Allocator.Persistent),
            interactionCells = new NativeParallelMultiHashMap<SpatialInteractionKey, Entity>(1024, Allocator.Persistent),
            itemCells        = new NativeParallelMultiHashMap<int2, Entity>(512, Allocator.Persistent),
        });
    }

    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.TryGetSingleton<SpatialHashRegistry>(out SpatialHashRegistry singleton))
        {
            if (singleton.waypointCells.IsCreated)    singleton.waypointCells.Dispose();
            if (singleton.interactionCells.IsCreated) singleton.interactionCells.Dispose();
            if (singleton.itemCells.IsCreated)        singleton.itemCells.Dispose();
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        InteractionLibrary interactionLib = SystemAPI.GetSingleton<InteractionLibrary>();
        if (!interactionLib.library.IsCreated) return;

        RefRW<SpatialHashRegistry> registryRW = SystemAPI.GetSingletonRW<SpatialHashRegistry>();
        registryRW.ValueRW.waypointCells.Clear();
        registryRW.ValueRW.interactionCells.Clear();
        registryRW.ValueRW.itemCells.Clear();

        JobHandle interactionsHandle = new RegisterInteractionsJob
        {
            interactionLibrary = interactionLib.library,
            interactionWriter  = registryRW.ValueRW.interactionCells.AsParallelWriter(),
        }.ScheduleParallel(state.Dependency);

        JobHandle itemsHandle = new RegisterItemsJob
        {
            itemWriter = registryRW.ValueRW.itemCells.AsParallelWriter(),
        }.ScheduleParallel(state.Dependency);

        state.Dependency = JobHandle.CombineDependencies(interactionsHandle, itemsHandle);

        // Complete before leaving so awareness systems read a fully populated registry.
        state.Dependency.Complete();
    }

    public static int2 GetCell(float3 position) => new int2(
        (int)math.floor(position.x / CELL_SIZE),
        (int)math.floor(position.z / CELL_SIZE));
}

[BurstCompile]
[WithAll(typeof(Item), typeof(LocalTransform), typeof(EquipBy))]
public partial struct RegisterItemsJob : IJobEntity
{
    public NativeParallelMultiHashMap<int2, Entity>.ParallelWriter itemWriter;

    public void Execute(Entity entity, in LocalTransform transform, in EquipBy equip)
    {
        if (equip.owner != Entity.Null) return;
        itemWriter.Add(InteractionSpatialHashSystem.GetCell(transform.Position), entity);
    }
}

[BurstCompile]
[WithPresent(typeof(Interaction))]
public partial struct RegisterInteractionsJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<InteractionLibraryBlob> interactionLibrary;
    public NativeParallelMultiHashMap<SpatialInteractionKey, Entity>.ParallelWriter interactionWriter;

    public void Execute(Entity entity, in LocalTransform transform, in Interaction interaction)
    {
        int2 cell = InteractionSpatialHashSystem.GetCell(transform.Position);

        ref InteractionBlob blob = ref interactionLibrary.Value.interactions[(int)interaction.action];
        if (blob.satisfiedNeed != NeedType.None)
            interactionWriter.Add(new SpatialInteractionKey(cell, blob.satisfiedNeed), entity);
    }
}
