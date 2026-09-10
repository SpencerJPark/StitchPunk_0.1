// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.IO;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Creates, renames and trashes <see cref="TexturePackRecipeAsset"/> assets — the write path
    /// behind the Texture Packer tab's recipe catalog.
    /// </summary>
    public static class TexturePackRecipeAssetUtility
    {
        public const string RecipeFolderPrefsKey = "DotsAnimationToolkit.TexturePacker.RecipeFolder";
        public const string DefaultAssetName = "NewTexturePackRecipe";

        /// The last folder a recipe was created, saved or picked in, when it still exists; otherwise "Assets".
        public static string RecallRecipeFolder()
        {
            string storedFolder = EditorPrefs.GetString(RecipeFolderPrefsKey, string.Empty);
            return !string.IsNullOrEmpty(storedFolder) && AssetDatabase.IsValidFolder(storedFolder)
                ? storedFolder
                : "Assets";
        }

        public static void RememberRecipeFolder(string projectRelativeFolder)
        {
            EditorPrefs.SetString(RecipeFolderPrefsKey, projectRelativeFolder);
        }

        /// Asks for a name and folder (starting in the remembered folder) and creates an empty recipe there. Null when the owner cancels.
        public static TexturePackRecipeAsset CreateRecipeWithPrompt()
        {
            string chosenPath = EditorUtility.SaveFilePanelInProject(
                "New texture pack recipe",
                DefaultAssetName,
                "asset",
                "Choose the recipe's name and folder.",
                RecallRecipeFolder());

            return string.IsNullOrEmpty(chosenPath) ? null : CreateRecipe(chosenPath);
        }

        /// Creates an empty recipe at exactly assetPath (must end in .asset, under the project). Null when the path is unusable. What the prompt and the drive both call.
        public static TexturePackRecipeAsset CreateRecipe(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) ||
                !assetPath.StartsWith("Assets/", System.StringComparison.Ordinal) ||
                !assetPath.EndsWith(".asset", System.StringComparison.Ordinal))
            {
                Debug.LogWarning(
                    "[DOTS Animation Toolkit] Texture Packer: Cannot create a recipe at '" + assetPath + "'.");
                return null;
            }

            TexturePackRecipeAsset newRecipe = ScriptableObject.CreateInstance<TexturePackRecipeAsset>();

            AssetDatabase.CreateAsset(newRecipe, assetPath);
            AssetDatabase.SaveAssets();

            string containingFolder = Path.GetDirectoryName(assetPath);
            RememberRecipeFolder(containingFolder != null ? containingFolder.Replace('\\', '/') : "Assets");

            return newRecipe;
        }

        public static bool RenameRecipe(TexturePackRecipeAsset recipe, string newName)
        {
            if (recipe == null || string.IsNullOrWhiteSpace(newName))
            {
                return false;
            }

            string sanitizedName = ClipSetSaveLocation.SanitizeAssetName(newName);
            if (recipe.name == sanitizedName)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(recipe);
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            string failureReason = AssetDatabase.RenameAsset(assetPath, sanitizedName);
            if (!string.IsNullOrEmpty(failureReason))
            {
                Debug.LogWarning(
                    "[DOTS Animation Toolkit] Texture Packer: Could not rename recipe to '" + sanitizedName +
                    "': " + failureReason, recipe);
                return false;
            }

            AssetDatabase.SaveAssets();
            return true;
        }

        public static bool TrashRecipe(TexturePackRecipeAsset recipe)
        {
            if (recipe == null)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(recipe);
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            if (!AssetDatabase.MoveAssetToTrash(assetPath))
            {
                Debug.LogWarning(
                    "[DOTS Animation Toolkit] Texture Packer: Could not move recipe '" + assetPath +
                    "' to the trash.", recipe);
                return false;
            }

            return true;
        }
    }
}
