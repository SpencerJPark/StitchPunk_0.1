using DotsMovementToolkit;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class SurfaceSpeedTests
    {
        private const int CellsPerTile = 4;
        private const float TileSizeMetres = 100f;
        private const float RoadMultiplier = 1.25f;
        private const float ShallowMultiplier = 0.5f;
        private const float BaseMoveSpeed = 4f;
        private const float BaseRunSpeed = 8f;
        private const float Tolerance = 1e-4f;

        private World testWorld;
        private SystemHandle sampleHandle;
        private SystemHandle applyHandle;
        private SystemHandle revertHandle;
        private BlobAssetReference<TileNavCostBlob> tileBlob;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("SurfaceSpeedTests");
            EntityManager entityManager = testWorld.EntityManager;
            entityManager.CreateSingleton(new TerrainNavWindowSettings
            {
                radiusTiles = 1,
                tileSizeMetres = TileSizeMetres,
                cellsPerTile = CellsPerTile
            });
            entityManager.CreateSingleton(new TerrainSurfaceSpeedSettings
            {
                roadMultiplier = RoadMultiplier,
                shallowMultiplier = ShallowMultiplier
            });
            sampleHandle = testWorld.GetOrCreateSystem<TerrainSurfaceSpeedSampleSystem>();
            applyHandle = testWorld.GetOrCreateSystem<TerrainSurfaceSpeedApplySystem>();
            revertHandle = testWorld.GetOrCreateSystem<TerrainSurfaceSpeedRevertSystem>();
            CreateTileWithRoadAndShallowCells();
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
            // Persistent blobs are not owned by the world.
            if (tileBlob.IsCreated) tileBlob.Dispose();
        }

        [Test]
        public void SampleSystem_AttachesTheFactorAndReadsRoadAndShallowFromTheTileBlob()
        {
            Entity unit = CreateUnit(new float3(-20f, 0f, -20f));
            Assert.IsFalse(testWorld.EntityManager.HasComponent<SurfaceSpeedFactor>(unit));

            sampleHandle.Update(testWorld.Unmanaged);
            Assert.IsTrue(testWorld.EntityManager.HasComponent<SurfaceSpeedFactor>(unit));
            Assert.AreEqual(RoadMultiplier, ReadFactor(unit).factor, Tolerance);

            MoveUnit(unit, new float3(30f, 0f, 30f));
            sampleHandle.Update(testWorld.Unmanaged);
            Assert.AreEqual(ShallowMultiplier, ReadFactor(unit).factor, Tolerance);

            MoveUnit(unit, new float3(-45f, 0f, 45f));
            sampleHandle.Update(testWorld.Unmanaged);
            Assert.AreEqual(1f, ReadFactor(unit).factor, Tolerance);

            MoveUnit(unit, new float3(500f, 0f, 500f));
            sampleHandle.Update(testWorld.Unmanaged);
            Assert.AreEqual(1f, ReadFactor(unit).factor, Tolerance);
        }

        [Test]
        public void ApplyThenRevert_ScalesMovementBetweenThemAndRestoresItExactly()
        {
            Entity unit = CreateUnitWithFactor(0.5f);

            applyHandle.Update(testWorld.Unmanaged);
            AssertMovementSpeeds(unit, BaseMoveSpeed * 0.5f, BaseRunSpeed * 0.5f);
            Assert.AreEqual(0.5f, ReadFactor(unit).appliedFactor, Tolerance);

            revertHandle.Update(testWorld.Unmanaged);
            AssertMovementSpeeds(unit, BaseMoveSpeed, BaseRunSpeed);
            Assert.AreEqual(0f, ReadFactor(unit).appliedFactor, Tolerance);
        }

        [Test]
        public void ApplyDoesNothingWhileRolling()
        {
            Entity unit = CreateUnitWithFactor(0.5f);
            testWorld.EntityManager.AddComponent<PlayerRoll>(unit);
            testWorld.EntityManager.SetComponentEnabled<PlayerRoll>(unit, true);

            applyHandle.Update(testWorld.Unmanaged);

            AssertMovementSpeeds(unit, BaseMoveSpeed, BaseRunSpeed);
            Assert.AreEqual(0f, ReadFactor(unit).appliedFactor, Tolerance);
        }

        [Test]
        public void RevertRestoresEvenWhenMovementWasDisabledAfterApply()
        {
            Entity unit = CreateUnitWithFactor(0.5f);
            applyHandle.Update(testWorld.Unmanaged);
            testWorld.EntityManager.SetComponentEnabled<Movement>(unit, false);

            revertHandle.Update(testWorld.Unmanaged);

            AssertMovementSpeeds(unit, BaseMoveSpeed, BaseRunSpeed);
            Assert.AreEqual(0f, ReadFactor(unit).appliedFactor, Tolerance);
        }

        [Test]
        public void FactorOfOneLeavesMovementUntouched()
        {
            Entity unit = CreateUnitWithFactor(1f);

            applyHandle.Update(testWorld.Unmanaged);

            AssertMovementSpeeds(unit, BaseMoveSpeed, BaseRunSpeed);
            Assert.AreEqual(0f, ReadFactor(unit).appliedFactor, Tolerance);
        }

        private void CreateTileWithRoadAndShallowCells()
        {
            int cellCount = CellsPerTile * CellsPerTile;
            using (BlobBuilder builder = new BlobBuilder(Allocator.Temp))
            {
                ref TileNavCostBlob root = ref builder.ConstructRoot<TileNavCostBlob>();
                root.cellsPerSide = CellsPerTile;
                root.layerCount = 1;
                BlobBuilderArray<byte> costArray = builder.Allocate(ref root.costs, cellCount);
                BlobBuilderArray<byte> surfaceArray = builder.Allocate(ref root.surface, cellCount);
                for (int cellIndex = 0; cellIndex < cellCount; cellIndex++)
                {
                    costArray[cellIndex] = 1;
                    surfaceArray[cellIndex] = (byte)TerrainSurface.Normal;
                }
                surfaceArray[1 * CellsPerTile + 1] = (byte)TerrainSurface.Road;
                surfaceArray[3 * CellsPerTile + 3] = (byte)TerrainSurface.Shallow;
                tileBlob = builder.CreateBlobAssetReference<TileNavCostBlob>(Allocator.Persistent);
            }

            Entity tileEntity = testWorld.EntityManager.CreateEntity(typeof(TileNavCost));
            testWorld.EntityManager.SetComponentData(tileEntity, new TileNavCost { blob = tileBlob, coordinate = new int2(0, 0) });
        }

        private Entity CreateUnit(float3 position)
        {
            Entity unit = testWorld.EntityManager.CreateEntity(typeof(Movement), typeof(LocalTransform));
            testWorld.EntityManager.SetComponentData(unit, new Movement { moveSpeed = BaseMoveSpeed, runSpeed = BaseRunSpeed });
            testWorld.EntityManager.SetComponentData(unit, LocalTransform.FromPosition(position));
            return unit;
        }

        private Entity CreateUnitWithFactor(float factor)
        {
            Entity unit = CreateUnit(float3.zero);
            testWorld.EntityManager.AddComponentData(unit, new SurfaceSpeedFactor { factor = factor, appliedFactor = 0f });
            return unit;
        }

        private void MoveUnit(Entity unit, float3 position)
        {
            testWorld.EntityManager.SetComponentData(unit, LocalTransform.FromPosition(position));
        }

        private SurfaceSpeedFactor ReadFactor(Entity unit)
        {
            return testWorld.EntityManager.GetComponentData<SurfaceSpeedFactor>(unit);
        }

        private void AssertMovementSpeeds(Entity unit, float expectedMoveSpeed, float expectedRunSpeed)
        {
            Movement movement = testWorld.EntityManager.GetComponentData<Movement>(unit);
            Assert.AreEqual(expectedMoveSpeed, movement.moveSpeed, Tolerance);
            Assert.AreEqual(expectedRunSpeed, movement.runSpeed, Tolerance);
        }
    }
}
