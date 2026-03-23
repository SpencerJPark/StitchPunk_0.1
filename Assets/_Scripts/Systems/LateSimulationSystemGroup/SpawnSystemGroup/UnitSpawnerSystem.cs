using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Runs once per enabled UnitSpawner.
// First tries to reclaim dormant (Disabled) pool units of the matching type,
// then instantiates new ones for any shortfall.
//
// Body and brain are instantiated as separate root entities and cross-linked via
// BrainLink / BodyLink. This avoids the LinkedEntityGroup IEnableableComponent
// copy bug and makes runtime brain-swapping straightforward.
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
            Entity brainPrefab = GetBrainPrefabForType(prefabs, targetType);
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
                // Re-enable the brain so AI wakes back up after pooling.
                if (SystemAPI.HasComponent<BrainLink>(pooledEntities[i]))
                {
                    Entity brain = SystemAPI.GetComponent<BrainLink>(pooledEntities[i]).brain;
                    if (brain != Entity.Null)
                    {
                        ecb.RemoveComponent<Disabled>(brain);
                        ecb.SetComponentEnabled<NeedsAction>(brain, true);
                    }
                }
                reclaimed.Set(i, true);
                spawned++;
            }

            // Instantiate new entities for any remaining shortfall.
            for (int i = spawned; i < needed; i++)
            {
                float3 pos = RandomPositionInRange(center, range, ref _random);

                // Instantiate body and brain as independent root entities.
                // This guarantees IEnableableComponent bits are copied reliably
                // (LinkedEntityGroup member copy is not trustworthy for enabled bits).
                Entity newBody = ecb.Instantiate(bodyPrefab);
                ecb.SetComponent(newBody, LocalTransform.FromPosition(pos));
                ecb.AddComponent(newBody, new PoolOwner { unitType = targetType });
                // Signal AnimatorTargetInitSystem to rebuild the AnimatorTarget buffer.
                ecb.AddComponent<NeedsAnimatorInit>(newBody);
                // ECB.Instantiate does not reliably copy IEnableableComponent enabled bits.
                // Explicitly disable all pathfinding followers/requests so they start clean.
                ecb.SetComponentEnabled<PathRequest>(newBody, false);
                ecb.SetComponentEnabled<DStarLiteFollower>(newBody, false);
                ecb.SetComponentEnabled<FlowFieldFollower>(newBody, false);
                ecb.SetComponentEnabled<HordeMembership>(newBody, false);

                if (brainPrefab != Entity.Null)
                {
                    Entity newBrain = ecb.Instantiate(brainPrefab);

                    // Brain must share the body's world position so spatial-hash
                    // range queries in the scoring systems return correct results.
                    ecb.SetComponent(newBrain, LocalTransform.FromPosition(pos));

                    // ECB.Instantiate does not reliably copy IEnableableComponent enabled bits.
                    // Force NeedsAction on so the AI scoring systems process this brain immediately.
                    ecb.SetComponentEnabled<NeedsAction>(newBrain, true);

                    // Cross-link body ↔ brain. ECB entity IDs are resolved at playback,
                    // so these deferred-entity references are safe to use here.
                    // AddComponent for BrainLink because MaleCitizen.prefab has no BrainLinkAuthoring,
                    // so the component does not exist on the baked body entity.
                    // HasBrain is baked by BrainLinkAuthoring but that baker runs on the brain prefab,
                    // not the body prefab — add it explicitly here.
                    ecb.AddComponent(newBody,  new BrainLink { brain = newBrain });
                    ecb.AddComponent<HasBrain>(newBody);
                    ecb.SetComponent(newBrain, new BodyLink  { body  = newBody  });
                    ecb.AddComponent(newBrain, new PoolOwner { unitType = targetType });
                }
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

    private static Entity GetBrainPrefabForType(DynamicBuffer<UnitPrefabEntry> prefabs, UnitType type)
    {
        for (int i = 0; i < prefabs.Length; i++)
            if (prefabs[i].unitType == type) return prefabs[i].brainPrefab;
        return Entity.Null;
    }

    private static float3 RandomPositionInRange(float3 center, float range, ref Random random)
    {
        float2 dir = random.NextFloat2Direction();
        float dist = random.NextFloat(0f, range);
        return center + new float3(dir.x * dist, 0f, dir.y * dist);
    }
}
