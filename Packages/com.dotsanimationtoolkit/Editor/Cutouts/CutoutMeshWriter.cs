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
        private const string MeshFileSuffix = "_CutoutMesh";

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

            return RecallMeshFolder() + "/" + sourceName + MeshFileSuffix + ".asset";
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

            Mesh existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(outputPath);
            Mesh targetMesh = existingMesh != null ? existingMesh : new Mesh();
            bool built = CutoutMeshBuilder.TryBuild(
                workingCopy.outlinePixels, workingCopy.FrameSize, workingCopy.originPixels, workingCopy.pixelsPerUnit,
                workingCopy.facing, workingCopy.normalMode, workingCopy.roundness, targetMesh, out failureReason);
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
                // "Arm_CutoutMesh" pairs with "Arm_Cutout"; a mesh named anything else gets "_Cutout" appended.
                string cutoutBaseName = meshFileName.EndsWith("Mesh", System.StringComparison.Ordinal)
                    ? meshFileName.Substring(0, meshFileName.Length - "Mesh".Length)
                    : meshFileName + "_Cutout";
                string cutoutPath = AssetDatabase.GenerateUniqueAssetPath(meshFolder + "/" + cutoutBaseName + ".asset");
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
            failureReason = string.Empty;
            return true;
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
