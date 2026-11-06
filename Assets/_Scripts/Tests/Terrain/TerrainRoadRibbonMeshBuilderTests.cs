using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainRoadRibbonMeshBuilderTests
    {
        private const int RingVertexCount = TerrainRoadRibbonMeshBuilder.RingVertexCount;
        private const int CentreVertexIndex = 3;
        private const float FlatVertexHeight = TerrainTileCreator.GroundTopHeight + 0.02f;

        private static readonly float[] ExpectedOffsets = { -2.1f, -1.6f, -0.7f, 0f, 0.7f, 1.6f, 2.1f };

        private TerrainRoadRibbonStyle style;
        private List<Mesh> createdMeshes;

        [SetUp]
        public void CreateStyleAndMeshList()
        {
            style = ScriptableObject.CreateInstance<TerrainRoadRibbonStyle>();
            createdMeshes = new List<Mesh>();
        }

        [TearDown]
        public void DestroyStyleAndMeshes()
        {
            UnityEngine.Object.DestroyImmediate(style);
            for (int meshIndex = 0; meshIndex < createdMeshes.Count; meshIndex++)
            {
                if (createdMeshes[meshIndex] != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdMeshes[meshIndex]);
                }
            }
        }

        [Test]
        public void StraightRoad_HasExpectedCountsAndIsSymmetricAboutTheCentreline()
        {
            Mesh mesh = BuildMesh(CreateInput(CreateStraightLine(-40f, 40f)), out int vertexCount, out int triangleCount);

            Assert.AreEqual(81, vertexCount / RingVertexCount);
            Assert.AreEqual(567, vertexCount);
            Assert.AreEqual(960, triangleCount);

            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            for (int ringIndex = 0; ringIndex < 81; ringIndex++)
            {
                for (int crossIndex = 0; crossIndex < RingVertexCount; crossIndex++)
                {
                    int mirrorIndex = RingVertexCount - 1 - crossIndex;
                    Vector3 vertex = vertices[ringIndex * RingVertexCount + crossIndex];
                    Vector3 mirror = vertices[ringIndex * RingVertexCount + mirrorIndex];
                    Assert.AreEqual(0f, vertex.z + mirror.z, 1e-4f, "ring " + ringIndex + " vertex " + crossIndex);
                    Assert.AreEqual(vertex.y, mirror.y, 1e-4f);
                    Assert.AreEqual(ExpectedOffsets[crossIndex], uvs[ringIndex * RingVertexCount + crossIndex].x, 1e-4f);
                }
            }
        }

        [Test]
        public void FirstRing_HasTheSpecifiedVertexColours()
        {
            Mesh mesh = BuildMesh(CreateInput(CreateStraightLine(-40f, 40f)), out int vertexCount, out int triangleCount);
            Color32[] colors = mesh.colors32;

            byte[] expectedRed = { 255, 255, 0, 0, 0, 255, 255 };
            byte[] expectedGreen = { 0, 0, 255, 0, 255, 0, 0 };
            byte[] expectedBlue = { 255, 0, 0, 0, 0, 0, 255 };
            for (int crossIndex = 0; crossIndex < RingVertexCount; crossIndex++)
            {
                Assert.AreEqual(expectedRed[crossIndex], colors[crossIndex].r, "red " + crossIndex);
                Assert.AreEqual(expectedGreen[crossIndex], colors[crossIndex].g, "green " + crossIndex);
                Assert.AreEqual(expectedBlue[crossIndex], colors[crossIndex].b, "blue " + crossIndex);
                Assert.AreEqual(255, colors[crossIndex].a, "alpha " + crossIndex);
            }
        }

        [Test]
        public void RoadWithIntegerStartCrossingATileBorder_GivesBothTilesTheSameBorderRing()
        {
            List<Vector2> centreline = CreateStraightLine(-30f, 130f);
            Mesh westMesh = BuildMesh(CreateInput(centreline, new Rect(-50f, -50f, 100f, 100f)), out int westVertexCount, out int westTriangleCount);
            Mesh eastMesh = BuildMesh(CreateInput(centreline, new Rect(50f, -50f, 100f, 100f)), out int eastVertexCount, out int eastTriangleCount);

            Assert.AreEqual(81, westVertexCount / RingVertexCount);
            Assert.AreEqual(81, eastVertexCount / RingVertexCount);
            AssertLastRingEqualsFirstRing(westMesh, eastMesh);
        }

        [Test]
        public void RoadWithNonIntegerStartCrossingATileBorder_InsertsABorderRingInTileZeroAndMatchesTileOne()
        {
            List<Vector2> centreline = CreateStraightLine(-30.5f, 129.5f);
            Mesh westMesh = BuildMesh(CreateInput(centreline, new Rect(-50f, -50f, 100f, 100f)), out int westVertexCount, out int westTriangleCount);
            Mesh eastMesh = BuildMesh(CreateInput(centreline, new Rect(50f, -50f, 100f, 100f)), out int eastVertexCount, out int eastTriangleCount);

            Assert.AreEqual(82, westVertexCount / RingVertexCount);
            AssertLastRingEqualsFirstRing(westMesh, eastMesh);
        }

        [Test]
        public void ClosedLoopCircle_AddsASeamRingAtTheSamePositionWithUvContinuingByTheLoopLength()
        {
            const int segmentCount = 360;
            const float radius = 20f;
            List<Vector2> centreline = new List<Vector2>();
            for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
            {
                float angle = segmentIndex * Mathf.PI * 2f / segmentCount;
                centreline.Add(new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius));
            }
            TerrainRoadRibbonInput input = CreateInput(centreline);
            input.closedLoop = true;

            Mesh mesh = BuildMesh(input, out int vertexCount, out int triangleCount);

            Assert.AreEqual(127, vertexCount / RingVertexCount);
            Assert.AreEqual(889, vertexCount);
            Assert.AreEqual(1512, triangleCount);
            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            int lastCentreIndex = 126 * RingVertexCount + CentreVertexIndex;
            Assert.AreEqual(0f, Vector3.Distance(vertices[CentreVertexIndex], vertices[lastCentreIndex]), 1e-4f);
            Assert.AreEqual(125.66f, uvs[lastCentreIndex].y - uvs[CentreVertexIndex].y, 0.05f);
        }

        [Test]
        public void HundredMetreRoadAndDiagonalRoad_StayWithinTheVertexBudget()
        {
            Mesh straightMesh = BuildMesh(CreateInput(CreateStraightLine(-50f, 50f)), out int straightVertexCount, out int straightTriangleCount);
            Assert.AreEqual(707, straightVertexCount);
            Assert.LessOrEqual(straightVertexCount, TerrainRoadRibbonMeshBuilder.MaxVerticesPerTile);

            List<Vector2> diagonal = new List<Vector2> { new Vector2(-50f, -50f), new Vector2(50f, 50f) };
            Mesh diagonalMesh = BuildMesh(CreateInput(diagonal), out int diagonalVertexCount, out int diagonalTriangleCount);
            Assert.Less(diagonalVertexCount, 1100);
        }

        [Test]
        public void BridgeCut_DropsTheBridgedRingsAndLeavesNoTriangleAcrossTheGap()
        {
            TerrainRoadRibbonInput input = CreateInput(CreateStraightLine(-40f, 40f));
            input.crossingAt = (worldX, worldZ) => Mathf.Abs(worldX) <= 4.5f ? TerrainRibbonCrossingState.Bridge : TerrainRibbonCrossingState.None;

            Mesh mesh = BuildMesh(input, out int vertexCount, out int triangleCount);

            Assert.AreEqual(72, vertexCount / RingVertexCount);
            Assert.AreEqual(840, triangleCount);
            float[] ringCentreXValues = ReadRingCentreXValues(mesh);
            int[] triangles = mesh.triangles;
            for (int triangleStart = 0; triangleStart < triangles.Length; triangleStart += 3)
            {
                bool touchesWest = false;
                bool touchesEast = false;
                for (int corner = 0; corner < 3; corner++)
                {
                    float ringX = ringCentreXValues[triangles[triangleStart + corner] / RingVertexCount];
                    touchesWest |= ringX <= -5f;
                    touchesEast |= ringX >= 5f;
                }
                Assert.IsFalse(touchesWest && touchesEast, "triangle " + triangleStart / 3);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Ford_SetsWetnessAndRutFadeOnlyInsideTheFordFootprint(bool fadeRutsOnFord)
        {
            style.fadeRutsOnFord = fadeRutsOnFord;
            TerrainRoadRibbonInput input = CreateInput(CreateStraightLine(-40f, 40f));
            input.crossingAt = (worldX, worldZ) => Mathf.Abs(worldX) <= 4.5f ? TerrainRibbonCrossingState.Ford : TerrainRibbonCrossingState.None;

            Mesh mesh = BuildMesh(input, out int vertexCount, out int triangleCount);

            float[] ringCentreXValues = ReadRingCentreXValues(mesh);
            Vector2[] secondaryUvs = mesh.uv2;
            for (int ringIndex = 0; ringIndex < ringCentreXValues.Length; ringIndex++)
            {
                bool insideFord = Mathf.Abs(ringCentreXValues[ringIndex]) <= 4.5f;
                Vector2 expected = insideFord ? new Vector2(1f, fadeRutsOnFord ? 0f : 1f) : new Vector2(0f, 1f);
                for (int crossIndex = 0; crossIndex < RingVertexCount; crossIndex++)
                {
                    Vector2 actual = secondaryUvs[ringIndex * RingVertexCount + crossIndex];
                    Assert.AreEqual(expected.x, actual.x, 1e-5f, "ring x " + ringCentreXValues[ringIndex]);
                    Assert.AreEqual(expected.y, actual.y, 1e-5f, "ring x " + ringCentreXValues[ringIndex]);
                }
            }
        }

        [Test]
        public void JunctionPoint_FadesRingOpacityOverThreeMetres()
        {
            TerrainRoadRibbonInput input = CreateInput(CreateStraightLine(-40f, 40f));
            input.junctionPointsXZ = new List<Vector2> { Vector2.zero };

            Mesh mesh = BuildMesh(input, out int vertexCount, out int triangleCount);

            float[] ringCentreXValues = ReadRingCentreXValues(mesh);
            Color32[] colors = mesh.colors32;
            for (int ringIndex = 0; ringIndex < ringCentreXValues.Length; ringIndex++)
            {
                float absoluteX = Mathf.Abs(ringCentreXValues[ringIndex]);
                for (int crossIndex = 0; crossIndex < RingVertexCount; crossIndex++)
                {
                    byte alpha = colors[ringIndex * RingVertexCount + crossIndex].a;
                    if (absoluteX < 0.5f)
                    {
                        Assert.AreEqual(0, alpha);
                    }
                    else if (Mathf.Abs(absoluteX - 1f) < 0.5f)
                    {
                        Assert.AreEqual(85, alpha, 1);
                    }
                    else if (absoluteX >= 3f)
                    {
                        Assert.AreEqual(255, alpha);
                    }
                }
            }
        }

        [Test]
        public void SlopedGround_RaisesVerticesWithTheGroundAndTiltsTheNormals()
        {
            TerrainRoadRibbonInput input = CreateInput(CreateStraightLine(-40f, 40f));
            input.groundHeightAt = (worldX, worldZ) => 0.2f * worldX;

            Mesh mesh = BuildMesh(input, out int vertexCount, out int triangleCount);

            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
            {
                Assert.AreEqual(0.2f * vertices[vertexIndex].x + FlatVertexHeight, vertices[vertexIndex].y, 1e-4f);
                Assert.AreEqual(-0.196f, normals[vertexIndex].x, 0.002f);
                Assert.AreEqual(0.981f, normals[vertexIndex].y, 0.002f);
            }
        }

        [Test]
        public void RoadEntirelyOutsideTheTile_ReturnsNullWithZeroCounts()
        {
            Mesh mesh = TerrainRoadRibbonMeshBuilder.Build(
                CreateInput(CreateStraightLine(200f, 300f)), Vector3.zero, out int vertexCount, out int triangleCount);

            Assert.IsNull(mesh);
            Assert.AreEqual(0, vertexCount);
            Assert.AreEqual(0, triangleCount);
        }

        private static List<Vector2> CreateStraightLine(float startX, float endX)
        {
            return new List<Vector2> { new Vector2(startX, 0f), new Vector2(endX, 0f) };
        }

        private TerrainRoadRibbonInput CreateInput(List<Vector2> centreline)
        {
            return CreateInput(centreline, new Rect(-50f, -50f, 100f, 100f));
        }

        private TerrainRoadRibbonInput CreateInput(List<Vector2> centreline, Rect tileRectXZ)
        {
            TerrainRoadRibbonInput input = new TerrainRoadRibbonInput();
            input.centrelineXZ = centreline;
            input.closedLoop = false;
            input.widthMetres = 4f;
            input.style = style;
            input.tileRectXZ = tileRectXZ;
            input.groundHeightAt = (worldX, worldZ) => 0f;
            input.crossingAt = null;
            input.junctionPointsXZ = null;
            return input;
        }

        private Mesh BuildMesh(TerrainRoadRibbonInput input, out int vertexCount, out int triangleCount)
        {
            Mesh mesh = TerrainRoadRibbonMeshBuilder.Build(input, Vector3.zero, out vertexCount, out triangleCount);
            Assert.IsNotNull(mesh);
            createdMeshes.Add(mesh);
            return mesh;
        }

        private static float[] ReadRingCentreXValues(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            float[] ringCentreXValues = new float[vertices.Length / RingVertexCount];
            for (int ringIndex = 0; ringIndex < ringCentreXValues.Length; ringIndex++)
            {
                ringCentreXValues[ringIndex] = vertices[ringIndex * RingVertexCount + CentreVertexIndex].x;
            }
            return ringCentreXValues;
        }

        private static void AssertLastRingEqualsFirstRing(Mesh westMesh, Mesh eastMesh)
        {
            Vector3[] westVertices = westMesh.vertices;
            Vector3[] eastVertices = eastMesh.vertices;
            int westLastRingStart = westVertices.Length - RingVertexCount;
            for (int crossIndex = 0; crossIndex < RingVertexCount; crossIndex++)
            {
                Assert.AreEqual(50f, westVertices[westLastRingStart + CentreVertexIndex].x, 1e-5f);
                Assert.AreEqual(0f, Vector3.Distance(westVertices[westLastRingStart + crossIndex], eastVertices[crossIndex]), 1e-5f,
                    "vertex " + crossIndex);
            }
        }
    }
}
