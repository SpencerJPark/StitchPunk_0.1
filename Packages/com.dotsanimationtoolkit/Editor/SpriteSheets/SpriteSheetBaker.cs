// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.IO;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Bakes a SpriteSheetAsset's frames, in list order, into one Texture2DArray asset at its outputPath.</summary>
    public sealed class SpriteSheetBaker
    {
        public const string OutputFilePrefix = "T_";
        public const string OutputFileSuffix = "_Array.asset";

        private sealed class DecodedSourcePixels
        {
            public Color32[] pixels;
            public long fileWriteTicks;
        }

        private readonly Dictionary<string, DecodedSourcePixels> sourceCache = new Dictionary<string, DecodedSourcePixels>();

        // "<folder>/CitizenHead.asset" -> "<folder>/T_CitizenHead_Array.asset", the existing array naming convention.
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
            if (string.IsNullOrEmpty(outputPath) || !outputPath.StartsWith("Assets/") || !outputPath.EndsWith(".asset"))
            {
                error = "The output path must be under Assets/ and end with .asset.";
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

            Texture2DArray textureArray = new Texture2DArray(
                expectedSize.x, expectedSize.y, sheet.frames.Count, TextureFormat.RGBA32, sheet.generateMips, sheet.linear)
            {
                filterMode = sheet.filterMode,
                wrapMode = sheet.wrapMode
            };

            for (int layerIndex = 0; layerIndex < frameLayerPixels.Length; layerIndex++)
            {
                textureArray.SetPixels32(frameLayerPixels[layerIndex], layerIndex, 0);
            }
            textureArray.Apply(sheet.generateMips, false);
            textureArray.name = Path.GetFileNameWithoutExtension(outputPath);

            Texture2DArray existingArray = AssetDatabase.LoadAssetAtPath<Texture2DArray>(outputPath);
            if (existingArray != null)
            {
                EditorUtility.CopySerialized(textureArray, existingArray);
                Object.DestroyImmediate(textureArray);
                EditorUtility.SetDirty(existingArray);
                AssetDatabase.SaveAssetIfDirty(existingArray);
                sheet.texture = existingArray;
            }
            else
            {
                Object existingMainAsset = AssetDatabase.LoadMainAssetAtPath(outputPath);
                if (existingMainAsset != null)
                {
                    Object.DestroyImmediate(textureArray);
                    error = "\"" + outputPath + "\" already holds a " + existingMainAsset.GetType().Name + ", not a Texture2DArray.";
                    return false;
                }

                AssetDatabase.CreateAsset(textureArray, outputPath);
                sheet.texture = textureArray;
            }

            sheet.layerSize = expectedSize;
            for (int frameIndex = 0; frameIndex < sheet.frames.Count; frameIndex++)
            {
                sheet.frames[frameIndex].index = frameIndex;
            }

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
