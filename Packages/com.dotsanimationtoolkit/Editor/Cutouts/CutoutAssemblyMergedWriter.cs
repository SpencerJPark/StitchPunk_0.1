// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Bakes an assembly's still pieces into one skinned mesh, one bone per piece weighted 100%,
    /// plus the prefab and rig that carry it. Sheet flipbook pieces are refused.
    /// </summary>
    public static class CutoutAssemblyMergedWriter
    {
        private const float DepthPerDrawOrder = -0.001f;
        private const string BodyNodeName = "Body";

        public static bool TryWrite(CutoutAssemblyAsset assembly, out string report)
        {
            if (assembly == null)
            {
                report = "No assembly is loaded.";
                return false;
            }

            if (string.IsNullOrEmpty(assembly.outputFolder)
                || !assembly.outputFolder.StartsWith("Assets", System.StringComparison.Ordinal))
            {
                report = "Pick an output folder inside the project first.";
                return false;
            }

            Mesh builtMesh = new Mesh();
            if (!TryBuildMergedMesh(assembly.pieces, builtMesh, out Matrix4x4[] boneWorldMatrices, out string failureReason))
            {
                Object.DestroyImmediate(builtMesh);
                report = failureReason;
                return false;
            }

            string meshPath = assembly.outputFolder + "/" + assembly.name + "_Merged.asset";
            Mesh savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (savedMesh == null)
            {
                builtMesh.name = assembly.name + "_Merged";
                AssetDatabase.CreateAsset(builtMesh, meshPath);
                savedMesh = builtMesh;
            }
            else
            {
                // Same asset, same GUID: the prefab's renderer keeps its reference.
                savedMesh.Clear();
                EditorUtility.CopySerialized(builtMesh, savedMesh);
                Object.DestroyImmediate(builtMesh);
                EditorUtility.SetDirty(savedMesh);
            }

            GameObject prefabRoot = BuildPrefabHierarchy(assembly, savedMesh, boneWorldMatrices);
            string prefabPath = assembly.outputFolder + "/" + assembly.name + ".prefab";
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            Object.DestroyImmediate(prefabRoot);

            RigAsset rig = SaveRig(assembly, savedPrefab);

            assembly.outputMergedMesh = savedMesh;
            assembly.outputPrefab = savedPrefab;
            assembly.outputRig = rig;
            EditorUtility.SetDirty(assembly);
            AssetDatabase.SaveAssets();

            report = "Merged " + assembly.pieces.Count + " pieces into " + savedMesh.vertexCount
                + " vertices on " + assembly.pieces.Count + " bones.";
            return true;
        }

        public static bool TryBuildMergedMesh(
            IReadOnlyList<CutoutAssemblyPiece> pieces, Mesh target,
            out Matrix4x4[] boneWorldMatrices, out string failureReason)
        {
            boneWorldMatrices = null;
            if (pieces == null || pieces.Count == 0)
            {
                failureReason = "The assembly has no pieces.";
                return false;
            }

            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                CutoutAssemblyPiece piece = pieces[pieceIndex];
                if (piece == null || piece.cutout == null || piece.cutout.outputMesh == null)
                {
                    failureReason = "Piece " + pieceIndex + " has no cutout mesh.";
                    return false;
                }

                if (piece.cutout.IsSheetFlipbook)
                {
                    failureReason = "Merged assemblies take still pieces; put '" + piece.name
                        + "' (a sheet flipbook) in a Layered set.";
                    return false;
                }

                if (!ParentChainReachesRoot(pieces, pieceIndex))
                {
                    failureReason = "Piece '" + piece.name + "' has a bad parent index or a parent cycle.";
                    return false;
                }
            }

            Matrix4x4[] worldMatrices = new Matrix4x4[pieces.Count];
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                worldMatrices[pieceIndex] = PieceWorldMatrix(pieces[pieceIndex]);
            }

            List<int> emitOrder = new List<int>(pieces.Count);
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                int insertAt = emitOrder.Count;
                while (insertAt > 0 && pieces[emitOrder[insertAt - 1]].drawOrder > pieces[pieceIndex].drawOrder)
                {
                    insertAt--;
                }

                emitOrder.Insert(insertAt, pieceIndex);
            }

            int totalVertexCount = 0;
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                totalVertexCount += pieces[pieceIndex].cutout.outputMesh.vertexCount;
            }

            List<Vector3> vertices = new List<Vector3>(totalVertexCount);
            List<Vector3> normals = new List<Vector3>(totalVertexCount);
            List<Vector2> uvs = new List<Vector2>(totalVertexCount);
            List<int> triangles = new List<int>();
            NativeArray<byte> bonesPerVertex = new NativeArray<byte>(totalVertexCount, Allocator.Temp);
            NativeArray<BoneWeight1> boneWeights = new NativeArray<BoneWeight1>(totalVertexCount, Allocator.Temp);

            for (int orderIndex = 0; orderIndex < emitOrder.Count; orderIndex++)
            {
                int pieceIndex = emitOrder[orderIndex];
                Mesh pieceMesh = pieces[pieceIndex].cutout.outputMesh;
                Matrix4x4 worldMatrix = worldMatrices[pieceIndex];
                int vertexOffset = vertices.Count;

                Vector3[] pieceVertices = pieceMesh.vertices;
                Vector3[] pieceNormals = pieceMesh.normals;
                Vector2[] pieceUvs = pieceMesh.uv;
                for (int vertexIndex = 0; vertexIndex < pieceVertices.Length; vertexIndex++)
                {
                    vertices.Add(worldMatrix.MultiplyPoint3x4(pieceVertices[vertexIndex]));
                    normals.Add(pieceNormals.Length == pieceVertices.Length
                        ? worldMatrix.MultiplyVector(pieceNormals[vertexIndex]).normalized
                        : Vector3.back);
                    uvs.Add(pieceUvs.Length == pieceVertices.Length ? pieceUvs[vertexIndex] : Vector2.zero);

                    BoneWeight1 weight = new BoneWeight1();
                    weight.boneIndex = pieceIndex;
                    weight.weight = 1f;
                    boneWeights[vertexOffset + vertexIndex] = weight;
                    bonesPerVertex[vertexOffset + vertexIndex] = 1;
                }

                int[] pieceTriangles = pieceMesh.triangles;
                for (int triangleIndex = 0; triangleIndex < pieceTriangles.Length; triangleIndex++)
                {
                    triangles.Add(pieceTriangles[triangleIndex] + vertexOffset);
                }
            }

            Matrix4x4[] bindPoses = new Matrix4x4[pieces.Count];
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                bindPoses[pieceIndex] = worldMatrices[pieceIndex].inverse;
            }

            target.Clear();
            target.indexFormat = totalVertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            target.SetVertices(vertices);
            target.SetNormals(normals);
            target.SetUVs(0, uvs);
            target.SetTriangles(triangles, 0);
            target.bindposes = bindPoses;
            target.SetBoneWeights(bonesPerVertex, boneWeights);
            target.RecalculateBounds();
            bonesPerVertex.Dispose();
            boneWeights.Dispose();

            boneWorldMatrices = worldMatrices;
            failureReason = string.Empty;
            return true;
        }

        private static Matrix4x4 PieceWorldMatrix(CutoutAssemblyPiece piece)
        {
            Vector3 position = new Vector3(piece.positionWorld.x, piece.positionWorld.y, DepthPerDrawOrder * piece.drawOrder);
            Quaternion rotation = Quaternion.Euler(0f, 0f, piece.extraRotationDegrees);
            return Matrix4x4.TRS(position, rotation, Vector3.one);
        }

        private static bool ParentChainReachesRoot(IReadOnlyList<CutoutAssemblyPiece> pieces, int startIndex)
        {
            int current = pieces[startIndex].parentPieceIndex;
            int stepsLeft = pieces.Count;
            while (current != -1)
            {
                if (current < 0 || current >= pieces.Count || stepsLeft-- <= 0)
                {
                    return false;
                }

                current = pieces[current].parentPieceIndex;
            }

            return true;
        }

        private static GameObject BuildPrefabHierarchy(
            CutoutAssemblyAsset assembly, Mesh mergedMesh, Matrix4x4[] boneWorldMatrices)
        {
            GameObject root = new GameObject(assembly.name);
            Transform[] bones = new Transform[assembly.pieces.Count];
            HashSet<string> usedNames = new HashSet<string>();
            for (int pieceIndex = 0; pieceIndex < bones.Length; pieceIndex++)
            {
                string baseName = string.IsNullOrEmpty(assembly.pieces[pieceIndex].name)
                    ? "Piece" + pieceIndex
                    : assembly.pieces[pieceIndex].name;
                string uniqueName = baseName;
                int suffix = 2;
                while (uniqueName == BodyNodeName || !usedNames.Add(uniqueName))
                {
                    uniqueName = baseName + "_" + suffix++;
                }

                Transform bone = new GameObject(uniqueName).transform;
                bone.SetParent(root.transform, false);
                bone.position = boneWorldMatrices[pieceIndex].GetColumn(3);
                bone.rotation = boneWorldMatrices[pieceIndex].rotation;
                bones[pieceIndex] = bone;
            }

            for (int pieceIndex = 0; pieceIndex < bones.Length; pieceIndex++)
            {
                int parentIndex = assembly.pieces[pieceIndex].parentPieceIndex;
                if (parentIndex >= 0)
                {
                    bones[pieceIndex].SetParent(bones[parentIndex], true);
                }
            }

            GameObject body = new GameObject(BodyNodeName);
            body.transform.SetParent(root.transform, false);
            SkinnedMeshRenderer skinnedRenderer = body.AddComponent<SkinnedMeshRenderer>();
            skinnedRenderer.sharedMesh = mergedMesh;
            skinnedRenderer.bones = bones;
            skinnedRenderer.rootBone = root.transform;
            skinnedRenderer.sharedMaterial = assembly.material;
            return root;
        }

        private static RigAsset SaveRig(CutoutAssemblyAsset assembly, GameObject savedPrefab)
        {
            string rigPath = assembly.outputFolder + "/" + assembly.name + "_Rig.asset";
            RigAsset rig = assembly.outputRig != null ? assembly.outputRig : AssetDatabase.LoadAssetAtPath<RigAsset>(rigPath);
            bool isNewRig = rig == null;
            if (isNewRig)
            {
                rig = ScriptableObject.CreateInstance<RigAsset>();
            }

            rig.sourcePrefab = savedPrefab;
            RigTargetDefinition bodyTarget = null;
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                if (rig.targets[targetIndex] != null && rig.targets[targetIndex].sourceNodePath == BodyNodeName)
                {
                    bodyTarget = rig.targets[targetIndex];
                    break;
                }
            }

            if (bodyTarget == null)
            {
                bodyTarget = new RigTargetDefinition();
                bodyTarget.sourceNodePath = BodyNodeName;
                bodyTarget.displayName = BodyNodeName;
            }

            bodyTarget.kind = TargetKind.VatMesh;
            rig.targets.Clear();
            rig.targets.Add(bodyTarget);
            rig.EnsureStableIds();

            if (isNewRig)
            {
                AssetDatabase.CreateAsset(rig, rigPath);
            }

            EditorUtility.SetDirty(rig);
            return rig;
        }
    }
}
