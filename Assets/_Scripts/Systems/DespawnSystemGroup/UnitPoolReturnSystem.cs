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

    private ComponentLookup<SpawnedByMarker> spawnedByMarkerLookup;
    private ComponentLookup<Despawn> despawnLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<Player>();
        spawnedByMarkerLookup = state.GetComponentLookup<SpawnedByMarker>(true);
        despawnLookup = state.GetComponentLookup<Despawn>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        spawnedByMarkerLookup.Update(ref state);
        despawnLookup.Update(ref state);
        float3 playerPos = SystemAPI.GetComponent<LocalTransform>(
            SystemAPI.GetSingletonEntity<Player>()).Position;

        EntityCommandBuffer ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        float thresholdSq = PoolReturnDistance * PoolReturnDistance;

        foreach ((RefRO<LocalTransform> transform, Entity entity) in
            SystemAPI.Query<RefRO<LocalTransform>>()
                .WithAll<PoolOwner>()
                .WithEntityAccess())
        {
            if (math.distancesq(transform.ValueRO.Position, playerPos) <= thresholdSq)
                continue;
            // Persistent marker units live on through the abstract tier; pooling them would hand their bodies to other spawners.
            if (spawnedByMarkerLookup.TryGetComponent(entity, out SpawnedByMarker markerStamp) && !markerStamp.despawnWithTile)
                continue;

            ecb.AddComponent<Disabled>(entity);
            // A pending Despawn would otherwise survive in the pool and re-pool the body the moment it is reclaimed.
            if (despawnLookup.HasComponent(entity))
                despawnLookup.SetComponentEnabled(entity, false);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
