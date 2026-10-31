#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class TerrainEditorWindow : EditorWindow
{
    const string StylesheetPath = "Assets/_Scripts/Editor/Terrain/Window/TerrainEditor.uss";
    const string ToolIndexSessionKey = "TerrainEditor.ToolIndex";
    const int FooterRefreshMilliseconds = 500;

    static readonly string[] ToolLabels = { "Tiles", "Paint", "Height", "Generate", "Roads", "Decals", "Scatter", "Nav", "Stream" };
    static readonly string[] ToolDisabledTooltips =
    {
        null, null, null, null, null,
        "Built in phase P6", "Built in phase P7", "Built in phase P8", "Built in phase P9"
    };

    readonly TerrainToolPanel[] panelsByToolIndex = new TerrainToolPanel[ToolLabels.Length];
    readonly Button[] railButtons = new Button[ToolLabels.Length];

    VisualElement contentHost;
    Label tileFooterLabel;
    Label brushFooterLabel;
    Label statusFooterLabel;
    IVisualElementScheduledItem footerRefreshTimer;
    TerrainToolPanel shownPanel;
    int selectedToolIndex = -1;

    [MenuItem("Stitch Punk/Terrain/Terrain Editor")]
    public static void Open()
    {
        TerrainEditorWindow window = GetWindow<TerrainEditorWindow>();
        window.titleContent = new GUIContent("Terrain Editor");
        window.minSize = new Vector2(560f, 420f);
    }

    void CreateGUI()
    {
        VisualElement root = rootVisualElement;
        root.Clear();
        StyleSheet stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylesheetPath);
        if (stylesheet != null)
        {
            root.styleSheets.Add(stylesheet);
        }
        root.AddToClassList("terrain-window");

        VisualElement main = new VisualElement();
        main.AddToClassList("terrain-main");
        VisualElement rail = new VisualElement();
        rail.AddToClassList("terrain-rail");
        contentHost = new VisualElement();
        contentHost.AddToClassList("terrain-content");
        main.Add(rail);
        main.Add(contentHost);

        for (int toolIndex = 0; toolIndex < ToolLabels.Length; toolIndex++)
        {
            int capturedIndex = toolIndex;
            Button railButton = new Button(() => SelectTool(capturedIndex));
            railButton.text = ToolLabels[toolIndex];
            railButton.AddToClassList("terrain-rail-button");
            if (!HasPanel(toolIndex))
            {
                railButton.SetEnabled(false);
                railButton.tooltip = ToolDisabledTooltips[toolIndex];
            }
            railButtons[toolIndex] = railButton;
            rail.Add(railButton);
        }

        VisualElement footer = new VisualElement();
        footer.AddToClassList("terrain-footer");
        tileFooterLabel = CreateFooterLabel(footer);
        brushFooterLabel = CreateFooterLabel(footer);
        statusFooterLabel = CreateFooterLabel(footer);

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
    }

    static bool HasPanel(int toolIndex)
    {
        return toolIndex >= 0 && toolIndex <= 4;
    }

    static Label CreateFooterLabel(VisualElement footer)
    {
        Label label = new Label();
        label.AddToClassList("terrain-footer-item");
        footer.Add(label);
        return label;
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

        for (int buttonIndex = 0; buttonIndex < railButtons.Length; buttonIndex++)
        {
            railButtons[buttonIndex].EnableInClassList("terrain-rail-button--active", buttonIndex == toolIndex);
        }

        selectedToolIndex = toolIndex;
        SessionState.SetInt(ToolIndexSessionKey, toolIndex);
        RefreshFooter();
    }

    void RefreshFooter()
    {
        if (tileFooterLabel == null)
        {
            return;
        }

        TerrainTile activeTile;
        tileFooterLabel.text = TerrainTilesPanel.TryGetActiveTile(out activeTile)
            ? "Tile (" + activeTile.tileCoordinate.x + ", " + activeTile.tileCoordinate.y + ")"
            : "No tile open";
        brushFooterLabel.text = "Brush " + BrushState.radiusMetres.ToString("F0") + " m";
        statusFooterLabel.text = shownPanel != null ? shownPanel.StatusText : string.Empty;
    }
}
#endif
