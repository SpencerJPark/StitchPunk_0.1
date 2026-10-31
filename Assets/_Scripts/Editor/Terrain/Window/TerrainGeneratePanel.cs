#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class TerrainGeneratePanel : TerrainToolPanel
{
    readonly VisualElement regionListContainer = new VisualElement();
    readonly VisualElement emptyState = new VisualElement();
    readonly VisualElement detailGroup = new VisualElement();
    readonly ObjectField profileField = new ObjectField("Profile");
    readonly Toggle overwriteHandAuthoredToggle = new Toggle("Overwrite hand-authored tiles");
    readonly Button generateButton = new Button();
    readonly Button previewButton = new Button();
    readonly Label regeneratedLabel = new Label();
    TerrainRegion selectedRegion;
    IVisualElementScheduledItem refreshTimer;
    string regeneratedText = string.Empty;

    public override string DisplayName => "Generate";
    public override string StatusText => selectedRegion != null ? "Generate: " + selectedRegion.name : "Generate: no region";

    public TerrainGeneratePanel()
    {
        AddToClassList("terrain-panel");

        Label title = new Label("Generate");
        title.AddToClassList("terrain-panel-title");
        Add(title);

        Label hint = new Label("Fill a region's tiles from its profile. Preview One Tile shows heights only; the Height panel's Rebuild Tile Mesh button undoes the preview.");
        hint.AddToClassList("terrain-panel-hint");
        Add(hint);

        Add(regionListContainer);

        emptyState.AddToClassList("terrain-empty");
        Label emptyTitle = new Label("No terrain regions");
        emptyTitle.AddToClassList("terrain-panel-title");
        Label emptyWhy = new Label("A region marks the area a generation profile fills. Create one around the active tile to start.");
        emptyWhy.AddToClassList("terrain-panel-hint");
        Button createButton = new Button(CreateRegionAroundActiveTile);
        createButton.text = "Create Region";
        createButton.AddToClassList("terrain-primary-button");
        emptyState.Add(emptyTitle);
        emptyState.Add(emptyWhy);
        emptyState.Add(createButton);
        Add(emptyState);

        profileField.objectType = typeof(TerrainGenerationProfile);
        profileField.allowSceneObjects = false;
        profileField.AddToClassList("terrain-row");
        profileField.RegisterValueChangedCallback(OnProfileChanged);
        detailGroup.Add(profileField);

        overwriteHandAuthoredToggle.value = false;
        overwriteHandAuthoredToggle.AddToClassList("terrain-row");
        detailGroup.Add(overwriteHandAuthoredToggle);

        generateButton.text = "Generate Region";
        generateButton.clicked += GenerateSelectedRegion;
        generateButton.AddToClassList("terrain-primary-button");
        detailGroup.Add(generateButton);

        previewButton.text = "Preview One Tile";
        previewButton.clicked += PreviewActiveTile;
        previewButton.AddToClassList("terrain-segment-button");
        detailGroup.Add(previewButton);

        regeneratedLabel.AddToClassList("terrain-panel-hint");
        detailGroup.Add(regeneratedLabel);
        Add(detailGroup);
    }

    public override void OnShown()
    {
        if (refreshTimer == null) refreshTimer = schedule.Execute(RefreshRegionList).Every(1000);
        else refreshTimer.Resume();
        RefreshRegionList();
    }

    public override void OnHidden()
    {
        if (refreshTimer != null) refreshTimer.Pause();
    }

    static int CountTiles(TerrainRegion region)
    {
        int count = 0;
        foreach (Vector2Int ignoredCoordinate in region.TilesInside()) count++;
        return count;
    }

    void RefreshRegionList()
    {
        TerrainRegion[] regions = Object.FindObjectsByType<TerrainRegion>(FindObjectsInactive.Include);
        if (selectedRegion != null && System.Array.IndexOf(regions, selectedRegion) < 0) selectedRegion = null;
        if (selectedRegion == null && regions.Length > 0) selectedRegion = regions[0];

        regionListContainer.Clear();
        foreach (TerrainRegion region in regions)
        {
            TerrainRegion capturedRegion = region;
            Button row = new Button(() => SelectRegion(capturedRegion));
            row.text = region.name + ", " + region.mode + ", " + CountTiles(region) + " tiles";
            row.AddToClassList("terrain-segment-button");
            if (region == selectedRegion) row.AddToClassList("terrain-segment-button--active");
            regionListContainer.Add(row);
        }

        emptyState.style.display = regions.Length == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        detailGroup.style.display = selectedRegion != null ? DisplayStyle.Flex : DisplayStyle.None;
        profileField.SetValueWithoutNotify(selectedRegion != null ? selectedRegion.profile : null);
        regeneratedLabel.text = regeneratedText;

        bool hasProfile = selectedRegion != null && selectedRegion.profile != null;
        generateButton.SetEnabled(hasProfile && selectedRegion.mode != TerrainRegionMode.Hand);
        TerrainTile activeTile;
        previewButton.SetEnabled(hasProfile && TerrainTilesPanel.TryGetActiveTile(out activeTile));
        RaiseStatusChanged();
    }

    void SelectRegion(TerrainRegion region)
    {
        selectedRegion = region;
        regeneratedText = string.Empty;
        RefreshRegionList();
    }

    void OnProfileChanged(ChangeEvent<Object> changeEvent)
    {
        if (selectedRegion == null) return;
        Undo.RecordObject(selectedRegion, "Set Terrain Region Profile");
        selectedRegion.profile = changeEvent.newValue as TerrainGenerationProfile;
        EditorUtility.SetDirty(selectedRegion);
        RefreshRegionList();
    }

    void CreateRegionAroundActiveTile()
    {
        GameObject regionObject = new GameObject("TerrainRegion");
        TerrainRegion createdRegion = regionObject.AddComponent<TerrainRegion>();
        TerrainTile activeTile;
        if (TerrainTilesPanel.TryGetActiveTile(out activeTile))
        {
            Vector2 center = TerrainTile.CenterOfTile(activeTile.tileCoordinate);
            regionObject.transform.position = new Vector3(center.x, 0f, center.y);
            // bounds are world coordinates, so the default box must be moved onto the tile by hand
            createdRegion.boundsMin = new Vector3(center.x - 150f, createdRegion.boundsMin.y, center.y - 150f);
            createdRegion.boundsMax = new Vector3(center.x + 150f, createdRegion.boundsMax.y, center.y + 150f);
        }
        Undo.RegisterCreatedObjectUndo(regionObject, "Create Terrain Region");
        Selection.activeGameObject = regionObject;
        selectedRegion = createdRegion;
        RefreshRegionList();
    }

    void GenerateSelectedRegion()
    {
        if (selectedRegion == null) return;
        bool allowHandAuthored = overwriteHandAuthoredToggle.value;
        List<Vector2Int> tiles = TerrainGenerateTool.ExistingTilesInside(selectedRegion);

        if (!allowHandAuthored && AnyTileHandAuthored(tiles) &&
            !EditorUtility.DisplayDialog("Hand-authored tiles", "Some tiles in this region are hand authored and will be skipped. Generate the rest?", "Generate", "Cancel"))
            return;

        try
        {
            TerrainGenerateTool.Generate(selectedRegion, allowHandAuthored,
                (float progress, string tileName) => EditorUtility.DisplayProgressBar("Generating terrain", tileName, progress));
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        StringBuilder builder = new StringBuilder("Regenerated:");
        foreach (Vector2Int coordinate in tiles)
        {
            if (!allowHandAuthored && IsHandAuthored(coordinate)) continue;
            builder.Append(" (").Append(coordinate.x).Append(", ").Append(coordinate.y).Append(')');
        }
        regeneratedText = builder.ToString();
        RefreshRegionList();
    }

    static bool IsHandAuthored(Vector2Int coordinate)
    {
        TerrainTileData tileData = AssetDatabase.LoadAssetAtPath<TerrainTileData>(TerrainTileAssets.TileDataPath(coordinate));
        return tileData != null && tileData.handAuthored;
    }

    static bool AnyTileHandAuthored(List<Vector2Int> tiles)
    {
        foreach (Vector2Int coordinate in tiles)
            if (IsHandAuthored(coordinate)) return true;
        return false;
    }

    void PreviewActiveTile()
    {
        TerrainTile activeTile;
        if (selectedRegion == null || selectedRegion.profile == null) return;
        if (!TerrainTilesPanel.TryGetActiveTile(out activeTile)) return;
        TerrainGenerateTool.PreviewOneTile(activeTile, selectedRegion.profile);
    }
}
#endif
