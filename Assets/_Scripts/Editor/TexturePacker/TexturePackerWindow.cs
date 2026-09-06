#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

// ===========================================================================
//  Texture Channel Packer
//  Open via: Window > Stitch Punk > Texture Channel Packer
//
//  Drag greyscale textures from the Project window onto the canvas. Each one
//  becomes a source node exposing its R/G/B/A channels as output ports. Wire
//  any source channel into any channel of the Pack Output node, set flat
//  values for the channels you leave unwired, and press Bake.
//
//  The bake overwrites its target PNG in place, so a repacked texture keeps
//  its GUID, import settings, and every material reference.
//
//  Save Recipe stores the whole graph (sources by GUID, wiring, inverts,
//  defaults, resolution, output path, layout) so a source can be repainted
//  and repacked with one click. Double-click a recipe asset to reopen it.
// ===========================================================================

public class TexturePackerWindow : EditorWindow
{
    private const int PreviewMaxDimension = 128;
    private const long PreviewDebounceMilliseconds = 250L;

    private TexturePackerGraphView graphView;
    private ObjectField recipeField;
    private IVisualElementScheduledItem scheduledPreview;

    private TexturePackRecipeSO loadedRecipe;
    private string outputAssetPath = string.Empty;

    /// <summary>Once the size has been derived from the first source (or a recipe), the user owns it.</summary>
    private bool resolutionAssigned;

    // -------------------------------------------------------------------------
    // Open
    // -------------------------------------------------------------------------

    [MenuItem("Window/Stitch Punk/Texture Channel Packer")]
    public static void OpenWindow()
    {
        TexturePackerWindow window = GetWindow<TexturePackerWindow>("Texture Packer");
        window.minSize = new Vector2(760f, 520f);
    }

    public static void OpenWith(TexturePackRecipeSO recipe)
    {
        TexturePackerWindow window = GetWindow<TexturePackerWindow>("Texture Packer");
        window.minSize = new Vector2(760f, 520f);
        window.LoadRecipe(recipe);
    }

    [OnOpenAsset]
    public static bool OnOpenRecipeAsset(EntityId entityId, int line)
    {
        Object openedAsset = EditorUtility.EntityIdToObject(entityId);
        if (openedAsset is TexturePackRecipeSO recipe)
        {
            OpenWith(recipe);
            return true;
        }
        return false;
    }

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    private void CreateGUI()
    {
        VisualElement root = rootVisualElement;
        root.style.flexDirection = FlexDirection.Column;
        root.style.flexGrow = 1f;

        root.Add(BuildToolbar());

        graphView = new TexturePackerGraphView();
        graphView.style.flexGrow = 1f;
        graphView.GraphChanged += OnGraphChanged;
        graphView.OutputNode.SettingsChanged += SchedulePreviewRefresh;
        graphView.OutputNode.BakeRequested += Bake;
        root.Add(graphView);

        // A recipe assigned before CreateGUI ran (double-click open) still needs its graph built.
        if (loadedRecipe != null)
        {
            LoadRecipe(loadedRecipe);
        }
        else
        {
            SchedulePreviewRefresh();
        }
    }

    private void OnDisable()
    {
        graphView?.OutputNode?.DisposePreviewTexture();
        TexturePackerBaker.ClearSourceCache();
    }

    private VisualElement BuildToolbar()
    {
        Toolbar toolbar = new Toolbar();

        ToolbarButton bakeButton = new ToolbarButton(Bake);
        bakeButton.text = "Bake";
        bakeButton.tooltip = "Write the packed PNG to disk, overwriting the output asset in place.";
        toolbar.Add(bakeButton);

        ToolbarButton bakeAsButton = new ToolbarButton(BakeAs);
        bakeAsButton.text = "Bake As…";
        bakeAsButton.tooltip = "Choose a new output path, then bake.";
        toolbar.Add(bakeAsButton);

        ToolbarSpacer spacer = new ToolbarSpacer();
        toolbar.Add(spacer);

        recipeField = new ObjectField();
        recipeField.objectType = typeof(TexturePackRecipeSO);
        recipeField.allowSceneObjects = false;
        recipeField.tooltip = "Assign a recipe to load its graph. Save Recipe writes back into it.";
        recipeField.style.width = 220f;
        recipeField.RegisterValueChangedCallback(changeEvent =>
        {
            TexturePackRecipeSO selectedRecipe = changeEvent.newValue as TexturePackRecipeSO;
            if (selectedRecipe == null)
            {
                loadedRecipe = null;
                return;
            }
            if (selectedRecipe != loadedRecipe)
            {
                LoadRecipe(selectedRecipe);
            }
        });
        toolbar.Add(recipeField);

        ToolbarButton saveRecipeButton = new ToolbarButton(SaveRecipe);
        saveRecipeButton.text = "Save Recipe";
        saveRecipeButton.tooltip = "Snapshot this graph to a recipe asset for one-click repacks.";
        toolbar.Add(saveRecipeButton);

        ToolbarButton clearButton = new ToolbarButton(ClearGraph);
        clearButton.text = "Clear";
        clearButton.tooltip = "Remove every source node and wire. The output node stays.";
        toolbar.Add(clearButton);

        return toolbar;
    }

    // -------------------------------------------------------------------------
    // Graph -> job description
    // -------------------------------------------------------------------------

    private PackJobDescription BuildJobDescription()
    {
        PackJobDescription description = new PackJobDescription
        {
            channels = new PackChannelJob[PackChannelIndex.Count],
            resolution = graphView.OutputNode.Resolution,
            outputAssetPath = outputAssetPath
        };

        for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
        {
            description.channels[channelIndex] = BuildChannelJob(channelIndex);
        }

        return description;
    }

    private PackChannelJob BuildChannelJob(int channelIndex)
    {
        PackChannelJob channelJob = new PackChannelJob
        {
            sourceAssetPath = string.Empty,
            sourceChannel = -1,
            invert = graphView.OutputNode.GetInvert(channelIndex),
            defaultValue = graphView.OutputNode.GetDefaultValue(channelIndex)
        };

        Edge wire = graphView.OutputNode.GetChannelPort(channelIndex).connections.FirstOrDefault();
        if (wire?.output?.node is not SourceImageNodeView sourceNode || sourceNode.IsMissing)
        {
            return channelJob;
        }

        int sourceChannel = sourceNode.FindChannelIndex(wire.output);
        string sourceAssetPath = AssetDatabase.GUIDToAssetPath(sourceNode.TextureGuid);
        if (sourceChannel < 0 || string.IsNullOrEmpty(sourceAssetPath))
        {
            return channelJob;
        }

        channelJob.sourceAssetPath = sourceAssetPath;
        channelJob.sourceChannel = sourceChannel;
        return channelJob;
    }

    // -------------------------------------------------------------------------
    // Bake
    // -------------------------------------------------------------------------

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

    /// <summary>Folder of the given asset path, as a forward-slashed project-relative path. Falls back to "Assets".</summary>
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
        PackJobDescription description = BuildJobDescription();
        description.outputAssetPath = targetAssetPath;

        if (!TexturePackerBaker.Bake(description))
        {
            return;
        }

        outputAssetPath = targetAssetPath;
        graphView.OutputNode.SetOutputPathLabel(outputAssetPath);

        if (loadedRecipe != null && loadedRecipe.outputAssetPath != outputAssetPath)
        {
            loadedRecipe.outputAssetPath = outputAssetPath;
            EditorUtility.SetDirty(loadedRecipe);
        }

        Texture2D bakedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(outputAssetPath);
        if (bakedTexture != null)
        {
            EditorGUIUtility.PingObject(bakedTexture);
        }

        SchedulePreviewRefresh();
    }

    // -------------------------------------------------------------------------
    // Preview
    // -------------------------------------------------------------------------

    private void OnGraphChanged()
    {
        AutoAssignResolution();
        SchedulePreviewRefresh();
    }

    /// <summary>The first source dropped in sets the output size; after that the field is the user's.</summary>
    private void AutoAssignResolution()
    {
        if (resolutionAssigned)
        {
            return;
        }

        Vector2Int largestSourceSize = Vector2Int.zero;
        foreach (SourceImageNodeView sourceNode in graphView.EnumerateSourceNodes())
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

        graphView.OutputNode.Resolution = largestSourceSize;
        resolutionAssigned = true;
    }

    private void SchedulePreviewRefresh()
    {
        scheduledPreview?.Pause();
        scheduledPreview = rootVisualElement.schedule
            .Execute(RefreshPreview)
            .StartingIn(PreviewDebounceMilliseconds);
    }

    private void RefreshPreview()
    {
        if (graphView == null)
        {
            return;
        }

        PackJobDescription description = BuildJobDescription();
        Texture2D previewTexture = TexturePackerBaker.BakePreview(
            description, PreviewMaxDimension, graphView.OutputNode.PreviewChannel);

        graphView.OutputNode.SetPreviewTexture(previewTexture);
    }

    // -------------------------------------------------------------------------
    // Recipes
    // -------------------------------------------------------------------------

    private void ClearGraph()
    {
        graphView.ClearSources();
        loadedRecipe = null;
        recipeField.SetValueWithoutNotify(null);
        outputAssetPath = string.Empty;
        resolutionAssigned = false;
        graphView.OutputNode.SetOutputPathLabel(outputAssetPath);

        for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
        {
            graphView.OutputNode.SetInvert(channelIndex, false);
            graphView.OutputNode.SetDefaultValue(channelIndex, channelIndex == PackChannelIndex.Alpha ? 1f : 0f);
        }

        graphView.OutputNode.RefreshChannelRows();
        SchedulePreviewRefresh();
    }

    private void LoadRecipe(TexturePackRecipeSO recipe)
    {
        loadedRecipe = recipe;

        // Double-click open assigns the recipe before CreateGUI has built the graph.
        if (graphView == null)
        {
            return;
        }

        recipeField?.SetValueWithoutNotify(recipe);
        graphView.ClearSources();

        if (recipe.channels == null || recipe.channels.Length != PackChannelIndex.Count)
        {
            recipe.channels = TexturePackRecipeSO.CreateDefaultChannels();
        }

        // Layout entries place their nodes; a wired source with no saved layout still gets one.
        Dictionary<string, Vector2> layoutByGuid = new Dictionary<string, Vector2>();
        foreach (SourceNodeLayout layout in recipe.sourceLayouts)
        {
            if (!string.IsNullOrEmpty(layout.sourceTextureGuid))
            {
                layoutByGuid[layout.sourceTextureGuid] = layout.position;
            }
        }

        Vector2 fallbackPosition = new Vector2(120f, 120f);
        foreach (KeyValuePair<string, Vector2> layoutEntry in layoutByGuid)
        {
            graphView.AddSourceNode(layoutEntry.Key, layoutEntry.Value);
        }

        foreach (PackChannel channel in recipe.channels)
        {
            if (!channel.IsWired || layoutByGuid.ContainsKey(channel.sourceTextureGuid))
            {
                continue;
            }
            graphView.AddSourceNode(channel.sourceTextureGuid, fallbackPosition);
            fallbackPosition += new Vector2(30f, 30f);
        }

        graphView.OutputNode.SetPosition(new Rect(recipe.outputNodePosition, Vector2.zero));
        graphView.OutputNode.Resolution = recipe.resolution;

        for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
        {
            PackChannel channel = recipe.channels[channelIndex];
            graphView.OutputNode.SetInvert(channelIndex, channel.invert);
            graphView.OutputNode.SetDefaultValue(channelIndex, channel.defaultValue);

            if (!channel.IsWired || channel.sourceChannel >= PackChannelIndex.Count)
            {
                continue;
            }

            SourceImageNodeView sourceNode = graphView.FindSourceNode(channel.sourceTextureGuid);
            if (sourceNode == null)
            {
                continue;
            }

            graphView.ConnectPorts(
                sourceNode.GetChannelPort(channel.sourceChannel),
                graphView.OutputNode.GetChannelPort(channelIndex));
        }

        outputAssetPath = recipe.outputAssetPath;
        resolutionAssigned = true;
        graphView.OutputNode.SetOutputPathLabel(outputAssetPath);
        graphView.OutputNode.RefreshChannelRows();
        SchedulePreviewRefresh();
    }

    private void SaveRecipe()
    {
        TexturePackRecipeSO targetRecipe = loadedRecipe;

        if (targetRecipe == null)
        {
            // Default the recipe beside the texture it produces — the pairing is obvious
            // when you find the PNG later and wonder how it was made.
            string startDirectory = DirectoryOfOrAssets(outputAssetPath);
            string startName = string.IsNullOrEmpty(outputAssetPath)
                ? "NewTexturePackRecipe"
                : Path.GetFileNameWithoutExtension(outputAssetPath) + "_Recipe";

            string chosenPath = EditorUtility.SaveFilePanelInProject(
                "Save texture pack recipe", startName, "asset",
                "Choose where the recipe asset is written.", startDirectory);

            if (string.IsNullOrEmpty(chosenPath))
            {
                return;
            }

            targetRecipe = CreateInstance<TexturePackRecipeSO>();
            AssetDatabase.CreateAsset(targetRecipe, chosenPath);
            loadedRecipe = targetRecipe;
            recipeField.SetValueWithoutNotify(targetRecipe);
        }

        WriteGraphInto(targetRecipe);
        EditorUtility.SetDirty(targetRecipe);
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(targetRecipe);
        Debug.Log("Texture Channel Packer: recipe saved to " + AssetDatabase.GetAssetPath(targetRecipe));
    }

    private void WriteGraphInto(TexturePackRecipeSO recipe)
    {
        recipe.channels = new PackChannel[PackChannelIndex.Count];
        for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
        {
            PackChannelJob channelJob = BuildChannelJob(channelIndex);
            recipe.channels[channelIndex] = new PackChannel
            {
                sourceTextureGuid = channelJob.IsWired ? AssetDatabase.AssetPathToGUID(channelJob.sourceAssetPath) : string.Empty,
                sourceChannel = channelJob.IsWired ? channelJob.sourceChannel : -1,
                invert = channelJob.invert,
                defaultValue = channelJob.defaultValue
            };
        }

        recipe.resolution = graphView.OutputNode.Resolution;
        recipe.outputAssetPath = outputAssetPath;
        recipe.outputNodePosition = graphView.OutputNode.GetPosition().position;

        recipe.sourceLayouts = new List<SourceNodeLayout>();
        foreach (SourceImageNodeView sourceNode in graphView.EnumerateSourceNodes())
        {
            recipe.sourceLayouts.Add(new SourceNodeLayout
            {
                sourceTextureGuid = sourceNode.TextureGuid,
                position = sourceNode.GetPosition().position
            });
        }
    }
}
#endif
