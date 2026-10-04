// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.IO;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public static class CutoutMeshWriter
    {
        public const string MeshFolderPrefsKey = "DotsAnimationToolkit.Cutouts.MeshFolder";
        private const string FallbackFolder = "Assets";

        public static string RecallMeshFolder()
        {
            string storedFolder = EditorPrefs.GetString(MeshFolderPrefsKey, string.Empty);
            return !string.IsNullOrEmpty(storedFolder) && AssetDatabase.IsValidFolder(storedFolder)
                ? storedFolder
                : FallbackFolder;
        }

        public static void RememberMeshFolder(string projectRelativeFolder)
        {
            EditorPrefs.SetString(MeshFolderPrefsKey, projectRelativeFolder);
        }

        public static string DefaultMeshPathFor(UnityEngine.Object flipbookOrArray)
        {
            string sourceName = flipbookOrArray != null ? flipbookOrArray.name : "Cutout";
            char[] invalidFileNameCharacters = Path.GetInvalidFileNameChars();
            for (int characterIndex = 0; characterIndex < invalidFileNameCharacters.Length; characterIndex++)
            {
                sourceName = sourceName.Replace(invalidFileNameCharacters[characterIndex].ToString(), string.Empty);
            }

            if (string.IsNullOrWhiteSpace(sourceName))
            {
                sourceName = "Cutout";
            }

            return RecallMeshFolder() + "/" + sourceName + ".asset";
        }

        public static bool TrySave(
            CutoutAsset workingCopy, CutoutAsset existingCutout, out CutoutAsset savedCutout, out string failureReason)
        {
            savedCutout = null;
            if (workingCopy == null)
            {
                failureReason = "Nothing is loaded.";
                return false;
            }

            string outputPath = workingCopy.outputPath;
            if (string.IsNullOrEmpty(outputPath)
                || !outputPath.StartsWith("Assets/", System.StringComparison.Ordinal)
                || !outputPath.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase))
            {
                failureReason = "The output path must be a project path ending in .asset.";
                return false;
            }

            if (workingCopy.ResolveArray() == null)
            {
                failureReason = "The flipbook has no texture array.";
                return false;
            }

            if (workingCopy.outputMesh != null)
            {
                string previousMeshPath = AssetDatabase.GetAssetPath(workingCopy.outputMesh);
                if (!string.IsNullOrEmpty(previousMeshPath) && previousMeshPath != outputPath)
                {
                    if (AssetDatabase.LoadMainAssetAtPath(outputPath) != null)
                    {
                        failureReason = "Another asset already uses that name.";
                        return false;
                    }

                    EnsureFolderExists(Path.GetDirectoryName(outputPath).Replace('\\', '/'));
                    string moveError = AssetDatabase.MoveAsset(previousMeshPath, outputPath);
                    if (!string.IsNullOrEmpty(moveError))
                    {
                        failureReason = moveError;
                        return false;
                    }
                }
            }

            Mesh existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(outputPath);
            Mesh targetMesh = existingMesh != null ? existingMesh : new Mesh();
            bool built = CutoutMeshBuilder.TryBuild(
                workingCopy.outlinePixels, workingCopy.FrameSize, workingCopy.originPixels, workingCopy.pixelsPerUnit,
                workingCopy.facing, workingCopy.normalMode, workingCopy.roundness, workingCopy.innerEdges,
                targetMesh, out failureReason);
            if (!built)
            {
                if (existingMesh == null)
                {
                    UnityEngine.Object.DestroyImmediate(targetMesh);
                }

                return false;
            }

            string meshFolder = Path.GetDirectoryName(outputPath).Replace('\\', '/');
            string meshFileName = Path.GetFileNameWithoutExtension(outputPath);
            if (existingMesh == null)
            {
                targetMesh.name = meshFileName;
                EnsureFolderExists(meshFolder);
                AssetDatabase.CreateAsset(targetMesh, outputPath);
            }
            else
            {
                // Same asset, same GUID: every MeshFilter using it updates.
                EditorUtility.SetDirty(existingMesh);
            }

            workingCopy.outputMesh = targetMesh;
            AssetDatabase.SaveAssetIfDirty(targetMesh);

            if (existingCutout == null)
            {
                CutoutAsset createdCutout = UnityEngine.Object.Instantiate(workingCopy);
                createdCutout.hideFlags = HideFlags.None;
                string cutoutPath = AssetDatabase.GenerateUniqueAssetPath(meshFolder + "/" + meshFileName + "_Cutout.asset");
                createdCutout.name = Path.GetFileNameWithoutExtension(cutoutPath);
                AssetDatabase.CreateAsset(createdCutout, cutoutPath);
                savedCutout = createdCutout;
            }
            else
            {
                workingCopy.name = existingCutout.name;
                EditorUtility.CopySerialized(workingCopy, existingCutout);
                EditorUtility.SetDirty(existingCutout);
                savedCutout = existingCutout;
            }

            AssetDatabase.SaveAssetIfDirty(savedCutout);
            RenameCutoutAssetToMatchMesh(savedCutout, meshFileName);
            failureReason = string.Empty;
            return true;
        }

        private static void RenameCutoutAssetToMatchMesh(CutoutAsset savedCutout, string meshFileName)
        {
            string cutoutPath = AssetDatabase.GetAssetPath(savedCutout);
            string wantedName = meshFileName + "_Cutout";
            if (string.IsNullOrEmpty(cutoutPath) || savedCutout.name == wantedName)
            {
                return;
            }

            string renameError = AssetDatabase.RenameAsset(cutoutPath, wantedName);
            if (!string.IsNullOrEmpty(renameError))
            {
                Debug.LogWarning("Cutout asset was not renamed: " + renameError);
            }
        }

        private static void EnsureFolderExists(string projectRelativeFolder)
        {
            if (AssetDatabase.IsValidFolder(projectRelativeFolder))
            {
                return;
            }

            string parentFolder = Path.GetDirectoryName(projectRelativeFolder).Replace('\\', '/');
            if (!string.IsNullOrEmpty(parentFolder))
            {
                EnsureFolderExists(parentFolder);
            }

            AssetDatabase.CreateFolder(parentFolder, Path.GetFileName(projectRelativeFolder));
        }
    }
}
