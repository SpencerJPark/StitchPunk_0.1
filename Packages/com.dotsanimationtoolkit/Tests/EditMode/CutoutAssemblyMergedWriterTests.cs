// Copyright (c) 2026 Spencer Park. All rights reserved.
using System.Collections.Generic;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public class CutoutAssemblyMergedWriterTests
    {
        private readonly List<Object> createdObjects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int objectIndex = 0; objectIndex < createdObjects.Count; objectIndex++)
            {
                Object.DestroyImmediate(createdObjects[objectIndex]);
            }

            createdObjects.Clear();
        }

        private CutoutAssemblyPiece MakePiece(string pieceName, int vertexCount, int drawOrder)
        {
            Mesh pieceMesh = new Mesh();
            Vector3[] vertices = new Vector3[vertexCount];
            int[] triangles = new int[] { 0, 1, 2 };
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                vertices[vertexIndex] = new Vector3(vertexIndex, vertexIndex % 2, 0f);
            }

            pieceMesh.vertices = vertices;
            pieceMesh.triangles = triangles;
            CutoutAsset cutout = ScriptableObject.CreateInstance<CutoutAsset>();
            cutout.outputMesh = pieceMesh;
            createdObjects.Add(pieceMesh);
            createdObjects.Add(cutout);

            CutoutAssemblyPiece piece = new CutoutAssemblyPiece();
            piece.name = pieceName;
            piece.cutout = cutout;
            piece.drawOrder = drawOrder;
            piece.positionWorld = new Vector2(drawOrder, 1f);
            piece.extraRotationDegrees = 30f * drawOrder;
            return piece;
        }

        [Test]
        public void MergedMesh_EveryVertexWeightsFullyToItsPiecesBone()
        {
            List<CutoutAssemblyPiece> pieces = new List<CutoutAssemblyPiece>
            {
                MakePiece("Front", 4, 2),
                MakePiece("Back", 3, 0),
                MakePiece("Middle", 5, 1)
            };
            pieces[1].parentPieceIndex = 0;
            Mesh merged = new Mesh();
            createdObjects.Add(merged);

            bool built = CutoutAssemblyMergedWriter.TryBuildMergedMesh(
                pieces, merged, out Matrix4x4[] boneWorldMatrices, out string failureReason);

            Assert.IsTrue(built, failureReason);
            Assert.AreEqual(12, merged.vertexCount);
            Assert.AreEqual(3, boneWorldMatrices.Length);
            NativeArray<byte> bonesPerVertex = merged.GetBonesPerVertex();
            NativeArray<BoneWeight1> boneWeights = merged.GetAllBoneWeights();
            Assert.AreEqual(12, bonesPerVertex.Length);

            // Emit order is drawOrder ascending: Back (3, bone 1), Middle (5, bone 2), Front (4, bone 0).
            int[] expectedBoneByVertex = { 1, 1, 1, 2, 2, 2, 2, 2, 0, 0, 0, 0 };
            for (int vertexIndex = 0; vertexIndex < 12; vertexIndex++)
            {
                Assert.AreEqual(1, bonesPerVertex[vertexIndex]);
                Assert.AreEqual(expectedBoneByVertex[vertexIndex], boneWeights[vertexIndex].boneIndex);
                Assert.AreEqual(1f, boneWeights[vertexIndex].weight);
            }
        }
    }
}
