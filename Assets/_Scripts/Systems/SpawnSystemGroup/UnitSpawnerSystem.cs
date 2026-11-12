using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Reclaims dormant pooled units of the matching type first, then instantiates the shortfall.
// Disables the UnitSpawner once its spawn count is fulfilled.
[UpdateInGroup(typeof(SpawnSystemGroup))]
public partial struct UnitSpawnerSystem : ISystem
{
    // Spawn points must land at least this far beyond the camera view radius so the player never
    // sees a unit pop in. Larger than CameraVisibilitySystem's paddings on purpose.
    private const float SPAWN_VIEW_PADDING = 10f;
    private const int SPAWN_POSITION_ATTEMPTS = 8;
    private const float TERRAIN_TILE_SIZE_METRES = 100f;

    private EntityQuery _poolQuery;
    private Random _random;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<UnitDataLibrary>();
        _random = Random.CreateFromIndex(1234u);

        _poolQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<PoolOwner, Disabled>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
            .Build(ref state);
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity libraryEntity = SystemAPI.GetSingletonEntity<UnitDataLibrary>();
        DynamicBuffer<UnitPrefabEntry> prefabs = SystemAPI.GetBuffer<UnitPrefabEntry>(libraryEntity);

        // Collect spawner data before any structural changes.
        NativeList<Entity> spawnerEntities    = new NativeList<Entity>(Allocator.Temp);
        NativeList<UnitType> spawnerTypes     = new NativeList<UnitType>(Allocator.Temp);
        NativeList<int> spawnerCounts         = new NativeList<int>(Allocator.Temp);
        NativeList<float3> spawnerPositions   = new NativeList<float3>(Allocator.Temp);
        NativeList<float> spawnerRanges       = new NativeList<float>(Allocator.Temp);

        foreach ((RefRO<UnitSpawner> spawner, RefRO<LocalTransform> transform, Entity entity) in
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

        // Spawn out of the player's view when the CameraView singleton is available (written by
        // AudioManager from the active camera). Optional on purpose — scenes without it spawn anywhere.
        bool avoidCameraView = SystemAPI.TryGetSingleton(out CameraView cameraView);
        float noSpawnRadius = avoidCameraView ? cameraView.viewRadius + SPAWN_VIEW_PADDING : 0f;
        float noSpawnRadiusSq = noSpawnRadius * noSpawnRadius;

        ComponentLookup<TerrainSpawnMarkerData> markerLookup = SystemAPI.GetComponentLookup<TerrainSpawnMarkerData>(true);

        NativeArray<Entity> pooledEntities    = _poolQuery.ToEntityArray(Allocator.Temp);
        NativeArray<PoolOwner> pooledOwners   = _poolQuery.ToComponentDataArray<PoolOwner>(Allocator.Temp);
        // Pool slots claimed this frame, so none is assigned twice.
        NativeBitArray reclaimed = new NativeBitArray(pooledEntities.Length, Allocator.Temp);

        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

        for (int s = 0; s < spawnerEntities.Length; s++)
        {
            UnitType targetType = spawnerTypes[s];
            int needed          = spawnerCounts[s];
            float3 center       = spawnerPositions[s];
            float range         = spawnerRanges[s];

            // Terrain marker units are stamped so they can leave with their tile or count against its budget.
            bool fromMarker = markerLookup.TryGetComponent(spawnerEntities[s], out TerrainSpawnMarkerData marker);
            SpawnedByMarker markerStamp = default;
            if (fromMarker)
            {
                markerStamp = new SpawnedByMarker
                {
                    markerKey = marker.markerKey,
                    homeTile = TerrainNavWindowMath.TileCoordinateOf(center, TERRAIN_TILE_SIZE_METRES),
                    despawnWithTile = !marker.persistent
                };
            }

            Entity bodyPrefab  = GetBodyPrefabForType(prefabs, targetType);
            
            if (bodyPrefab == Entity.Null) continue;

            int spawned = 0;

            for (int i = 0; i < pooledEntities.Length && spawned < needed; i++)
            {
                if (reclaimed.IsSet(i) || pooledOwners[i].unitType != targetType)
                    continue;

                float3 pos = RandomSpawnPosition(center, range, ref _random,
                    avoidCameraView, cameraView.center, noSpawnRadiusSq);
                // ECB is FIFO, so Remove runs before SetComponent.
                ecb.RemoveComponent<Disabled>(pooledEntities[i]);
                ecb.SetComponent(pooledEntities[i], LocalTransform.FromPosition(pos));
                ecb.SetComponentEnabled<NewlySpawned>(pooledEntities[i], true);
                // A reclaimed body may still carry the stamp of its previous life.
                ecb.RemoveComponent<SpawnedByMarker>(pooledEntities[i]);
                if (fromMarker)
                    ecb.AddComponent(pooledEntities[i], markerStamp);
               
                reclaimed.Set(i, true);
                spawned++;
            }

            for (int i = spawned; i < needed; i++)
            {
                float3 pos = RandomSpawnPosition(center, range, ref _random,
                    avoidCameraView, cameraView.center, noSpawnRadiusSq);
                
                Entity newBody = ecb.Instantiate(bodyPrefab);
                ecb.SetComponent(newBody, LocalTransform.FromPosition(pos));
                ecb.AddComponent(newBody, new PoolOwner { unitType = targetType });
                if (fromMarker)
                    ecb.AddComponent(newBody, markerStamp);

                ecb.SetComponentEnabled<NewlySpawned>(newBody, true);
                ecb.SetComponentEnabled<ActionRequest>(newBody, true);
            }

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

    // Re-rolls until the candidate lands outside the camera's no-spawn radius. When the whole
    // spawner area is on screen every attempt fails — spawn anyway (gameplay wins over hiding
    // the pop-in).
    private static float3 RandomSpawnPosition(
        float3 center, float range, ref Random random,
        bool avoidCameraView, float3 viewCenter, float noSpawnRadiusSq)
    {
        float3 candidate = RandomPositionInRange(center, range, ref random);
        if (!avoidCameraView)
            return candidate;

        for (int attempt = 0; attempt < SPAWN_POSITION_ATTEMPTS; attempt++)
        {
            float2 offsetFromCamera = candidate.xz - viewCenter.xz;
            if (math.lengthsq(offsetFromCamera) > noSpawnRadiusSq)
                return candidate;

            candidate = RandomPositionInRange(center, range, ref random);
        }

        return candidate;
    }
}
