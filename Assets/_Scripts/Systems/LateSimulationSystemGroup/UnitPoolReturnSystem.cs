using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Checks active pool-managed units against the player's position each frame.
// Any unit beyond PoolReturnDistance is disabled and returned to the pool,
// where UnitSpawnerSystem can reclaim it when a spawner needs units of that type.
[BurstCompile]
[UpdateInGroup(typeof(DespawnSystemGroup))]
public partial struct UnitPoolReturnSystem : ISystem
{
    // Units farther than this (world units) from the player are pooled.
    // Two chunks (~200 units) keeps nearby battles intact while trimming distant stragglers.
    // Tune upward if large battles at range are common.
    private const float PoolReturnDistance = 200f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<Player>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float3 playerPos = SystemAPI.GetComponent<LocalTransform>(
            SystemAPI.GetSingletonEntity<Player>()).Position;

        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        float thresholdSq = PoolReturnDistance * PoolReturnDistance;

        foreach (var (transform, entity) in
            SystemAPI.Query<RefRO<LocalTransform>>()
                .WithAll<PoolOwner>()
                .WithEntityAccess())
        {
            if (math.distancesq(transform.ValueRO.Position, playerPos) > thresholdSq)
                ecb.AddComponent<Disabled>(entity);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
