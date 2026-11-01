using NUnit.Framework;
using UnityEngine.TestTools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class TerrainSpawnTests
    {
        private const float SecondsPerDay = 1440f;
        private const uint PersistentKey = 11u;

        private World testWorld;
        private SystemHandle spawnHandle;
        private SystemHandle despawnHandle;
        private Entity settingsEntity;
        private Entity clockEntity;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("TerrainSpawnTests");
            EntityManager entityManager = testWorld.EntityManager;

            settingsEntity = entityManager.CreateEntity(typeof(TerrainStreamingSettings));
            DynamicBuffer<TerrainTileEntry> tileEntries = entityManager.AddBuffer<TerrainTileEntry>(settingsEntity);
            tileEntries.Add(new TerrainTileEntry { coordinate = new int2(0, 0), state = TerrainTileState.Loaded });
            tileEntries.Add(new TerrainTileEntry { coordinate = new int2(1, 0), state = TerrainTileState.Unloaded });

            clockEntity = entityManager.CreateEntity(typeof(WorldClock));
            entityManager.SetComponentData(clockEntity, new WorldClock { secondsPerDay = SecondsPerDay });

            spawnHandle = testWorld.GetOrCreateSystem<TerrainSpawnSystem>();
            despawnHandle = testWorld.GetOrCreateSystem<TerrainSpawnDespawnSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
        }

        [Test]
        public void AMarkerOnALoadedTileAsksForItsUnitsAndIsHandledOnce()
        {
            Entity marker = CreateMarker(new float3(10f, 0f, 0f), persistent: false, count: 3, markerKey: 1u);

            spawnHandle.Update(testWorld.Unmanaged);

            EntityManager entityManager = testWorld.EntityManager;
            Assert.IsTrue(entityManager.IsComponentEnabled<UnitSpawner>(marker));
            Assert.IsTrue(entityManager.IsComponentEnabled<TerrainSpawnMarkerHandled>(marker));
            Assert.AreEqual(3, entityManager.GetComponentData<UnitSpawner>(marker).spawnCount);

            entityManager.SetComponentEnabled<UnitSpawner>(marker, false);
            spawnHandle.Update(testWorld.Unmanaged);
            Assert.IsFalse(entityManager.IsComponentEnabled<UnitSpawner>(marker), "a handled marker must not ask again");
        }

        [Test]
        public void AMarkerOnATileThatIsNotLoadedWaits()
        {
            Entity marker = CreateMarker(new float3(100f, 0f, 0f), persistent: false, count: 2, markerKey: 2u);

            spawnHandle.Update(testWorld.Unmanaged);

            Assert.IsFalse(testWorld.EntityManager.IsComponentEnabled<UnitSpawner>(marker));
            Assert.IsFalse(testWorld.EntityManager.IsComponentEnabled<TerrainSpawnMarkerHandled>(marker));
        }

        [Test]
        public void APersistentMarkerSpawnsOnceEvenWhenTheTileReloadsItsMarker()
        {
            CreateMarker(new float3(10f, 0f, 0f), persistent: true, count: 2, markerKey: PersistentKey);
            spawnHandle.Update(testWorld.Unmanaged);

            Entity reloadedMarker = CreateMarker(new float3(10f, 0f, 0f), persistent: true, count: 2, markerKey: PersistentKey);
            spawnHandle.Update(testWorld.Unmanaged);

            Assert.IsFalse(testWorld.EntityManager.IsComponentEnabled<UnitSpawner>(reloadedMarker));
            Assert.IsTrue(testWorld.EntityManager.IsComponentEnabled<TerrainSpawnMarkerHandled>(reloadedMarker));
        }

        [Test]
        public void AnOnDemandMarkerSpawnsAgainEveryTimeItsTileLoads()
        {
            CreateMarker(new float3(10f, 0f, 0f), persistent: false, count: 2, markerKey: 3u);
            spawnHandle.Update(testWorld.Unmanaged);

            Entity reloadedMarker = CreateMarker(new float3(10f, 0f, 0f), persistent: false, count: 2, markerKey: 3u);
            spawnHandle.Update(testWorld.Unmanaged);

            Assert.IsTrue(testWorld.EntityManager.IsComponentEnabled<UnitSpawner>(reloadedMarker));
        }

        [Test]
        public void APersistentMarkerRespawnsOnceItsRespawnTimeHasPassed()
        {
            CreateMarker(new float3(10f, 0f, 0f), persistent: true, count: 2, markerKey: PersistentKey, respawnAfterGameDays: 2f);
            spawnHandle.Update(testWorld.Unmanaged);

            testWorld.EntityManager.SetComponentData(clockEntity, new WorldClock { secondsPerDay = SecondsPerDay, totalWorldSeconds = 2.5 * SecondsPerDay });
            Entity reloadedMarker = CreateMarker(new float3(10f, 0f, 0f), persistent: true, count: 2, markerKey: PersistentKey, respawnAfterGameDays: 2f);
            spawnHandle.Update(testWorld.Unmanaged);

            Assert.IsTrue(testWorld.EntityManager.IsComponentEnabled<UnitSpawner>(reloadedMarker));
        }

        [Test]
        public void ATileNeverHostsMoreThanItsMarkerUnitBudget()
        {
            Entity marker = CreateMarker(new float3(10f, 0f, 0f), persistent: false, count: 500, markerKey: 4u);
            LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("marker budget"));

            spawnHandle.Update(testWorld.Unmanaged);

            Assert.AreEqual(64, testWorld.EntityManager.GetComponentData<UnitSpawner>(marker).spawnCount);
        }

        [Test]
        public void AnOnDemandUnitIsSentToDespawnWhenItsHomeTileIsNoLongerLoaded()
        {
            Entity unit = CreateStampedUnit(new int2(0, 0), despawnWithTile: true);

            despawnHandle.Update(testWorld.Unmanaged);
            Assert.IsFalse(testWorld.EntityManager.IsComponentEnabled<Despawn>(unit), "its tile is still loaded");

            SetTileState(0, TerrainTileState.Unloaded);
            despawnHandle.Update(testWorld.Unmanaged);
            Assert.IsTrue(testWorld.EntityManager.IsComponentEnabled<Despawn>(unit));
        }

        [Test]
        public void APersistentUnitStaysWhenItsTileUnloads()
        {
            Entity unit = CreateStampedUnit(new int2(0, 0), despawnWithTile: false);

            SetTileState(0, TerrainTileState.Unloaded);
            despawnHandle.Update(testWorld.Unmanaged);

            Assert.IsFalse(testWorld.EntityManager.IsComponentEnabled<Despawn>(unit));
        }

        [Test]
        public void UnitSpawnerSystemStampsTheUnitsOfAMarkerAndOnlyThose()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity prefab = entityManager.CreateEntity(typeof(Prefab), typeof(LocalTransform), typeof(NewlySpawned), typeof(ActionRequest));
            entityManager.SetComponentEnabled<NewlySpawned>(prefab, false);
            entityManager.SetComponentEnabled<ActionRequest>(prefab, false);
            Entity libraryEntity = entityManager.CreateEntity(typeof(UnitDataLibrary));
            DynamicBuffer<UnitPrefabEntry> prefabs = entityManager.AddBuffer<UnitPrefabEntry>(libraryEntity);
            prefabs.Add(new UnitPrefabEntry { unitType = UnitType.MaleCitizen, bodyPrefab = prefab });
            entityManager.CreateEntity(typeof(GameSceneTag));

            Entity marker = CreateMarker(new float3(10f, 0f, 0f), persistent: false, count: 2, markerKey: 9u);
            Entity plainSpawner = entityManager.CreateEntity(typeof(LocalTransform), typeof(UnitSpawner));
            entityManager.SetComponentData(plainSpawner, new UnitSpawner { unitType = UnitType.MaleCitizen, spawnCount = 1, range = 1f });
            spawnHandle.Update(testWorld.Unmanaged);

            testWorld.GetOrCreateSystem<UnitSpawnerSystem>().Update(testWorld.Unmanaged);

            EntityQuery stampedQuery = entityManager.CreateEntityQuery(typeof(SpawnedByMarker));
            NativeArray<SpawnedByMarker> stamps = stampedQuery.ToComponentDataArray<SpawnedByMarker>(Allocator.Temp);
            Assert.AreEqual(2, stamps.Length);
            Assert.AreEqual(9u, stamps[0].markerKey);
            Assert.IsTrue(stamps[0].despawnWithTile);
            Assert.AreEqual(new int2(0, 0), stamps[0].homeTile);
            stamps.Dispose();
            Assert.AreEqual(3, entityManager.CreateEntityQuery(typeof(PoolOwner)).CalculateEntityCount(), "two marker units and one plain unit");
        }

        [Test]
        public void DistancePoolingSkipsPersistentMarkerUnitsAndClearsAPendingDespawnOnTheOthers()
        {
            EntityManager entityManager = testWorld.EntityManager;
            entityManager.CreateEntity(typeof(GameSceneTag));
            Entity player = entityManager.CreateEntity(typeof(Player), typeof(LocalTransform));
            entityManager.SetComponentData(player, LocalTransform.FromPosition(float3.zero));

            Entity persistentUnit = CreateFarPoolUnit(new float3(500f, 0f, 0f));
            entityManager.AddComponentData(persistentUnit, new SpawnedByMarker { markerKey = 1u, despawnWithTile = false });
            Entity onDemandUnit = CreateFarPoolUnit(new float3(-500f, 0f, 0f));
            entityManager.AddComponentData(onDemandUnit, new SpawnedByMarker { markerKey = 2u, despawnWithTile = true });
            entityManager.SetComponentEnabled<Despawn>(onDemandUnit, true);

            testWorld.GetOrCreateSystem<UnitPoolReturnSystem>().Update(testWorld.Unmanaged);

            Assert.IsFalse(entityManager.HasComponent<Disabled>(persistentUnit), "a persistent marker unit must not be pooled by distance");
            Assert.IsTrue(entityManager.HasComponent<Disabled>(onDemandUnit));
            Assert.IsFalse(entityManager.IsComponentEnabled<Despawn>(onDemandUnit), "a pooled body must not keep a pending Despawn");
        }

        private Entity CreateFarPoolUnit(float3 position)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity unit = entityManager.CreateEntity(typeof(LocalTransform), typeof(PoolOwner), typeof(Despawn));
            entityManager.SetComponentData(unit, LocalTransform.FromPosition(position));
            entityManager.SetComponentEnabled<Despawn>(unit, false);
            return unit;
        }

        private Entity CreateMarker(float3 position, bool persistent, int count, uint markerKey, float respawnAfterGameDays = 0f)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity marker = entityManager.CreateEntity(typeof(LocalTransform), typeof(TerrainSpawnMarkerData), typeof(UnitSpawner), typeof(TerrainSpawnMarkerHandled));
            entityManager.SetComponentData(marker, LocalTransform.FromPosition(position));
            entityManager.SetComponentData(marker, new TerrainSpawnMarkerData
            {
                unitType = UnitType.MaleCitizen,
                count = count,
                radiusMetres = 5f,
                persistent = persistent,
                respawnAfterGameDays = respawnAfterGameDays,
                markerKey = markerKey
            });
            entityManager.SetComponentData(marker, new UnitSpawner { unitType = UnitType.MaleCitizen, spawnCount = count, range = 5f });
            entityManager.SetComponentEnabled<UnitSpawner>(marker, false);
            entityManager.SetComponentEnabled<TerrainSpawnMarkerHandled>(marker, false);
            return marker;
        }

        private Entity CreateStampedUnit(int2 homeTile, bool despawnWithTile)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity unit = entityManager.CreateEntity(typeof(SpawnedByMarker), typeof(Despawn));
            entityManager.SetComponentData(unit, new SpawnedByMarker { markerKey = 5u, homeTile = homeTile, despawnWithTile = despawnWithTile });
            entityManager.SetComponentEnabled<Despawn>(unit, false);
            return unit;
        }

        private void SetTileState(int tileX, TerrainTileState tileState)
        {
            DynamicBuffer<TerrainTileEntry> tileEntries = testWorld.EntityManager.GetBuffer<TerrainTileEntry>(settingsEntity);
            for (int index = 0; index < tileEntries.Length; index++)
            {
                if (tileEntries[index].coordinate.x != tileX)
                    continue;
                TerrainTileEntry tileEntry = tileEntries[index];
                tileEntry.state = tileState;
                tileEntries[index] = tileEntry;
            }
        }
    }
}
