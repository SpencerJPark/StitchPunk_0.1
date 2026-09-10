// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Makes double-clicking a <see cref="TexturePackRecipeAsset"/> open the Clip Editor on its Texture Packer tab with that recipe loaded.</summary>
    // Mirrors CutsceneAssetOpener — one entry path per asset kind, never a second that can disagree about which one is open.
    internal static class TexturePackRecipeAssetOpener
    {
        [OnOpenAsset]
        private static bool OnOpenTexturePackRecipeAsset(EntityId entityId, int line)
        {
            TexturePackRecipeAsset openedRecipe = EditorUtility.EntityIdToObject(entityId) as TexturePackRecipeAsset;
            if (openedRecipe == null)
            {
                return false;
            }

            ClipEditorWindow.FocusTexturePackerTab(openedRecipe);
            return true;
        }
    }
}
