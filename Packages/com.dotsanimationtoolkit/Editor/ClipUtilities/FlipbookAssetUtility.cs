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

            FlipbookAsset wrappingFlipbook = FindFlipbookWrappingArrayPath(arrayAssetPath);
            if (wrappingFlipbook != null)
            {
                return wrappingFlipbook;
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

        private static FlipbookAsset FindFlipbookWrappingArrayPath(string arrayAssetPath)
        {
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
            return null;
        }

        // Writes every layer of an importer-owned array out as a png and wraps them in a NEW editable flipbook; the source array is never touched.
        public static FlipbookAsset ExtractArrayLayersToEditableFlipbook(Texture2DArray array)
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

            TextureImporter arrayImporter = AssetImporter.GetAtPath(arrayAssetPath) as TextureImporter;
            bool isLinear = arrayImporter != null && !arrayImporter.sRGBTexture;
            string arrayFolder = (Path.GetDirectoryName(arrayAssetPath) ?? "Assets").Replace('\\', '/');
            string framesFolder = arrayFolder + "/" + array.name + "_Frames";
            int layerCount = array.depth;

            // Look up only: creating the names wrapper here would rename the source array's catalog row to "<Array>_Flipbook".
            FlipbookAsset existingFlipbook = FindFlipbookWrappingArrayPath(arrayAssetPath);
            HashSet<string> takenNames = new HashSet<string>();
            string[] frameNames = new string[layerCount];
            string[] pngPaths = new string[layerCount];
            char[] invalidFileNameCharacters = Path.GetInvalidFileNameChars();

            try
            {
                if (!AssetDatabase.IsValidFolder(framesFolder))
                {
                    AssetDatabase.CreateFolder(arrayFolder, array.name + "_Frames");
                }

                RenderTextureReadWrite readWrite = isLinear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB;
                RenderTexture previousActive = RenderTexture.active;

                AssetDatabase.StartAssetEditing();
                try
                {
                    for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
                    {
                        EditorUtility.DisplayProgressBar(
                            "Extracting array layers", array.name + " layer " + layerIndex, (float)layerIndex / layerCount);

                        FlipbookFrame existingFrame = existingFlipbook != null ? existingFlipbook.FindFrameByLayerIndex(layerIndex) : null;
                        string rawName = existingFrame != null && !string.IsNullOrEmpty(existingFrame.name)
                            ? existingFrame.name
                            : layerIndex.ToString();
                        foreach (char invalidCharacter in invalidFileNameCharacters)
                        {
                            rawName = rawName.Replace(invalidCharacter, '_');
                        }

                        string frameName = FlipbookValidation.DedupeFrameName(rawName, takenNames);
                        takenNames.Add(frameName);
                        frameNames[layerIndex] = frameName;

                        RenderTexture layerTarget = RenderTexture.GetTemporary(
                            array.width, array.height, 0, RenderTextureFormat.ARGB32, readWrite);
                        Texture2D layerReadback = new Texture2D(
                            array.width, array.height, TextureFormat.RGBA32, false, isLinear);
                        try
                        {
                            Graphics.Blit(array, layerTarget, layerIndex, 0);
                            RenderTexture.active = layerTarget;
                            layerReadback.ReadPixels(new Rect(0f, 0f, array.width, array.height), 0, 0);
                            layerReadback.Apply();

                            pngPaths[layerIndex] = framesFolder + "/" + frameName + ".png";
                            File.WriteAllBytes(pngPaths[layerIndex], layerReadback.EncodeToPNG());
                        }
                        finally
                        {
                            RenderTexture.active = previousActive;
                            RenderTexture.ReleaseTemporary(layerTarget);
                            Object.DestroyImmediate(layerReadback);
                        }
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                FlipbookAsset extractedFlipbook = ScriptableObject.CreateInstance<FlipbookAsset>();
                extractedFlipbook.layerSize = new Vector2Int(array.width, array.height);
                extractedFlipbook.texture = null;
                extractedFlipbook.frames = new List<FlipbookFrame>();
                if (arrayImporter != null)
                {
                    extractedFlipbook.filterMode = arrayImporter.filterMode;
                    extractedFlipbook.wrapMode = arrayImporter.wrapMode;
                    extractedFlipbook.generateMips = arrayImporter.mipmapEnabled;
                }

                extractedFlipbook.linear = isLinear;
                extractedFlipbook.outputPath = arrayFolder + "/" + array.name + "_Editable_Array.asset";

                for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
                {
                    EditorUtility.DisplayProgressBar(
                        "Importing extracted layers", pngPaths[layerIndex], (float)layerIndex / layerCount);

                    AssetDatabase.ImportAsset(pngPaths[layerIndex], ImportAssetOptions.ForceSynchronousImport);
                    TextureImporter pngImporter = AssetImporter.GetAtPath(pngPaths[layerIndex]) as TextureImporter;
                    if (pngImporter != null)
                    {
                        pngImporter.textureType = TextureImporterType.Default;
                        pngImporter.sRGBTexture = !isLinear;
                        pngImporter.isReadable = true;
                        pngImporter.npotScale = TextureImporterNPOTScale.None;
                        pngImporter.textureCompression = TextureImporterCompression.Uncompressed;
                        pngImporter.mipmapEnabled = false;
                        if (arrayImporter != null)
                        {
                            pngImporter.filterMode = arrayImporter.filterMode;
                            pngImporter.wrapMode = arrayImporter.wrapMode;
                        }

                        pngImporter.SaveAndReimport();
                    }

                    extractedFlipbook.frames.Add(new FlipbookFrame
                    {
                        name = frameNames[layerIndex],
                        index = layerIndex,
                        source = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPaths[layerIndex])
                    });
                }

                string editablePath = AssetDatabase.GenerateUniqueAssetPath(arrayFolder + "/" + array.name + "_Editable.asset");
                AssetDatabase.CreateAsset(extractedFlipbook, editablePath);
                AssetDatabase.SaveAssetIfDirty(extractedFlipbook);

                return extractedFlipbook;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
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
