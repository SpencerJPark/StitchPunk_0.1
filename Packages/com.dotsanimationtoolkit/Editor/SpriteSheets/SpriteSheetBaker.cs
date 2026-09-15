// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.IO;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Bakes a SpriteSheetAsset's frames, in list order, into one grid PNG imported as a Texture2DArray at its outputPath.</summary>
    public sealed class SpriteSheetBaker
    {
        public const string OutputFilePrefix = "T_";
        public const string OutputFileSuffix = "_Array.png";

        private static readonly string[] OverridablePlatformNames =
        {
            "Standalone", "iPhone", "Android", "WebGL", "Windows Store Apps", "PS4", "PS5",
            "XboxOne", "GameCoreXboxOne", "GameCoreScarlett", "Nintendo Switch", "tvOS", "VisionOS", "Server"
        };

        private sealed class DecodedSourcePixels
        {
            public Color32[] pixels;
            public long fileWriteTicks;
        }

        private readonly Dictionary<string, DecodedSourcePixels> sourceCache = new Dictionary<string, DecodedSourcePixels>();

        // "<folder>/CitizenHead.asset" -> "<folder>/T_CitizenHead_Array.png", the existing array naming convention.
        public static string DefaultOutputPathFor(string sheetAssetPath)
        {
            string directory = Path.GetDirectoryName(sheetAssetPath);
            string folder = string.IsNullOrEmpty(directory) ? "Assets" : directory.Replace('\\', '/');
            return folder + "/" + OutputFilePrefix + Path.GetFileNameWithoutExtension(sheetAssetPath) + OutputFileSuffix;
        }

        public void ClearSourceCache()
        {
            sourceCache.Clear();
        }

        public bool Bake(SpriteSheetAsset sheet, out string error)
        {
            error = string.Empty;

            if (sheet == null || sheet.frames == null || sheet.frames.Count == 0)
            {
                error = "The sheet has no frames.";
                return false;
            }

            List<string> incompleteFrameNames = new List<string>();
            for (int frameIndex = 0; frameIndex < sheet.frames.Count; frameIndex++)
            {
                SpriteSheetFrame frame = sheet.frames[frameIndex];
                if (frame == null)
                {
                    incompleteFrameNames.Add("(null frame at index " + frameIndex + ")");
                }
                else if (frame.source == null)
                {
                    incompleteFrameNames.Add(frame.name);
                }
            }
            if (incompleteFrameNames.Count > 0)
            {
                error = "These frames have no source texture: " + string.Join(", ", incompleteFrameNames);
                return false;
            }

            Vector2Int expectedSize = new Vector2Int(sheet.frames[0].source.width, sheet.frames[0].source.height);

            List<(string name, Vector2Int size)> frameSizes = new List<(string name, Vector2Int size)>(sheet.frames.Count);
            for (int frameIndex = 0; frameIndex < sheet.frames.Count; frameIndex++)
            {
                SpriteSheetFrame frame = sheet.frames[frameIndex];
                frameSizes.Add((frame.name, new Vector2Int(frame.source.width, frame.source.height)));
            }

            List<string> mismatchedFrameNames = SpriteSheetValidation.FindSizeMismatches(frameSizes, expectedSize);
            if (mismatchedFrameNames.Count > 0)
            {
                error = "These frames are not " + expectedSize.x + "x" + expectedSize.y + ": " +
                        string.Join(", ", mismatchedFrameNames);
                return false;
            }

            string outputPath = sheet.outputPath;
            if (string.IsNullOrEmpty(outputPath) || !outputPath.StartsWith("Assets/") || !outputPath.EndsWith(".png"))
            {
                error = "The output path must be under Assets/ and end with .png.";
                return false;
            }

            string outputFolder = Path.GetDirectoryName(outputPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(outputFolder) || !AssetDatabase.IsValidFolder(outputFolder))
            {
                error = "The output folder \"" + outputFolder + "\" does not exist.";
                return false;
            }

            Color32[][] frameLayerPixels = new Color32[sheet.frames.Count][];
            for (int frameIndex = 0; frameIndex < sheet.frames.Count; frameIndex++)
            {
                Color32[] framePixels = GetFramePixels(sheet.frames[frameIndex].source, expectedSize, out string pixelError);
                if (framePixels == null)
                {
                    error = pixelError;
                    return false;
                }
                frameLayerPixels[frameIndex] = framePixels;
            }

            int frameCount = sheet.frames.Count;
            int columns = Mathf.CeilToInt(Mathf.Sqrt(frameCount));
            int rows = Mathf.CeilToInt(frameCount / (float)columns);

            Texture2D gridTexture = new Texture2D(columns * expectedSize.x, rows * expectedSize.y, TextureFormat.RGBA32, false, true);
            Color32[] transparentPixels = new Color32[gridTexture.width * gridTexture.height];
            gridTexture.SetPixels32(transparentPixels);

            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                int column = frameIndex % columns;
                int row = frameIndex / columns;
                // Texture2D rows count from the bottom, so row r sits at y = (rows - 1 - r) * height.
                gridTexture.SetPixels32(
                    column * expectedSize.x, (rows - 1 - row) * expectedSize.y, expectedSize.x, expectedSize.y, frameLayerPixels[frameIndex]);
            }

            byte[] pngBytes = gridTexture.EncodeToPNG();
            Object.DestroyImmediate(gridTexture);

            Object existingMainAsset = AssetDatabase.LoadMainAssetAtPath(outputPath);
            if (existingMainAsset != null && !(AssetImporter.GetAtPath(outputPath) is TextureImporter))
            {
                error = "\"" + outputPath + "\" already holds a " + existingMainAsset.GetType().Name + ", not a texture.";
                return false;
            }

            // Re-bakes rewrite the same PNG, so the GUID never changes.
            File.WriteAllBytes(Path.GetFullPath(outputPath), pngBytes);
            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceSynchronousImport);

            TextureImporter outputImporter = AssetImporter.GetAtPath(outputPath) as TextureImporter;
            if (outputImporter == null)
            {
                error = "\"" + outputPath + "\" did not import as a texture.";
                return false;
            }

            if (!ApplyImportSettings(outputImporter, sheet, rows, columns, out string importSettingsError))
            {
                error = importSettingsError;
                return false;
            }

            outputImporter.SaveAndReimport();

            Texture2DArray importedArray = AssetDatabase.LoadAssetAtPath<Texture2DArray>(outputPath);
            if (importedArray == null)
            {
                error = "\"" + outputPath + "\" did not import as a Texture2DArray.";
                return false;
            }

            sheet.texture = importedArray;
            sheet.layerSize = expectedSize;
            for (int frameIndex = 0; frameIndex < sheet.frames.Count; frameIndex++)
            {
                sheet.frames[frameIndex].index = frameIndex;
            }

            return true;
        }

        private static bool ApplyImportSettings(
            TextureImporter outputImporter, SpriteSheetAsset sheet, int rows, int columns, out string error)
        {
            error = string.Empty;

            if (sheet.importSettingsSource != null)
            {
                string referencePath = AssetDatabase.GetAssetPath(sheet.importSettingsSource);
                TextureImporter referenceImporter = AssetImporter.GetAtPath(referencePath) as TextureImporter;
                if (referenceImporter == null)
                {
                    error = "\"" + sheet.importSettingsSource.name +
                            "\" is not an imported texture; pick an array made from a PNG or clear Match import settings of.";
                    return false;
                }

                TextureImporterSettings importSettings = new TextureImporterSettings();
                referenceImporter.ReadTextureSettings(importSettings);
                importSettings.textureShape = TextureImporterShape.Texture2DArray;
                importSettings.flipbookRows = rows;
                importSettings.flipbookColumns = columns;
                outputImporter.SetTextureSettings(importSettings);

                outputImporter.maxTextureSize = referenceImporter.maxTextureSize;
                outputImporter.textureCompression = referenceImporter.textureCompression;
                outputImporter.crunchedCompression = referenceImporter.crunchedCompression;
                outputImporter.compressionQuality = referenceImporter.compressionQuality;

                for (int platformIndex = 0; platformIndex < OverridablePlatformNames.Length; platformIndex++)
                {
                    string platformName = OverridablePlatformNames[platformIndex];
                    TextureImporterPlatformSettings referencePlatform = referenceImporter.GetPlatformTextureSettings(platformName);
                    if (referencePlatform.overridden)
                    {
                        outputImporter.SetPlatformTextureSettings(referencePlatform);
                    }
                    else
                    {
                        outputImporter.ClearPlatformTextureSettings(platformName);
                    }
                }

                return true;
            }

            TextureImporterSettings defaultSettings = new TextureImporterSettings();
            outputImporter.ReadTextureSettings(defaultSettings);
            defaultSettings.textureType = TextureImporterType.Default;
            defaultSettings.textureShape = TextureImporterShape.Texture2DArray;
            defaultSettings.flipbookRows = rows;
            defaultSettings.flipbookColumns = columns;
            defaultSettings.filterMode = sheet.filterMode;
            defaultSettings.wrapMode = sheet.wrapMode;
            defaultSettings.mipmapEnabled = sheet.generateMips;
            defaultSettings.sRGBTexture = !sheet.linear;
            defaultSettings.aniso = 1;
            defaultSettings.alphaIsTransparency = false;
            defaultSettings.readable = false;
            outputImporter.SetTextureSettings(defaultSettings);

            outputImporter.maxTextureSize = 2048;
            outputImporter.textureCompression = TextureImporterCompression.Compressed;
            outputImporter.crunchedCompression = false;
            outputImporter.compressionQuality = 50;

            return true;
        }

        private Color32[] GetFramePixels(Texture2D source, Vector2Int expectedSize, out string error)
        {
            error = string.Empty;

            if (source.isReadable)
            {
                return source.GetPixels32();
            }

            string assetPath = AssetDatabase.GetAssetPath(source);
            DecodedSourcePixels decodedSource = DecodeSource(assetPath, expectedSize);
            if (decodedSource == null)
            {
                error = "Could not read pixels from \"" + assetPath + "\" for frame source \"" + source.name + "\".";
                return null;
            }

            return decodedSource.pixels;
        }

        private DecodedSourcePixels DecodeSource(string assetPath, Vector2Int expectedSize)
        {
            string absolutePath = Path.GetFullPath(assetPath);
            long fileWriteTicks = File.Exists(absolutePath) ? File.GetLastWriteTimeUtc(absolutePath).Ticks : 0L;

            if (sourceCache.TryGetValue(assetPath, out DecodedSourcePixels cachedSource) && cachedSource.fileWriteTicks == fileWriteTicks)
            {
                return cachedSource;
            }

            DecodedSourcePixels decodedSource = DecodeByteExact(absolutePath, expectedSize) ?? DecodeViaBlit(assetPath, expectedSize);
            if (decodedSource == null)
            {
                return null;
            }

            decodedSource.fileWriteTicks = fileWriteTicks;
            sourceCache[assetPath] = decodedSource;
            return decodedSource;
        }

        // Reads the file straight off disk. Byte-exact and immune to import settings, but only
        // trusted when the decode lands on the expected size — otherwise the blit fallback takes over.
        private DecodedSourcePixels DecodeByteExact(string absolutePath, Vector2Int expectedSize)
        {
            if (!File.Exists(absolutePath))
            {
                return null;
            }

            string extension = Path.GetExtension(absolutePath).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
            {
                return null;
            }

            byte[] fileBytes = File.ReadAllBytes(absolutePath);
            Texture2D decodeTarget = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!decodeTarget.LoadImage(fileBytes))
            {
                Object.DestroyImmediate(decodeTarget);
                return null;
            }

            if (decodeTarget.width != expectedSize.x || decodeTarget.height != expectedSize.y)
            {
                Object.DestroyImmediate(decodeTarget);
                return null;
            }

            DecodedSourcePixels decodedSource = new DecodedSourcePixels { pixels = decodeTarget.GetPixels32() };
            Object.DestroyImmediate(decodeTarget);
            return decodedSource;
        }

        // Fallback for formats LoadImage cannot decode. Blits the imported texture through a linear
        // RenderTexture sized to the expected frame size, resolving any decode-size mismatch.
        private DecodedSourcePixels DecodeViaBlit(string assetPath, Vector2Int expectedSize)
        {
            Texture2D importedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (importedTexture == null)
            {
                return null;
            }

            RenderTexture blitTarget = RenderTexture.GetTemporary(
                expectedSize.x, expectedSize.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

            RenderTexture previousActive = RenderTexture.active;
            Graphics.Blit(importedTexture, blitTarget);
            RenderTexture.active = blitTarget;

            Texture2D readbackTexture = new Texture2D(expectedSize.x, expectedSize.y, TextureFormat.RGBA32, false, true);
            readbackTexture.ReadPixels(new Rect(0f, 0f, expectedSize.x, expectedSize.y), 0, 0);
            readbackTexture.Apply();

            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(blitTarget);

            DecodedSourcePixels decodedSource = new DecodedSourcePixels { pixels = readbackTexture.GetPixels32() };
            Object.DestroyImmediate(readbackTexture);
            return decodedSource;
        }
    }
}
