// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Bakes a PackRequest to a PNG on disk or a small in-memory preview, sharing one decode cache between both.</summary>
    public sealed class TexturePackBaker
    {
        private const string LogPrefix = "[DOTS Animation Toolkit] Texture Packer: ";

        private readonly Dictionary<string, PackSourcePixels> sourceCache = new Dictionary<string, PackSourcePixels>();

        public void ClearSourceCache()
        {
            sourceCache.Clear();
        }

        public bool Bake(PackRequest request)
        {
            if (!ValidateRequest(request, out string errorMessage))
            {
                Debug.LogError(LogPrefix + errorMessage);
                return false;
            }

            int width = request.resolution.x;
            int height = request.resolution.y;

            if (!ResolveWiredSources(request, out Dictionary<string, PackSourcePixels> sourcesByPath))
            {
                return false;
            }

            if (!TexturePackMath.ComposePixels(request, sourcesByPath, width, height, out Color32[] packedPixels))
            {
                return false;
            }

            // Drop the alpha channel entirely when nothing needs it — a fully opaque
            // alpha would otherwise double the file size for no information.
            bool needsAlpha = request.channels[PackChannelIndex.Alpha].IsWired ||
                              !Mathf.Approximately(request.channels[PackChannelIndex.Alpha].defaultValue, 1f);
            TextureFormat outputFormat = needsAlpha ? TextureFormat.RGBA32 : TextureFormat.RGB24;

            Texture2D packedTexture = new Texture2D(width, height, outputFormat, false, true);
            packedTexture.SetPixels32(packedPixels);
            packedTexture.Apply();
            byte[] pngBytes = packedTexture.EncodeToPNG();
            Object.DestroyImmediate(packedTexture);

            string absoluteOutputPath = Path.GetFullPath(request.outputAssetPath);
            string outputDirectory = Path.GetDirectoryName(absoluteOutputPath);
            if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            bool isNewAsset = !File.Exists(absoluteOutputPath);
            File.WriteAllBytes(absoluteOutputPath, pngBytes);
            AssetDatabase.ImportAsset(request.outputAssetPath, ImportAssetOptions.ForceUpdate);

            // Only stamp import settings the first time. After that the texture is the
            // user's to configure — a repack must not silently undo their changes.
            if (isNewAsset)
            {
                TextureImporter packedImporter = AssetImporter.GetAtPath(request.outputAssetPath) as TextureImporter;
                if (packedImporter != null)
                {
                    packedImporter.sRGBTexture = false;
                    packedImporter.mipmapEnabled = false;
                    // Block compression (BC/DXT) corrupts hard channel-mask edges into
                    // mismatched blocks — packed data textures must stay uncompressed.
                    packedImporter.textureCompression = TextureImporterCompression.Uncompressed;
                    packedImporter.wrapMode = TextureWrapMode.Repeat;
                    packedImporter.alphaSource = needsAlpha
                        ? TextureImporterAlphaSource.FromInput
                        : TextureImporterAlphaSource.None;
                    packedImporter.SaveAndReimport();
                }
            }

            ClearSourceCache();
            Debug.Log(LogPrefix + "baked " + width + "x" + height + " " + outputFormat +
                      " -> " + request.outputAssetPath + (isNewAsset ? " (new asset)" : " (overwritten in place)"));
            return true;
        }

        public Texture2D BakePreview(PackRequest request, int maxDimension, PackPreviewChannel previewChannel)
        {
            int sourceWidth = Mathf.Max(1, request.resolution.x);
            int sourceHeight = Mathf.Max(1, request.resolution.y);
            float scale = Mathf.Min(1f, (float)maxDimension / Mathf.Max(sourceWidth, sourceHeight));

            int previewWidth = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * scale));
            int previewHeight = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * scale));

            if (!ResolveWiredSources(request, out Dictionary<string, PackSourcePixels> sourcesByPath))
            {
                return null;
            }

            if (!TexturePackMath.ComposePixels(request, sourcesByPath, previewWidth, previewHeight, out Color32[] packedPixels))
            {
                return null;
            }

            if (previewChannel != PackPreviewChannel.RGB)
            {
                TexturePackMath.IsolateChannel(packedPixels, (int)previewChannel - 1);
            }
            else
            {
                // The composite is shown opaque — a packed alpha channel would otherwise
                // punch holes in the thumbnail and hide the colour channels underneath.
                for (int pixelIndex = 0; pixelIndex < packedPixels.Length; pixelIndex++)
                {
                    packedPixels[pixelIndex].a = 255;
                }
            }

            Texture2D previewTexture = new Texture2D(previewWidth, previewHeight, TextureFormat.RGBA32, false, true);
            previewTexture.hideFlags = HideFlags.HideAndDontSave;
            previewTexture.filterMode = FilterMode.Bilinear;
            previewTexture.SetPixels32(packedPixels);
            previewTexture.Apply();
            return previewTexture;
        }

        private bool ResolveWiredSources(PackRequest request, out Dictionary<string, PackSourcePixels> sourcesByPath)
        {
            sourcesByPath = new Dictionary<string, PackSourcePixels>();
            for (int channelIndex = 0; channelIndex < request.channels.Length; channelIndex++)
            {
                PackChannelBinding channelBinding = request.channels[channelIndex];
                if (!channelBinding.IsWired || sourcesByPath.ContainsKey(channelBinding.sourceAssetPath))
                {
                    continue;
                }

                PackSourcePixels sourcePixels = DecodeSource(channelBinding.sourceAssetPath);
                if (sourcePixels == null)
                {
                    return false;
                }

                sourcesByPath[channelBinding.sourceAssetPath] = sourcePixels;
            }

            return true;
        }

        private PackSourcePixels DecodeSource(string assetPath)
        {
            string absolutePath = Path.GetFullPath(assetPath);
            long fileWriteTicks = File.Exists(absolutePath) ? File.GetLastWriteTimeUtc(absolutePath).Ticks : 0L;

            if (sourceCache.TryGetValue(assetPath, out PackSourcePixels cachedSource) && cachedSource.fileWriteTicks == fileWriteTicks)
            {
                return cachedSource;
            }

            PackSourcePixels decodedSource = DecodeByteExact(absolutePath) ?? DecodeViaBlit(assetPath);
            if (decodedSource == null)
            {
                Debug.LogError(LogPrefix + "could not read pixels from " + assetPath +
                               " — it is neither a decodable PNG/JPG nor a texture the Editor can blit.");
                return null;
            }

            decodedSource.fileWriteTicks = fileWriteTicks;
            sourceCache[assetPath] = decodedSource;
            return decodedSource;
        }

        // Reads the file straight off disk. Byte-exact and immune to import settings,
        // but only PNG and JPG can be decoded this way.
        private PackSourcePixels DecodeByteExact(string absolutePath)
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

            PackSourcePixels decodedSource = new PackSourcePixels
            {
                pixels = decodeTarget.GetPixels32(),
                width = decodeTarget.width,
                height = decodeTarget.height
            };
            Object.DestroyImmediate(decodeTarget);
            return decodedSource;
        }

        // Fallback for formats LoadImage cannot decode. Blits the imported texture through a
        // linear RenderTexture, which reads any displayable texture — but the values that come
        // back have passed through the importer, so an sRGB-flagged source is colour-converted.
        private PackSourcePixels DecodeViaBlit(string assetPath)
        {
            Texture2D importedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (importedTexture == null)
            {
                return null;
            }

            RenderTexture blitTarget = RenderTexture.GetTemporary(
                importedTexture.width, importedTexture.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

            RenderTexture previousActive = RenderTexture.active;
            Graphics.Blit(importedTexture, blitTarget);
            RenderTexture.active = blitTarget;

            Texture2D readbackTexture = new Texture2D(importedTexture.width, importedTexture.height, TextureFormat.RGBA32, false, true);
            readbackTexture.ReadPixels(new Rect(0f, 0f, importedTexture.width, importedTexture.height), 0, 0);
            readbackTexture.Apply();

            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(blitTarget);

            PackSourcePixels decodedSource = new PackSourcePixels
            {
                pixels = readbackTexture.GetPixels32(),
                width = readbackTexture.width,
                height = readbackTexture.height
            };
            Object.DestroyImmediate(readbackTexture);

            Debug.LogWarning(LogPrefix + assetPath + " is not a PNG/JPG, so it was read through a " +
                             "RenderTexture blit. Values reflect the texture's import settings (sRGB, compression) " +
                             "rather than the file on disk — re-export as PNG for a byte-exact pack.");
            return decodedSource;
        }

        private bool ValidateRequest(PackRequest request, out string errorMessage)
        {
            if (request.channels == null || request.channels.Length != PackChannelIndex.Count)
            {
                errorMessage = "the job description must carry exactly four channels.";
                return false;
            }

            if (request.resolution.x < 1 || request.resolution.y < 1)
            {
                errorMessage = "output resolution must be at least 1x1.";
                return false;
            }

            if (string.IsNullOrEmpty(request.outputAssetPath))
            {
                errorMessage = "no output path was chosen.";
                return false;
            }

            if (!request.outputAssetPath.StartsWith("Assets/"))
            {
                errorMessage = "the output path must be inside the project's Assets folder.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }
    }
}
