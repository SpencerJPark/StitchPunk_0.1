using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Runs once per enabled UnitSpawner.
// First tries to reclaim dormant (Disabled) pool units of the matching type,
// then instantiates new ones for any shortfall.
//
// All AI state (Brain, MotivationState, ActionOptions, PlayerControlled, etc.)
// lives on the unit entity itself — no separate brain entity or BrainLink needed.
//
// Disables the UnitSpawner component after fulfilling its spawn count.
[UpdateInGroup(typeof(SpawnSystemGroup))]
public partial struct UnitSpawnerSystem : ISystem
{
    private EntityQuery _poolQuery;
    private Random _random;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<UnitDataLibrary>();
        _random = Random.CreateFromIndex(1234u);

        // Only entities that are explicitly in the pool: have PoolOwner AND are disabled.
        _poolQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<PoolOwner, Disabled>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
            .Build(ref state);
    }

    public void OnUpdate(ref SystemState state)
    {
        // UnitPrefabEntry is a DynamicBuffer on the UnitDataLibrary singleton entity.
        // Look up by entity to avoid ambiguity with any other entity that may carry this buffer.
        Entity libraryEntity = SystemAPI.GetSingletonEntity<UnitDataLibrary>();
        var prefabs = SystemAPI.GetBuffer<UnitPrefabEntry>(libraryEntity);

        // --- Collect spawner data before any structural changes ---
        var spawnerEntities  = new NativeList<Entity>(Allocator.Temp);
        var spawnerTypes     = new NativeList<UnitType>(Allocator.Temp);
        var spawnerCounts    = new NativeList<int>(Allocator.Temp);
        var spawnerPositions = new NativeList<float3>(Allocator.Temp);
        var spawnerRanges    = new NativeList<float>(Allocator.Temp);

        foreach (var (spawner, transform, entity) in
            SystemAPI.Query<RefRO<UnitSpawner>, RefRO<LocalTransform>>().WithEntityAccess())
        {
            spawnerEntities.Add(entity);
            spawnerTypes.Add(spawner.ValueRO.unitType);
            spawnerCounts.Add(spawner.ValueRO.spawnCount);
            spawnerPositions.Add(transform.ValueRO.Position);
            spawnerRanges.Add(spawner.ValueRO.range);
        }

        if (spawnerEntities.IsEmpty)
        {
            spawnerEntities.Dispose(); spawnerTypes.Dispose();
            spawnerCounts.Dispose(); spawnerPositions.Dispose(); spawnerRanges.Dispose();
            return;
        }

        // --- Snapshot the current pool ---
        var pooledEntities = _poolQuery.ToEntityArray(Allocator.Temp);
        var pooledOwners   = _poolQuery.ToComponentDataArray<PoolOwner>(Allocator.Temp);
        // Tracks which pool slots have been claimed this frame so we don't double-assign.
        var reclaimed = new NativeBitArray(pooledEntities.Length, Allocator.Temp);

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        for (int s = 0; s < spawnerEntities.Length; s++)
        {
            UnitType targetType = spawnerTypes[s];
            int needed          = spawnerCounts[s];
            float3 center       = spawnerPositions[s];
            float range         = spawnerRanges[s];

            Entity bodyPrefab  = GetBodyPrefabForType(prefabs, targetType);
            
            if (bodyPrefab == Entity.Null) continue;

            int spawned = 0;

            // Reclaim dormant pool units first (cheaper than instantiating).
            for (int i = 0; i < pooledEntities.Length && spawned < needed; i++)
            {
                if (reclaimed.IsSet(i) || pooledOwners[i].unitType != targetType)
                    continue;

                float3 pos = RandomPositionInRange(center, range, ref _random);
                // Re-enable and reposition. ECB is FIFO, so Remove runs before SetComponent.
                ecb.RemoveComponent<Disabled>(pooledEntities[i]);
                ecb.SetComponent(pooledEntities[i], LocalTransform.FromPosition(pos));
                ecb.SetComponentEnabled<NewlySpawned>(pooledEntities[i], true);
               
                reclaimed.Set(i, true);
                spawned++;
            }

            // Instantiate new entities for any remaining shortfall.
            for (int i = spawned; i < needed; i++)
            {
                float3 pos = RandomPositionInRange(center, range, ref _random);
                
                Entity newBody = ecb.Instantiate(bodyPrefab);
                ecb.SetComponent(newBody, LocalTransform.FromPosition(pos));
                ecb.AddComponent(newBody, new PoolOwner { unitType = targetType });

                ecb.SetComponentEnabled<NewlySpawned>(newBody, true);
                ecb.SetComponentEnabled<ActionRequest>(newBody, true);
            }

            // Spawner has done its job — disable it until something re-activates it.
            ecb.SetComponentEnabled<UnitSpawner>(spawnerEntities[s], false);
        }

        ecb.Playback(state.EntityManager);

        reclaimed.Dispose();
        pooledEntities.Dispose();
        pooledOwners.Dispose();
        ecb.Dispose();

        spawnerEntities.Dispose(); spawnerTypes.Dispose();
        spawnerCounts.Dispose(); spawnerPositions.Dispose(); spawnerRanges.Dispose();
    }

    private static Entity GetBodyPrefabForType(DynamicBuffer<UnitPrefabEntry> prefabs, UnitType type)
    {
        for (int i = 0; i < prefabs.Length; i++)
            if (prefabs[i].unitType == type) return prefabs[i].bodyPrefab;
        return Entity.Null;
    }

    private static float3 RandomPositionInRange(float3 center, float range, ref Random random)
    {
        float2 dir = random.NextFloat2Direction();
        float dist = random.NextFloat(0f, range);
        return center + new float3(dir.x * dist, 0f, dir.y * dist);
    }
}
