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
    const string SeedSessionKey = "TerrainGeneratePanel.RandomWorldSeed";
    const string SizeIndexSessionKey = "TerrainGeneratePanel.RandomWorldSizeIndex";
    const int PreviewResolutionPixels = 128;
    const long PreviewDebounceMilliseconds = 200;
    static readonly int[] RandomWorldTilesPerSide = { 7, 11 };
    readonly Image worldPreviewImage = new Image();
    readonly IntegerField seedField = new IntegerField();
    readonly Label buildStatusLabel = new Label();
    Texture2D worldPreviewTexture;
    IVisualElementScheduledItem previewDebounce;
    int randomWorldSeed;
    int randomWorldSizeIndex;
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

        Add(BuildRandomWorldCard());

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
        RenderWorldPreviewNow();
    }

    public override void OnHidden()
    {
        if (refreshTimer != null) refreshTimer.Pause();
        if (previewDebounce != null) previewDebounce.Pause();
        DestroyWorldPreviewTexture();
    }

    VisualElement BuildRandomWorldCard()
    {
        randomWorldSeed = SessionState.GetInt(SeedSessionKey, 12345);
        randomWorldSizeIndex = Mathf.Clamp(SessionState.GetInt(SizeIndexSessionKey, 0), 0, RandomWorldTilesPerSide.Length - 1);

        VisualElement cardBody;
        VisualElement card = TerrainUi.MakeCard("Random world", out cardBody);
        cardBody.Add(TerrainUi.MakeHint("Reroll until the map looks right, then build it. Hills are shaded, mountains are pale, red dots are locations, white is the camp."));

        worldPreviewImage.scaleMode = ScaleMode.ScaleToFit;
        worldPreviewImage.RegisterCallback<GeometryChangedEvent>((GeometryChangedEvent geometryChanged) =>
        {
            float width = worldPreviewImage.layout.width;
            if (width > 0f && !Mathf.Approximately(worldPreviewImage.style.height.value.value, width)) worldPreviewImage.style.height = width;
        });
        cardBody.Add(worldPreviewImage);

        VisualElement seedRow = new VisualElement();
        seedRow.AddToClassList("terrain-row");
        seedField.label = "Seed";
        seedField.SetValueWithoutNotify(randomWorldSeed);
        seedField.RegisterValueChangedCallback((ChangeEvent<int> changeEvent) => OnRandomWorldSeedChanged(changeEvent.newValue));
        seedRow.Add(seedField);
        seedRow.Add(TerrainUi.MakeIconSquare("Refresh", "Reroll seed", RerollRandomWorldSeed));
        cardBody.Add(seedRow);

        VisualElement sizeRow = new VisualElement();
        sizeRow.AddToClassList("terrain-row");
        Label sizeLabel = new Label("Size");
        sizeLabel.AddToClassList("terrain-label");
        sizeRow.Add(sizeLabel);
        sizeRow.Add(TerrainUi.MakeSegmented(new string[] { "7 x 7", "11 x 11" }, randomWorldSizeIndex, OnRandomWorldSizeChanged));
        cardBody.Add(sizeRow);

        cardBody.Add(TerrainUi.MakePrimary("Build World", null, "Replace the RandomWorld terrain and scene with this seed", BuildRandomWorldAfterConfirm));
        buildStatusLabel.AddToClassList("terrain-panel-hint");
        cardBody.Add(buildStatusLabel);
        return card;
    }

    void OnRandomWorldSeedChanged(int newSeed)
    {
        randomWorldSeed = newSeed;
        SessionState.SetInt(SeedSessionKey, newSeed);
        if (previewDebounce == null) previewDebounce = schedule.Execute(RenderWorldPreviewNow);
        previewDebounce.Pause();
        previewDebounce.ExecuteLater(PreviewDebounceMilliseconds);
    }

    void RerollRandomWorldSeed()
    {
        int newSeed = UnityEngine.Random.Range(1, 1000000);
        seedField.SetValueWithoutNotify(newSeed);
        randomWorldSeed = newSeed;
        SessionState.SetInt(SeedSessionKey, newSeed);
        RenderWorldPreviewNow();
    }

    void OnRandomWorldSizeChanged(int sizeIndex)
    {
        randomWorldSizeIndex = sizeIndex;
        SessionState.SetInt(SizeIndexSessionKey, sizeIndex);
        RenderWorldPreviewNow();
    }

    void RenderWorldPreviewNow()
    {
        if (previewDebounce != null) previewDebounce.Pause();
        worldPreviewTexture = TerrainWorldRecipePreview.RenderToTexture(randomWorldSeed, RandomWorldTilesPerSide[randomWorldSizeIndex], PreviewResolutionPixels, worldPreviewTexture);
        worldPreviewImage.image = worldPreviewTexture;
    }

    void DestroyWorldPreviewTexture()
    {
        worldPreviewImage.image = null;
        if (worldPreviewTexture != null) Object.DestroyImmediate(worldPreviewTexture);
        worldPreviewTexture = null;
    }

    void BuildRandomWorldAfterConfirm()
    {
        int tilesPerSide = RandomWorldTilesPerSide[randomWorldSizeIndex];
        if (!EditorUtility.DisplayDialog("Build random world",
                "Seed " + randomWorldSeed + ", " + tilesPerSide + " x " + tilesPerSide + " tiles.\n\nThis replaces Assets/Terrain/RandomWorld and the RandomWorld scene (Assets/Scenes/RandomWorld.unity).",
                "Build World", "Cancel"))
            return;
        buildStatusLabel.text = TerrainRandomWorldBuilder.BuildRandomWorld(tilesPerSide, randomWorldSeed);
        RaiseStatusChanged();
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
