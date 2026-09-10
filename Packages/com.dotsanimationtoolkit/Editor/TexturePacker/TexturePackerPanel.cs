// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Hosts the texture packer sidebar and graph, and owns baking, preview refresh, and recipe load/save/discard.</summary>
    public sealed class TexturePackerPanel : VisualElement, IDisposable
    {
        private const int PreviewDebounceMilliseconds = 250;
        private const int PreviewMaxDimension = 128;
        private const int SourceChannelPreviewMaxDimension = 96;

        public TexturePackRecipeAsset LoadedRecipe { get; private set; }
        public TexturePackerSidebar Sidebar { get; }
        public TexturePackerGraphView Graph { get; }

        private readonly VisualElement graphHost;
        private readonly Label recipeLabel;
        private readonly TexturePackBaker baker = new TexturePackBaker();

        private string outputAssetPath = string.Empty;
        private bool resolutionAssigned;
        private bool hasUnsavedChanges;
        private bool isRestoringGraph;
        private IVisualElementScheduledItem scheduledPreview;

        public TexturePackerPanel()
        {
            style.flexGrow = 1f;

            recipeLabel = new Label();
            recipeLabel.name = "texture-packer-recipe-label";

            Sidebar = new TexturePackerSidebar();
            Sidebar.Images.ImagesActivated += textures => Graph.AddSourcesAtVisibleCenter(textures);
            Sidebar.Recipes.RecipeSelected += OnRecipeSelected;
            Sidebar.Recipes.NewRequested += OnNewRequested;
            Sidebar.Recipes.SaveRequested += SaveRecipe;
            Sidebar.Recipes.RecipeRenameRequested += OnRecipeRenameRequested;
            Sidebar.Recipes.RecipeDeleteRequested += OnRecipeDeleteRequested;

            Graph = new TexturePackerGraphView();
            Graph.style.flexGrow = 1f;
            Graph.GraphChanged += OnGraphChanged;
            Graph.OutputNode.SettingsChanged += OnOutputSettingsChanged;
            Graph.OutputNode.BakeRequested += Bake;
            Graph.OutputNode.MatchLargestSourceRequested += OnMatchLargestSourceRequested;
            Graph.SourceChannelViewChanged += OnSourceChannelViewChanged;

            graphHost = new VisualElement();
            graphHost.name = "texture-packer-graph-host";
            graphHost.style.flexGrow = 1f;
            graphHost.Add(Graph);

            VisualElement graphColumn = new VisualElement();
            graphColumn.name = "texture-packer-graph-column";
            graphColumn.style.flexGrow = 1f;
            graphColumn.style.minWidth = 480f;
            graphColumn.Add(BuildHeader());
            graphColumn.Add(graphHost);

            TwoPaneSplitView splitView = new TwoPaneSplitView(0, 280f, TwoPaneSplitViewOrientation.Horizontal);
            splitView.style.flexGrow = 1f;
            splitView.Add(Sidebar);
            splitView.Add(graphColumn);
            Add(splitView);

            RefreshRecipeLabel();
            SchedulePreviewRefresh();
        }

        private VisualElement BuildHeader()
        {
            VisualElement header = new VisualElement();
            header.name = "texture-packer-header";
            header.AddToClassList("toolkit-pane-header");
            header.Add(recipeLabel);

            VisualElement actions = new VisualElement();
            actions.AddToClassList("toolkit-pane-actions");

            Button bakeButton = ToolkitIcons.MakeIconTextButton(
                Bake, "d_PreTextureRGB", "Write the packed PNG to disk, overwriting the output asset in place.", "Bake");
            bakeButton.name = "texture-packer-bake-button";
            actions.Add(bakeButton);

            Button bakeAsButton = ToolkitIcons.MakeIconTextButton(
                BakeAs, "d_PreTextureRGB", "Choose a new output path, then bake.", "Bake As…");
            bakeAsButton.name = "texture-packer-bake-as-button";
            actions.Add(bakeAsButton);

            Button clearButton = ToolkitIcons.MakeIconTextButton(
                OnClearButtonClicked, "d_TreeEditor.Trash", "Remove every source node and wire. The output node stays.", "Clear");
            clearButton.name = "texture-packer-clear-button";
            actions.Add(clearButton);

            header.Add(actions);
            return header;
        }

        public void RescanProject()
        {
            Sidebar.RescanProject();
            Sidebar.Images.SetOnCanvasGuids(Graph.CollectSourceGuids());
        }

        public void LoadRecipe(TexturePackRecipeAsset recipe)
        {
            isRestoringGraph = true;
            Graph.LoadFromRecipe(recipe);
            isRestoringGraph = false;

            LoadedRecipe = recipe;
            outputAssetPath = recipe.outputAssetPath;
            Graph.OutputNode.SetOutputPathLabel(outputAssetPath);
            resolutionAssigned = recipe.HasAnySource;
            hasUnsavedChanges = false;
            RefreshRecipeLabel();
            Sidebar.Recipes.SetSelectedRecipe(recipe);
            Sidebar.SetMode(TexturePackerSidebar.SidebarMode.Recipes);
            TexturePackRecipeAssetUtility.RememberRecipeFolder(DirectoryOfOrAssets(AssetDatabase.GetAssetPath(recipe)));
            Sidebar.Images.SetOnCanvasGuids(Graph.CollectSourceGuids());
            SchedulePreviewRefresh();
        }

        public void Dispose()
        {
            Graph.OutputNode.DisposePreviewTexture();
            foreach (SourceImageNodeView sourceNode in Graph.EnumerateSourceNodes())
            {
                sourceNode.DisposeChannelPreview();
            }
            baker.ClearSourceCache();
            scheduledPreview?.Pause();
        }

        private void Bake()
        {
            if (string.IsNullOrEmpty(outputAssetPath))
            {
                BakeAs();
                return;
            }
            BakeTo(outputAssetPath);
        }

        private void BakeAs()
        {
            string startDirectory = DirectoryOfOrAssets(outputAssetPath);
            string startName = string.IsNullOrEmpty(outputAssetPath) ? "T_Packed" : Path.GetFileNameWithoutExtension(outputAssetPath);

            string chosenPath = EditorUtility.SaveFilePanelInProject(
                "Bake packed texture", startName, "png",
                "Choose where the packed texture is written.", startDirectory);

            if (string.IsNullOrEmpty(chosenPath))
            {
                return;
            }

            BakeTo(chosenPath);
        }

        // Folder of the given asset path, as a forward-slashed project-relative path. Falls back to "Assets".
        private static string DirectoryOfOrAssets(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return "Assets";
            }

            string directory = Path.GetDirectoryName(assetPath);
            return string.IsNullOrEmpty(directory) ? "Assets" : directory.Replace('\\', '/');
        }

        private void BakeTo(string targetAssetPath)
        {
            PackRequest request = Graph.BuildPackRequest(targetAssetPath);

            if (!baker.Bake(request))
            {
                return;
            }

            outputAssetPath = targetAssetPath;
            Graph.OutputNode.SetOutputPathLabel(outputAssetPath);
            MarkUnsaved();

            Texture2D bakedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(outputAssetPath);
            if (bakedTexture != null)
            {
                EditorGUIUtility.PingObject(bakedTexture);
            }

            SchedulePreviewRefresh();
        }

        private void OnGraphChanged()
        {
            AutoAssignResolution();
            Sidebar.Images.SetOnCanvasGuids(Graph.CollectSourceGuids());
            MarkUnsaved();
            SchedulePreviewRefresh();
        }

        private void OnOutputSettingsChanged()
        {
            MarkUnsaved();
            SchedulePreviewRefresh();
        }

        private void OnMatchLargestSourceRequested()
        {
            resolutionAssigned = false;
            AutoAssignResolution();
            SchedulePreviewRefresh();
            MarkUnsaved();
        }

        private void OnSourceChannelViewChanged(SourceImageNodeView node, int channelIndex)
        {
            if (channelIndex < 0)
            {
                node.SetChannelPreview(null);
                return;
            }

            string sourcePath = AssetDatabase.GetAssetPath(node.SourceTexture);

            PackRequest request = new PackRequest
            {
                channels = new PackChannelBinding[PackChannelIndex.Count],
                resolution = new Vector2Int(node.SourceTexture.width, node.SourceTexture.height),
                outputAssetPath = string.Empty
            };
            request.channels[PackChannelIndex.Red] = new PackChannelBinding { sourceAssetPath = sourcePath, sourceChannel = channelIndex, defaultValue = 0f };
            request.channels[PackChannelIndex.Green] = new PackChannelBinding { sourceAssetPath = sourcePath, sourceChannel = channelIndex, defaultValue = 0f };
            request.channels[PackChannelIndex.Blue] = new PackChannelBinding { sourceAssetPath = sourcePath, sourceChannel = channelIndex, defaultValue = 0f };
            request.channels[PackChannelIndex.Alpha] = new PackChannelBinding { sourceAssetPath = string.Empty, sourceChannel = -1, defaultValue = 1f };

            node.SetChannelPreview(baker.BakePreview(request, SourceChannelPreviewMaxDimension, PackPreviewChannel.RGB));
        }

        // The first source dropped in sets the output size; after that the field is the user's.
        private void AutoAssignResolution()
        {
            if (resolutionAssigned)
            {
                return;
            }

            Vector2Int largestSourceSize = Vector2Int.zero;
            foreach (SourceImageNodeView sourceNode in Graph.EnumerateSourceNodes())
            {
                if (sourceNode.IsMissing)
                {
                    continue;
                }
                largestSourceSize.x = Mathf.Max(largestSourceSize.x, sourceNode.SourceTexture.width);
                largestSourceSize.y = Mathf.Max(largestSourceSize.y, sourceNode.SourceTexture.height);
            }

            if (largestSourceSize.x < 1 || largestSourceSize.y < 1)
            {
                return;
            }

            Graph.OutputNode.Resolution = largestSourceSize;
            resolutionAssigned = true;
        }

        private void SchedulePreviewRefresh()
        {
            scheduledPreview?.Pause();
            scheduledPreview = this.schedule
                .Execute(RefreshPreview)
                .StartingIn(PreviewDebounceMilliseconds);
        }

        private void RefreshPreview()
        {
            PackRequest request = Graph.BuildPackRequest(outputAssetPath);
            Texture2D previewTexture = baker.BakePreview(request, PreviewMaxDimension, Graph.OutputNode.PreviewChannel);
            Graph.OutputNode.SetPreviewTexture(previewTexture);
        }

        private void OnClearButtonClicked()
        {
            if (!ConfirmDiscardIfUnsaved())
            {
                return;
            }

            ClearGraph();
        }

        private void ClearGraph()
        {
            isRestoringGraph = true;
            Graph.ClearSources();
            outputAssetPath = string.Empty;
            resolutionAssigned = false;
            Graph.OutputNode.SetOutputPathLabel(outputAssetPath);

            for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
            {
                Graph.OutputNode.SetInvert(channelIndex, false);
                Graph.OutputNode.SetDefaultValue(channelIndex, channelIndex == PackChannelIndex.Alpha ? 1f : 0f);
            }

            Graph.OutputNode.RefreshChannelRows();
            isRestoringGraph = false;

            LoadedRecipe = null;
            Sidebar.Recipes.SetSelectedRecipe(null);
            hasUnsavedChanges = false;
            RefreshRecipeLabel();
        }

        private void OnRecipeSelected(TexturePackRecipeAsset recipe)
        {
            if (!ConfirmDiscardIfUnsaved())
            {
                Sidebar.Recipes.SetSelectedRecipe(LoadedRecipe);
                return;
            }

            LoadRecipe(recipe);
        }

        private void OnNewRequested()
        {
            if (!ConfirmDiscardIfUnsaved())
            {
                return;
            }

            TexturePackRecipeAsset newRecipe = TexturePackRecipeAssetUtility.CreateRecipeWithPrompt();
            if (newRecipe == null)
            {
                return;
            }

            RescanProject();
            LoadRecipe(newRecipe);
            EditorGUIUtility.PingObject(newRecipe);
        }

        private void SaveRecipe()
        {
            if (LoadedRecipe == null)
            {
                TexturePackRecipeAsset newRecipe = TexturePackRecipeAssetUtility.CreateRecipeWithPrompt();
                if (newRecipe == null)
                {
                    return;
                }

                LoadedRecipe = newRecipe;
                Sidebar.Recipes.SetSelectedRecipe(newRecipe);
            }

            Graph.WriteToRecipe(LoadedRecipe);
            LoadedRecipe.outputAssetPath = outputAssetPath;
            EditorUtility.SetDirty(LoadedRecipe);
            AssetDatabase.SaveAssets();
            hasUnsavedChanges = false;
            RefreshRecipeLabel();
            Sidebar.Recipes.RefreshRows();
            TexturePackRecipeAssetUtility.RememberRecipeFolder(DirectoryOfOrAssets(AssetDatabase.GetAssetPath(LoadedRecipe)));
            EditorGUIUtility.PingObject(LoadedRecipe);
            Debug.Log("[DOTS Animation Toolkit] Texture Packer: recipe saved to " + AssetDatabase.GetAssetPath(LoadedRecipe));
        }

        private void OnRecipeRenameRequested(TexturePackRecipeAsset recipe, string newName)
        {
            TexturePackRecipeAssetUtility.RenameRecipe(recipe, newName);
            Sidebar.Recipes.RescanProject();
            Sidebar.Recipes.SetSelectedRecipe(LoadedRecipe);
        }

        private void OnRecipeDeleteRequested(TexturePackRecipeAsset recipe)
        {
            bool confirmedDelete = EditorUtility.DisplayDialog(
                "Delete Recipe",
                "Delete '" + recipe.name + "'? The packed texture it produced is not touched.",
                "Delete", "Cancel");

            if (!confirmedDelete)
            {
                return;
            }

            TexturePackRecipeAssetUtility.TrashRecipe(recipe);

            if (recipe == LoadedRecipe)
            {
                LoadedRecipe = null;
                RefreshRecipeLabel();
            }

            RescanProject();
        }

        private bool ConfirmDiscardIfUnsaved()
        {
            if (!hasUnsavedChanges)
            {
                return true;
            }

            string message = LoadedRecipe != null
                ? "'" + LoadedRecipe.name + "' has unsaved changes. Discard them?"
                : "The graph has unsaved changes.";

            return EditorUtility.DisplayDialog("Unsaved changes", message, "Discard", "Cancel");
        }

        private void MarkUnsaved()
        {
            if (isRestoringGraph)
            {
                return;
            }

            hasUnsavedChanges = true;
            RefreshRecipeLabel();
        }

        private void RefreshRecipeLabel()
        {
            string baseText = LoadedRecipe != null ? "Recipe: " + LoadedRecipe.name : "No recipe";
            recipeLabel.text = hasUnsavedChanges ? baseText + " ●" : baseText;
        }
    }
}
