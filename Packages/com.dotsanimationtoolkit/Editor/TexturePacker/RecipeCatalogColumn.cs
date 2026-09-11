// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Texture Packer's recipe catalog: the shared column configured for TexturePackRecipeAsset, headerless so the sidebar hosts its New/Save/Refresh actions.</summary>
    public sealed class RecipeCatalogColumn : ToolkitCatalogColumn<TexturePackRecipeAsset>
    {
        public event Action<TexturePackRecipeAsset> RecipeSelected;
        public event Action SaveRequested;
        public event Action<TexturePackRecipeAsset, string> RecipeRenameRequested;
        public event Action<TexturePackRecipeAsset> RecipeDeleteRequested;

        public TexturePackRecipeAsset SelectedRecipe => SelectedAsset;

        public RecipeCatalogColumn() : base(BuildOptions())
        {
            // Save sits between New and Refresh, where the sidebar showed it before.
            Button saveButton = ToolkitIcons.MakeIconTextButton(
                RaiseSaveRequested, "d_SaveAs", "Write the current graph into the selected recipe, or into a new one", "Save");
            saveButton.name = "recipes-save-button";
            HeaderActions.Insert(1, saveButton);

            AssetSelected += RaiseRecipeSelected;
            RenameRequested += RaiseRecipeRenameRequested;
            DeleteRequested += RaiseRecipeDeleteRequested;
        }

        public void RescanProject()
        {
            Rescan();
        }

        public void SetSelectedRecipe(TexturePackRecipeAsset recipe)
        {
            Select(recipe);
        }

        private static CatalogColumnOptions<TexturePackRecipeAsset> BuildOptions()
        {
            return new CatalogColumnOptions<TexturePackRecipeAsset>
            {
                elementName = "recipe-catalog-column",
                namePrefix = "recipes",
                title = null,
                newButtonIconName = "d_Toolbar Plus",
                newButtonTooltip = "Create a recipe: choose its name and folder",
                refreshButtonIconName = "d_Refresh",
                refreshButtonTooltip = "Rescan the project for recipes",
                emptyProjectMessage = "No recipes in this project yet. Press New.",
                emptySearchMessage = "No recipes match your search.",
                scan = ScanProjectRecipes,
                secondLine = DescribeRecipe,
                tooltip = DescribeRecipeOutput,
                allowRename = true,
                allowDelete = true,
            };
        }

        private static IReadOnlyList<TexturePackRecipeAsset> ScanProjectRecipes()
        {
            List<TexturePackRecipeAsset> foundRecipes = new List<TexturePackRecipeAsset>();
            string[] recipeGuids = AssetDatabase.FindAssets("t:TexturePackRecipeAsset");
            for (int guidIndex = 0; guidIndex < recipeGuids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(recipeGuids[guidIndex]);
                TexturePackRecipeAsset recipe = AssetDatabase.LoadAssetAtPath<TexturePackRecipeAsset>(assetPath);
                if (recipe != null)
                {
                    foundRecipes.Add(recipe);
                }
            }

            foundRecipes.Sort((firstRecipe, secondRecipe) =>
                string.Compare(firstRecipe.name, secondRecipe.name, StringComparison.OrdinalIgnoreCase));

            return foundRecipes;
        }

        private static string DescribeRecipe(TexturePackRecipeAsset recipe)
        {
            int wiredChannelCount = recipe != null ? recipe.WiredChannelCount : 0;
            string outputFileName = recipe != null && !string.IsNullOrEmpty(recipe.outputAssetPath)
                ? Path.GetFileName(recipe.outputAssetPath)
                : string.Empty;
            return wiredChannelCount.ToString() + " wired · "
                + (string.IsNullOrEmpty(outputFileName) ? "no output yet" : outputFileName);
        }

        private static string DescribeRecipeOutput(TexturePackRecipeAsset recipe)
        {
            return recipe != null ? recipe.outputAssetPath : string.Empty;
        }

        private void RaiseSaveRequested()
        {
            SaveRequested?.Invoke();
        }

        private void RaiseRecipeSelected(TexturePackRecipeAsset recipe)
        {
            RecipeSelected?.Invoke(recipe);
        }

        private void RaiseRecipeRenameRequested(TexturePackRecipeAsset recipe, string newName)
        {
            RecipeRenameRequested?.Invoke(recipe, newName);
        }

        private void RaiseRecipeDeleteRequested(TexturePackRecipeAsset recipe)
        {
            RecipeDeleteRequested?.Invoke(recipe);
        }
    }
}
