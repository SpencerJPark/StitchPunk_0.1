// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.IO;
using UnityEditor;
using UnityEngine;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Creates, renames, trashes and saves SpriteSheetAsset assets for the Sprite Sheets tab.</summary>
    public static class SpriteSheetAssetUtility
    {
        public const string SheetFolderPrefsKey = "DotsAnimationToolkit.SpriteSheets.SheetFolder";
        public const string DefaultAssetName = "NewSpriteSheet";

        // The last folder a sheet was created, saved or picked in, when it still exists; otherwise "Assets".
        public static string RecallSheetFolder()
        {
            string storedFolder = EditorPrefs.GetString(SheetFolderPrefsKey, string.Empty);
            return !string.IsNullOrEmpty(storedFolder) && AssetDatabase.IsValidFolder(storedFolder)
                ? storedFolder
                : "Assets";
        }

        public static void RememberSheetFolder(string projectRelativeFolder)
        {
            EditorPrefs.SetString(SheetFolderPrefsKey, projectRelativeFolder);
        }

        // Asks for a name and folder (starting in the remembered folder) and creates an empty sheet there. Null when the owner cancels.
        public static SpriteSheetAsset CreateSheetWithPrompt()
        {
            string chosenPath = EditorUtility.SaveFilePanelInProject(
                "New sprite sheet",
                DefaultAssetName,
                "asset",
                "Choose the sprite sheet's name and folder.",
                RecallSheetFolder());

            return string.IsNullOrEmpty(chosenPath) ? null : CreateSheet(chosenPath);
        }

        // Creates an empty sheet at exactly assetPath (must end in .asset, under the project). Null when the path is unusable. What the prompt and the drive both call.
        public static SpriteSheetAsset CreateSheet(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) ||
                !assetPath.StartsWith("Assets/", System.StringComparison.Ordinal) ||
                !assetPath.EndsWith(".asset", System.StringComparison.Ordinal))
            {
                Debug.LogWarning(
                    "[DOTS Animation Toolkit] Sprite Sheets: Cannot create a sprite sheet at '" + assetPath + "'.");
                return null;
            }

            SpriteSheetAsset newSheet = ScriptableObject.CreateInstance<SpriteSheetAsset>();

            AssetDatabase.CreateAsset(newSheet, assetPath);

            string containingFolder = Path.GetDirectoryName(assetPath);
            RememberSheetFolder(containingFolder != null ? containingFolder.Replace('\\', '/') : "Assets");

            return newSheet;
        }

        public static bool RenameSheet(SpriteSheetAsset sheet, string newName)
        {
            if (sheet == null || string.IsNullOrWhiteSpace(newName))
            {
                return false;
            }

            string sanitizedName = ClipSetSaveLocation.SanitizeAssetName(newName);
            if (sheet.name == sanitizedName)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(sheet);
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            string failureReason = AssetDatabase.RenameAsset(assetPath, sanitizedName);
            if (!string.IsNullOrEmpty(failureReason))
            {
                Debug.LogWarning(
                    "[DOTS Animation Toolkit] Sprite Sheets: Could not rename sprite sheet to '" + sanitizedName +
                    "': " + failureReason, sheet);
                return false;
            }

            return true;
        }

        public static bool TrashSheet(SpriteSheetAsset sheet)
        {
            if (sheet == null)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(sheet);
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            if (!AssetDatabase.MoveAssetToTrash(assetPath))
            {
                Debug.LogWarning(
                    "[DOTS Animation Toolkit] Sprite Sheets: Could not move sprite sheet '" + assetPath +
                    "' to the trash.", sheet);
                return false;
            }

            return true;
        }

        // The sheet that already wraps array, else a new "<ArrayName>_Sheet.asset" beside it with frames named by layer number.
        public static SpriteSheetAsset GetOrCreateSheetForArray(Texture2DArray array)
        {
            return null;
        }

        // A HideAndDontSave sheet over array with one numeric frame per layer; nothing touches disk.
        public static SpriteSheetAsset CreateWorkingCopyForArray(Texture2DArray array)
        {
            return null;
        }

        // Appends numeric frames or drops trailing ones so an imported sheet matches its array's depth; returns how many were dropped.
        public static int ReconcileFramesWithArrayDepth(SpriteSheetAsset sheet)
        {
            return 0;
        }

        // The tab edits this in-memory copy so nothing reaches disk until Save (A81 D23 rule).
        public static SpriteSheetAsset CreateWorkingCopy(SpriteSheetAsset loadedSheet)
        {
            if (loadedSheet == null)
            {
                return null;
            }

            SpriteSheetAsset workingCopy = Object.Instantiate(loadedSheet);
            workingCopy.name = loadedSheet.name;
            workingCopy.hideFlags = HideFlags.HideAndDontSave;
            return workingCopy;
        }

        public static void SaveWorkingCopy(SpriteSheetAsset workingCopy, SpriteSheetAsset loadedSheet)
        {
            if (workingCopy == null || loadedSheet == null)
            {
                return;
            }

            string keptName = loadedSheet.name;
            EditorUtility.CopySerialized(workingCopy, loadedSheet);
            loadedSheet.name = keptName;
            loadedSheet.hideFlags = HideFlags.None;

            EditorUtility.SetDirty(loadedSheet);
            AssetDatabase.SaveAssetIfDirty(loadedSheet);

            string assetPath = AssetDatabase.GetAssetPath(loadedSheet);
            string containingFolder = Path.GetDirectoryName(assetPath);
            RememberSheetFolder(containingFolder != null ? containingFolder.Replace('\\', '/') : "Assets");
        }
    }
}
