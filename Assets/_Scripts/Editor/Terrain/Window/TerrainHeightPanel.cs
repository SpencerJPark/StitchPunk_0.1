#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Editor;
using UnityEngine.UIElements;

public sealed class TerrainHeightPanel : TerrainToolPanel
{
    static readonly TerrainBrushTool[] HeightTools =
    {
        TerrainBrushTool.Raise, TerrainBrushTool.Lower, TerrainBrushTool.Smooth,
        TerrainBrushTool.Flatten, TerrainBrushTool.Noise, TerrainBrushTool.Ramp
    };

    static readonly string[] HeightToolLabels = { "Raise", "Lower", "Smooth", "Flatten", "Noise", "Ramp" };

    static readonly string[] HeightToolHints =
    {
        "Drag to raise the ground. Hold Ctrl to lower.",
        "Drag to lower the ground. Hold Ctrl to raise.",
        "Drag to even out bumps. Strength is how much it blends.",
        "Press where the ground should level to, then drag. It levels to the height under your first click.",
        "Drag to add small random bumps.",
        "Press at the bottom, drag to the top: cuts a slope between the two points. Strength sets how wide it is."
    };

    static readonly TerrainKeyHint[] Hints =
    {
        new TerrainKeyHint { keys = "Drag", action = "sculpt" },
        new TerrainKeyHint { keys = "Ctrl", action = "invert" },
        new TerrainKeyHint { keys = "Shift", action = "smooth" },
        new TerrainKeyHint { keys = "[ ]", action = "size" },
        new TerrainKeyHint { keys = "- =", action = "strength" }
    };

    readonly Button rebuildButton;
    readonly VisualElement segmented;
    readonly VisualElement modeCard;
    readonly VisualElement brushCard;
    readonly VisualElement emptyState;
    readonly Label modeHint;
    readonly Action refreshBrushCard;
    TerrainBrushTool lastReportedTool;
    int lastSegmentedIndex = -1;

    public override string DisplayName => "Sculpt";
    public override string StatusText => BrushState.activeTool.ToString();
    public override IReadOnlyList<TerrainKeyHint> KeyHints => Hints;

    public TerrainHeightPanel()
    {
        VisualElement actions;
        VisualElement body;
        VisualElement pane = TerrainUi.MakePane("Sculpt", out actions, out body);
        pane.style.flexGrow = 1f;
        Add(pane);

        rebuildButton = TerrainUi.MakeGhost(
            "Rebuild mesh", ToolkitIcons.Loop,
            "Rebuild the open tile's mesh from its height data (use after a Generate preview).", RebuildActiveTile);
        actions.Add(rebuildButton);

        segmented = TerrainUi.MakeSegmented(HeightToolLabels, SelectedIndexForActiveTool(), SelectTool);
        body.Add(segmented);

        VisualElement modeBody;
        modeCard = TerrainUi.MakeCard("Mode", out modeBody);
        modeHint = TerrainUi.MakeHint(string.Empty);
        modeBody.Add(modeHint);
        body.Add(modeCard);

        brushCard = TerrainBrushCard.Make(out refreshBrushCard);
        body.Add(brushCard);

        emptyState = TerrainUi.MakeEmpty(
            "No tile open", "Open a tile in the World tab, then drag in the Scene view to sculpt it.", null, null);
        body.Add(emptyState);

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

    static int SelectedIndexForActiveTool()
    {
        return Math.Max(0, Array.IndexOf(HeightTools, BrushState.activeTool));
    }

    void SelectTool(int toolIndex)
    {
        BrushState.activeTool = HeightTools[toolIndex];
        lastSegmentedIndex = toolIndex;
        RefreshControls();
    }

    void RebuildActiveTile()
    {
        if (TerrainTilesPanel.TryGetActiveTile(out TerrainTile tile) && tile != null)
            TerrainTileBuilder.Rebuild(tile);
    }

    void RefreshControls()
    {
        bool hasTile = TerrainTilesPanel.TryGetActiveTile(out TerrainTile tile) && tile != null;
        rebuildButton.SetEnabled(hasTile);
        segmented.style.display = hasTile ? DisplayStyle.Flex : DisplayStyle.None;
        modeCard.style.display = hasTile ? DisplayStyle.Flex : DisplayStyle.None;
        brushCard.style.display = hasTile ? DisplayStyle.Flex : DisplayStyle.None;
        emptyState.style.display = hasTile ? DisplayStyle.None : DisplayStyle.Flex;

        int toolIndex = SelectedIndexForActiveTool();
        if (toolIndex != lastSegmentedIndex)
        {
            lastSegmentedIndex = toolIndex;
            ToolkitChrome.SetSegmentedSelection(segmented, toolIndex);
        }
        modeHint.text = HeightToolHints[toolIndex];
        refreshBrushCard();

        if (lastReportedTool != BrushState.activeTool)
        {
            lastReportedTool = BrushState.activeTool;
            RaiseStatusChanged();
        }
    }
}
#endif
