using System;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainSplatCodecTests
    {
        private const int Resolution = 128;
        private const float TileSizeMetres = 100f;
        private const int StampRadiusTexels = 9;

        [Test]
        public void RegionEncodeAfterABrushStamp_EqualsTheFullEncodeEverywhere()
        {
            System.Random random = new System.Random(11);
            Color32[] mask = TerrainSplatCodec.CreateFilledMask(Resolution, TerrainSplatLayer.Dirt);
            for (int discIndex = 0; discIndex < 4; discIndex++)
            {
                StampDisc(mask, random.Next(0, Resolution), random.Next(0, Resolution), 12 + random.Next(0, 10), RandomLayer(random));
            }

            Color32[] signedDistanceField = TerrainSplatCodec.EncodeSignedDistanceField(mask, Resolution, TileSizeMetres);

            Vector2Int[] stampCentres = new Vector2Int[]
            {
                new Vector2Int(random.Next(20, 100), random.Next(20, 100)),
                new Vector2Int(2, 3),
                new Vector2Int(random.Next(20, 100), random.Next(20, 100)),
                new Vector2Int(random.Next(20, 100), random.Next(20, 100)),
                new Vector2Int(60, 60)
            };

            for (int stampIndex = 0; stampIndex < stampCentres.Length; stampIndex++)
            {
                Vector2Int centre = stampCentres[stampIndex];
                if (stampIndex == 4)
                    centre = new Vector2Int(stampCentres[2].x + 5, stampCentres[2].y + 4);

                StampDisc(mask, centre.x, centre.y, StampRadiusTexels, RandomLayer(random));
                int minX = Mathf.Max(0, centre.x - StampRadiusTexels);
                int minY = Mathf.Max(0, centre.y - StampRadiusTexels);
                int maxX = Mathf.Min(Resolution - 1, centre.x + StampRadiusTexels);
                int maxY = Mathf.Min(Resolution - 1, centre.y + StampRadiusTexels);
                RectInt dirtyTexels = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);

                TerrainSplatCodec.EncodeSignedDistanceFieldRegion(mask, Resolution, TileSizeMetres, dirtyTexels, signedDistanceField);

                Color32[] expected = TerrainSplatCodec.EncodeSignedDistanceField(mask, Resolution, TileSizeMetres);
                for (int texelIndex = 0; texelIndex < expected.Length; texelIndex++)
                {
                    Color32 actualTexel = signedDistanceField[texelIndex];
                    Color32 expectedTexel = expected[texelIndex];
                    bool matches = actualTexel.r == expectedTexel.r && actualTexel.g == expectedTexel.g
                        && actualTexel.b == expectedTexel.b && actualTexel.a == expectedTexel.a;
                    Assert.IsTrue(matches,
                        "Stamp " + stampIndex + " texel (" + (texelIndex % Resolution) + ", " + (texelIndex / Resolution)
                        + ") region " + actualTexel + " vs full " + expectedTexel);
                }
            }
        }

        [Test]
        public void RegionEncodeWithAnEmptyRect_ChangesNothing()
        {
            System.Random random = new System.Random(11);
            Color32[] mask = TerrainSplatCodec.CreateFilledMask(Resolution, TerrainSplatLayer.Dirt);
            StampDisc(mask, 50, 50, 20, TerrainSplatLayer.Forest);
            Color32[] signedDistanceField = TerrainSplatCodec.EncodeSignedDistanceField(mask, Resolution, TileSizeMetres);
            Color32[] snapshot = (Color32[])signedDistanceField.Clone();

            StampDisc(mask, 70, 70, StampRadiusTexels, RandomLayer(random));
            TerrainSplatCodec.EncodeSignedDistanceFieldRegion(mask, Resolution, TileSizeMetres, new RectInt(60, 60, 0, 10), signedDistanceField);

            for (int texelIndex = 0; texelIndex < snapshot.Length; texelIndex++)
            {
                Color32 actualTexel = signedDistanceField[texelIndex];
                Color32 expectedTexel = snapshot[texelIndex];
                bool matches = actualTexel.r == expectedTexel.r && actualTexel.g == expectedTexel.g
                    && actualTexel.b == expectedTexel.b && actualTexel.a == expectedTexel.a;
                Assert.IsTrue(matches, "Texel " + texelIndex + " changed under an empty rect");
            }
        }

        private static TerrainSplatLayer RandomLayer(System.Random random)
        {
            return (TerrainSplatLayer)random.Next(0, 4);
        }

        private static void StampDisc(Color32[] mask, int centreX, int centreY, int radiusTexels, TerrainSplatLayer layer)
        {
            Color32 colour = TerrainSplatCodec.ColourOfLayer(layer);
            int radiusSquared = radiusTexels * radiusTexels;
            for (int texelY = Mathf.Max(0, centreY - radiusTexels); texelY <= Mathf.Min(Resolution - 1, centreY + radiusTexels); texelY++)
            {
                for (int texelX = Mathf.Max(0, centreX - radiusTexels); texelX <= Mathf.Min(Resolution - 1, centreX + radiusTexels); texelX++)
                {
                    int deltaX = texelX - centreX;
                    int deltaY = texelY - centreY;
                    if (deltaX * deltaX + deltaY * deltaY <= radiusSquared)
                        mask[texelY * Resolution + texelX] = colour;
                }
            }
        }
    }
}
