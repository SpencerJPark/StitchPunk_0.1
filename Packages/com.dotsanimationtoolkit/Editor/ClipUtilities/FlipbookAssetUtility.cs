// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Creates, renames, trashes and saves FlipbookAsset assets for the Flipbooks tab.</summary>
    public static class FlipbookAssetUtility
    {
        public const string FlipbookFolderPrefsKey = "DotsAnimationToolkit.Flipbooks.FlipbookFolder";
        public const string DefaultAssetName = "NewFlipbook";

        // The last folder a flipbook was created, saved or picked in, when it still exists; otherwise "Assets".
        public static string RecallFlipbookFolder()
        {
            string storedFolder = EditorPrefs.GetString(FlipbookFolderPrefsKey, string.Empty);
            return !string.IsNullOrEmpty(storedFolder) && AssetDatabase.IsValidFolder(storedFolder)
                ? storedFolder
                : "Assets";
        }

        public static void RememberFlipbookFolder(string projectRelativeFolder)
        {
            EditorPrefs.SetString(FlipbookFolderPrefsKey, projectRelativeFolder);
        }

        // Asks for a name and folder (starting in the remembered folder) and creates an empty flipbook there. Null when the owner cancels.
        public static FlipbookAsset CreateFlipbookWithPrompt()
        {
            string chosenPath = EditorUtility.SaveFilePanelInProject(
                "New flipbook",
                DefaultAssetName,
                "asset",
                "Choose the flipbook's name and folder.",
                RecallFlipbookFolder());

            return string.IsNullOrEmpty(chosenPath) ? null : CreateFlipbook(chosenPath);
        }

        // Creates an empty flipbook at exactly assetPath (must end in .asset, under the project). Null when the path is unusable. What the prompt and the drive both call.
        public static FlipbookAsset CreateFlipbook(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) ||
                !assetPath.StartsWith("Assets/", System.StringComparison.Ordinal) ||
                !assetPath.EndsWith(".asset", System.StringComparison.Ordinal))
            {
                Debug.LogWarning(
                    "[DOTS Animation Toolkit] Flipbooks: Cannot create a flipbook at '" + assetPath + "'.");
                return null;
            }

            FlipbookAsset newFlipbook = ScriptableObject.CreateInstance<FlipbookAsset>();

            AssetDatabase.CreateAsset(newFlipbook, assetPath);

            string containingFolder = Path.GetDirectoryName(assetPath);
            RememberFlipbookFolder(containingFolder != null ? containingFolder.Replace('\\', '/') : "Assets");

            return newFlipbook;
        }

        public static bool RenameFlipbook(FlipbookAsset flipbook, string newName)
        {
            if (flipbook == null || string.IsNullOrWhiteSpace(newName))
            {
                return false;
            }

            string sanitizedName = ClipSetSaveLocation.SanitizeAssetName(newName);
            if (flipbook.name == sanitizedName)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(flipbook);
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            string failureReason = AssetDatabase.RenameAsset(assetPath, sanitizedName);
            if (!string.IsNullOrEmpty(failureReason))
            {
                Debug.LogWarning(
                    "[DOTS Animation Toolkit] Flipbooks: Could not rename flipbook to '" + sanitizedName +
                    "': " + failureReason, flipbook);
                return false;
            }

            return true;
        }

        public static bool TrashFlipbook(FlipbookAsset flipbook)
        {
            if (flipbook == null)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(flipbook);
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            if (!AssetDatabase.MoveAssetToTrash(assetPath))
            {
                Debug.LogWarning(
                    "[DOTS Animation Toolkit] Flipbooks: Could not move flipbook '" + assetPath +
                    "' to the trash.", flipbook);
                return false;
            }

            return true;
        }

        // The flipbook that already wraps array, else a new "<ArrayName>_Flipbook.asset" beside it with frames named by layer number.
        public static FlipbookAsset GetOrCreateFlipbookForArray(Texture2DArray array)
        {
            if (array == null)
            {
                return null;
            }

            string arrayAssetPath = AssetDatabase.GetAssetPath(array);
            if (string.IsNullOrEmpty(arrayAssetPath))
            {
                return null;
            }

            string[] existingFlipbookGuids = AssetDatabase.FindAssets("t:FlipbookAsset");
            for (int guidIndex = 0; guidIndex < existingFlipbookGuids.Length; guidIndex++)
            {
                string existingFlipbookPath = AssetDatabase.GUIDToAssetPath(existingFlipbookGuids[guidIndex]);
                FlipbookAsset existingFlipbook = AssetDatabase.LoadAssetAtPath<FlipbookAsset>(existingFlipbookPath);
                if (existingFlipbook != null && existingFlipbook.texture != null &&
                    AssetDatabase.GetAssetPath(existingFlipbook.texture) == arrayAssetPath)
                {
                    return existingFlipbook;
                }
            }

            FlipbookAsset flipbook = ScriptableObject.CreateInstance<FlipbookAsset>();
            flipbook.texture = array;
            flipbook.layerSize = new Vector2Int(array.width, array.height);
            flipbook.frames = BuildNumericFramesForDepth(array.depth);

            string containingFolder = Path.GetDirectoryName(arrayAssetPath);
            string normalizedFolder = containingFolder != null ? containingFolder.Replace('\\', '/') : "Assets";
            string desiredPath = normalizedFolder + "/" + array.name + "_Flipbook.asset";
            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(desiredPath);

            AssetDatabase.CreateAsset(flipbook, uniquePath);
            AssetDatabase.SaveAssetIfDirty(flipbook);

            return flipbook;
        }

        // A HideAndDontSave flipbook over array with one numeric frame per layer; nothing touches disk.
        public static FlipbookAsset CreateWorkingCopyForArray(Texture2DArray array)
        {
            if (array == null)
            {
                return null;
            }

            FlipbookAsset workingCopy = ScriptableObject.CreateInstance<FlipbookAsset>();
            workingCopy.name = array.name;
            workingCopy.hideFlags = HideFlags.HideAndDontSave;
            workingCopy.texture = array;
            workingCopy.layerSize = new Vector2Int(array.width, array.height);
            workingCopy.frames = BuildNumericFramesForDepth(array.depth);

            return workingCopy;
        }

        // Appends numeric frames or drops trailing ones so an imported flipbook matches its array's depth; returns how many were dropped.
        public static int ReconcileFramesWithArrayDepth(FlipbookAsset flipbook)
        {
            if (flipbook == null || flipbook.texture == null)
            {
                return 0;
            }

            int arrayDepth = ((Texture2DArray)flipbook.texture).depth;
            if (flipbook.frames == null)
            {
                flipbook.frames = new List<FlipbookFrame>();
            }

            if (flipbook.frames.Count > arrayDepth)
            {
                int removedFrameCount = flipbook.frames.Count - arrayDepth;
                flipbook.frames.RemoveRange(arrayDepth, removedFrameCount);
                return removedFrameCount;
            }

            HashSet<string> takenNames = new HashSet<string>();
            for (int frameIndex = 0; frameIndex < flipbook.frames.Count; frameIndex++)
            {
                takenNames.Add(flipbook.frames[frameIndex].name);
            }

            for (int layerPosition = flipbook.frames.Count; layerPosition < arrayDepth; layerPosition++)
            {
                string dedupedName = FlipbookValidation.DedupeFrameName(layerPosition.ToString(), takenNames);
                takenNames.Add(dedupedName);
                flipbook.frames.Add(new FlipbookFrame { name = dedupedName, index = layerPosition, source = null });
            }

            return 0;
        }

        // One numeric frame per array layer, named by its layer index.
        private static List<FlipbookFrame> BuildNumericFramesForDepth(int layerCount)
        {
            List<FlipbookFrame> frames = new List<FlipbookFrame>();
            for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                frames.Add(new FlipbookFrame { name = layerIndex.ToString(), index = layerIndex, source = null });
            }

            return frames;
        }

        // The tab edits this in-memory copy so nothing reaches disk until Save (A81 D23 rule).
        public static FlipbookAsset CreateWorkingCopy(FlipbookAsset loadedFlipbook)
        {
            if (loadedFlipbook == null)
            {
                return null;
            }

            FlipbookAsset workingCopy = Object.Instantiate(loadedFlipbook);
            workingCopy.name = loadedFlipbook.name;
            workingCopy.hideFlags = HideFlags.HideAndDontSave;
            return workingCopy;
        }

        public static void SaveWorkingCopy(FlipbookAsset workingCopy, FlipbookAsset loadedFlipbook)
        {
            if (workingCopy == null || loadedFlipbook == null)
            {
                return;
            }

            string keptName = loadedFlipbook.name;
            EditorUtility.CopySerialized(workingCopy, loadedFlipbook);
            loadedFlipbook.name = keptName;
            loadedFlipbook.hideFlags = HideFlags.None;

            EditorUtility.SetDirty(loadedFlipbook);
            AssetDatabase.SaveAssetIfDirty(loadedFlipbook);

            string assetPath = AssetDatabase.GetAssetPath(loadedFlipbook);
            string containingFolder = Path.GetDirectoryName(assetPath);
            RememberFlipbookFolder(containingFolder != null ? containingFolder.Replace('\\', '/') : "Assets");
        }
    }
}
