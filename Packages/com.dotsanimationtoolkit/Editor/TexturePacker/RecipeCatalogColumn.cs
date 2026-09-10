// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Catalog column listing project texture pack recipes with search, selection, and new/save/refresh actions.</summary>
    public sealed class RecipeCatalogColumn : VisualElement
    {
        private readonly List<TexturePackRecipeAsset> catalogRecipes = new List<TexturePackRecipeAsset>();
        private readonly List<TexturePackRecipeAsset> filteredRecipes = new List<TexturePackRecipeAsset>();

        private string searchText = string.Empty;
        private readonly ToolbarSearchField searchField;
        private readonly ListView recipesListView;
        private readonly Label emptyLabel;
        private readonly VisualElement headerActions;

        public event Action<TexturePackRecipeAsset> RecipeSelected;
        public event Action NewRequested;
        public event Action SaveRequested;
        public event Action<TexturePackRecipeAsset, string> RecipeRenameRequested;
        public event Action<TexturePackRecipeAsset> RecipeDeleteRequested;

        public VisualElement HeaderActions => headerActions;

        public TexturePackRecipeAsset SelectedRecipe { get; private set; }

        public RecipeCatalogColumn()
        {
            name = "recipe-catalog-column";
            style.flexGrow = 1f;
            style.minWidth = 200f;
            style.paddingTop = 8f;
            style.paddingLeft = 10f;
            style.paddingRight = 10f;

            headerActions = new VisualElement();
            headerActions.AddToClassList("toolkit-pane-actions");

            Button newButton = ToolkitIcons.MakeIconTextButton(
                RaiseNewRequested, "d_Toolbar Plus", "Create a recipe: choose its name and folder", "New");
            newButton.name = "recipes-new-button";
            headerActions.Add(newButton);

            Button saveButton = ToolkitIcons.MakeIconTextButton(
                RaiseSaveRequested, "d_SaveAs", "Write the current graph into the selected recipe, or into a new one", "Save");
            saveButton.name = "recipes-save-button";
            headerActions.Add(saveButton);

            Button refreshButton = ToolkitIcons.MakeIconTextButton(
                RescanProject, "d_Refresh", "Rescan the project for recipes", "Refresh");
            refreshButton.name = "recipes-refresh-button";
            headerActions.Add(refreshButton);

            searchField = new ToolbarSearchField();
            searchField.name = "recipes-search";
            // alignSelf: Stretch alone was not enough -- the field's own internal content
            // (text input + icon + cancel button) imposes a min-content width Yoga still honours
            // over stretch, so it kept overflowing a narrow column regardless of min-width: 0.
            // An explicit percentage width is clamped to the parent's box unconditionally.
            searchField.style.width = new Length(100f, LengthUnit.Percent);
            searchField.style.minWidth = 0f;
            searchField.style.marginTop = 4f;
            searchField.style.marginLeft = 0f;
            searchField.style.marginRight = 0f;
            searchField.RegisterValueChangedCallback(OnSearchTextChanged);
            Add(searchField);

            recipesListView = new ListView();
            recipesListView.name = "recipes-list";
            // DynamicHeight virtualization renders zero rows in this Unity version -- stick with
            // FixedHeight, sized tight to the row's measured content plus its own top/bottom margin.
            recipesListView.fixedItemHeight = 64f;
            recipesListView.selectionType = SelectionType.Single;
            recipesListView.style.flexGrow = 1f;
            recipesListView.style.marginTop = 4f;
            recipesListView.makeItem = MakeRecipeRow;
            recipesListView.bindItem = BindRecipeRow;
            recipesListView.itemsSource = filteredRecipes;
            recipesListView.selectionChanged += OnRecipesListSelectionChanged;
            Add(recipesListView);

            emptyLabel = new Label("No recipes in this project yet. Press New.");
            emptyLabel.AddToClassList("clip-editor__hint");
            Add(emptyLabel);

            RefreshEmptyState();
        }

        public void RescanProject()
        {
            catalogRecipes.Clear();
            string[] recipeGuids = AssetDatabase.FindAssets("t:TexturePackRecipeAsset");
            for (int guidIndex = 0; guidIndex < recipeGuids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(recipeGuids[guidIndex]);
                TexturePackRecipeAsset recipe = AssetDatabase.LoadAssetAtPath<TexturePackRecipeAsset>(assetPath);
                if (recipe != null)
                {
                    catalogRecipes.Add(recipe);
                }
            }

            catalogRecipes.Sort((firstRecipe, secondRecipe) =>
                string.Compare(firstRecipe.name, secondRecipe.name, StringComparison.OrdinalIgnoreCase));

            ApplyFilter();
        }

        public void SetSelectedRecipe(TexturePackRecipeAsset recipe)
        {
            SelectedRecipe = recipe;
            recipesListView.SetSelectionWithoutNotify(
                recipe != null && filteredRecipes.Contains(recipe)
                    ? new List<int> { filteredRecipes.IndexOf(recipe) }
                    : new List<int>());
            recipesListView.Rebuild();
        }

        public void RefreshRows()
        {
            recipesListView.RefreshItems();
        }

        private void RaiseNewRequested()
        {
            NewRequested?.Invoke();
        }

        private void RaiseSaveRequested()
        {
            SaveRequested?.Invoke();
        }

        private VisualElement MakeRecipeRow()
        {
            // ListView (FixedHeight virtualization) tags whatever makeItem returns with its own
            // internal item classes and forcibly zeroes ITS margin to keep the fixed-slot math
            // exact. A margin on this outer slot is a no-op, so the boxed row that actually wants
            // the gap has to live one level deeper, as a plain child Unity's pooling never touches.
            VisualElement itemSlot = new VisualElement();
            // Unity also paints its own hover/selected background straight onto this slot -- an
            // inline override beats that USS state styling unconditionally, so the slot itself
            // never shades and only the boxed row below reacts.
            itemSlot.style.backgroundColor = new StyleColor(Color.clear);

            VisualElement row = new VisualElement();
            row.name = "recipe-row-box";
            row.AddToClassList("toolkit-box");
            row.style.marginTop = 4f;
            row.style.marginBottom = 4f;
            row.style.marginLeft = 0f;
            row.style.marginRight = 0f;

            VisualElement headerRow = new VisualElement();
            headerRow.AddToClassList("toolkit-box__header");

            Label titleLabel = new Label();
            titleLabel.name = "recipe-row-title";
            titleLabel.AddToClassList("toolkit-box__title");
            headerRow.Add(titleLabel);

            row.Add(headerRow);

            Label infoLabel = new Label();
            infoLabel.name = "recipe-row-info";
            infoLabel.AddToClassList("toolkit-box__label");
            infoLabel.AddToClassList("clip-editor__hint");
            row.Add(infoLabel);

            // Closes over the row element itself (stable identity, never recreated) rather than
            // any per-bind data -- the callback reads row.userData live when the menu opens, so a
            // recycled row always acts on whatever it is currently showing.
            row.AddManipulator(new ContextualMenuManipulator(
                populateEvent => PopulateRecipeRowContextMenu(populateEvent, row)));

            itemSlot.Add(row);
            return itemSlot;
        }

        private void PopulateRecipeRowContextMenu(ContextualMenuPopulateEvent populateEvent, VisualElement row)
        {
            TexturePackRecipeAsset targetRecipe = row.userData as TexturePackRecipeAsset;
            if (targetRecipe == null)
            {
                return;
            }

            Label titleLabel = row.Q<Label>("recipe-row-title");
            populateEvent.menu.AppendAction(
                "Rename",
                renameAction => InlineRenameEditing.Begin(
                    titleLabel,
                    targetRecipe.name,
                    committedName => RecipeRenameRequested?.Invoke(targetRecipe, committedName)),
                DropdownMenuAction.AlwaysEnabled);
            populateEvent.menu.AppendAction(
                "Delete",
                deleteAction => RecipeDeleteRequested?.Invoke(targetRecipe),
                DropdownMenuAction.AlwaysEnabled);
        }

        private void BindRecipeRow(VisualElement element, int index)
        {
            if (index < 0 || index >= filteredRecipes.Count)
            {
                return;
            }

            TexturePackRecipeAsset recipe = filteredRecipes[index];

            // The boxed row (userData, the selected-state class) lives one level below the item
            // slot ListView hands bindItem -- see MakeRecipeRow.
            VisualElement row = element.Q<VisualElement>("recipe-row-box");
            row.userData = recipe;

            Label titleLabel = row.Q<Label>("recipe-row-title");
            titleLabel.text = recipe != null ? recipe.name : string.Empty;

            Label infoLabel = row.Q<Label>("recipe-row-info");
            int wiredChannelCount = recipe != null ? recipe.WiredChannelCount : 0;
            string outputFileName = recipe != null && !string.IsNullOrEmpty(recipe.outputAssetPath)
                ? Path.GetFileName(recipe.outputAssetPath)
                : string.Empty;
            infoLabel.text = wiredChannelCount.ToString() + " wired · "
                + (string.IsNullOrEmpty(outputFileName) ? "no output yet" : outputFileName);

            row.tooltip = recipe != null ? recipe.outputAssetPath : string.Empty;

            row.EnableInClassList("toolkit-box--selected", recipe == SelectedRecipe);
        }

        private void OnRecipesListSelectionChanged(IEnumerable<object> selectedItems)
        {
            foreach (object selectedItem in selectedItems)
            {
                TexturePackRecipeAsset recipe = selectedItem as TexturePackRecipeAsset;
                SelectedRecipe = recipe;
                RecipeSelected?.Invoke(recipe);
                return;
            }
        }

        private void OnSearchTextChanged(ChangeEvent<string> changeEvent)
        {
            searchText = changeEvent.newValue ?? string.Empty;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            filteredRecipes.Clear();
            for (int index = 0; index < catalogRecipes.Count; index++)
            {
                TexturePackRecipeAsset recipe = catalogRecipes[index];
                if (recipe == null)
                {
                    continue;
                }
                if (string.IsNullOrEmpty(searchText)
                    || recipe.name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    filteredRecipes.Add(recipe);
                }
            }

            recipesListView.Rebuild();
            RefreshEmptyState();

            if (SelectedRecipe != null)
            {
                recipesListView.SetSelectionWithoutNotify(
                    filteredRecipes.Contains(SelectedRecipe)
                        ? new List<int> { filteredRecipes.IndexOf(SelectedRecipe) }
                        : new List<int>());
            }
        }

        private void RefreshEmptyState()
        {
            bool isEmpty = filteredRecipes.Count == 0;
            recipesListView.style.display = isEmpty ? DisplayStyle.None : DisplayStyle.Flex;
            emptyLabel.style.display = isEmpty ? DisplayStyle.Flex : DisplayStyle.None;
            if (isEmpty)
            {
                emptyLabel.text = catalogRecipes.Count == 0
                    ? "No recipes in this project yet. Press New."
                    : "No recipes match your search.";
            }
        }
    }
}
