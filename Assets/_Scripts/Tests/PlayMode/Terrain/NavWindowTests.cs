using System.Collections.Generic;
using DotsMovementToolkit;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class NavWindowTests
    {
        private const int WindowCells = 20;
        private const int CellsPerTile = 4;
        private const float TileSizeMetres = 100f;
        private const byte WallCost = 255;

        private World testWorld;
        private SystemHandle navGridHandle;
        private SystemHandle navWindowHandle;
        private Entity focusEntity;
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
        public void FocusInTileZeroZero_CostAtATileCentreEqualsThatTilesBlobValue()
        {
            BuildWorld(true);
            CreateTile(new int2(0, 0), 10);
            CreateTile(new int2(1, 0), 11);
            SetFocusPosition(new float3(0f, 0f, 0f));

            UpdateBothSystems();

            Assert.AreEqual(10, ReadCostAtTileCentre(new int2(0, 0), new int2(0, 0)));
            Assert.AreEqual(11, ReadCostAtTileCentre(new int2(1, 0), new int2(0, 0)));
            // Window min tile is focus - (radius + 1) = (-2, -2), so the origin is that tile's minimum corner.
            NavGridConfig gridConfig = testWorld.EntityManager.GetComponentData<NavGridConfig>(navGridHandle);
            Assert.AreEqual(-250f, gridConfig.gridOrigin.x, 0.001f);
            Assert.AreEqual(-250f, gridConfig.gridOrigin.z, 0.001f);
        }

        [Test]
        public void UnloadedTileIsWall()
        {
            BuildWorld(true);
            CreateTile(new int2(0, 0), 10);
            SetFocusPosition(float3.zero);

            UpdateBothSystems();

            NativeArray<byte> costs = ReadCosts();
            Assert.AreEqual(WallCost, costs[0]);
            // Tile (1,0) exists in the window but has no entity.
            Assert.AreEqual(WallCost, ReadCostAtTileCentre(new int2(1, 0), new int2(0, 0)));
        }

        [Test]
        public void FocusMovesOneTile_OriginMovesByExactlyOneTileAndValuesFollowTheTile()
        {
            BuildWorld(true);
            CreateTile(new int2(0, 0), 10);
            CreateTile(new int2(1, 0), 11);
            SetFocusPosition(float3.zero);
            UpdateBothSystems();
            float originXBefore = testWorld.EntityManager.GetComponentData<NavGridConfig>(navGridHandle).gridOrigin.x;

            SetFocusPosition(new float3(TileSizeMetres, 0f, 0f));
            UpdateBothSystems();

            float originXAfter = testWorld.EntityManager.GetComponentData<NavGridConfig>(navGridHandle).gridOrigin.x;
            Assert.AreEqual(TileSizeMetres, originXAfter - originXBefore, 0.001f);
            int2 newFocusTile = new int2(1, 0);
            Assert.AreEqual(11, ReadCostAtTileCentre(new int2(1, 0), newFocusTile));
            Assert.AreEqual(10, ReadCostAtTileCentre(new int2(0, 0), newFocusTile));
        }

        [Test]
        public void VersionsBumpOncePerRebuildNotPerFrame()
        {
            BuildWorld(true);
            CreateTile(new int2(0, 0), 10);
            SetFocusPosition(float3.zero);
            UpdateNavGridOnly();
            NavGridCostMap before = testWorld.EntityManager.GetComponentData<NavGridCostMap>(navGridHandle);

            UpdateBothSystems();
            NavGridCostMap afterFirst = testWorld.EntityManager.GetComponentData<NavGridCostMap>(navGridHandle);
            Assert.AreEqual(before.costMapVersion + 1, afterFirst.costMapVersion);
            Assert.AreEqual(before.windowVersion + 1, afterFirst.windowVersion);

            UpdateBothSystems();
            NavGridCostMap afterIdle = testWorld.EntityManager.GetComponentData<NavGridCostMap>(navGridHandle);
            Assert.AreEqual(afterFirst.costMapVersion, afterIdle.costMapVersion);
            Assert.AreEqual(afterFirst.windowVersion, afterIdle.windowVersion);

            SetFocusPosition(new float3(TileSizeMetres, 0f, 0f));
            UpdateBothSystems();
            NavGridCostMap afterMove = testWorld.EntityManager.GetComponentData<NavGridCostMap>(navGridHandle);
            Assert.AreEqual(afterIdle.costMapVersion + 1, afterMove.costMapVersion);
            Assert.AreEqual(afterIdle.windowVersion + 1, afterMove.windowVersion);
        }

        [Test]
        public void WindowRebuildInvalidatesCachedFlowFields()
        {
            BuildWorld(true);
            CreateTile(new int2(0, 0), 10);
            SetFocusPosition(float3.zero);
            UpdateBothSystems();

            const int targetCount = 3;
            NativeArray<int> bestCosts = new NativeArray<int>(1, Allocator.Persistent);
            NativeArray<float2> vectors = new NativeArray<float2>(1, Allocator.Persistent);
            NativeArray<FlowFieldSystem.FlowFieldTarget> targets =
                new NativeArray<FlowFieldSystem.FlowFieldTarget>(targetCount, Allocator.Persistent);
            try
            {
                for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                    targets[targetIndex] = new FlowFieldSystem.FlowFieldTarget
                    {
                        gridPosition = new int2(targetIndex, 0),
                        layer = 0,
                        isValid = true
                    };
                Entity flowFieldEntity = testWorld.EntityManager.CreateEntity(typeof(FlowFieldSystem.FlowFieldData));
                testWorld.EntityManager.SetComponentData(flowFieldEntity, new FlowFieldSystem.FlowFieldData
                {
                    bestCosts = bestCosts,
                    vectors = vectors,
                    targets = targets
                });

                UpdateBothSystems();
                for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                    Assert.IsTrue(targets[targetIndex].isValid, "idle update must not invalidate target " + targetIndex);

                SetFocusPosition(new float3(TileSizeMetres, 0f, 0f));
                UpdateBothSystems();

                for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                    Assert.IsFalse(targets[targetIndex].isValid, "rebuild must invalidate target " + targetIndex);
            }
            finally
            {
                bestCosts.Dispose();
                vectors.Dispose();
                targets.Dispose();
            }
        }

        [Test]
        public void ExternalCostRegionsOff_LeavesTheWindowAlone()
        {
            BuildWorld(false);
            CreateTile(new int2(0, 0), 10);
            SetFocusPosition(new float3(TileSizeMetres, 0f, 0f));

            UpdateBothSystems();

            NavGridConfig gridConfig = testWorld.EntityManager.GetComponentData<NavGridConfig>(navGridHandle);
            NavGridCostMap costMap = testWorld.EntityManager.GetComponentData<NavGridCostMap>(navGridHandle);
            Assert.AreEqual(0u, costMap.windowVersion);
            Assert.AreEqual(0f, gridConfig.gridOrigin.x, 0.001f);
            Assert.AreEqual(0f, gridConfig.gridOrigin.z, 0.001f);
        }

        private void BuildWorld(bool externalCostRegions)
        {
            testWorld = new World("NavWindowTests");
            testWorld.EntityManager.CreateSingleton(new NavGridSettings
            {
                width = WindowCells,
                height = WindowCells,
                layerCount = 1,
                cellSize = 1f,
                layerHeight = 3f,
                gridOrigin = float3.zero,
                wallCost = WallCost,
                heavyCost = 50,
                defaultCost = 1,
                externalCostRegions = externalCostRegions
            });
            navGridHandle = testWorld.GetOrCreateSystem<NavGridSystem>();
            navWindowHandle = testWorld.GetOrCreateSystem<NavWindowSystem>();

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

        private void UpdateNavGridOnly()
        {
            navGridHandle.Update(testWorld.Unmanaged);
        }

        private void UpdateBothSystems()
        {
            navGridHandle.Update(testWorld.Unmanaged);
            navWindowHandle.Update(testWorld.Unmanaged);
        }

        private void SetFocusPosition(float3 position)
        {
            testWorld.EntityManager.SetComponentData(focusEntity, LocalTransform.FromPosition(position));
        }

        private void CreateTile(int2 coordinate, byte uniformCost)
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
                    costArray[cellIndex] = uniformCost;
                    surfaceArray[cellIndex] = 0;
                }
                blob = builder.CreateBlobAssetReference<TileNavCostBlob>(Allocator.Persistent);
            }
            createdBlobs.Add(blob);

            Entity tileEntity = testWorld.EntityManager.CreateEntity(typeof(TileNavCost));
            testWorld.EntityManager.SetComponentData(tileEntity, new TileNavCost { blob = blob, coordinate = coordinate });
        }

        private NativeArray<byte> ReadCosts()
        {
            return testWorld.EntityManager.GetComponentData<NavGridCostMap>(navGridHandle).costs;
        }

        // Cell (1,1) inside the tile is clear of the tile's edges, so a one-cell offset error still lands in the same tile.
        private byte ReadCostAtTileCentre(int2 tileCoordinate, int2 focusTile)
        {
            int radiusTiles = 1;
            int tilesPerSide = TerrainNavWindowMath.WindowTilesPerSide(radiusTiles);
            int2 minTile = TerrainNavWindowMath.WindowMinTile(focusTile, radiusTiles);
            bool inWindow = TerrainNavWindowMath.TryGetTileCellOffset(tileCoordinate, minTile, tilesPerSide, CellsPerTile, out int2 cellOffset);
            Assert.IsTrue(inWindow, "tile expected inside the window");
            int2 cell = cellOffset + new int2(1, 1);
            return ReadCosts()[cell.y * WindowCells + cell.x];
        }
    }
}
