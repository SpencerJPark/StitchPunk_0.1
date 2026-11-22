// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.IO;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Writes a layered cutout assembly as a prefab with one mesh node per piece, plus a rig
    /// with one target per node. Positions and rotations are absolute in assembly space.
    /// </summary>
    public static class CutoutAssemblyLayeredWriter
    {
        private const float DepthPerDrawOrderStep = 0.001f;

        public static bool TryWrite(CutoutAssemblyAsset assembly, out string report)
        {
            if (!TryValidate(assembly, out report))
            {
                return false;
            }

            string prefabPath = assembly.outputFolder.TrimEnd('/') + "/" + assembly.name + ".prefab";
            string rigPath = assembly.outputFolder.TrimEnd('/') + "/" + assembly.name + "_Rig.asset";
            EnsureFolderExists(assembly.outputFolder);

            GameObject rootObject = new GameObject(assembly.name);
            GameObject[] nodes = new GameObject[assembly.pieces.Count];
            HashSet<string> usedNames = new HashSet<string>();
            for (int pieceIndex = 0; pieceIndex < nodes.Length; pieceIndex++)
            {
                CutoutAssemblyPiece piece = assembly.pieces[pieceIndex];
                GameObject node = new GameObject(MakeUniqueName(piece.name, usedNames));
                node.transform.SetPositionAndRotation(
                    new Vector3(piece.positionWorld.x, piece.positionWorld.y, WorldZForDrawOrder(piece.drawOrder)),
                    Quaternion.Euler(0f, 0f, piece.extraRotationDegrees));
                node.AddComponent<MeshFilter>().sharedMesh = piece.cutout.outputMesh;
                node.AddComponent<MeshRenderer>().sharedMaterial = assembly.material;
                node.transform.SetParent(rootObject.transform, true);
                nodes[pieceIndex] = node;
            }
            for (int pieceIndex = 0; pieceIndex < nodes.Length; pieceIndex++)
            {
                int parentIndex = assembly.pieces[pieceIndex].parentPieceIndex;
                if (parentIndex >= 0)
                {
                    nodes[pieceIndex].transform.SetParent(nodes[parentIndex].transform, true);
                }
            }

            string[] nodePaths = new string[nodes.Length];
            string[] nodeNames = new string[nodes.Length];
            for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
            {
                nodePaths[nodeIndex] = PrefabAuthoringBridge.GetHierarchyPath(nodes[nodeIndex].transform, rootObject.transform);
                nodeNames[nodeIndex] = nodes[nodeIndex].name;
            }

            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(rootObject, prefabPath);
            Object.DestroyImmediate(rootObject);

            RigAsset rig = assembly.outputRig;
            if (rig == null)
            {
                rig = AssetDatabase.LoadAssetAtPath<RigAsset>(rigPath);
            }
            if (rig == null)
            {
                rig = ScriptableObject.CreateInstance<RigAsset>();
                AssetDatabase.CreateAsset(rig, rigPath);
            }
            rig.sourcePrefab = savedPrefab;
            RebuildRigTargets(rig, assembly, nodePaths, nodeNames);
            rig.EnsureStableIds();

            assembly.outputPrefab = savedPrefab;
            assembly.outputRig = rig;
            EditorUtility.SetDirty(rig);
            EditorUtility.SetDirty(assembly);
            AssetDatabase.SaveAssets();

            report = "Built " + assembly.name + ".prefab with " + nodes.Length + " parts and its rig.";
            if (assembly.material == null)
            {
                report += " Warning: no material is set, so the parts have none.";
            }
            return true;
        }

        private static void RebuildRigTargets(RigAsset rig, CutoutAssemblyAsset assembly, string[] nodePaths, string[] nodeNames)
        {
            List<RigTargetDefinition> previousTargets = rig.targets ?? new List<RigTargetDefinition>();
            List<RigTargetDefinition> rebuiltTargets = new List<RigTargetDefinition>(nodePaths.Length);
            for (int nodeIndex = 0; nodeIndex < nodePaths.Length; nodeIndex++)
            {
                RigTargetDefinition target = FindTargetByPath(previousTargets, nodePaths[nodeIndex]);
                if (target == null)
                {
                    target = new RigTargetDefinition();
                    target.sourceNodePath = nodePaths[nodeIndex];
                }
                target.displayName = nodeNames[nodeIndex];
                target.kind = assembly.pieces[nodeIndex].cutout.IsSheetFlipbook ? TargetKind.FlipbookPlane : TargetKind.Quad;
                rebuiltTargets.Add(target);
            }
            rig.targets = rebuiltTargets;
        }

        private static RigTargetDefinition FindTargetByPath(List<RigTargetDefinition> targets, string nodePath)
        {
            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            {
                if (targets[targetIndex] != null && targets[targetIndex].sourceNodePath == nodePath)
                {
                    return targets[targetIndex];
                }
            }
            return null;
        }

        private static bool TryValidate(CutoutAssemblyAsset assembly, out string report)
        {
            report = string.Empty;
            if (assembly == null || assembly.pieces == null || assembly.pieces.Count == 0)
            {
                report = "The assembly has no pieces.";
                return false;
            }
            if (string.IsNullOrEmpty(assembly.outputFolder))
            {
                report = "Choose an output folder first.";
                return false;
            }
            for (int pieceIndex = 0; pieceIndex < assembly.pieces.Count; pieceIndex++)
            {
                CutoutAssemblyPiece piece = assembly.pieces[pieceIndex];
                string label = string.IsNullOrEmpty(piece.name) ? "Piece " + pieceIndex : piece.name;
                if (piece.cutout == null || piece.cutout.outputMesh == null)
                {
                    report = "'" + label + "' has no mesh yet — cut it first.";
                    return false;
                }
                if (piece.parentPieceIndex < -1 || piece.parentPieceIndex >= assembly.pieces.Count)
                {
                    report = "'" + label + "' has a parent that does not exist.";
                    return false;
                }
            }
            for (int pieceIndex = 0; pieceIndex < assembly.pieces.Count; pieceIndex++)
            {
                if (ParentChainHasCycle(assembly.pieces, pieceIndex))
                {
                    report = "The parent links form a loop.";
                    return false;
                }
            }
            return true;
        }

        private static bool ParentChainHasCycle(List<CutoutAssemblyPiece> pieces, int startIndex)
        {
            int current = startIndex;
            for (int step = 0; step <= pieces.Count; step++)
            {
                current = pieces[current].parentPieceIndex;
                if (current < 0)
                {
                    return false;
                }
            }
            return true;
        }

        // Higher draw order sits nearer the camera, which looks along +Z, so it gets a more negative z.
        private static float WorldZForDrawOrder(int drawOrder)
        {
            return -DepthPerDrawOrderStep * drawOrder;
        }

        private static string MakeUniqueName(string wantedName, HashSet<string> usedNames)
        {
            string baseName = string.IsNullOrEmpty(wantedName) ? "Piece" : wantedName;
            string candidate = baseName;
            int suffix = 2;
            while (!usedNames.Add(candidate))
            {
                candidate = baseName + " " + suffix;
                suffix++;
            }
            return candidate;
        }

        private static void EnsureFolderExists(string projectFolder)
        {
            if (AssetDatabase.IsValidFolder(projectFolder))
            {
                return;
            }
            Directory.CreateDirectory(projectFolder);
            AssetDatabase.Refresh();
        }
    }
}
