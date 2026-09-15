// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Sidebar host that switches between the image catalog and the recipe catalog, sharing one header.</summary>
    public sealed class TexturePackerSidebar : CatalogSidebarElement
    {
        public enum SidebarMode
        {
            Images,
            Recipes,
        }

        private const string ImagesModeName = "images";
        private const string RecipesModeName = "recipes";

        public SidebarMode ActiveMode { get; private set; }

        public ImageCatalogColumn Images { get; }

        public RecipeCatalogColumn Recipes { get; }

        public TexturePackerSidebar()
        {
            name = "texture-packer-sidebar";

            Images = new ImageCatalogColumn();
            Recipes = new RecipeCatalogColumn();

            AddMode(ImagesModeName, "Images", Images, Images.HeaderActions);
            AddMode(RecipesModeName, "Recipes", Recipes, Recipes.HeaderActions);

            // Keeps ActiveMode in sync even when a toggle click drives the mode change directly.
            ModeChanged += modeName => ActiveMode = modeName == RecipesModeName ? SidebarMode.Recipes : SidebarMode.Images;

            SetMode(SidebarMode.Images);
        }

        public void SetMode(SidebarMode mode)
        {
            base.SetMode(mode == SidebarMode.Images ? ImagesModeName : RecipesModeName);
        }

        public void RescanProject()
        {
            Images.RescanProject();
            Recipes.RescanProject();
        }
    }
}
