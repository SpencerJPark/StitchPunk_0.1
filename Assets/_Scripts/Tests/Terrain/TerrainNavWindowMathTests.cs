using NUnit.Framework;
using Unity.Mathematics;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainNavWindowMathTests
    {
        private const float TileSizeMetres = 100f;

        [Test]
        public void PositionJustInsideTileEdgeBelongsToCentreTile()
        {
            int2 tile = TerrainNavWindowMath.TileCoordinateOf(new float3(49.9f, 0f, -49.9f), TileSizeMetres);
            Assert.AreEqual(new int2(0, 0), tile);
        }

        [Test]
        public void PositionJustPastTileEdgeBelongsToNeighbourTile()
        {
            int2 tile = TerrainNavWindowMath.TileCoordinateOf(new float3(50.1f, 0f, 0f), TileSizeMetres);
            Assert.AreEqual(new int2(1, 0), tile);
        }

        [Test]
        public void NegativeAndPositiveCoordinatesRoundToNearestTileCentre()
        {
            int2 tile = TerrainNavWindowMath.TileCoordinateOf(new float3(-150.2f, 0f, 260f), TileSizeMetres);
            Assert.AreEqual(new int2(-2, 3), tile);
        }

        [Test]
        public void WindowOfRadiusOneIsFiveTilesPerSide()
        {
            Assert.AreEqual(5, TerrainNavWindowMath.WindowTilesPerSide(1));
        }

        [Test]
        public void WindowMinTileSitsRadiusPlusOneBelowFocus()
        {
            int2 minTile = TerrainNavWindowMath.WindowMinTile(new int2(4, -2), 1);
            Assert.AreEqual(new int2(2, -4), minTile);
        }

        [Test]
        public void WindowOriginIsTheMinimumCornerOfTheMinTile()
        {
            float3 origin = TerrainNavWindowMath.WindowOrigin(new int2(2, -4), TileSizeMetres, 7f);
            Assert.AreEqual(new float3(150f, 7f, -450f), origin);
        }

        [Test]
        public void FocusTileCentreLandsAtWindowCentreCell()
        {
            int2 focusTile = new int2(4, -2);
            int2 minTile = TerrainNavWindowMath.WindowMinTile(focusTile, 1);
            float3 origin = TerrainNavWindowMath.WindowOrigin(minTile, TileSizeMetres, 0f);
            float3 focusCentre = new float3(400f, 0f, -200f);

            float3 cell = (focusCentre - origin) / 1f;

            Assert.AreEqual(250f, cell.x, 0.0001f);
            Assert.AreEqual(250f, cell.z, 0.0001f);
        }

        [Test]
        public void MinTileMapsToCellOffsetZero()
        {
            bool inside = TerrainNavWindowMath.TryGetTileCellOffset(new int2(2, -4), new int2(2, -4), 5, 100, out int2 offset);
            Assert.IsTrue(inside);
            Assert.AreEqual(new int2(0, 0), offset);
        }

        [Test]
        public void FarCornerTileMapsToLastTileCellOffset()
        {
            bool inside = TerrainNavWindowMath.TryGetTileCellOffset(new int2(6, 0), new int2(2, -4), 5, 100, out int2 offset);
            Assert.IsTrue(inside);
            Assert.AreEqual(new int2(400, 400), offset);
        }

        [Test]
        public void TileBeyondFarEdgeIsOutsideWindow()
        {
            bool inside = TerrainNavWindowMath.TryGetTileCellOffset(new int2(7, 0), new int2(2, -4), 5, 100, out int2 offset);
            Assert.IsFalse(inside);
        }

        [Test]
        public void TileBeforeMinEdgeIsOutsideWindow()
        {
            bool inside = TerrainNavWindowMath.TryGetTileCellOffset(new int2(1, -4), new int2(2, -4), 5, 100, out int2 offset);
            Assert.IsFalse(inside);
        }
    }
}
