using DotsAnimationToolkit;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

// Corpse-stacking spatial hash — singleton owned by CorpseCellSystem below. Rebuilt every frame from
// settled corpses (world-services reset pattern, like DamageBus), so revive/despawn bookkeeping is
// free. Keyed by XZ cell; value is the corpse root's Y. A native container through a singleton
// bypasses ECS dependency tracking — readers must register their JobHandle via
// CorpseCellSystem.AddJobHandleForReader.
public struct CorpseCells : IComponentData
{
    public NativeParallelMultiHashMap<int2, float> map;
}

// Managed owner of the corpse spatial hash (world-services charter: spatial hashes live in
// GameManagerSystemGroup). Rebuilt from scratch every frame from SETTLED corpses (RagdollActor
// enabled + RagdollState.flags has Sleeping) — the recycled-transport pattern of DamageBusSystem —
// so revive/despawn bookkeeping is free: a corpse that stops existing simply stops being added.
//
// Position registry only. The legacy Ragdoll2D drop had no real physics, so corpses needed an
// artificial per-cell landing-height raise (corpseStackOffset/corpseStackMax) to avoid clipping into
// each other; the toolkit's ragdoll is real Unity Physics box colliders, which should stack corpses
// correctly through actual body-vs-body collision once self-collision groups are authored on the
// rig — verify this in play-test before re-adding a height hack here.
//
// A native container handed out through a singleton bypasses ECS automatic dependency tracking, so
// this system carries the reader JobHandle for any future consumer via AddJobHandleForReader, and
// the rebuild completes it before clearing (the ECB-owner pattern). Main-thread rebuild is fine —
// settled corpses cap out in the low hundreds.
[UpdateInGroup(typeof(GameManagerSystemGroup))]
public partial class CorpseCellSystem : SystemBase
{
    private const float CELL_SIZE = 2f;

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
        // Last frame's readers must finish before the map is recycled.
        readerHandle.Complete();
        readerHandle = default;

        cells.Clear();

        foreach ((RefRO<LocalTransform> transform, RefRO<RagdollState> ragdollState) in
            SystemAPI.Query<RefRO<LocalTransform>, RefRO<RagdollState>>()
                .WithAll<RagdollActor>())
        {
            if ((ragdollState.ValueRO.flags & RagdollStateFlags.Sleeping) == 0)
                continue;

            float3 position = transform.ValueRO.Position;
            int2 cell = new int2(
                (int)math.floor(position.x / CELL_SIZE),
                (int)math.floor(position.z / CELL_SIZE));
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
