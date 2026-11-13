using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainCardBatchMeshBuilderTests
    {
        private const float Tolerance = 1e-4f;

        private List<Mesh> createdMeshes;

        [SetUp]
        public void CreateMeshList()
        {
            createdMeshes = new List<Mesh>();
        }

        [TearDown]
        public void DestroyMeshes()
        {
            for (int meshIndex = 0; meshIndex < createdMeshes.Count; meshIndex++)
            {
                if (createdMeshes[meshIndex] != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdMeshes[meshIndex]);
                }
            }
        }

        private static TerrainCard CreateCard(Vector3 basePosition, Vector2 sizeMetres, Rect uvRect)
        {
            TerrainCard card = new TerrainCard();
            card.basePosition = basePosition;
            card.sizeMetres = sizeMetres;
            card.uvRect = uvRect;
            card.tint01 = 0.5f;
            return card;
        }

        private Mesh BuildMesh(List<TerrainCard> cards, Vector3 origin, float yawDegrees, out int vertexCount)
        {
            Mesh mesh = TerrainCardBatchMeshBuilder.Build(cards, origin, yawDegrees, out vertexCount);
            createdMeshes.Add(mesh);
            return mesh;
        }

        [Test]
        public void TopEdgeSitsDirectlyAboveBase_AndOriginIsSubtracted()
        {
            List<TerrainCard> cards = new List<TerrainCard>
            {
                CreateCard(new Vector3(10f, 2f, 5f), new Vector2(2f, 3f), new Rect(0f, 0f, 1f, 1f)),
                CreateCard(new Vector3(-4f, 0.5f, 8f), new Vector2(1f, 1.5f), new Rect(0f, 0f, 1f, 1f))
            };
            Vector3 origin = new Vector3(100f, 1f, -50f);

            Mesh mesh = BuildMesh(cards, origin, 37f, out int vertexCount);

            Assert.AreEqual(8, vertexCount);
            Assert.AreEqual(8, mesh.vertexCount);
            Vector3[] vertices = mesh.vertices;
            for (int cardIndex = 0; cardIndex < cards.Count; cardIndex++)
            {
                int first = cardIndex * 4;
                Vector3 baseCentre = (vertices[first] + vertices[first + 1]) * 0.5f;
                Vector3 expectedBase = cards[cardIndex].basePosition - origin;
                Assert.AreEqual(expectedBase.x, baseCentre.x, Tolerance);
                Assert.AreEqual(expectedBase.y, baseCentre.y, Tolerance);
                Assert.AreEqual(expectedBase.z, baseCentre.z, Tolerance);
                Assert.AreEqual(vertices[first].x, vertices[first + 2].x, Tolerance);
                Assert.AreEqual(vertices[first].z, vertices[first + 2].z, Tolerance);
                Assert.AreEqual(vertices[first + 1].x, vertices[first + 3].x, Tolerance);
                Assert.AreEqual(vertices[first + 1].z, vertices[first + 3].z, Tolerance);
                Assert.AreEqual(cards[cardIndex].sizeMetres.y, vertices[first + 2].y - vertices[first].y, Tolerance);
                Assert.AreEqual(cards[cardIndex].sizeMetres.y, vertices[first + 3].y - vertices[first + 1].y, Tolerance);
            }
        }

        [Test]
        public void CardSizeIsWrittenToUvThree_ForEveryVertexOfTheCard()
        {
            List<TerrainCard> cards = new List<TerrainCard>
            {
                CreateCard(Vector3.zero, new Vector2(2f, 3f), new Rect(0f, 0f, 1f, 1f)),
                CreateCard(new Vector3(5f, 0f, 5f), new Vector2(1f, 1.5f), new Rect(0f, 0f, 1f, 1f))
            };

            Mesh mesh = BuildMesh(cards, Vector3.zero, 0f, out int unusedCount);

            List<Vector2> sizes = new List<Vector2>();
            mesh.GetUVs(3, sizes);
            Assert.AreEqual(8, sizes.Count);
            for (int vertexIndex = 0; vertexIndex < sizes.Count; vertexIndex++)
            {
                Vector2 expected = cards[vertexIndex / 4].sizeMetres;
                Assert.AreEqual(expected.x, sizes[vertexIndex].x, Tolerance);
                Assert.AreEqual(expected.y, sizes[vertexIndex].y, Tolerance);
            }
        }

        [Test]
        public void CameraYaw_RotatesTheWidthAxis()
        {
            List<TerrainCard> cards = new List<TerrainCard>
            {
                CreateCard(Vector3.zero, new Vector2(2f, 1f), new Rect(0f, 0f, 1f, 1f))
            };

            Mesh yawZeroMesh = BuildMesh(cards, Vector3.zero, 0f, out int unusedCountA);
            Vector3 yawZeroWidth = yawZeroMesh.vertices[1] - yawZeroMesh.vertices[0];
            Assert.AreEqual(2f, yawZeroWidth.x, Tolerance);
            Assert.AreEqual(0f, yawZeroWidth.z, Tolerance);

            Mesh yawNinetyMesh = BuildMesh(cards, Vector3.zero, 90f, out int unusedCountB);
            Vector3 yawNinetyWidth = yawNinetyMesh.vertices[1] - yawNinetyMesh.vertices[0];
            Assert.AreEqual(0f, yawNinetyWidth.x, Tolerance);
            Assert.AreEqual(-2f, yawNinetyWidth.z, Tolerance);
        }

        [Test]
        public void UvRect_IsAppliedToUvZero()
        {
            List<TerrainCard> cards = new List<TerrainCard>
            {
                CreateCard(Vector3.zero, Vector2.one, new Rect(0.25f, 0.5f, 0.25f, 0.5f))
            };

            Mesh mesh = BuildMesh(cards, Vector3.zero, 0f, out int unusedCount);

            Vector2[] uvs = mesh.uv;
            Assert.AreEqual(0.25f, uvs[0].x, Tolerance);
            Assert.AreEqual(0.5f, uvs[0].y, Tolerance);
            Assert.AreEqual(0.5f, uvs[3].x, Tolerance);
            Assert.AreEqual(1.0f, uvs[3].y, Tolerance);
        }
    }
}
