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
    }
}
