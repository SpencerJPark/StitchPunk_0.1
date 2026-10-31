using System.Collections.Generic;
using DotsMovementToolkit;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class NavFootprintTests
    {
        private const int WindowCells = 20;
        private const int CellsPerTile = 4;
        private const float TileSizeMetres = 100f;
        // The window must span real metres for footprints to hit it: 100 m tile / 4 cells.
        private const float CellSizeMetres = TileSizeMetres / CellsPerTile;
        private const byte WallCost = 255;
        private const byte OpenCost = 1;

        private World testWorld;
        private SystemHandle navGridHandle;
        private SystemHandle changeDetectHandle;
        private SystemHandle navWindowHandle;
        private SystemHandle stampHandle;
        private Entity focusEntity;
        private Entity footprintEntity;
        private readonly List<BlobAssetReference<TileNavCostBlob>> createdBlobs = new List<BlobAssetReference<TileNavCostBlob>>();

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
            // Persistent blobs are not owned by the world.
            foreach (BlobAssetReference<TileNavCostBlob> blob in createdBlobs)
                if (blob.IsCreated) blob.Dispose();
            createdBlobs.Clear();
        }

        [Test]
        public void FootprintCostIsStampedIntoTheCostMap()
        {
            BuildWorld();
            CreateTile(new int2(0, 0));
            CreateFootprint(float3.zero, new float2(30f, 30f), 0f);

            UpdateAllSystems();

            Assert.AreEqual(WallCost, ReadCostAtWorldPoint(new float2(-12.5f, -12.5f)));
            Assert.AreEqual(WallCost, ReadCostAtWorldPoint(new float2(12.5f, 12.5f)));
            Assert.AreEqual(OpenCost, ReadCostAtWorldPoint(new float2(-37.5f, 0f)));
            Assert.AreEqual(OpenCost, ReadCostAtWorldPoint(new float2(0f, 37.5f)));
        }

        [Test]
        public void AMovedFootprintClearsItsOldCellsAndStampsTheNewOnes()
        {
            BuildWorld();
            CreateTile(new int2(0, 0));
            CreateTile(new int2(1, 0));
            CreateFootprint(float3.zero, new float2(30f, 30f), 0f);
            UpdateAllSystems();
            Assert.AreEqual(WallCost, ReadCostAtWorldPoint(new float2(12.5f, 12.5f)));

            // Only the change detector can make the window rebuild that clears the old stamp.
            SetFootprintPosition(new float3(50f, 0f, 0f));
            UpdateAllSystems();

            Assert.AreEqual(OpenCost, ReadCostAtWorldPoint(new float2(12.5f, 12.5f)));
            Assert.AreEqual(OpenCost, ReadCostAtWorldPoint(new float2(-12.5f, -12.5f)));
            Assert.AreEqual(WallCost, ReadCostAtWorldPoint(new float2(37.5f, 12.5f)));
            Assert.AreEqual(WallCost, ReadCostAtWorldPoint(new float2(62.5f, -12.5f)));
        }

        [Test]
        public void StampSurvivesAWindowMove()
        {
            BuildWorld();
            CreateTile(new int2(0, 0));
            CreateTile(new int2(1, 0));
            CreateFootprint(float3.zero, new float2(30f, 30f), 0f);
            UpdateAllSystems();
            float originXBefore = testWorld.EntityManager.GetComponentData<NavGridConfig>(navGridHandle).gridOrigin.x;

            SetFocusPosition(new float3(TileSizeMetres, 0f, 0f));
            UpdateAllSystems();

            float originXAfter = testWorld.EntityManager.GetComponentData<NavGridConfig>(navGridHandle).gridOrigin.x;
            Assert.AreEqual(TileSizeMetres, originXAfter - originXBefore, 0.001f);
            Assert.AreEqual(WallCost, ReadCostAtWorldPoint(new float2(-12.5f, -12.5f)));
            Assert.AreEqual(WallCost, ReadCostAtWorldPoint(new float2(12.5f, 12.5f)));
            Assert.AreEqual(OpenCost, ReadCostAtWorldPoint(new float2(-37.5f, 0f)));
        }

        [Test]
        public void WorldWithoutFootprintsNeverRequestsARebuild()
        {
            BuildWorld();
            CreateTile(new int2(0, 0));

            UpdateAllSystems();
            NavGridCostMap afterFirst = testWorld.EntityManager.GetComponentData<NavGridCostMap>(navGridHandle);
            UpdateAllSystems();
            UpdateAllSystems();
            NavGridCostMap afterIdle = testWorld.EntityManager.GetComponentData<NavGridCostMap>(navGridHandle);

            Assert.AreEqual(afterFirst.costMapVersion, afterIdle.costMapVersion);
            Assert.AreEqual(afterFirst.windowVersion, afterIdle.windowVersion);
        }

        [Test]
        public void RotatedFootprintStampsLongAlongZNotAlongX()
        {
            BuildWorld();
            CreateTile(new int2(0, 0));
            // Centre sits on a cell centre so the 5 m half-width still covers it; 60 m half-length is then rotated from X onto Z.
            CreateFootprint(new float3(12.5f, 0f, 12.5f), new float2(60f, 5f), math.PI * 0.5f);

            UpdateAllSystems();

            Assert.AreEqual(WallCost, ReadCostAtWorldPoint(new float2(12.5f, 12.5f)));
            Assert.AreEqual(WallCost, ReadCostAtWorldPoint(new float2(12.5f, 37.5f)));
            Assert.AreEqual(OpenCost, ReadCostAtWorldPoint(new float2(37.5f, 12.5f)));
        }

        private void BuildWorld()
        {
            testWorld = new World("NavFootprintTests");
            testWorld.EntityManager.CreateSingleton(new NavGridSettings
            {
                width = WindowCells,
                height = WindowCells,
                layerCount = 1,
                cellSize = CellSizeMetres,
                layerHeight = 3f,
                gridOrigin = float3.zero,
                wallCost = WallCost,
                heavyCost = 50,
                defaultCost = 1,
                externalCostRegions = true
            });
            navGridHandle = testWorld.GetOrCreateSystem<NavGridSystem>();
            navWindowHandle = testWorld.GetOrCreateSystem<NavWindowSystem>();
            changeDetectHandle = testWorld.GetOrCreateSystem<NavFootprintChangeDetectSystem>();
            stampHandle = testWorld.GetOrCreateSystem<NavFootprintStampSystem>();

            EntityQuery windowSettingsQuery = testWorld.EntityManager.CreateEntityQuery(typeof(TerrainNavWindowSettings));
            windowSettingsQuery.SetSingleton(new TerrainNavWindowSettings
            {
                radiusTiles = 1,
                tileSizeMetres = TileSizeMetres,
                cellsPerTile = CellsPerTile
            });

            focusEntity = testWorld.EntityManager.CreateEntity(typeof(TerrainNavFocus), typeof(LocalTransform));
            testWorld.EntityManager.SetComponentData(focusEntity, LocalTransform.FromPosition(float3.zero));
        }

        private void UpdateAllSystems()
        {
            navGridHandle.Update(testWorld.Unmanaged);
            changeDetectHandle.Update(testWorld.Unmanaged);
            navWindowHandle.Update(testWorld.Unmanaged);
            stampHandle.Update(testWorld.Unmanaged);
        }

        private void SetFocusPosition(float3 position)
        {
            testWorld.EntityManager.SetComponentData(focusEntity, LocalTransform.FromPosition(position));
        }

        private void CreateFootprint(float3 position, float2 halfExtents, float rotationRadians)
        {
            footprintEntity = testWorld.EntityManager.CreateEntity(typeof(NavFootprint), typeof(LocalTransform));
            testWorld.EntityManager.SetComponentData(footprintEntity, new NavFootprint
            {
                halfExtents = halfExtents,
                rotationRadians = rotationRadians,
                cost = WallCost
            });
            testWorld.EntityManager.SetComponentData(footprintEntity, LocalTransform.FromPosition(position));
        }

        private void SetFootprintPosition(float3 position)
        {
            testWorld.EntityManager.SetComponentData(footprintEntity, LocalTransform.FromPosition(position));
        }

        private void CreateTile(int2 coordinate)
        {
            BlobAssetReference<TileNavCostBlob> blob;
            using (BlobBuilder builder = new BlobBuilder(Allocator.Temp))
            {
                ref TileNavCostBlob root = ref builder.ConstructRoot<TileNavCostBlob>();
                root.cellsPerSide = CellsPerTile;
                root.layerCount = 1;
                BlobBuilderArray<byte> costArray = builder.Allocate(ref root.costs, CellsPerTile * CellsPerTile);
                BlobBuilderArray<byte> surfaceArray = builder.Allocate(ref root.surface, CellsPerTile * CellsPerTile);
                for (int cellIndex = 0; cellIndex < CellsPerTile * CellsPerTile; cellIndex++)
                {
                    costArray[cellIndex] = OpenCost;
                    surfaceArray[cellIndex] = 0;
                }
                blob = builder.CreateBlobAssetReference<TileNavCostBlob>(Allocator.Persistent);
            }
            createdBlobs.Add(blob);

            Entity tileEntity = testWorld.EntityManager.CreateEntity(typeof(TileNavCost));
            testWorld.EntityManager.SetComponentData(tileEntity, new TileNavCost { blob = blob, coordinate = coordinate });
        }

        // Resolved through the live gridOrigin so the lookup stays correct after the window slides.
        private byte ReadCostAtWorldPoint(float2 worldPointXZ)
        {
            NavGridConfig gridConfig = testWorld.EntityManager.GetComponentData<NavGridConfig>(navGridHandle);
            NavGridCostMap costMap = testWorld.EntityManager.GetComponentData<NavGridCostMap>(navGridHandle);
            int2 cell = (int2)math.floor((worldPointXZ - gridConfig.gridOrigin.xz) / gridConfig.cellSize);
            Assert.IsTrue(cell.x >= 0 && cell.y >= 0 && cell.x < gridConfig.width && cell.y < gridConfig.height, "point expected inside the window");
            return costMap.costs[cell.y * gridConfig.width + cell.x];
        }
    }
}
