// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Capture file naming, folder creation, frame writes and the stills import settings.</summary>
    public static class PngSequenceWriter
    {
        public static string FrameFileName(string captureName, int oneBasedFrameNumber) { throw new NotImplementedException(); }

        public static string GifFileName(string captureName) { throw new NotImplementedException(); }

        // Returns the absolute path of the created (or existing) folder.
        public static string EnsureOutputFolder(string projectRelativeFolder) { throw new NotImplementedException(); }

        // Returns the project-relative path written.
        public static string WriteFile(string projectRelativeFolder, string fileName, byte[] fileBytes) { throw new NotImplementedException(); }

        public static List<string> FindFilesThatWouldBeOverwritten(
            string projectRelativeFolder, string captureName, int frameCount, CaptureOutputFormat format)
        {
            throw new NotImplementedException();
        }

        public static void RefreshAndApplyStillImportSettings(IReadOnlyList<string> writtenProjectRelativePaths, bool transparentBackground)
        {
            throw new NotImplementedException();
        }
    }
}
