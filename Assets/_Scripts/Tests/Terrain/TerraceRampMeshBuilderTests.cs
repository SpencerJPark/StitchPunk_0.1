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

        [Test]
        public void SideWallsFaceAwayFromCentreLineAndMeasureDepthDownFromTheTopEdge()
        {
            TerraceRampFootprint ramp = new TerraceRampFootprint(new Vector2(10f, 20f), 30f, 6f, 9f, 0f, 3f);
            Mesh mesh = Build(ramp, Vector3.zero);
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.GetTriangles(1);
            Vector3 centre = new Vector3(ramp.centre.x, 0f, ramp.centre.y);

            Assert.Greater(triangles.Length, 0);
            for (int index = 0; index < triangles.Length; index += 3)
            {
                Vector3 first = vertices[triangles[index]];
                Vector3 second = vertices[triangles[index + 1]];
                Vector3 third = vertices[triangles[index + 2]];
                Vector3 faceNormal = Vector3.Cross(second - first, third - first);
                if (faceNormal.sqrMagnitude < 1e-8f)
                {
                    continue;
                }
                Vector3 outwardFromCentreLine = new Vector3(first.x - centre.x, 0f, first.z - centre.z);
                Vector3 forward = new Vector3(Mathf.Sin(30f * Mathf.Deg2Rad), 0f, Mathf.Cos(30f * Mathf.Deg2Rad));
                outwardFromCentreLine -= forward * Vector3.Dot(outwardFromCentreLine, forward);
                Assert.Greater(Vector3.Dot(faceNormal, outwardFromCentreLine), 0f, "wall triangle winds toward the ramp");
                Assert.Greater(Vector3.Dot(normals[triangles[index]], outwardFromCentreLine), 0f, "wall vertex normal points toward the ramp");
            }

            const int surfaceVertexCount = 7 * 10;
            for (int vertexIndex = surfaceVertexCount; vertexIndex < vertices.Length; vertexIndex += 2)
            {
                Assert.AreEqual(0f, uvs[vertexIndex].x, 1e-4f, "top edge vertex");
                Assert.AreEqual(vertices[vertexIndex].y - vertices[vertexIndex + 1].y, uvs[vertexIndex + 1].x, 1e-4f, "bottom vertex uv.x is metres down");
            }
        }

        [Test]
        public void TerracedTileEmitsNoWallVertexInsideARampFootprint()
        {
            TerrainTileData tile = ScriptableObject.CreateInstance<TerrainTileData>();
            TerrainWorldProfile world = ScriptableObject.CreateInstance<TerrainWorldProfile>();
            tile.EnsureHeightArrays();
            tile.baseTerraceLevels = new byte[TerrainTileData.TerraceCellsPerSide * TerrainTileData.TerraceCellsPerSide];
            for (int cellZ = 30; cellZ <= 69; cellZ++)
            {
                for (int cellX = 30; cellX <= 69; cellX++)
                {
                    tile.baseTerraceLevels[cellZ * TerrainTileData.TerraceCellsPerSide + cellX] = 1;
                }
            }
            // Ramp mouth straddles the plateau's south edge (world z = -20) at an angle so no footprint edge lands on a subcell line.
            TerraceRampFootprint ramp = new TerraceRampFootprint(new Vector2(0.13f, -20f), 7f, 5.3f, 8f, 0f, 3f);
            Mesh tileMesh = TerraceTileMeshBuilder.BuildTerracedTileMesh(tile, world, null, new List<TerraceRampFootprint> { ramp }, 0f);
            meshesToDestroy.Add(tileMesh);

            Vector2[] corners = new Vector2[4];
            TerraceRampMeshBuilder.GetFootprintCornersXZ(ramp, corners);
            Vector3[] vertices = tileMesh.vertices;
            int[] wallTriangles = tileMesh.GetTriangles(1);
            Assert.Greater(wallTriangles.Length, 0);
            for (int index = 0; index < wallTriangles.Length; index++)
            {
                Vector3 position = vertices[wallTriangles[index]];
                Vector2 worldXZ = new Vector2(position.x, position.z);
                Assert.IsFalse(IsStrictlyInsideQuad(corners, worldXZ), "wall vertex floats inside the ramp footprint at " + worldXZ);
            }

            Object.DestroyImmediate(tile);
            Object.DestroyImmediate(world);
        }

        // Inside by more than a millimetre of every edge; corners wind consistently so all signed distances share a sign.
        private static bool IsStrictlyInsideQuad(Vector2[] corners, Vector2 point)
        {
            int positiveCount = 0;
            int negativeCount = 0;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 edge = corners[(corner + 1) % 4] - corners[corner];
                Vector2 toPoint = point - corners[corner];
                float signedDistance = (edge.x * toPoint.y - edge.y * toPoint.x) / edge.magnitude;
                positiveCount += signedDistance > 0.001f ? 1 : 0;
                negativeCount += signedDistance < -0.001f ? 1 : 0;
            }
            return positiveCount == 4 || negativeCount == 4;
        }
    }
}
