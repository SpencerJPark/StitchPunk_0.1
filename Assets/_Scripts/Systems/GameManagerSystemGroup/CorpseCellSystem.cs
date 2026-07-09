using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

// Managed owner of the corpse-stacking spatial hash (world-services charter: spatial hashes live in
// GameManagerSystemGroup). Rebuilt from scratch every frame from SETTLED corpses (Ragdoll2DLaunch
// enabled + sleeping) — the recycled-transport pattern of DamageBusSystem — so revive/despawn
// bookkeeping is free: a corpse that stops existing simply stops being added.
//
// A native container handed out through a singleton bypasses ECS automatic dependency tracking, so
// this system carries the reader JobHandle: Ragdoll2DSystem registers its landing-query read via
// AddJobHandleForReader, and the rebuild completes it before clearing (the ECB-owner pattern).
// Main-thread rebuild is fine — settled corpses cap out in the low hundreds.
[UpdateInGroup(typeof(GameManagerSystemGroup))]
public partial class CorpseCellSystem : SystemBase
{
    private NativeParallelMultiHashMap<int2, float> cells;
    private JobHandle                               readerHandle;

    // Readers of the CorpseCells map register their JobHandle here (ECB-owner pattern).
    public void AddJobHandleForReader(JobHandle readerDependency)
    {
        readerHandle = JobHandle.CombineDependencies(readerHandle, readerDependency);
    }

    protected override void OnCreate()
    {
        RequireForUpdate<GameSceneTag>();

        cells = new NativeParallelMultiHashMap<int2, float>(256, Allocator.Persistent);

        Entity singleton = EntityManager.CreateEntity();
        EntityManager.SetName(singleton, "CorpseCells");
        EntityManager.AddComponentData(singleton, new CorpseCells { map = cells });
    }

    protected override void OnUpdate()
    {
        // Last frame's ragdoll landing queries must finish before the map is recycled.
        readerHandle.Complete();
        readerHandle = default;

        cells.Clear();

        float cellSize = 1f;
        if (SystemAPI.TryGetSingleton(out RagdollSimConfig simConfig) && simConfig.corpseCellSize > 0f)
            cellSize = simConfig.corpseCellSize;

        foreach ((RefRO<LocalTransform> transform, RefRO<Ragdoll2DLaunch> launch) in
            SystemAPI.Query<RefRO<LocalTransform>, RefRO<Ragdoll2DLaunch>>()
                .WithAll<Ragdoll2DConfig>())
        {
            if (launch.ValueRO.sleeping == 0)
                continue;

            float3 position = transform.ValueRO.Position;
            int2 cell = new int2(
                (int)math.floor(position.x / cellSize),
                (int)math.floor(position.z / cellSize));
            cells.Add(cell, position.y);
        }
    }

    protected override void OnDestroy()
    {
        readerHandle.Complete();

        if (cells.IsCreated)
            cells.Dispose();
    }
}
