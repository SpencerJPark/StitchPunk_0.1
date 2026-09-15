// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Creates, renames, trashes and saves SpriteSheetAsset assets for the Sprite Sheets tab.</summary>
    public static class SpriteSheetAssetUtility
    {
        public const string SheetFolderPrefsKey = "DotsAnimationToolkit.SpriteSheets.SheetFolder";
        public const string DefaultAssetName = "NewSpriteSheet";

        public static string RecallSheetFolder()
        {
            throw new NotImplementedException();
        }

        public static void RememberSheetFolder(string projectRelativeFolder)
        {
            throw new NotImplementedException();
        }

        public static SpriteSheetAsset CreateSheetWithPrompt()
        {
            throw new NotImplementedException();
        }

        public static SpriteSheetAsset CreateSheet(string assetPath)
        {
            throw new NotImplementedException();
        }

        public static bool RenameSheet(SpriteSheetAsset sheet, string newName)
        {
            throw new NotImplementedException();
        }

        public static bool TrashSheet(SpriteSheetAsset sheet)
        {
            throw new NotImplementedException();
        }

        // The tab edits this in-memory copy so nothing reaches disk until Save (A81 D23 rule).
        public static SpriteSheetAsset CreateWorkingCopy(SpriteSheetAsset loadedSheet)
        {
            throw new NotImplementedException();
        }

        public static void SaveWorkingCopy(SpriteSheetAsset workingCopy, SpriteSheetAsset loadedSheet)
        {
            throw new NotImplementedException();
        }
    }
}
