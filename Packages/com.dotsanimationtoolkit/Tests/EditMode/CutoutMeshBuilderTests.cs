// Copyright (c) 2026 Spencer Park. All rights reserved.
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public class CutoutMeshBuilderTests
    {
        private static readonly Vector2[] FullQuadOutline =
        {
            new Vector2(0f, 0f), new Vector2(100f, 0f), new Vector2(100f, 200f), new Vector2(0f, 200f)
        };

        private Mesh mesh;

        [SetUp]
        public void SetUp()
        {
            mesh = new Mesh();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(mesh);
        }

        private void BuildQuad(CutoutFacing facing, CutoutNormalMode normalMode, float roundness)
        {
            bool built = CutoutMeshBuilder.TryBuild(FullQuadOutline, new Vector2Int(100, 200), new Vector2(50f, 0f),
                100f, facing, normalMode, roundness, mesh, out string failureReason);
            Assert.IsTrue(built, failureReason);
        }

        [Test]
        public void Quad_OriginAtBottomCentre_SitsOnZero()
        {
            BuildQuad(CutoutFacing.NegativeZ, CutoutNormalMode.Flat, 0f);
            Vector3[] vertices = mesh.vertices;
            float minimumX = float.MaxValue;
            float minimumY = float.MaxValue;
            float maximumY = float.MinValue;
            foreach (Vector3 vertex in vertices)
            {
                minimumX = Mathf.Min(minimumX, vertex.x);
                minimumY = Mathf.Min(minimumY, vertex.y);
                maximumY = Mathf.Max(maximumY, vertex.y);
            }
            Assert.AreEqual(0f, minimumY, 1e-5f);
            Assert.AreEqual(-0.5f, minimumX, 1e-5f);
            Assert.AreEqual(2f, maximumY, 1e-5f);
        }

        [Test]
        public void FacingNegativeZ_NormalsPointNegativeZ()
        {
            BuildQuad(CutoutFacing.NegativeZ, CutoutNormalMode.Flat, 0f);
            foreach (Vector3 normal in mesh.normals)
            {
                Assert.AreEqual(new Vector3(0f, 0f, -1f), normal);
            }
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            Assert.Greater(triangles.Length, 0);
            for (int index = 0; index < triangles.Length; index += 3)
            {
                Vector3 vertexA = vertices[triangles[index]];
                Vector3 vertexB = vertices[triangles[index + 1]];
                Vector3 vertexC = vertices[triangles[index + 2]];
                Assert.Less(Vector3.Cross(vertexB - vertexA, vertexC - vertexA).z, 0f);
            }
        }

        [Test]
        public void Rounded_CornerNormalsLeanAwayFromCentroid()
        {
            BuildQuad(CutoutFacing.NegativeZ, CutoutNormalMode.Rounded, 1f);
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector3 centroid = Vector3.zero;
            foreach (Vector3 vertex in vertices)
            {
                centroid += vertex;
            }
            centroid /= vertices.Length;
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector2 outward = (Vector2)(vertices[index] - centroid);
                Assert.Greater(Vector2.Dot(new Vector2(normals[index].x, normals[index].y), outward), 0f);
                Assert.Less(normals[index].z, 0f);
                Assert.AreEqual(45f, Vector3.Angle(normals[index], new Vector3(0f, 0f, -1f)), 0.5f);
            }
        }
    }
}
