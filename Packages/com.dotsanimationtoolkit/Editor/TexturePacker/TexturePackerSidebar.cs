// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Sidebar host that switches between the image catalog and the recipe catalog, sharing one header.</summary>
    public sealed class TexturePackerSidebar : VisualElement
    {
        public enum SidebarMode
        {
            Images,
            Recipes,
        }

        private const string TabUssClassName = "clip-editor__tab";
        private const string TabActiveUssClassName = "clip-editor__tab--active";

        private readonly ToolbarToggle imagesToggle;
        private readonly ToolbarToggle recipesToggle;
        private readonly VisualElement actionsSlot;

        private bool isApplyingMode;

        public SidebarMode Mode { get; private set; }

        public ImageCatalogColumn Images { get; }

        public RecipeCatalogColumn Recipes { get; }

        public TexturePackerSidebar()
        {
            name = "texture-packer-sidebar";
            style.flexGrow = 1f;
            style.minWidth = 200f;
            style.paddingTop = 8f;
            style.paddingLeft = 10f;
            style.paddingRight = 10f;
            style.paddingBottom = 10f;

            VisualElement header = new VisualElement();
            header.AddToClassList("toolkit-pane-header");

            imagesToggle = new ToolbarToggle { name = "sidebar-images-toggle", text = "Images" };
            imagesToggle.AddToClassList(TabUssClassName);
            imagesToggle.RegisterValueChangedCallback(OnImagesToggleChanged);
            header.Add(imagesToggle);

            recipesToggle = new ToolbarToggle { name = "sidebar-recipes-toggle", text = "Recipes" };
            recipesToggle.AddToClassList(TabUssClassName);
            recipesToggle.RegisterValueChangedCallback(OnRecipesToggleChanged);
            header.Add(recipesToggle);

            actionsSlot = new VisualElement { name = "sidebar-actions" };
            actionsSlot.AddToClassList("toolkit-pane-actions");
            header.Add(actionsSlot);

            Add(header);

            Images = new ImageCatalogColumn();
            Recipes = new RecipeCatalogColumn();
            Add(Images);
            Add(Recipes);

            SetMode(SidebarMode.Images);
        }

        public void SetMode(SidebarMode mode)
        {
            // The two assignments below raise change callbacks on the toggles; the guard stops SetMode
            // from re-entering itself, and also lets a click on the already-lit toggle snap back to true.
            isApplyingMode = true;
            imagesToggle.SetValueWithoutNotify(mode == SidebarMode.Images);
            recipesToggle.SetValueWithoutNotify(mode == SidebarMode.Recipes);
            isApplyingMode = false;

            imagesToggle.EnableInClassList(TabActiveUssClassName, mode == SidebarMode.Images);
            recipesToggle.EnableInClassList(TabActiveUssClassName, mode == SidebarMode.Recipes);

            Mode = mode;
            Images.style.display = mode == SidebarMode.Images ? DisplayStyle.Flex : DisplayStyle.None;
            Recipes.style.display = mode == SidebarMode.Recipes ? DisplayStyle.Flex : DisplayStyle.None;

            actionsSlot.Clear();
            VisualElement activeColumnHeaderActions = mode == SidebarMode.Images ? Images.HeaderActions : Recipes.HeaderActions;
            if (activeColumnHeaderActions != null)
            {
                actionsSlot.Add(activeColumnHeaderActions);
            }
        }

        public void RescanProject()
        {
            Images.RescanProject();
            Recipes.RescanProject();
        }

        private void OnImagesToggleChanged(ChangeEvent<bool> changeEvent)
        {
            if (isApplyingMode)
            {
                return;
            }

            SetMode(SidebarMode.Images);
        }

        private void OnRecipesToggleChanged(ChangeEvent<bool> changeEvent)
        {
            if (isApplyingMode)
            {
                return;
            }

            SetMode(SidebarMode.Recipes);
        }
    }
}
