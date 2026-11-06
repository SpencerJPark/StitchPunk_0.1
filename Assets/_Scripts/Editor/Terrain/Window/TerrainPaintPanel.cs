#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class TerrainPaintPanel : TerrainToolPanel
{
    const int RefreshIntervalMilliseconds = 250;

    static readonly TerrainSplatLayer[] LayerOrder =
    {
        TerrainSplatLayer.Dirt, TerrainSplatLayer.Grass, TerrainSplatLayer.Forest, TerrainSplatLayer.Stone
    };

    static readonly Color[] FallbackLayerColours =
    {
        new Color(0.45f, 0.32f, 0.2f), new Color(0.35f, 0.6f, 0.25f),
        new Color(0.12f, 0.33f, 0.15f), new Color(0.55f, 0.55f, 0.58f)
    };

    static readonly TerrainKeyHint[] Hints =
    {
        new TerrainKeyHint { keys = "Drag", action = "paint" },
        new TerrainKeyHint { keys = "[ ]", action = "size" },
        new TerrainKeyHint { keys = "- =", action = "strength" }
    };

    readonly VisualElement[] layerRows = new VisualElement[LayerOrder.Length];
    readonly VisualElement[] layerChips = new VisualElement[LayerOrder.Length];
    readonly VisualElement cardsHost = new VisualElement();
    readonly VisualElement emptyHost = new VisualElement();
    Action brushRefresh;
    TerrainSplatLayer lastReportedLayer;

    public override string DisplayName => "Paint";
    public override string StatusText => "Paint: " + BrushState.paintLayer;
    public override IReadOnlyList<TerrainKeyHint> KeyHints => Hints;

    public TerrainPaintPanel()
    {
        style.flexGrow = 1f;

        VisualElement actions;
        VisualElement body;
        VisualElement pane = TerrainUi.MakePane("Paint", out actions, out body);
        Add(pane);

        VisualElement layerCardBody;
        VisualElement layerCard = TerrainUi.MakeCard("Layer", out layerCardBody);
        for (int layerIndex = 0; layerIndex < LayerOrder.Length; layerIndex++)
        {
            TerrainSplatLayer layer = LayerOrder[layerIndex];
            VisualElement row = new VisualElement();
            row.AddToClassList("toolkit-list-row");
            row.AddToClassList("terrain-paint-row");
            VisualElement chip = new VisualElement();
            chip.AddToClassList("terrain-paint-chip");
            Label name = new Label(layer.ToString());
            name.AddToClassList("terrain-paint-name");
            row.Add(chip);
            row.Add(name);
            row.RegisterCallback<ClickEvent>((ClickEvent clickEvent) => SelectLayer(layer));
            layerCardBody.Add(row);
            layerRows[layerIndex] = row;
            layerChips[layerIndex] = chip;
        }
        layerCardBody.Add(TerrainUi.MakeHint("Pick a layer, then drag in the Scene view. The ground blends between the four layers."));
        cardsHost.Add(layerCard);
        cardsHost.Add(TerrainBrushCard.Make(out brushRefresh));

        emptyHost.Add(TerrainUi.MakeEmpty("No tile open", "Open a tile in the World tab, then drag in the Scene view to paint it.", null, null));

        body.Add(cardsHost);
        body.Add(emptyHost);

        lastReportedLayer = BrushState.paintLayer;
        RefreshControls();
        schedule.Execute(RefreshControls).Every(RefreshIntervalMilliseconds);
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

    void SelectLayer(TerrainSplatLayer layer)
    {
        BrushState.paintLayer = layer;
        BrushState.activeTool = TerrainBrushTool.Paint;
        RefreshControls();
    }

    void RefreshControls()
    {
        TerrainTile tile = null;
        bool hasTile = TerrainTilesPanel.TryGetActiveTile(out tile) && tile != null;
        cardsHost.style.display = hasTile ? DisplayStyle.Flex : DisplayStyle.None;
        emptyHost.style.display = hasTile ? DisplayStyle.None : DisplayStyle.Flex;

        TerrainPalette palette = hasTile ? tile.palette : null;
        for (int layerIndex = 0; layerIndex < LayerOrder.Length; layerIndex++)
        {
            Color chipColour = FallbackLayerColours[layerIndex];
            if (palette != null)
            {
                Color tint = palette.GetLayer(layerIndex).tint;
                if (tint != Color.white)
                    chipColour = tint;
            }
            layerChips[layerIndex].style.backgroundColor = chipColour;
            layerRows[layerIndex].EnableInClassList("toolkit-list-row--selected", LayerOrder[layerIndex] == BrushState.paintLayer);
        }
        brushRefresh?.Invoke();

        if (lastReportedLayer != BrushState.paintLayer)
        {
            lastReportedLayer = BrushState.paintLayer;
            RaiseStatusChanged();
        }
    }
}
#endif
