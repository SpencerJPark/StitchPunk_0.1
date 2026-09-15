// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.IO;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Bakes a SpriteSheetAsset's frames, in list order, into one Texture2DArray asset at its outputPath.</summary>
    public sealed class SpriteSheetBaker
    {
        public const string OutputFilePrefix = "T_";
        public const string OutputFileSuffix = "_Array.asset";

        // "Assets/Heads/CitizenHead.asset" -> "Assets/Heads/T_CitizenHead_Array.asset", the game's existing array naming.
        public static string DefaultOutputPathFor(string sheetAssetPath)
        {
            string directory = Path.GetDirectoryName(sheetAssetPath);
            string folder = string.IsNullOrEmpty(directory) ? "Assets" : directory.Replace('\\', '/');
            return folder + "/" + OutputFilePrefix + Path.GetFileNameWithoutExtension(sheetAssetPath) + OutputFileSuffix;
        }

        public bool Bake(SpriteSheetAsset sheet, out string error)
        {
            throw new NotImplementedException();
        }

        public void ClearSourceCache()
        {
        }
    }
}
