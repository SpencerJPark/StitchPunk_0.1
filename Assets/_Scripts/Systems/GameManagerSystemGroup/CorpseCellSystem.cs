using DotsAnimationToolkit;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

// Keyed by XZ cell; value is the corpse root's Y. A native container through a singleton bypasses
// ECS dependency tracking, so readers must register their JobHandle via AddJobHandleForReader.
public struct CorpseCells : IComponentData
{
    public NativeParallelMultiHashMap<int2, float> map;
}

// Rebuilt every frame from settled corpses (Sleeping flag), so revive/despawn needs no bookkeeping.
// Position registry only: no landing-height hack, real ragdoll colliders should stack corpses.
// Main-thread rebuild is fine, settled corpses cap out in the low hundreds.
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
