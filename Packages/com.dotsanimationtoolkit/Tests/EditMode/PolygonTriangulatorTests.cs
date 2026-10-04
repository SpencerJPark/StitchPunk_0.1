// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public class PolygonTriangulatorTests
    {
        [Test]
        public void EarClip_ConcaveL_ProducesFourTriangles()
        {
            // Notch corner (1,1) sits strictly inside the (0,0)-(3,0)-(0,3) triangle, so skipping the containment check emits a clockwise ear.
            Vector2[] polygon =
            {
                new Vector2(0f, 0f), new Vector2(3f, 0f), new Vector2(3f, 1f),
                new Vector2(1f, 1f), new Vector2(1f, 3f), new Vector2(0f, 3f)
            };
            List<int> triangles = new List<int>();

            bool succeeded = PolygonTriangulator.TryEarClip(polygon, triangles);

            Assert.IsTrue(succeeded);
            Assert.AreEqual(12, triangles.Count);
            float summedArea = 0f;
            for (int triangleStart = 0; triangleStart < triangles.Count; triangleStart += 3)
            {
                Vector2[] corners =
                {
                    polygon[triangles[triangleStart]], polygon[triangles[triangleStart + 1]], polygon[triangles[triangleStart + 2]]
                };
                float triangleArea = PolygonTriangulator.SignedArea(corners);
                Assert.Greater(triangleArea, 0f);
                summedArea += triangleArea;
            }
            Assert.AreEqual(5f, summedArea, 1e-4f);
        }

        [Test]
        public void SelfIntersectingBowtie_IsRejected()
        {
            Vector2[] bowtie = { new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0f, 1f) };
            List<int> triangles = new List<int>();

            Assert.IsTrue(PolygonTriangulator.IsSelfIntersecting(bowtie));
            Assert.IsFalse(PolygonTriangulator.TryEarClip(bowtie, triangles));
        }

        static Vector2[] BuildRegularHexagon()
        {
            Vector2[] hexagon = new Vector2[6];
            for (int vertexIndex = 0; vertexIndex < hexagon.Length; vertexIndex++)
            {
                float angle = Mathf.PI / 3f * vertexIndex;
                hexagon[vertexIndex] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            return hexagon;
        }

        // True when every triangle has all its vertices in 0..3 or all in {3,4,5,0}, so none straddles the 0-3 diagonal.
        static bool NoTriangleStraddlesDiagonalZeroThree(List<int> triangles)
        {
            for (int triangleStart = 0; triangleStart < triangles.Count; triangleStart += 3)
            {
                bool allOnFirstSide = true;
                bool allOnSecondSide = true;
                for (int corner = 0; corner < 3; corner++)
                {
                    int vertexIndex = triangles[triangleStart + corner];
                    allOnFirstSide &= vertexIndex <= 3;
                    allOnSecondSide &= vertexIndex >= 3 || vertexIndex == 0;
                }
                if (!allOnFirstSide && !allOnSecondSide)
                    return false;
            }
            return true;
        }

        [Test]
        public void DrawnDiagonal_IsKeptAsATriangleEdge()
        {
            Vector2[] hexagon = BuildRegularHexagon();
            List<int> plainTriangles = new List<int>();
            List<int> constrainedTriangles = new List<int>();
            List<int> skippedEdgeIndices = new List<int>();
            List<Vector2Int> innerEdges = new List<Vector2Int> { new Vector2Int(0, 3) };

            bool plainSucceeded = PolygonTriangulator.TryEarClip(hexagon, plainTriangles);
            bool constrainedSucceeded = PolygonTriangulator.TryTriangulateWithEdges(hexagon, innerEdges, constrainedTriangles, skippedEdgeIndices);

            Assert.IsTrue(plainSucceeded);
            Assert.IsFalse(NoTriangleStraddlesDiagonalZeroThree(plainTriangles), "plain ear clipping fans from vertex 5 and must cross 0-3");
            Assert.IsTrue(constrainedSucceeded);
            Assert.AreEqual(12, constrainedTriangles.Count);
            Assert.AreEqual(0, skippedEdgeIndices.Count);
            Assert.IsTrue(NoTriangleStraddlesDiagonalZeroThree(constrainedTriangles));
        }

        [Test]
        public void CrossingEdge_IsRefused()
        {
            Vector2[] hexagon = BuildRegularHexagon();
            List<Vector2Int> keptEdges = new List<Vector2Int> { new Vector2Int(0, 3) };

            bool crossingValid = PolygonTriangulator.IsValidInnerEdge(hexagon, keptEdges, 1, 4, out string crossingReason);
            bool neighbourValid = PolygonTriangulator.IsValidInnerEdge(hexagon, keptEdges, 0, 2, out string neighbourReason);

            Assert.IsFalse(crossingValid);
            Assert.IsNotEmpty(crossingReason);
            Assert.IsTrue(neighbourValid);
            Assert.IsEmpty(neighbourReason);
        }
    }
}
