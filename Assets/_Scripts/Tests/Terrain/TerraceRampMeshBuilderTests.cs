using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    public class TerraceRampMeshBuilderTests
    {
        private readonly List<Mesh> meshesToDestroy = new List<Mesh>();

        [TearDown]
        public void DestroyMeshes()
        {
            foreach (Mesh mesh in meshesToDestroy)
            {
                Object.DestroyImmediate(mesh);
            }
            meshesToDestroy.Clear();
        }

        private Mesh Build(TerraceRampFootprint ramp, Vector3 tileOrigin)
        {
            Mesh mesh = TerraceRampMeshBuilder.BuildRampMesh(ramp, tileOrigin);
            meshesToDestroy.Add(mesh);
            return mesh;
        }

        [Test]
        public void SurfaceCornersMatchFootprintCornersAtLowerAndUpperHeight()
        {
            TerraceRampFootprint ramp = new TerraceRampFootprint(new Vector2(10f, 20f), 30f, 6f, 9f, 0f, 3f);
            Mesh mesh = Build(ramp, Vector3.zero);
            Vector2[] corners = new Vector2[4];
            TerraceRampMeshBuilder.GetFootprintCornersXZ(ramp, corners);
            Vector3[] vertices = mesh.vertices;
            int[] surfaceIndices = mesh.GetIndices(0);

            for (int cornerIndex = 0; cornerIndex < 4; cornerIndex++)
            {
                float expectedHeight = cornerIndex < 2 ? 0f : 3f;
                bool found = false;
                foreach (int vertexIndex in surfaceIndices)
                {
                    Vector3 vertex = vertices[vertexIndex];
                    if (Mathf.Abs(vertex.x - corners[cornerIndex].x) < 0.001f
                        && Mathf.Abs(vertex.z - corners[cornerIndex].y) < 0.001f)
                    {
                        Assert.AreEqual(expectedHeight, vertex.y, 0.001f, "corner " + cornerIndex);
                        found = true;
                        break;
                    }
                }
                Assert.IsTrue(found, "no surface vertex at corner " + cornerIndex);
            }
        }

        [Test]
        public void EverySurfaceTriangleTiltsByTheRampSlopeAngle()
        {
            TerraceRampFootprint ramp = new TerraceRampFootprint(new Vector2(10f, 20f), 30f, 6f, 9f, 0f, 3f);
            Mesh mesh = Build(ramp, Vector3.zero);
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.GetIndices(0);
            float expectedDegrees = Mathf.Atan(3f / 9f) * Mathf.Rad2Deg;

            Assert.Greater(indices.Length, 0);
            for (int index = 0; index < indices.Length; index += 3)
            {
                Vector3 edgeA = vertices[indices[index + 1]] - vertices[indices[index]];
                Vector3 edgeB = vertices[indices[index + 2]] - vertices[indices[index]];
                Vector3 normal = Vector3.Cross(edgeA, edgeB).normalized;
                Assert.AreEqual(expectedDegrees, Vector3.Angle(normal, Vector3.up), 0.5f);
            }
        }

        [Test]
        public void TileOriginShiftsEveryVertexByMinusItsX()
        {
            TerraceRampFootprint ramp = new TerraceRampFootprint(new Vector2(10f, 20f), 30f, 6f, 9f, 0f, 3f);
            Vector3[] atZero = Build(ramp, Vector3.zero).vertices;
            Vector3[] shifted = Build(ramp, new Vector3(100f, 0f, 0f)).vertices;

            Assert.AreEqual(atZero.Length, shifted.Length);
            for (int index = 0; index < atZero.Length; index++)
            {
                Assert.AreEqual(atZero[index].x - 100f, shifted[index].x, 0.0001f);
                Assert.AreEqual(atZero[index].z, shifted[index].z, 0.0001f);
            }
        }
    }
}
