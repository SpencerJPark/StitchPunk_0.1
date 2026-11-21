using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public class TerraceLevelFieldTests
    {
        private readonly List<TerrainTileData> createdTiles = new List<TerrainTileData>();

        [TearDown]
        public void DestroyCreatedTiles()
        {
            foreach (TerrainTileData tile in createdTiles)
            {
                Object.DestroyImmediate(tile);
            }

            createdTiles.Clear();
        }

        private TerrainTileData CreateTile(Vector2Int coordinate)
        {
            TerrainTileData tile = ScriptableObject.CreateInstance<TerrainTileData>();
            tile.coordinate = coordinate;
            tile.baseTerraceLevels = new byte[TerrainTileData.TerraceCellsPerSide * TerrainTileData.TerraceCellsPerSide];
            createdTiles.Add(tile);
            return tile;
        }

        private static void FillCells(TerrainTileData tile, int minX, int maxX, int minZ, int maxZ, byte level)
        {
            for (int cellZ = minZ; cellZ <= maxZ; cellZ++)
            {
                for (int cellX = minX; cellX <= maxX; cellX++)
                {
                    tile.baseTerraceLevels[cellZ * TerrainTileData.TerraceCellsPerSide + cellX] = level;
                }
            }
        }

        [Test]
        public void ConvexCornerIsRoundedByTheArcAndStraightEdgesAreUntouched()
        {
            const int samplesPerMetre = 8;
            const float radius = 2.5f;
            TerrainTileData tile = CreateTile(Vector2Int.zero);
            FillCells(tile, 40, 99, 40, 99, 1);

            float[] signedDistance = TerraceLevelField.BuildSignedDistance(tile, 1, radius, null, samplesPerMetre);
            int side = TerrainTileData.TerraceCellsPerSide * samplesPerMetre + 1;

            float diagonalCrossingMetres = -1f;
            for (int sampleIndex = 40 * samplesPerMetre - 8; sampleIndex < side - 1; sampleIndex++)
            {
                float before = signedDistance[sampleIndex * side + sampleIndex];
                float after = signedDistance[(sampleIndex + 1) * side + sampleIndex + 1];
                if (before < 0f && after >= 0f)
                {
                    diagonalCrossingMetres = (sampleIndex + before / (before - after)) / samplesPerMetre;
                    break;
                }
            }

            float expectedDiagonalMetres = 42.5f - radius * Mathf.Cos(Mathf.PI / 4f);
            Assert.AreEqual(expectedDiagonalMetres, diagonalCrossingMetres, 0.2f);

            int edgeSampleX = 60 * samplesPerMetre;
            float edgeCrossingMetres = -1f;
            for (int sampleZ = 36 * samplesPerMetre; sampleZ < 44 * samplesPerMetre; sampleZ++)
            {
                float before = signedDistance[sampleZ * side + edgeSampleX];
                float after = signedDistance[(sampleZ + 1) * side + edgeSampleX];
                if (before < 0f && after >= 0f)
                {
                    edgeCrossingMetres = (sampleZ + before / (before - after)) / samplesPerMetre;
                    break;
                }
            }

            Assert.AreEqual(40f, edgeCrossingMetres, 0.2f);
        }

        [Test]
        public void SharedSeamColumnMatchesBetweenNeighbouringTiles()
        {
            const int samplesPerMetre = 2;
            TerrainTileData western = CreateTile(new Vector2Int(0, 0));
            TerrainTileData eastern = CreateTile(new Vector2Int(1, 0));
            FillCells(western, 90, 99, 30, 60, 1);
            FillCells(eastern, 0, 9, 30, 60, 1);
            System.Func<Vector2Int, TerrainTileData> lookup = (Vector2Int coordinate) =>
            {
                if (coordinate == western.coordinate)
                {
                    return western;
                }

                return coordinate == eastern.coordinate ? eastern : null;
            };

            float[] westernField = TerraceLevelField.BuildSignedDistance(western, 1, 1.5f, lookup, samplesPerMetre);
            float[] easternField = TerraceLevelField.BuildSignedDistance(eastern, 1, 1.5f, lookup, samplesPerMetre);

            int side = TerrainTileData.TerraceCellsPerSide * samplesPerMetre + 1;
            for (int sampleZ = 0; sampleZ < side; sampleZ++)
            {
                Assert.AreEqual(
                    westernField[sampleZ * side + side - 1],
                    easternField[sampleZ * side],
                    1e-4f,
                    "seam row " + sampleZ);
            }
        }

        [Test]
        public void ResolvedLevelPrefersHandOverridesAndFallsBackToBase()
        {
            TerrainTileData tile = CreateTile(Vector2Int.zero);
            Assert.AreEqual(0, TerraceLevelField.ResolvedLevel(tile, 5, 5));

            tile.baseTerraceLevels[5 * TerrainTileData.TerraceCellsPerSide + 5] = 2;
            tile.handTerraceLevels = new byte[TerrainTileData.TerraceCellsPerSide * TerrainTileData.TerraceCellsPerSide];
            for (int cellIndex = 0; cellIndex < tile.handTerraceLevels.Length; cellIndex++)
            {
                tile.handTerraceLevels[cellIndex] = TerrainTileData.NoHandTerraceLevel;
            }

            Assert.AreEqual(2, TerraceLevelField.ResolvedLevel(tile, 5, 5));

            tile.handTerraceLevels[5 * TerrainTileData.TerraceCellsPerSide + 5] = 0;
            Assert.AreEqual(0, TerraceLevelField.ResolvedLevel(tile, 5, 5));

            tile.baseTerraceLevels = null;
            tile.handTerraceLevels = null;
            Assert.AreEqual(0, TerraceLevelField.ResolvedLevel(tile, 5, 5));
        }
    }
}
