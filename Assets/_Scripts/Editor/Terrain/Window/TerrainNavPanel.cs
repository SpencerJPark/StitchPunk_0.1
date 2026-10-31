#if UNITY_EDITOR
using System.Collections.Generic;
using DotsMovementToolkit;
using DotsMovementToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class TerrainNavPanel : TerrainToolPanel
{
    const string AutoBakeEditorPrefsKey = "TerrainEditor.NavAutoBake";
    const long AutoBakeDebounceMilliseconds = 500;
    const byte WallCost = 255;
    const byte HeavyCostMinimum = 50;

    static readonly List<string> DebugOverlayChoices = new List<string> { "Off", "Obstacles Only", "Full Grid" };

    readonly Label summaryLabel = new Label();
    readonly Label blockedLabel = new Label();
    readonly Label heavyLabel = new Label();
    readonly Label shallowLabel = new Label();
    readonly Label roadLabel = new Label();
    readonly Toggle autoBakeToggle = new Toggle("Rebake after road and water edits");
    readonly PopupField<string> debugOverlayField = new PopupField<string>("Debug overlay", DebugOverlayChoices, 0);
    readonly Label noGridHint = new Label("No NavGridAuthoring is loaded in the open scenes.");
    IVisualElementScheduledItem refreshTimer;
    IVisualElementScheduledItem debouncedBakeItem;
    bool subscribedToBakers;
    int bakedTileCount;

    public override string DisplayName => "Nav";
    public override string StatusText => "Nav: " + bakedTileCount + " tiles baked";

    public TerrainNavPanel()
    {
        AddToClassList("terrain-panel");

        Label title = new Label("Nav");
        title.AddToClassList("terrain-panel-title");
        Add(title);

        Label hint = new Label("Bakes each tile's walkable cost: slopes, water, roads, props on the wall layer.");
        hint.AddToClassList("terrain-panel-hint");
        Add(hint);

        summaryLabel.AddToClassList("terrain-warning");
        Add(summaryLabel);
        AddSummaryRow(blockedLabel);
        AddSummaryRow(heavyLabel);
        AddSummaryRow(shallowLabel);
        AddSummaryRow(roadLabel);

        Button bakeThisTileButton = new Button(BakeActiveTile);
        bakeThisTileButton.text = "Bake This Tile";
        bakeThisTileButton.AddToClassList("terrain-primary-button");
        Add(bakeThisTileButton);

        Button bakeAllButton = new Button(BakeAllAndRefresh);
        bakeAllButton.text = "Bake All Open Tiles";
        bakeAllButton.AddToClassList("terrain-segment-button");
        Add(bakeAllButton);

        autoBakeToggle.AddToClassList("terrain-row");
        autoBakeToggle.SetValueWithoutNotify(EditorPrefs.GetBool(AutoBakeEditorPrefsKey, false));
        autoBakeToggle.RegisterValueChangedCallback(OnAutoBakeToggled);
        Add(autoBakeToggle);

        debugOverlayField.AddToClassList("terrain-row");
        debugOverlayField.RegisterValueChangedCallback(OnDebugOverlayChanged);
        Add(debugOverlayField);
        noGridHint.AddToClassList("terrain-panel-hint");
        Add(noGridHint);
    }

    public override void OnShown()
    {
        UpdateBakerSubscription();
        if (refreshTimer == null) refreshTimer = schedule.Execute(RefreshAll).Every(1000);
        else refreshTimer.Resume();
        RefreshAll();
    }

    public override void OnHidden()
    {
        SetBakerSubscription(false);
        if (refreshTimer != null) refreshTimer.Pause();
        if (debouncedBakeItem != null) debouncedBakeItem.Pause();
    }

    void AddSummaryRow(Label rowLabel)
    {
        rowLabel.AddToClassList("terrain-row");
        Add(rowLabel);
    }

    void OnAutoBakeToggled(ChangeEvent<bool> changeEvent)
    {
        EditorPrefs.SetBool(AutoBakeEditorPrefsKey, changeEvent.newValue);
        UpdateBakerSubscription();
    }

    void UpdateBakerSubscription()
    {
        SetBakerSubscription(autoBakeToggle.value);
    }

    void SetBakerSubscription(bool shouldSubscribe)
    {
        if (shouldSubscribe == subscribedToBakers) return;
        subscribedToBakers = shouldSubscribe;
        if (shouldSubscribe)
        {
            TerrainRoadBaker.BakeFinished += OnRoadOrWaterBakeFinished;
            TerrainWaterBaker.BakeFinished += OnRoadOrWaterBakeFinished;
        }
        else
        {
            TerrainRoadBaker.BakeFinished -= OnRoadOrWaterBakeFinished;
            TerrainWaterBaker.BakeFinished -= OnRoadOrWaterBakeFinished;
            if (debouncedBakeItem != null) debouncedBakeItem.Pause();
        }
    }

    // Road and water bakers fire once per tile; waiting out the burst means one nav bake per edit session.
    void OnRoadOrWaterBakeFinished()
    {
        if (debouncedBakeItem == null) debouncedBakeItem = schedule.Execute(BakeAllAndRefresh);
        debouncedBakeItem.ExecuteLater(AutoBakeDebounceMilliseconds);
    }

    void BakeActiveTile()
    {
        TerrainTile activeTile;
        if (!TerrainTilesPanel.TryGetActiveTile(out activeTile)) return;
        TerrainNavBaker.BakeTile(activeTile);
        RefreshAll();
    }

    void BakeAllAndRefresh()
    {
        TerrainNavBaker.BakeAllOpenTiles();
        RefreshAll();
    }

    void RefreshAll()
    {
        RefreshCounts();
        RefreshDebugOverlayField();
    }

    void RefreshCounts()
    {
        TerrainTile[] openTiles = Object.FindObjectsByType<TerrainTile>(FindObjectsInactive.Include);
        int previousBakedTileCount = bakedTileCount;
        bakedTileCount = 0;
        for (int tileIndex = 0; tileIndex < openTiles.Length; tileIndex++)
        {
            TerrainTileNavAuthoring tileAuthoring = openTiles[tileIndex].GetComponent<TerrainTileNavAuthoring>();
            if (HasBakedArrays(tileAuthoring)) bakedTileCount++;
        }
        if (bakedTileCount != previousBakedTileCount) RaiseStatusChanged();

        TerrainTile activeTile;
        TerrainTileNavAuthoring activeAuthoring = null;
        if (TerrainTilesPanel.TryGetActiveTile(out activeTile)) activeAuthoring = activeTile.GetComponent<TerrainTileNavAuthoring>();

        if (!HasBakedArrays(activeAuthoring))
        {
            summaryLabel.text = "Not baked";
            blockedLabel.text = string.Empty;
            heavyLabel.text = string.Empty;
            shallowLabel.text = string.Empty;
            roadLabel.text = string.Empty;
            return;
        }

        int blockedCount = 0;
        int heavyCount = 0;
        int shallowCount = 0;
        int roadCount = 0;
        for (int cellIndex = 0; cellIndex < activeAuthoring.costs.Length; cellIndex++)
        {
            byte cost = activeAuthoring.costs[cellIndex];
            if (cost == WallCost) blockedCount++;
            else if (cost >= HeavyCostMinimum) heavyCount++;
        }
        for (int cellIndex = 0; cellIndex < activeAuthoring.surface.Length; cellIndex++)
        {
            byte surfaceValue = activeAuthoring.surface[cellIndex];
            if (surfaceValue == (byte)TerrainSurface.Shallow) shallowCount++;
            else if (surfaceValue == (byte)TerrainSurface.Road) roadCount++;
        }
        summaryLabel.text = string.Empty;
        blockedLabel.text = "Blocked: " + blockedCount;
        heavyLabel.text = "Heavy: " + heavyCount;
        shallowLabel.text = "Shallow: " + shallowCount;
        roadLabel.text = "Road: " + roadCount;
    }

    static bool HasBakedArrays(TerrainTileNavAuthoring tileAuthoring)
    {
        return tileAuthoring != null
            && tileAuthoring.costs != null && tileAuthoring.costs.Length > 0
            && tileAuthoring.surface != null && tileAuthoring.surface.Length > 0;
    }

    void RefreshDebugOverlayField()
    {
        NavGridAuthoring[] grids = Object.FindObjectsByType<NavGridAuthoring>(FindObjectsInactive.Exclude);
        bool hasGrid = grids.Length > 0;
        debugOverlayField.style.display = hasGrid ? DisplayStyle.Flex : DisplayStyle.None;
        noGridHint.style.display = hasGrid ? DisplayStyle.None : DisplayStyle.Flex;
        if (hasGrid)
        {
            debugOverlayField.SetValueWithoutNotify(DebugOverlayChoices[(int)grids[0].debugDisplayMode]);
        }
    }

    void OnDebugOverlayChanged(ChangeEvent<string> changeEvent)
    {
        int choiceIndex = DebugOverlayChoices.IndexOf(changeEvent.newValue);
        if (choiceIndex < 0) return;
        NavGridAuthoring[] grids = Object.FindObjectsByType<NavGridAuthoring>(FindObjectsInactive.Exclude);
        for (int gridIndex = 0; gridIndex < grids.Length; gridIndex++)
        {
            Undo.RecordObject(grids[gridIndex], "Nav Debug Overlay");
            grids[gridIndex].debugDisplayMode = (NavGridDebugDisplayMode)choiceIndex;
            EditorUtility.SetDirty(grids[gridIndex]);
        }
    }
}
#endif
