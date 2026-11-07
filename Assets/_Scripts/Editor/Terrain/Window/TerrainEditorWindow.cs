#if UNITY_EDITOR
using System;
using DotsAnimationToolkit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class TerrainEditorWindow : EditorWindow
{
    const string ToolIndexSessionKey = "TerrainEditor.ToolIndex";
    const int FooterRefreshMilliseconds = 500;
    const int PanelCount = 9;
    const string FooterSeparator = "  \u2022  ";

    static readonly string[] TabLabels = { "World", "Sculpt", "Paint", "Roads", "Water", "Decals", "Props", "Generate", "Nav" };
    // Monochrome editor built-in icons (verified to resolve), one weight like the rest of the rail (R21).
    static readonly string[] TabIcons =
    {
        "Grid.Default", "TerrainInspector.TerrainToolRaise", "TerrainInspector.TerrainToolSplat", "TerrainInspector.TerrainToolSmoothHeight", "d_Particle Effect",
        "d_Image Icon", "TerrainInspector.TerrainToolTrees", "d_Profiler.Rendering", "TerrainInspector.TerrainToolSettings"
    };
    // Display position -> panel index (0 Tiles, 1 Paint, 2 Height, 3 Generate, 4 Roads, 5 Water, 6 Decals, 7 Scatter, 8 Nav).
    static readonly int[] PanelIndexByTabPosition = { 0, 2, 1, 4, 5, 6, 7, 3, 8 };

    readonly TerrainToolPanel[] panelsByToolIndex = new TerrainToolPanel[PanelCount];

    ScrollView contentHost;
    Label footerLabel;
    Action<int> setSelectedTabPosition;
    IVisualElementScheduledItem footerRefreshTimer;
    TerrainToolPanel shownPanel;
    int selectedToolIndex = -1;

    [MenuItem("Stitch Punk/Terrain/Terrain Editor")]
    public static void Open()
    {
        TerrainEditorWindow window = GetWindow<TerrainEditorWindow>();
        window.titleContent = new GUIContent("Terrain Editor");
        window.minSize = new Vector2(280f, 360f);
    }

    void CreateGUI()
    {
        VisualElement root = rootVisualElement;
        root.Clear();
        TerrainUi.ApplyStyles(root);
        root.AddToClassList("terrain-window");

        VisualElement tabBar = TerrainUi.MakeTabRail(TabLabels, TabIcons, SelectTabPosition, out setSelectedTabPosition);
        contentHost = new ScrollView(ScrollViewMode.Vertical);
        contentHost.AddToClassList("terrain-content");

        VisualElement footer = ToolkitChrome.MakeStatusRow(out footerLabel, out VisualElement footerActions, true);

        VisualElement main = new VisualElement();
        main.AddToClassList("terrain-main");
        main.Add(tabBar);
        main.Add(contentHost);

        root.Add(main);
        root.Add(footer);

        Selection.selectionChanged -= RefreshFooter;
        Selection.selectionChanged += RefreshFooter;
        footerRefreshTimer = root.schedule.Execute(RefreshFooter).Every(FooterRefreshMilliseconds);

        int restoredIndex = SessionState.GetInt(ToolIndexSessionKey, 0);
        if (!HasPanel(restoredIndex))
        {
            restoredIndex = 0;
        }
        selectedToolIndex = -1;
        SelectTool(restoredIndex);
    }

    void OnDisable()
    {
        Selection.selectionChanged -= RefreshFooter;
        if (footerRefreshTimer != null)
        {
            footerRefreshTimer.Pause();
            footerRefreshTimer = null;
        }
        if (shownPanel != null)
        {
            shownPanel.StatusChanged -= RefreshFooter;
            shownPanel.OnHidden();
            shownPanel = null;
        }
        TerrainSceneHud.Clear();
    }

    static bool HasPanel(int toolIndex)
    {
        return toolIndex >= 0 && toolIndex < PanelCount;
    }

    void SelectTabPosition(int tabPosition)
    {
        SelectTool(PanelIndexByTabPosition[tabPosition]);
    }

    TerrainToolPanel GetOrCreatePanel(int toolIndex)
    {
        if (panelsByToolIndex[toolIndex] == null)
        {
            switch (toolIndex)
            {
                case 0: panelsByToolIndex[toolIndex] = new TerrainTilesPanel(); break;
                case 1: panelsByToolIndex[toolIndex] = new TerrainPaintPanel(); break;
                case 2: panelsByToolIndex[toolIndex] = new TerrainHeightPanel(); break;
                case 3: panelsByToolIndex[toolIndex] = new TerrainGeneratePanel(); break;
                case 4: panelsByToolIndex[toolIndex] = new TerrainRoadsPanel(); break;
                case 5: panelsByToolIndex[toolIndex] = new TerrainWaterPanel(); break;
                case 6: panelsByToolIndex[toolIndex] = new TerrainDecalPanel(); break;
                case 7: panelsByToolIndex[toolIndex] = new TerrainScatterPanel(); break;
                case 8: panelsByToolIndex[toolIndex] = new TerrainNavPanel(); break;
            }
        }
        return panelsByToolIndex[toolIndex];
    }

    void SelectTool(int toolIndex)
    {
        if (!HasPanel(toolIndex) || toolIndex == selectedToolIndex)
        {
            return;
        }

        if (shownPanel != null)
        {
            shownPanel.StatusChanged -= RefreshFooter;
            shownPanel.OnHidden();
            shownPanel.RemoveFromHierarchy();
        }

        shownPanel = GetOrCreatePanel(toolIndex);
        contentHost.Add(shownPanel);
        shownPanel.StatusChanged += RefreshFooter;
        shownPanel.OnShown();
        TerrainSceneHud.SetActiveTool(shownPanel.DisplayName, shownPanel.KeyHints);

        setSelectedTabPosition(Array.IndexOf(PanelIndexByTabPosition, toolIndex));

        selectedToolIndex = toolIndex;
        SessionState.SetInt(ToolIndexSessionKey, toolIndex);
        RefreshFooter();
    }

    void RefreshFooter()
    {
        if (footerLabel == null)
        {
            return;
        }

        TerrainTile activeTile;
        string tileText = TerrainTilesPanel.TryGetActiveTile(out activeTile)
            ? "Tile (" + activeTile.tileCoordinate.x + ", " + activeTile.tileCoordinate.y + ")"
            : "No tile open";
        string toolName = shownPanel != null ? shownPanel.DisplayName : string.Empty;
        string statusText = shownPanel != null ? shownPanel.StatusText : string.Empty;
        string line = toolName + FooterSeparator + tileText + FooterSeparator + "Brush " + BrushState.radiusMetres.ToString("F0") + " m";
        if (!string.IsNullOrEmpty(statusText))
        {
            line += FooterSeparator + statusText;
        }
        ToolkitChrome.SetStatus(footerLabel, line, ToolkitStatusTone.Neutral);
    }
}
#endif
