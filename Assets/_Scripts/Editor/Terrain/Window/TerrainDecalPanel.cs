#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class TerrainDecalPanel : TerrainToolPanel
{
    const float ThumbnailSizePixels = 28f;
    const string DecalsChildName = "Decals";

    static readonly TerrainBrushTool[] DecalTools = { TerrainBrushTool.Decal, TerrainBrushTool.DecalErase, TerrainBrushTool.DecalRegion };
    static readonly string[] DecalToolLabels = { "Stamp", "Erase", "Region" };

    readonly ObjectField setField = new ObjectField("Decal Set");
    readonly VisualElement emptyState = new VisualElement();
    readonly VisualElement setGroup = new VisualElement();
    readonly VisualElement entryListContainer = new VisualElement();
    readonly Slider densitySlider = new Slider(0f, 10f);
    readonly IntegerField seedField = new IntegerField("Seed");
    readonly Button[] toolButtons = new Button[3];
    readonly Button scatterTileButton = new Button();
    readonly List<Sprite> thumbnailSprites = new List<Sprite>();
    IVisualElementScheduledItem refreshTimer;
    TerrainDecalSet listedSet;
    int listedEntryCount = -1;
    int listedSelectedIndex = -1;

    public override string DisplayName => "Decals";

    public override string StatusText
    {
        get
        {
            TerrainDecalSet set = BrushState.activeDecalSet;
            bool hasEntry = set != null && BrushState.decalEntryIndex >= 0 && BrushState.decalEntryIndex < set.entries.Count;
            return "Decals: " + (hasEntry ? set.entries[BrushState.decalEntryIndex].label : "none");
        }
    }

    public TerrainDecalPanel()
    {
        AddToClassList("terrain-panel");

        Label title = new Label("Decals");
        title.AddToClassList("terrain-panel-title");
        Add(title);

        Label hint = new Label("Stamp or erase decals under the brush, or drag a region to scatter the set across it.");
        hint.AddToClassList("terrain-panel-hint");
        Add(hint);

        setField.objectType = typeof(TerrainDecalSet);
        setField.allowSceneObjects = false;
        setField.AddToClassList("terrain-row");
        setField.RegisterValueChangedCallback(OnDecalSetChanged);
        Add(setField);

        emptyState.AddToClassList("terrain-empty");
        Label emptyTitle = new Label("No decal set");
        emptyTitle.AddToClassList("terrain-panel-title");
        Label emptyWhy = new Label("A decal set holds the atlas and the entries the brush stamps. Create the default one to start.");
        emptyWhy.AddToClassList("terrain-panel-hint");
        Button createButton = new Button(CreateDefaultSet);
        createButton.text = "Create Default Set";
        createButton.AddToClassList("terrain-primary-button");
        emptyState.Add(emptyTitle);
        emptyState.Add(emptyWhy);
        emptyState.Add(createButton);
        Add(emptyState);

        setGroup.Add(entryListContainer);

        VisualElement densityRow = new VisualElement();
        densityRow.AddToClassList("terrain-row");
        Label densityLabel = new Label("Density");
        densityLabel.AddToClassList("terrain-label");
        densitySlider.showInputField = true;
        densitySlider.RegisterValueChangedCallback(OnDensityChanged);
        densityRow.Add(densityLabel);
        densityRow.Add(densitySlider);
        setGroup.Add(densityRow);
        Add(setGroup);

        seedField.AddToClassList("terrain-row");
        seedField.SetValueWithoutNotify(TerrainDecalBrush.seed);
        seedField.RegisterValueChangedCallback(changeEvent => TerrainDecalBrush.seed = changeEvent.newValue);
        Add(seedField);

        VisualElement toolRow = new VisualElement();
        toolRow.AddToClassList("terrain-segment-row");
        for (int toolIndex = 0; toolIndex < DecalTools.Length; toolIndex++)
        {
            TerrainBrushTool tool = DecalTools[toolIndex];
            Button button = new Button(() => SelectTool(tool));
            button.text = DecalToolLabels[toolIndex];
            button.AddToClassList("terrain-segment-button");
            toolButtons[toolIndex] = button;
            toolRow.Add(button);
        }
        Add(toolRow);

        scatterTileButton.text = "Scatter Tile";
        scatterTileButton.clicked += ScatterActiveTile;
        scatterTileButton.AddToClassList("terrain-primary-button");
        Add(scatterTileButton);

        VisualElement maintenanceRow = new VisualElement();
        maintenanceRow.AddToClassList("terrain-segment-row");
        Button clearButton = new Button(ClearUnlockedOnActiveTile);
        clearButton.text = "Clear Unlocked";
        clearButton.AddToClassList("terrain-segment-button");
        Button lockButton = new Button(LockAllOnActiveTile);
        lockButton.text = "Lock All";
        lockButton.AddToClassList("terrain-segment-button");
        maintenanceRow.Add(clearButton);
        maintenanceRow.Add(lockButton);
        Add(maintenanceRow);
    }

    public override void OnShown()
    {
        BrushState.activeTool = TerrainBrushTool.Decal;
        if (refreshTimer == null) refreshTimer = schedule.Execute(Refresh).Every(250);
        else refreshTimer.Resume();
        listedSet = null;
        Refresh();
    }

    public override void OnHidden()
    {
        if (IsDecalTool(BrushState.activeTool)) BrushState.activeTool = TerrainBrushTool.None;
        if (refreshTimer != null) refreshTimer.Pause();
        entryListContainer.Clear();
        DestroyThumbnails();
        listedSet = null;
    }

    static bool IsDecalTool(TerrainBrushTool tool)
    {
        return System.Array.IndexOf(DecalTools, tool) >= 0;
    }

    void Refresh()
    {
        if (BrushState.activeDecalSet == null)
        {
            string[] guids = AssetDatabase.FindAssets("t:TerrainDecalSet");
            if (guids.Length > 0)
                BrushState.activeDecalSet = AssetDatabase.LoadAssetAtPath<TerrainDecalSet>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        TerrainDecalSet set = BrushState.activeDecalSet;
        setField.SetValueWithoutNotify(set);
        emptyState.style.display = set == null ? DisplayStyle.Flex : DisplayStyle.None;
        setGroup.style.display = set != null ? DisplayStyle.Flex : DisplayStyle.None;

        int entryCount = set != null ? set.entries.Count : 0;
        if (set != null) BrushState.decalEntryIndex = Mathf.Clamp(BrushState.decalEntryIndex, 0, Mathf.Max(0, entryCount - 1));
        bool listChanged = set != listedSet || entryCount != listedEntryCount || BrushState.decalEntryIndex != listedSelectedIndex;
        if (listChanged) RebuildEntryList(set);

        if (set != null && entryCount > 0)
            densitySlider.SetValueWithoutNotify(set.entries[BrushState.decalEntryIndex].densityPer100SquareMetres);
        densitySlider.SetEnabled(set != null && entryCount > 0);

        for (int toolIndex = 0; toolIndex < DecalTools.Length; toolIndex++)
            toolButtons[toolIndex].EnableInClassList("terrain-segment-button--active", DecalTools[toolIndex] == BrushState.activeTool);

        TerrainTile activeTile;
        bool hasTile = TerrainTilesPanel.TryGetActiveTile(out activeTile);
        scatterTileButton.SetEnabled(hasTile && set != null);
        scatterTileButton.tooltip = !hasTile ? "Open a tile in the Tiles tab first" : set == null ? "Pick or create a decal set first" : string.Empty;

        if (listChanged) RaiseStatusChanged();
    }

    void RebuildEntryList(TerrainDecalSet set)
    {
        entryListContainer.Clear();
        DestroyThumbnails();
        listedSet = set;
        listedEntryCount = set != null ? set.entries.Count : 0;
        listedSelectedIndex = BrushState.decalEntryIndex;
        if (set == null) return;

        for (int entryIndex = 0; entryIndex < set.entries.Count; entryIndex++)
        {
            int capturedIndex = entryIndex;
            TerrainDecalEntry entry = set.entries[entryIndex];
            Button row = new Button(() => SelectEntry(capturedIndex));
            row.AddToClassList("terrain-list-row");
            if (entryIndex == BrushState.decalEntryIndex) row.AddToClassList("terrain-list-row--selected");

            VisualElement thumbnail = new VisualElement();
            thumbnail.style.width = ThumbnailSizePixels;
            thumbnail.style.height = ThumbnailSizePixels;
            Sprite sprite = CreateCellSprite(set, entry.atlasTile);
            if (sprite != null)
            {
                thumbnailSprites.Add(sprite);
                thumbnail.style.backgroundImage = new StyleBackground(sprite);
            }
            Label label = new Label(entry.label + ", density " + entry.densityPer100SquareMetres.ToString("0.#"));
            row.Add(thumbnail);
            row.Add(label);
            entryListContainer.Add(row);
        }
    }

    // Shader picks cells row-major from the top-left; texture v = 0 is the bottom, so flip the row.
    static Sprite CreateCellSprite(TerrainDecalSet set, int atlasTile)
    {
        if (set.atlas == null) return null;
        int columns = Mathf.Max(1, set.atlasGrid.x);
        int rows = Mathf.Max(1, set.atlasGrid.y);
        float cellWidth = set.atlas.width / (float)columns;
        float cellHeight = set.atlas.height / (float)rows;
        int column = atlasTile % columns;
        int rowFromTop = atlasTile / columns;
        if (rowFromTop >= rows) return null;
        Rect cellRect = new Rect(column * cellWidth, (rows - 1 - rowFromTop) * cellHeight, cellWidth, cellHeight);
        return Sprite.Create(set.atlas, cellRect, new Vector2(0.5f, 0.5f));
    }

    void DestroyThumbnails()
    {
        foreach (Sprite sprite in thumbnailSprites)
        {
            if (sprite != null) Object.DestroyImmediate(sprite);
        }
        thumbnailSprites.Clear();
    }

    void OnDecalSetChanged(ChangeEvent<Object> changeEvent)
    {
        BrushState.activeDecalSet = changeEvent.newValue as TerrainDecalSet;
        BrushState.decalEntryIndex = 0;
        Refresh();
    }

    void CreateDefaultSet()
    {
        BrushState.activeDecalSet = TerrainDecalScatterer.CreateDefaultDecalSetAsset();
        BrushState.decalEntryIndex = 0;
        Refresh();
    }

    void SelectEntry(int entryIndex)
    {
        BrushState.decalEntryIndex = entryIndex;
        Refresh();
    }

    void SelectTool(TerrainBrushTool tool)
    {
        BrushState.activeTool = tool;
        Refresh();
    }

    void OnDensityChanged(ChangeEvent<float> changeEvent)
    {
        TerrainDecalSet set = BrushState.activeDecalSet;
        int entryIndex = BrushState.decalEntryIndex;
        if (set == null || entryIndex < 0 || entryIndex >= set.entries.Count) return;
        Undo.RecordObject(set, "Set Decal Density");
        TerrainDecalEntry entry = set.entries[entryIndex];
        entry.densityPer100SquareMetres = changeEvent.newValue;
        set.entries[entryIndex] = entry;
        EditorUtility.SetDirty(set);
        listedEntryCount = -1;
    }

    void ScatterActiveTile()
    {
        TerrainTile tile;
        if (!TerrainTilesPanel.TryGetActiveTile(out tile) || BrushState.activeDecalSet == null) return;
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Scatter Decals Tile");
        TerrainDecalScatterer.Scatter(tile, BrushState.activeDecalSet, TerrainDecalBrush.seed, null);
    }

    void ClearUnlockedOnActiveTile()
    {
        Transform decalParent = ActiveTileDecalParent();
        if (decalParent != null) TerrainDecalScatterer.ClearUnlocked(decalParent);
    }

    void LockAllOnActiveTile()
    {
        Transform decalParent = ActiveTileDecalParent();
        if (decalParent != null) TerrainDecalScatterer.LockAll(decalParent);
    }

    static Transform ActiveTileDecalParent()
    {
        TerrainTile tile;
        if (!TerrainTilesPanel.TryGetActiveTile(out tile)) return null;
        return tile.transform.Find(DecalsChildName);
    }
}
#endif
