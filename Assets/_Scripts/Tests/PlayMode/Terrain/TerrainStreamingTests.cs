using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    // The scene guids are bogus, so a started load never streams in and stays Loading; that is the state under test.
    public sealed class TerrainStreamingTests
    {
        private const float TileSizeMetres = 100f;
        private const int FirstTileX = -3;
        private const int LastTileX = 3;

        private World testWorld;
        private SystemHandle streamingHandle;
        private Entity settingsEntity;
        private Entity focusEntity;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("TerrainStreamingTests");
            EntityManager entityManager = testWorld.EntityManager;

            settingsEntity = entityManager.CreateEntity(typeof(TerrainStreamingSettings));
            entityManager.SetComponentData(settingsEntity, new TerrainStreamingSettings
            {
                loadRadiusTiles = 1,
                unloadRadiusTiles = 2,
                maxLoadsPerFrame = 1
            });
            DynamicBuffer<TerrainTileEntry> entries = entityManager.AddBuffer<TerrainTileEntry>(settingsEntity);
            for (int tileX = FirstTileX; tileX <= LastTileX; tileX++)
            {
                entries.Add(new TerrainTileEntry
                {
                    coordinate = new int2(tileX, 0),
                    sceneGuid = new Unity.Entities.Hash128((uint)(100 + tileX - FirstTileX), 1, 2, 3),
                    state = TerrainTileState.Unloaded,
                    sceneEntity = Entity.Null
                });
            }

            focusEntity = entityManager.CreateEntity(typeof(TerrainNavFocus), typeof(LocalTransform));
            entityManager.SetComponentData(focusEntity, LocalTransform.FromPosition(float3.zero));

            streamingHandle = testWorld.GetOrCreateSystem<TerrainStreamingSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
        }

        [Test]
        public void FirstUpdateLoadsTheWholeRingAtOnce()
        {
            UpdateStreaming();

            for (int tileX = FirstTileX; tileX <= LastTileX; tileX++)
            {
                TerrainTileEntry entry = ReadEntry(tileX);
                if (tileX >= -1 && tileX <= 1)
                {
                    Assert.AreEqual(TerrainTileState.Loading, entry.state, "tile " + tileX);
                    Assert.AreNotEqual(Entity.Null, entry.sceneEntity, "tile " + tileX);
                }
                else
                {
                    Assert.AreEqual(TerrainTileState.Unloaded, entry.state, "tile " + tileX);
                }
            }
        }

        [Test]
        public void LaterLoadsRespectMaxLoadsPerFrame()
        {
            UpdateStreaming();

            SetFocusTile(3);
            UpdateStreaming();
            Assert.AreEqual(TerrainTileState.Loading, ReadEntry(3).state);
            Assert.AreEqual(TerrainTileState.Unloaded, ReadEntry(2).state);

            UpdateStreaming();
            Assert.AreEqual(TerrainTileState.Loading, ReadEntry(2).state);
        }

        [Test]
        public void HysteresisKeepsATileAtTheUnloadRadiusAndDropsItBeyond()
        {
            UpdateStreaming();
            WriteState(0, TerrainTileState.Loaded);

            SetFocusTile(2);
            UpdateStreaming();
            Assert.AreEqual(TerrainTileState.Loaded, ReadEntry(0).state);

            SetFocusTile(3);
            UpdateStreaming();
            Assert.AreEqual(TerrainTileState.Unloaded, ReadEntry(0).state);
        }

        [Test]
        public void NoFocusEntityMeansNothingHappens()
        {
            testWorld.EntityManager.DestroyEntity(focusEntity);

            UpdateStreaming();

            for (int tileX = FirstTileX; tileX <= LastTileX; tileX++)
                Assert.AreEqual(TerrainTileState.Unloaded, ReadEntry(tileX).state, "tile " + tileX);
        }

        private void UpdateStreaming()
        {
            streamingHandle.Update(testWorld.Unmanaged);
        }

        private void SetFocusTile(int tileX)
        {
            testWorld.EntityManager.SetComponentData(focusEntity, LocalTransform.FromPosition(new float3(tileX * TileSizeMetres, 0f, 0f)));
        }

        private TerrainTileEntry ReadEntry(int tileX)
        {
            DynamicBuffer<TerrainTileEntry> entries = testWorld.EntityManager.GetBuffer<TerrainTileEntry>(settingsEntity);
            return entries[tileX - FirstTileX];
        }

        private void WriteState(int tileX, TerrainTileState state)
        {
            DynamicBuffer<TerrainTileEntry> entries = testWorld.EntityManager.GetBuffer<TerrainTileEntry>(settingsEntity);
            TerrainTileEntry entry = entries[tileX - FirstTileX];
            entry.state = state;
            entries[tileX - FirstTileX] = entry;
        }
    }
}
