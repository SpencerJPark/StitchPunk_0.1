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
    const string RerollTooltip = "Reroll the hovered tile only. The 6 m ring at a seam comes from the world seed and cannot be rolled. Paint it by hand or change the world seed.";
    const float TileSizeMetres = TerrainTileData.TileSizeMetres;
    readonly Button rerollArmButton;
    readonly Label rerollTileInfoLabel = new Label();
    readonly Dictionary<Vector2Int, int> lastBuiltRollIndexByTile = new Dictionary<Vector2Int, int>();
    bool rerollArmed;
    bool isRebuildingTile;
    bool hasHoveredTile;
    Vector2Int hoveredTileCoordinate;
    float hoveredSurfaceHeight;
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

        rerollArmButton = TerrainUi.MakeSecondary("Reroll tile", "Refresh", RerollTooltip, ToggleRerollArmed);
        detailGroup.Add(rerollArmButton);

        VisualElement rerollRow = new VisualElement();
        rerollRow.AddToClassList("terrain-row");
        rerollTileInfoLabel.AddToClassList("terrain-panel-hint");
        rerollRow.Add(rerollTileInfoLabel);
        rerollRow.Add(TerrainUi.MakeIconSquare("Refresh", RerollTooltip + " Key R; Shift+R steps back.", RerollTileUnderCursorForward));
        detailGroup.Add(rerollRow);
        rerollTileInfoLabel.text = "Arm, then hover a tile";

        regeneratedLabel.AddToClassList("terrain-panel-hint");
        detailGroup.Add(regeneratedLabel);
        Add(detailGroup);
    }

    public override void OnShown()
    {
        if (refreshTimer == null) refreshTimer = schedule.Execute(RefreshRegionList).Every(1000);
        else refreshTimer.Resume();
        Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        Undo.undoRedoPerformed += OnUndoRedoPerformed;
        RefreshRegionList();
        RenderWorldPreviewNow();
    }

    public override void OnHidden()
    {
        Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        SetRerollArmed(false);
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
        worldPreviewImage.AddToClassList("terrain-world-preview");
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

    void ToggleRerollArmed()
    {
        SetRerollArmed(!rerollArmed);
    }

    void SetRerollArmed(bool armed)
    {
        SceneView.duringSceneGui -= OnRerollSceneGui;
        rerollArmed = armed;
        if (armed) SceneView.duringSceneGui += OnRerollSceneGui;
        else
        {
            hasHoveredTile = false;
            rerollTileInfoLabel.text = "Arm, then hover a tile";
        }
        rerollArmButton.EnableInClassList("terrain-segment-button--active", armed);
        SceneView.RepaintAll();
    }

    void OnRerollSceneGui(SceneView sceneView)
    {
        Event currentEvent = Event.current;
        if (currentEvent.type == EventType.MouseMove || currentEvent.type == EventType.Repaint)
        {
            UpdateHoveredTile(currentEvent.mousePosition);
            if (currentEvent.type == EventType.MouseMove) sceneView.Repaint();
        }

        if (hasHoveredTile && currentEvent.type == EventType.Repaint) DrawHoveredTileOutline();

        bool isPlainKeyPress = currentEvent.type == EventType.KeyDown && !currentEvent.control && !currentEvent.alt && !currentEvent.command;
        if (isPlainKeyPress && currentEvent.keyCode == KeyCode.R && hasHoveredTile)
        {
            Vector2Int coordinate = hoveredTileCoordinate;
            int rollDelta = currentEvent.shift ? -1 : 1;
            currentEvent.Use();
            EditorApplication.delayCall += () => RerollTile(coordinate, rollDelta);
        }
    }

    void UpdateHoveredTile(Vector2 mousePosition)
    {
        Ray mouseRay = HandleUtility.GUIPointToWorldRay(mousePosition);
        Vector3 hitPoint;
        RaycastHit raycastHit;
        if (Physics.Raycast(mouseRay, out raycastHit, 20000f)) hitPoint = raycastHit.point;
        else
        {
            float planeDistance;
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(mouseRay, out planeDistance))
            {
                hasHoveredTile = false;
                return;
            }
            hitPoint = mouseRay.GetPoint(planeDistance);
        }

        Vector2Int coordinate = new Vector2Int(Mathf.RoundToInt(hitPoint.x / TileSizeMetres), Mathf.RoundToInt(hitPoint.z / TileSizeMetres));
        if (!hasHoveredTile || coordinate != hoveredTileCoordinate) UpdateRerollInfoLabel(coordinate);
        hasHoveredTile = true;
        hoveredTileCoordinate = coordinate;
        hoveredSurfaceHeight = hitPoint.y;
    }

    void DrawHoveredTileOutline()
    {
        Vector2 centre = TerrainTile.CenterOfTile(hoveredTileCoordinate);
        float half = TileSizeMetres * 0.5f;
        Vector3[] corners = new Vector3[]
        {
            new Vector3(centre.x - half, hoveredSurfaceHeight, centre.y - half),
            new Vector3(centre.x + half, hoveredSurfaceHeight, centre.y - half),
            new Vector3(centre.x + half, hoveredSurfaceHeight, centre.y + half),
            new Vector3(centre.x - half, hoveredSurfaceHeight, centre.y + half),
        };
        Handles.DrawSolidRectangleWithOutline(corners, new Color(1f, 1f, 1f, 0.06f), Color.white);
    }

    void UpdateRerollInfoLabel(Vector2Int coordinate)
    {
        TerrainTileData tileData = LoadTileData(coordinate);
        if (tileData == null || selectedRegion == null || selectedRegion.profile == null)
        {
            rerollTileInfoLabel.text = "(" + coordinate.x + ", " + coordinate.y + ") no tile";
            return;
        }

        List<TerracePlateauStyle> styles = selectedRegion.profile.plateauStyles;
        string styleName = "?";
        if (styles != null && styles.Count > 0)
        {
            int styleIndex = TerracePlateauField.StyleIndexFor(selectedRegion.profile.seed, new Unity.Mathematics.int2(coordinate.x, coordinate.y), tileData.rollIndex, styles);
            styleName = styles[styleIndex].styleName;
        }
        rerollTileInfoLabel.text = "(" + coordinate.x + ", " + coordinate.y + ") " + styleName + " #" + tileData.rollIndex;
    }

    static TerrainTileData LoadTileData(Vector2Int coordinate)
    {
        TerrainTile tile = TerrainTileCreator.FindTileAt(coordinate);
        if (tile != null && tile.tileData != null) return tile.tileData;
        return AssetDatabase.LoadAssetAtPath<TerrainTileData>(TerrainTileAssets.TileDataPath(coordinate));
    }

    void RerollTileUnderCursorForward()
    {
        TerrainTile activeTile;
        if (hasHoveredTile) RerollTile(hoveredTileCoordinate, 1);
        else if (TerrainTilesPanel.TryGetActiveTile(out activeTile)) RerollTile(activeTile.tileCoordinate, 1);
    }

    void RerollTile(Vector2Int coordinate, int rollDelta)
    {
        if (isRebuildingTile || selectedRegion == null || selectedRegion.profile == null) return;
        TerrainTileData tileData = LoadTileData(coordinate);
        if (tileData == null) return;
        int newRollIndex = Mathf.Max(0, tileData.rollIndex + rollDelta);
        if (newRollIndex == tileData.rollIndex) return;

        bool allowHandAuthored = false;
        if (tileData.handAuthored)
        {
            if (!EditorUtility.DisplayDialog("Hand-authored tile", "This tile is hand authored. Rerolling replaces its generated layers; hand layers are kept. Reroll anyway?", "Reroll", "Cancel"))
                return;
            allowHandAuthored = true;
        }

        Undo.RecordObject(tileData, "Reroll Tile");
        tileData.rollIndex = newRollIndex;
        EditorUtility.SetDirty(tileData);
        RebuildTileFromRollIndex(coordinate, allowHandAuthored);
    }

    void RebuildTileFromRollIndex(Vector2Int coordinate, bool allowHandAuthored)
    {
        isRebuildingTile = true;
        try
        {
            EditorUtility.DisplayProgressBar("Rerolling tile", "(" + coordinate.x + ", " + coordinate.y + ")", 0.5f);
            TerrainGenerateTool.GenerateOneTile(selectedRegion, coordinate, allowHandAuthored);
            TerrainTileData tileData = LoadTileData(coordinate);
            if (tileData != null) lastBuiltRollIndexByTile[coordinate] = tileData.rollIndex;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            isRebuildingTile = false;
        }
        UpdateRerollInfoLabel(coordinate);
    }

    void OnUndoRedoPerformed()
    {
        if (isRebuildingTile || selectedRegion == null || selectedRegion.profile == null) return;
        foreach (KeyValuePair<Vector2Int, int> lastBuilt in new List<KeyValuePair<Vector2Int, int>>(lastBuiltRollIndexByTile))
        {
            TerrainTileData tileData = LoadTileData(lastBuilt.Key);
            if (tileData == null || tileData.rollIndex == lastBuilt.Value) continue;
            Vector2Int coordinate = lastBuilt.Key;
            EditorApplication.delayCall += () => RebuildTileFromRollIndex(coordinate, true);
        }
    }
}
#endif
