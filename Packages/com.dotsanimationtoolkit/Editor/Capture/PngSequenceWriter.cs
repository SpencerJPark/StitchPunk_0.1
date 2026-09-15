// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Capture file naming, folder creation, frame writes and the stills import settings.</summary>
    public static class PngSequenceWriter
    {
        public static string FrameFileName(string captureName, int oneBasedFrameNumber)
        {
            return captureName + "_" + oneBasedFrameNumber.ToString("D4", CultureInfo.InvariantCulture) + ".png";
        }

        public static string GifFileName(string captureName)
        {
            return captureName + ".gif";
        }

        // Returns the absolute path of the created (or existing) folder.
        public static string EnsureOutputFolder(string projectRelativeFolder)
        {
            string normalizedFolder = projectRelativeFolder.Replace('\\', '/').TrimEnd('/');
            string projectRootPath = Path.GetDirectoryName(Application.dataPath);
            string absoluteFolderPath = Path.Combine(projectRootPath, normalizedFolder);
            Directory.CreateDirectory(absoluteFolderPath);
            return absoluteFolderPath;
        }

        // Returns the project-relative path written.
        public static string WriteFile(string projectRelativeFolder, string fileName, byte[] fileBytes)
        {
            string normalizedFolder = projectRelativeFolder.Replace('\\', '/').TrimEnd('/');
            string absoluteFolderPath = EnsureOutputFolder(normalizedFolder);
            string absoluteFilePath = Path.Combine(absoluteFolderPath, fileName);
            File.WriteAllBytes(absoluteFilePath, fileBytes);
            return normalizedFolder + "/" + fileName;
        }

        public static List<string> FindFilesThatWouldBeOverwritten(
            string projectRelativeFolder, string captureName, int frameCount, CaptureOutputFormat format)
        {
            string normalizedFolder = projectRelativeFolder.Replace('\\', '/').TrimEnd('/');
            string projectRootPath = Path.GetDirectoryName(Application.dataPath);
            List<string> existingProjectRelativePaths = new List<string>();

            if (format == CaptureOutputFormat.Gif)
            {
                string gifFileName = GifFileName(captureName);
                string gifAbsolutePath = Path.Combine(projectRootPath, normalizedFolder, gifFileName);
                if (File.Exists(gifAbsolutePath))
                {
                    existingProjectRelativePaths.Add(normalizedFolder + "/" + gifFileName);
                }

                return existingProjectRelativePaths;
            }

            for (int frameIndex = 1; frameIndex <= frameCount; frameIndex++)
            {
                string frameFileName = FrameFileName(captureName, frameIndex);
                string frameAbsolutePath = Path.Combine(projectRootPath, normalizedFolder, frameFileName);
                if (File.Exists(frameAbsolutePath))
                {
                    existingProjectRelativePaths.Add(normalizedFolder + "/" + frameFileName);
                }
            }

            return existingProjectRelativePaths;
        }

        public static void RefreshAndApplyStillImportSettings(IReadOnlyList<string> writtenProjectRelativePaths, bool transparentBackground)
        {
            AssetDatabase.Refresh();

            AssetDatabase.StartAssetEditing();
            try
            {
                for (int pathIndex = 0; pathIndex < writtenProjectRelativePaths.Count; pathIndex++)
                {
                    string writtenPath = writtenProjectRelativePaths[pathIndex];
                    if (!writtenPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    TextureImporter importer = AssetImporter.GetAtPath(writtenPath) as TextureImporter;
                    if (importer == null)
                    {
                        continue;
                    }

                    // Captures are stills, not game textures: keep them lossless and untouched by mip/pot processing.
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.mipmapEnabled = false;
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.alphaIsTransparency = transparentBackground;
                    importer.SaveAndReimport();
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }
    }
}
