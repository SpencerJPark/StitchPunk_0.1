#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Scenes;
using SubSceneEditorUtility = Unity.Scenes.Editor.SubSceneUtility;
using SubSceneEditorInspectorUtility = Unity.Scenes.Editor.SubSceneInspectorUtility;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public enum TerrainTileCellState
{
    Empty,
    Exists,
    Open,
    Unsaved
}

// One painted element for the whole world rectangle; per-cell elements would be 900 layout nodes.
public sealed class TerrainTileMapElement : VisualElement
{
    public const float CellSizePixels = 14f;
    const float WorldBoundsLineWidth = 2f;

    public static readonly Color EmptyColor = new Color(0.14f, 0.14f, 0.14f, 1f);
    public static readonly Color ExistsColor = new Color(0.35f, 0.55f, 0.35f, 1f);
    public static readonly Color OpenColor = new Color(0.30f, 0.60f, 0.95f, 1f);
    public static readonly Color UnsavedColor = new Color(0.95f, 0.70f, 0.20f, 1f);
    public static readonly Color PlusColor = new Color(0.55f, 0.85f, 0.55f, 1f);
    static readonly Color CellOutlineColor = new Color(0.30f, 0.30f, 0.30f, 1f);
    static readonly Color WorldBoundsColor = new Color(0.85f, 0.85f, 0.85f, 1f);
    static readonly Color HoverColor = new Color(1f, 1f, 1f, 0.35f);

    Vector2Int originTile = new Vector2Int(-15, -15);
    int countX = 30;
    int countZ = 30;
    bool isHovering;
    Vector2Int hoveredCell;

    public Func<Vector2Int, TerrainTileCellState> StateOfCell;
    public event Action<Vector2Int> CellPrimaryClicked;
    public event Action<Vector2Int> CellSecondaryClicked;
    public event Action HoverChanged;
    public event Action<List<Vector2Int>> cellsDragSelected;

    readonly Label dragCountLabel;
    bool isDragArmed;
    bool isDragSpanningCells;
    int dragPointerId;
    Vector2Int dragAnchorCell;
    Vector2Int dragCurrentCell;

    public bool IsHovering => isHovering;
    public Vector2Int HoveredCell => hoveredCell;

    public TerrainTileMapElement()
    {
        AddToClassList("terrain-grid");
        generateVisualContent += DrawMap;
        RegisterCallback<PointerMoveEvent>(OnPointerMove);
        RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
        RegisterCallback<PointerDownEvent>(OnPointerDown);
        RegisterCallback<PointerMoveEvent>(OnDragPointerMove);
        RegisterCallback<PointerUpEvent>(OnPointerUp);
        RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        RegisterCallback<KeyDownEvent>(OnKeyDown);
        focusable = true;

        dragCountLabel = new Label();
        dragCountLabel.pickingMode = PickingMode.Ignore;
        dragCountLabel.style.position = Position.Absolute;
        dragCountLabel.style.display = DisplayStyle.None;
        dragCountLabel.style.color = PlusColor;
        dragCountLabel.style.backgroundColor = EmptyColor;
        dragCountLabel.style.paddingLeft = 4f;
        dragCountLabel.style.paddingRight = 4f;
        Add(dragCountLabel);
        ApplyWorldSize();
    }

    public void Configure(Vector2Int newOriginTile, int newCountX, int newCountZ)
    {
        originTile = newOriginTile;
        countX = newCountX;
        countZ = newCountZ;
        ApplyWorldSize();
        MarkDirtyRepaint();
    }

    void ApplyWorldSize()
    {
        style.width = countX * CellSizePixels;
        style.height = countZ * CellSizePixels;
        style.flexGrow = 0f;
        style.flexShrink = 0f;
    }

    Vector2 CellTopLeft(Vector2Int cell)
    {
        int column = cell.x - originTile.x;
        int row = countZ - 1 - (cell.y - originTile.y);
        return new Vector2(column * CellSizePixels, row * CellSizePixels);
    }

    bool TryCellAt(Vector2 localPosition, out Vector2Int cell)
    {
        int column = Mathf.FloorToInt(localPosition.x / CellSizePixels);
        int row = Mathf.FloorToInt(localPosition.y / CellSizePixels);
        cell = new Vector2Int(originTile.x + column, originTile.y + (countZ - 1 - row));
        return column >= 0 && column < countX && row >= 0 && row < countZ;
    }

    TerrainTileCellState StateOf(Vector2Int cell)
    {
        return StateOfCell != null ? StateOfCell(cell) : TerrainTileCellState.Empty;
    }

    bool IsPlusCell(Vector2Int cell)
    {
        if (StateOf(cell) != TerrainTileCellState.Empty) return false;
        return StateOf(cell + Vector2Int.right) != TerrainTileCellState.Empty
            || StateOf(cell + Vector2Int.left) != TerrainTileCellState.Empty
            || StateOf(cell + Vector2Int.up) != TerrainTileCellState.Empty
            || StateOf(cell + Vector2Int.down) != TerrainTileCellState.Empty;
    }

    public bool IsCreatableCell(Vector2Int cell)
    {
        bool insideWorld = cell.x >= originTile.x && cell.x < originTile.x + countX
            && cell.y >= originTile.y && cell.y < originTile.y + countZ;
        return insideWorld && IsPlusCell(cell);
    }

    static Color ColorOf(TerrainTileCellState state)
    {
        switch (state)
        {
            case TerrainTileCellState.Exists: return ExistsColor;
            case TerrainTileCellState.Open: return OpenColor;
            case TerrainTileCellState.Unsaved: return UnsavedColor;
            default: return EmptyColor;
        }
    }

    static void FillRectangle(Painter2D painter, Vector2 topLeft, float width, float height, Color color)
    {
        painter.fillColor = color;
        painter.BeginPath();
        painter.MoveTo(topLeft);
        painter.LineTo(topLeft + new Vector2(width, 0f));
        painter.LineTo(topLeft + new Vector2(width, height));
        painter.LineTo(topLeft + new Vector2(0f, height));
        painter.ClosePath();
        painter.Fill();
    }

    static void StrokeRectangle(Painter2D painter, Vector2 topLeft, float width, float height, Color color, float lineWidth)
    {
        painter.strokeColor = color;
        painter.lineWidth = lineWidth;
        painter.BeginPath();
        painter.MoveTo(topLeft);
        painter.LineTo(topLeft + new Vector2(width, 0f));
        painter.LineTo(topLeft + new Vector2(width, height));
        painter.LineTo(topLeft + new Vector2(0f, height));
        painter.ClosePath();
        painter.Stroke();
    }

    void DrawMap(MeshGenerationContext context)
    {
        Painter2D painter = context.painter2D;
        for (int column = 0; column < countX; column++)
        {
            for (int row = 0; row < countZ; row++)
            {
                Vector2Int cell = new Vector2Int(originTile.x + column, originTile.y + (countZ - 1 - row));
                Vector2 topLeft = new Vector2(column * CellSizePixels, row * CellSizePixels);
                TerrainTileCellState state = StateOf(cell);
                FillRectangle(painter, topLeft, CellSizePixels, CellSizePixels, ColorOf(state));
                if (state == TerrainTileCellState.Empty)
                {
                    StrokeRectangle(painter, topLeft, CellSizePixels, CellSizePixels, CellOutlineColor, 1f);
                    if (IsPlusCell(cell)) DrawPlus(painter, topLeft);
                }
            }
        }

        if (isDragArmed && isDragSpanningCells) DrawDragRectangle(painter);
        else if (isHovering)
            FillRectangle(painter, CellTopLeft(hoveredCell), CellSizePixels, CellSizePixels, HoverColor);

        float inset = WorldBoundsLineWidth * 0.5f;
        StrokeRectangle(painter, new Vector2(inset, inset), countX * CellSizePixels - WorldBoundsLineWidth, countZ * CellSizePixels - WorldBoundsLineWidth, WorldBoundsColor, WorldBoundsLineWidth);
    }

    static void DrawPlus(Painter2D painter, Vector2 topLeft)
    {
        float middle = CellSizePixels * 0.5f;
        float arm = CellSizePixels * 0.25f;
        painter.strokeColor = PlusColor;
        painter.lineWidth = 2f;
        painter.BeginPath();
        painter.MoveTo(topLeft + new Vector2(middle - arm, middle));
        painter.LineTo(topLeft + new Vector2(middle + arm, middle));
        painter.MoveTo(topLeft + new Vector2(middle, middle - arm));
        painter.LineTo(topLeft + new Vector2(middle, middle + arm));
        painter.Stroke();
    }

    void OnPointerMove(PointerMoveEvent pointerEvent)
    {
        Vector2Int cell;
        bool insideMap = TryCellAt(pointerEvent.localPosition, out cell);
        if (insideMap == isHovering && (!insideMap || cell == hoveredCell)) return;
        isHovering = insideMap;
        hoveredCell = cell;
        MarkDirtyRepaint();
        HoverChanged?.Invoke();
    }

    void OnPointerLeave(PointerLeaveEvent pointerEvent)
    {
        if (!isHovering) return;
        isHovering = false;
        MarkDirtyRepaint();
        HoverChanged?.Invoke();
    }

    void OnPointerDown(PointerDownEvent pointerEvent)
    {
        if (isDragArmed)
        {
            if (pointerEvent.button == 1) CancelDrag();
            return;
        }

        Vector2Int cell;
        if (!TryCellAt(pointerEvent.localPosition, out cell)) return;
        if (pointerEvent.button == 0)
        {
            if (!IsCreatableCell(cell))
            {
                CellPrimaryClicked?.Invoke(cell);
                return;
            }
            // A creatable cell waits for pointer up: no movement past one cell is a plain click.
            isDragArmed = true;
            isDragSpanningCells = false;
            dragAnchorCell = cell;
            dragCurrentCell = cell;
            dragPointerId = pointerEvent.pointerId;
            this.CapturePointer(dragPointerId);
            Focus();
            pointerEvent.StopPropagation();
        }
        else if (pointerEvent.button == 1) CellSecondaryClicked?.Invoke(cell);
    }

    void OnDragPointerMove(PointerMoveEvent pointerEvent)
    {
        if (!isDragArmed) return;
        Vector2Int cell = CellAtClamped(pointerEvent.localPosition);
        if (cell != dragCurrentCell || !isDragSpanningCells)
        {
            dragCurrentCell = cell;
            if (cell != dragAnchorCell) isDragSpanningCells = true;
            UpdateDragCountLabel(pointerEvent.localPosition);
            MarkDirtyRepaint();
        }
        else if (isDragSpanningCells) UpdateDragCountLabel(pointerEvent.localPosition);
    }

    void OnPointerUp(PointerUpEvent pointerEvent)
    {
        if (!isDragArmed || pointerEvent.button != 0) return;
        bool wasSpanningCells = isDragSpanningCells;
        Vector2Int anchorCell = dragAnchorCell;
        List<Vector2Int> cellsToCreate = CellsInDragRectangle();
        CancelDrag();
        if (!wasSpanningCells) CellPrimaryClicked?.Invoke(anchorCell);
        else if (cellsToCreate.Count > 0) cellsDragSelected?.Invoke(cellsToCreate);
    }

    void OnPointerCaptureOut(PointerCaptureOutEvent captureEvent)
    {
        if (isDragArmed) CancelDrag();
    }

    void OnKeyDown(KeyDownEvent keyEvent)
    {
        if (isDragArmed && keyEvent.keyCode == KeyCode.Escape)
        {
            CancelDrag();
            keyEvent.StopPropagation();
        }
    }

    void CancelDrag()
    {
        bool wasArmed = isDragArmed;
        isDragArmed = false;
        isDragSpanningCells = false;
        dragCountLabel.style.display = DisplayStyle.None;
        if (wasArmed && this.HasPointerCapture(dragPointerId)) this.ReleasePointer(dragPointerId);
        MarkDirtyRepaint();
    }

    Vector2Int CellAtClamped(Vector2 localPosition)
    {
        int column = Mathf.Clamp(Mathf.FloorToInt(localPosition.x / CellSizePixels), 0, countX - 1);
        int row = Mathf.Clamp(Mathf.FloorToInt(localPosition.y / CellSizePixels), 0, countZ - 1);
        return new Vector2Int(originTile.x + column, originTile.y + (countZ - 1 - row));
    }

    List<Vector2Int> CellsInDragRectangle()
    {
        List<Vector2Int> cells = new List<Vector2Int>();
        int minX = Mathf.Min(dragAnchorCell.x, dragCurrentCell.x);
        int maxX = Mathf.Max(dragAnchorCell.x, dragCurrentCell.x);
        int minZ = Mathf.Min(dragAnchorCell.y, dragCurrentCell.y);
        int maxZ = Mathf.Max(dragAnchorCell.y, dragCurrentCell.y);
        for (int cellZ = minZ; cellZ <= maxZ; cellZ++)
            for (int cellX = minX; cellX <= maxX; cellX++)
                if (StateOf(new Vector2Int(cellX, cellZ)) == TerrainTileCellState.Empty) cells.Add(new Vector2Int(cellX, cellZ));
        return cells;
    }

    void UpdateDragCountLabel(Vector2 localPosition)
    {
        int tileCount = CellsInDragRectangle().Count;
        dragCountLabel.text = tileCount + (tileCount == 1 ? " tile" : " tiles");
        dragCountLabel.style.left = localPosition.x + 12f;
        dragCountLabel.style.top = localPosition.y + 12f;
        dragCountLabel.style.display = DisplayStyle.Flex;
    }

    void DrawDragRectangle(Painter2D painter)
    {
        Vector2 anchorTopLeft = CellTopLeft(dragAnchorCell);
        Vector2 currentTopLeft = CellTopLeft(dragCurrentCell);
        Vector2 rectangleTopLeft = new Vector2(Mathf.Min(anchorTopLeft.x, currentTopLeft.x), Mathf.Min(anchorTopLeft.y, currentTopLeft.y));
        float width = Mathf.Abs(anchorTopLeft.x - currentTopLeft.x) + CellSizePixels;
        float height = Mathf.Abs(anchorTopLeft.y - currentTopLeft.y) + CellSizePixels;
        FillRectangle(painter, rectangleTopLeft, width, height, HoverColor);
        StrokeRectangle(painter, rectangleTopLeft, width, height, PlusColor, 2f);
    }
}

public sealed class TerrainTilesPanel : TerrainToolPanel
{
    readonly TerrainTileMapElement mapElement = new TerrainTileMapElement();
    readonly VisualElement mapGroup = new VisualElement();
    readonly VisualElement emptyState = new VisualElement();
    readonly Label overBudgetLabel = new Label();
    readonly HashSet<Vector2Int> tilesWithSceneAsset = new HashSet<Vector2Int>();
    readonly Dictionary<Vector2Int, bool> openTilesAndDirtyFlag = new Dictionary<Vector2Int, bool>();
    Vector2Int worldOriginTile = new Vector2Int(-15, -15);
    int worldTilesX = 30;
    int worldTilesZ = 30;
    IVisualElementScheduledItem refreshTimer;
    string statusText = string.Empty;

    public override string DisplayName => "Tiles";
    public override string StatusText => statusText;

    public TerrainTilesPanel()
    {
        AddToClassList("terrain-panel");

        Label title = new Label("Tiles");
        title.AddToClassList("terrain-panel-title");
        Add(title);

        Label hint = new Label("Click a tile to edit it, a plus to grow the world, right click for more.");
        hint.AddToClassList("terrain-panel-hint");
        Add(hint);

        ScrollView mapScroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
        mapScroll.Add(mapElement);
        mapGroup.Add(mapScroll);
        mapGroup.Add(BuildLegend());
        overBudgetLabel.style.color = new Color(0.9f, 0.25f, 0.25f);
        overBudgetLabel.style.display = DisplayStyle.None;
        mapGroup.Add(overBudgetLabel);
        Add(mapGroup);

        emptyState.AddToClassList("terrain-empty");
        Label emptyTitle = new Label("No terrain tiles yet");
        emptyTitle.AddToClassList("terrain-panel-title");
        Label emptyWhy = new Label("The world is built from 100 m tiles. Create the first one to start sculpting and painting.");
        emptyWhy.AddToClassList("terrain-panel-hint");
        emptyState.Add(emptyTitle);
        emptyState.Add(emptyWhy);
        Add(emptyState);

        Button createButton = new Button(TerrainTileCreatorWindow.Open);
        createButton.text = "Create Tile...";
        createButton.AddToClassList("terrain-primary-button");
        Add(createButton);

        mapElement.StateOfCell = StateOfCell;
        mapElement.CellPrimaryClicked += OnCellPrimaryClicked;
        mapElement.CellSecondaryClicked += OnCellSecondaryClicked;
        mapElement.cellsDragSelected += OnCellsDragSelected;
        mapElement.HoverChanged += OnHoverChanged;
    }

    static VisualElement BuildLegend()
    {
        VisualElement legend = new VisualElement();
        legend.AddToClassList("terrain-legend");
        legend.Add(BuildLegendItem("Empty", TerrainTileMapElement.EmptyColor));
        legend.Add(BuildLegendItem("Exists", TerrainTileMapElement.ExistsColor));
        legend.Add(BuildLegendItem("Open", TerrainTileMapElement.OpenColor));
        legend.Add(BuildLegendItem("Unsaved", TerrainTileMapElement.UnsavedColor));
        legend.Add(BuildLegendItem("Plus (create)", TerrainTileMapElement.PlusColor));
        return legend;
    }

    static VisualElement BuildLegendItem(string labelText, Color chipColor)
    {
        VisualElement item = new VisualElement();
        item.AddToClassList("terrain-legend-item");
        VisualElement chip = new VisualElement();
        chip.AddToClassList("terrain-legend-chip");
        chip.style.backgroundColor = chipColor;
        item.Add(chip);
        item.Add(new Label(labelText));
        return item;
    }

    public override void OnShown()
    {
        EditorApplication.projectChanged -= RefreshAll;
        EditorApplication.projectChanged += RefreshAll;
        if (refreshTimer == null) refreshTimer = schedule.Execute(RefreshOpenTiles).Every(500);
        else refreshTimer.Resume();
        RefreshAll();
    }

    public override void OnHidden()
    {
        EditorApplication.projectChanged -= RefreshAll;
        if (refreshTimer != null) refreshTimer.Pause();
    }

    void RefreshAll()
    {
        TerrainWorldProfile profile = TerrainWorldProfile.FindInProject();
        if (profile != null)
        {
            worldOriginTile = profile.worldOriginTile;
            worldTilesX = profile.worldTilesX;
            worldTilesZ = profile.worldTilesZ;
        }
        mapElement.Configure(worldOriginTile, worldTilesX, worldTilesZ);

        tilesWithSceneAsset.Clear();
        for (int column = 0; column < worldTilesX; column++)
        {
            for (int row = 0; row < worldTilesZ; row++)
            {
                Vector2Int coordinate = new Vector2Int(worldOriginTile.x + column, worldOriginTile.y + row);
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TerrainTileAssets.TileScenePath(coordinate)) != null)
                    tilesWithSceneAsset.Add(coordinate);
            }
        }
        RefreshOpenTiles();
        RefreshOverBudgetLabel();
    }

    // Counting markers walks every open tile's hierarchy, so this runs on refresh only, not on the 500 ms timer.
    void RefreshOverBudgetLabel()
    {
        List<string> overBudgetTiles = new List<string>();
        foreach (Vector2Int coordinate in openTilesAndDirtyFlag.Keys)
        {
            TerrainTileBudgetResult budget;
            if (TerrainBudgetCheck.TryEvaluateByCoordinate(coordinate, out budget) && budget.isOverBudget)
                overBudgetTiles.Add("(" + coordinate.x + ", " + coordinate.y + ") " + budget.summary);
        }
        overBudgetLabel.text = "Over budget: " + string.Join("; ", overBudgetTiles);
        overBudgetLabel.style.display = overBudgetTiles.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void RefreshOpenTiles()
    {
        openTilesAndDirtyFlag.Clear();
        foreach (TerrainTile tile in UnityEngine.Object.FindObjectsByType<TerrainTile>(FindObjectsInactive.Include))
            openTilesAndDirtyFlag[tile.tileCoordinate] = tile.gameObject.scene.isDirty;

        bool anyTile = tilesWithSceneAsset.Count > 0 || openTilesAndDirtyFlag.Count > 0;
        mapGroup.style.display = anyTile ? DisplayStyle.Flex : DisplayStyle.None;
        emptyState.style.display = anyTile ? DisplayStyle.None : DisplayStyle.Flex;
        mapElement.MarkDirtyRepaint();
    }

    TerrainTileCellState StateOfCell(Vector2Int coordinate)
    {
        bool isDirty;
        if (openTilesAndDirtyFlag.TryGetValue(coordinate, out isDirty))
            return isDirty ? TerrainTileCellState.Unsaved : TerrainTileCellState.Open;
        return tilesWithSceneAsset.Contains(coordinate) ? TerrainTileCellState.Exists : TerrainTileCellState.Empty;
    }

    void OnHoverChanged()
    {
        if (mapElement.IsHovering)
        {
            Vector2Int cell = mapElement.HoveredCell;
            string stateName = StateOfCell(cell) == TerrainTileCellState.Empty ? "empty" : StateOfCell(cell) == TerrainTileCellState.Exists ? "exists" : "open";
            statusText = "(" + cell.x + ", " + cell.y + ") " + stateName;
            TerrainTileBudgetResult budget;
            if (TerrainBudgetCheck.TryEvaluateByCoordinate(cell, out budget)) statusText += "  " + budget.summary;
        }
        else statusText = string.Empty;
        RaiseStatusChanged();
    }

    void OnCellPrimaryClicked(Vector2Int cell)
    {
        if (StateOfCell(cell) != TerrainTileCellState.Empty)
        {
            OpenTileForEdit(cell);
            return;
        }
        if (!mapElement.IsCreatableCell(cell)) return;

        SubScene created = TerrainTileCreator.CreateTileScene(cell, TerrainSplatLayer.Grass);
        if (created != null) SubSceneEditorUtility.EditScene(created);
        RefreshAll();
    }

    void OnCellsDragSelected(List<Vector2Int> cells)
    {
        TerrainTileCreator.CreateTileScenes(cells, TerrainSplatLayer.Grass);
        RefreshAll();
    }

    void OnCellSecondaryClicked(Vector2Int cell)
    {
        if (StateOfCell(cell) == TerrainTileCellState.Empty) return;
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("Close"), false, () => CloseTile(cell));
        menu.AddItem(new GUIContent("Select in Hierarchy"), false, () => SelectTileInHierarchy(cell));
        menu.AddItem(new GUIContent("Delete..."), false, () => DeleteTile(cell));
        menu.ShowAsContext();
    }

    public static SubScene FindSubSceneFor(Vector2Int coordinate)
    {
        string subSceneName = "Tile_" + coordinate.x + "_" + coordinate.y;
        foreach (SubScene subScene in UnityEngine.Object.FindObjectsByType<SubScene>(FindObjectsInactive.Include))
            if (subScene.name == subSceneName) return subScene;
        return null;
    }

    public static bool OpenTileForEdit(Vector2Int coordinate)
    {
        SubScene subScene = FindSubSceneFor(coordinate);
        if (subScene == null)
        {
            Debug.LogWarning("[Terrain] No SubScene named Tile_" + coordinate.x + "_" + coordinate.y + " in the loaded scenes; open the scene holding TerrainWorld first.");
            return false;
        }
        if (!subScene.EditingScene.isLoaded) SubSceneEditorUtility.EditScene(subScene);
        return true;
    }

    public static void SaveAndCloseTile(Vector2Int coordinate)
    {
        CloseTile(coordinate);
    }

    static void CloseTile(Vector2Int coordinate)
    {
        SubScene subScene = FindSubSceneFor(coordinate);
        if (subScene == null || !subScene.EditingScene.isLoaded) return;
        EditorSceneManager.SaveScene(subScene.EditingScene);
        SubSceneEditorInspectorUtility.CloseSceneWithoutSaving(subScene);
    }

    static void SelectTileInHierarchy(Vector2Int coordinate)
    {
        TerrainTile tile = TerrainTileCreator.FindTileAt(coordinate);
        if (tile != null)
        {
            Selection.activeGameObject = tile.gameObject;
            return;
        }
        SubScene subScene = FindSubSceneFor(coordinate);
        if (subScene != null) Selection.activeGameObject = subScene.gameObject;
    }

    void DeleteTile(Vector2Int coordinate)
    {
        string folder = TerrainTileAssets.FolderOf(coordinate);
        if (!EditorUtility.DisplayDialog("Delete tile", "Delete the folder " + folder + " and the tile scene, mask and data inside it? This cannot be undone.", "Delete", "Cancel")) return;

        SubScene subScene = FindSubSceneFor(coordinate);
        if (subScene != null)
        {
            Scene persistentScene = subScene.gameObject.scene;
            if (subScene.EditingScene.isLoaded) SubSceneEditorInspectorUtility.CloseSceneWithoutSaving(subScene);
            UnityEngine.Object.DestroyImmediate(subScene.gameObject);
            EditorSceneManager.MarkSceneDirty(persistentScene);
        }
        AssetDatabase.DeleteAsset(folder);
        RefreshAll();
    }

    public static bool TryGetActiveTile(out TerrainTile tile)
    {
        tile = null;
        if (Selection.activeGameObject != null)
        {
            tile = Selection.activeGameObject.GetComponentInParent<TerrainTile>();
            if (tile != null) return true;
        }
        TerrainTile[] openTiles = UnityEngine.Object.FindObjectsByType<TerrainTile>(FindObjectsInactive.Exclude);
        if (openTiles.Length == 0) return false;
        tile = openTiles[0];
        return true;
    }

}

[InitializeOnLoad]
public static class TerrainOpenTilesSessionRestore
{
    static TerrainOpenTilesSessionRestore()
    {
        EditorApplication.update += RetryWhileWindowOpen;
        BeginRetryWindow();
        EditorApplication.playModeStateChanged += (PlayModeStateChange change) =>
        {
            if (change == PlayModeStateChange.EnteredEditMode) BeginRetryWindow();
        };
        EditorSceneManager.sceneOpened += (Scene openedScene, OpenSceneMode openMode) => BeginRetryWindow();
    }

    // The subscenes are not always loaded yet when the reload finishes, so keep trying for a few seconds.
    const double RETRY_SECONDS = 10.0;
    static double retryUntilTime;

    static void BeginRetryWindow()
    {
        retryUntilTime = EditorApplication.timeSinceStartup + RETRY_SECONDS;
    }

    const string OPEN_TILES_SESSION_KEY = "TerrainEditor.OpenTiles";
    const double RECORD_POLL_SECONDS = 1.0;
    static double nextRecordPollTime;

    static void RetryWhileWindowOpen()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.timeSinceStartup <= retryUntilTime)
        {
            ReopenRecordedTilesInLoadedScenes();
            return;
        }
        // Recording is skipped inside the retry window: a reload would otherwise record "none open" before the restore runs.
        if (EditorApplication.timeSinceStartup < nextRecordPollTime) return;
        nextRecordPollTime = EditorApplication.timeSinceStartup + RECORD_POLL_SECONDS;
        RecordOpenTilesInLoadedScenes();
    }

    // Tiles stay closed by default (a closed tile is drawn by TerrainClosedTilePreview); only tiles the user had open are reopened.
    static void ReopenRecordedTilesInLoadedScenes()
    {
        string recordedNames = SessionState.GetString(OPEN_TILES_SESSION_KEY, "");
        if (recordedNames.Length == 0) return;
        HashSet<string> recordedNameSet = new HashSet<string>(recordedNames.Split('\n'));
        foreach (SubScene subScene in UnityEngine.Object.FindObjectsByType<SubScene>(FindObjectsInactive.Include))
        {
            if (!recordedNameSet.Contains(subScene.name) || subScene.EditingScene.isLoaded) continue;
            if (subScene.SceneAsset == null) continue;
            SubSceneEditorUtility.EditScene(subScene);
        }
    }

    static void RecordOpenTilesInLoadedScenes()
    {
        List<string> openTileNames = new List<string>();
        foreach (SubScene subScene in UnityEngine.Object.FindObjectsByType<SubScene>(FindObjectsInactive.Include))
            if (subScene.name.StartsWith("Tile_") && subScene.EditingScene.isLoaded) openTileNames.Add(subScene.name);
        openTileNames.Sort();
        string joinedNames = string.Join("\n", openTileNames);
        if (joinedNames != SessionState.GetString(OPEN_TILES_SESSION_KEY, "")) SessionState.SetString(OPEN_TILES_SESSION_KEY, joinedNames);
    }
}
#endif
