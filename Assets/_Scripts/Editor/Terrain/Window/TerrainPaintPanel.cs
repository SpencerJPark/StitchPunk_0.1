#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class TerrainPaintPanel : TerrainToolPanel
{
    static readonly TerrainSplatLayer[] LayerOrder =
    {
        TerrainSplatLayer.Dirt, TerrainSplatLayer.Grass, TerrainSplatLayer.Forest, TerrainSplatLayer.Stone
    };

    static readonly Color[] FallbackLayerColours =
    {
        new Color(0.45f, 0.32f, 0.2f), new Color(0.35f, 0.6f, 0.25f),
        new Color(0.12f, 0.33f, 0.15f), new Color(0.55f, 0.55f, 0.58f)
    };

    readonly VisualElement[] swatches = new VisualElement[LayerOrder.Length];
    readonly VisualElement[] swatchChips = new VisualElement[LayerOrder.Length];
    Slider radiusSlider;
    Slider hardnessSlider;
    Slider strengthSlider;
    TerrainSplatLayer lastReportedLayer;

    public override string DisplayName => "Paint";
    public override string StatusText => "Paint: " + BrushState.paintLayer;

    public TerrainPaintPanel()
    {
        AddToClassList("terrain-panel");

        Label title = new Label("Paint");
        title.AddToClassList("terrain-panel-title");
        Add(title);

        Label hint = new Label("Drag in the Scene view. The brush paints the layer you pick.");
        hint.AddToClassList("terrain-panel-hint");
        Add(hint);

        VisualElement swatchRow = new VisualElement();
        swatchRow.AddToClassList("terrain-swatch-row");
        Add(swatchRow);
        for (int layerIndex = 0; layerIndex < LayerOrder.Length; layerIndex++)
        {
            TerrainSplatLayer layer = LayerOrder[layerIndex];
            VisualElement swatch = new VisualElement();
            swatch.AddToClassList("terrain-swatch");
            VisualElement chip = new VisualElement();
            chip.AddToClassList("terrain-swatch-chip");
            Label name = new Label(layer.ToString());
            name.AddToClassList("terrain-label");
            swatch.Add(chip);
            swatch.Add(name);
            swatch.RegisterCallback<ClickEvent>(clickEvent => SelectLayer(layer));
            swatchRow.Add(swatch);
            swatches[layerIndex] = swatch;
            swatchChips[layerIndex] = chip;
        }

        radiusSlider = AddSliderRow("Radius", 1f, 40f, value => BrushState.radiusMetres = value);
        hardnessSlider = AddSliderRow("Hardness", 0f, 1f, value => BrushState.hardness = value);
        strengthSlider = AddSliderRow("Strength", 0f, 1f, value => BrushState.strength = value);

        lastReportedLayer = BrushState.paintLayer;
        RefreshControls();
        schedule.Execute(RefreshControls).Every(250);
    }

    public override void OnShown()
    {
        BrushState.activeTool = TerrainBrushTool.Paint;
        RefreshControls();
    }

    public override void OnHidden()
    {
        if (BrushState.activeTool == TerrainBrushTool.Paint)
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

    void SelectLayer(TerrainSplatLayer layer)
    {
        BrushState.paintLayer = layer;
        RefreshControls();
    }

    void RefreshControls()
    {
        radiusSlider.SetValueWithoutNotify(BrushState.radiusMetres);
        hardnessSlider.SetValueWithoutNotify(BrushState.hardness);
        strengthSlider.SetValueWithoutNotify(BrushState.strength);

        TerrainPalette palette = null;
        if (TerrainTilesPanel.TryGetActiveTile(out TerrainTile tile) && tile != null)
            palette = tile.palette;

        for (int layerIndex = 0; layerIndex < LayerOrder.Length; layerIndex++)
        {
            Color chipColour = FallbackLayerColours[layerIndex];
            if (palette != null)
            {
                Color tint = palette.GetLayer(layerIndex).tint;
                if (tint != Color.white)
                    chipColour = tint;
            }
            swatchChips[layerIndex].style.backgroundColor = chipColour;
            swatches[layerIndex].EnableInClassList("terrain-swatch--selected", LayerOrder[layerIndex] == BrushState.paintLayer);
        }

        if (lastReportedLayer != BrushState.paintLayer)
        {
            lastReportedLayer = BrushState.paintLayer;
            RaiseStatusChanged();
        }
    }
}
#endif
