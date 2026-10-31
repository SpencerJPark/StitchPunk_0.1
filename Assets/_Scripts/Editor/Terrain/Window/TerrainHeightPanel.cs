#if UNITY_EDITOR
using System;
using UnityEngine.UIElements;

public sealed class TerrainHeightPanel : TerrainToolPanel
{
    static readonly TerrainBrushTool[] HeightTools =
    {
        TerrainBrushTool.Raise, TerrainBrushTool.Lower, TerrainBrushTool.Smooth,
        TerrainBrushTool.Flatten, TerrainBrushTool.Noise, TerrainBrushTool.Ramp
    };

    readonly Button[] toolButtons = new Button[HeightTools.Length];
    Slider radiusSlider;
    Slider hardnessSlider;
    Slider strengthSlider;
    Button rebuildButton;
    TerrainBrushTool lastReportedTool;

    public override string DisplayName => "Height";
    public override string StatusText => "Height: " + BrushState.activeTool;

    public TerrainHeightPanel()
    {
        AddToClassList("terrain-panel");

        Label title = new Label("Height");
        title.AddToClassList("terrain-panel-title");
        Add(title);

        Label hint = new Label(
            "Drag in the Scene view. Strength is the rate for Raise, Lower and Noise, the blend for Smooth and Flatten, and the width for Ramp.");
        hint.AddToClassList("terrain-panel-hint");
        Add(hint);

        VisualElement segmentRow = new VisualElement();
        segmentRow.AddToClassList("terrain-segment-row");
        Add(segmentRow);
        for (int toolIndex = 0; toolIndex < HeightTools.Length; toolIndex++)
        {
            TerrainBrushTool tool = HeightTools[toolIndex];
            Button button = new Button(() => SelectTool(tool));
            button.text = tool.ToString();
            button.AddToClassList("terrain-segment-button");
            segmentRow.Add(button);
            toolButtons[toolIndex] = button;
        }

        radiusSlider = AddSliderRow("Radius", 1f, 40f, value => BrushState.radiusMetres = value);
        hardnessSlider = AddSliderRow("Hardness", 0f, 1f, value => BrushState.hardness = value);
        strengthSlider = AddSliderRow("Strength", 0f, 1f, value => BrushState.strength = value);

        rebuildButton = new Button(RebuildActiveTile);
        rebuildButton.text = "Rebuild Tile Mesh";
        rebuildButton.AddToClassList("terrain-primary-button");
        Add(rebuildButton);

        lastReportedTool = BrushState.activeTool;
        RefreshControls();
        schedule.Execute(RefreshControls).Every(250);
    }

    public override void OnShown()
    {
        if (Array.IndexOf(HeightTools, BrushState.activeTool) < 0)
            BrushState.activeTool = TerrainBrushTool.Raise;
        RefreshControls();
    }

    public override void OnHidden()
    {
        if (Array.IndexOf(HeightTools, BrushState.activeTool) >= 0)
            BrushState.activeTool = TerrainBrushTool.None;
    }

    Slider AddSliderRow(string labelText, float low, float high, Action<float> apply)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("terrain-row");
        Label label = new Label(labelText);
        label.AddToClassList("terrain-label");
        Slider slider = new Slider(low, high);
        slider.showInputField = true;
        slider.RegisterValueChangedCallback(changeEvent => apply(changeEvent.newValue));
        row.Add(label);
        row.Add(slider);
        Add(row);
        return slider;
    }

    void SelectTool(TerrainBrushTool tool)
    {
        BrushState.activeTool = tool;
        RefreshControls();
    }

    void RebuildActiveTile()
    {
        if (TerrainTilesPanel.TryGetActiveTile(out TerrainTile tile) && tile != null)
            TerrainTileBuilder.Rebuild(tile);
    }

    void RefreshControls()
    {
        radiusSlider.SetValueWithoutNotify(BrushState.radiusMetres);
        hardnessSlider.SetValueWithoutNotify(BrushState.hardness);
        strengthSlider.SetValueWithoutNotify(BrushState.strength);

        for (int toolIndex = 0; toolIndex < HeightTools.Length; toolIndex++)
            toolButtons[toolIndex].EnableInClassList("terrain-segment-button--active", HeightTools[toolIndex] == BrushState.activeTool);

        bool hasTile = TerrainTilesPanel.TryGetActiveTile(out TerrainTile tile) && tile != null;
        rebuildButton.SetEnabled(hasTile);
        rebuildButton.tooltip = hasTile ? "Rebuild the open tile's mesh from its height data." : "Open a tile in the Tiles panel first.";

        if (lastReportedTool != BrushState.activeTool)
        {
            lastReportedTool = BrushState.activeTool;
            RaiseStatusChanged();
        }
    }
}
#endif
