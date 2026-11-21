#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Editor;
using UnityEditor;
using UnityEngine.UIElements;

public sealed class TerrainTerracesPanel : TerrainToolPanel
{
    static readonly string[] ModeLabels = { "Paint", "Ramp" };

    static readonly string[] ModeHints =
    {
        "Drag to raise a plateau one level above where you start. Shift lowers, Ctrl clears your paint.",
        "Press on the lower ground, drag up across the edge, release: places a ramp."
    };

    static readonly TerrainKeyHint[] Hints =
    {
        new TerrainKeyHint { keys = "Drag", action = "paint" },
        new TerrainKeyHint { keys = "Shift", action = "lower" },
        new TerrainKeyHint { keys = "Ctrl", action = "clear" },
        new TerrainKeyHint { keys = "[ ]", action = "size" }
    };

    readonly VisualElement segmented;
    readonly Label modeHint;
    readonly Action refreshBrushSize;
    readonly Action refreshCornerRadius;
    readonly VisualElement cornerRadiusRow;
    readonly VisualElement noProfileState;
    int lastSegmentedIndex = -1;

    public override string DisplayName => "Terraces";
    public override string StatusText => TerrainTerraceBrush.StatusText;
    public override IReadOnlyList<TerrainKeyHint> KeyHints => Hints;

    public TerrainTerracesPanel()
    {
        VisualElement actions;
        VisualElement body;
        VisualElement pane = TerrainUi.MakePane("Terraces", out actions, out body);
        pane.style.flexGrow = 1f;
        Add(pane);

        actions.Add(TerrainUi.MakeGhost(
            "Rebuild open tiles", ToolkitIcons.Loop,
            "Rebuild every open tile's terrace meshes (use after changing the corner radius).", RebuildOpenTiles));

        segmented = TerrainUi.MakeSegmented(ModeLabels, (int)TerrainTerraceBrush.ActiveMode, SelectMode);
        body.Add(segmented);

        VisualElement modeBody;
        VisualElement modeCard = TerrainUi.MakeCard("Mode", out modeBody);
        modeHint = TerrainUi.MakeHint(string.Empty);
        modeBody.Add(modeHint);
        body.Add(modeCard);

        VisualElement brushBody;
        VisualElement brushCard = TerrainUi.MakeCard("Brush", out brushBody);
        brushBody.Add(TerrainUi.MakeSliderRow(
            "Size", 1f, 40f,
            () => TerrainTerraceBrush.BrushRadiusMetres,
            (float value) => TerrainTerraceBrush.BrushRadiusMetres = value,
            "Brush radius in metres.", out refreshBrushSize));
        body.Add(brushCard);

        VisualElement outlineBody;
        VisualElement outlineCard = TerrainUi.MakeCard("Outline", out outlineBody);
        cornerRadiusRow = TerrainUi.MakeSliderRow(
            "Corner", 0.5f, 8f,
            ReadCornerRadius, WriteCornerRadius,
            "Rounded outline corner radius in metres. Rebuild open tiles to apply.", out refreshCornerRadius);
        outlineBody.Add(cornerRadiusRow);
        noProfileState = TerrainUi.MakeEmpty(
            "No world profile", "Create a Terrain World Profile asset to set the corner radius.", null, null);
        outlineBody.Add(noProfileState);
        body.Add(outlineCard);

        RefreshControls();
    }

    public override void OnShown()
    {
        TerrainTerraceBrush.Enable();
        TerrainTerraceBrush.StateChanged += OnBrushStateChanged;
        RefreshControls();
    }

    public override void OnHidden()
    {
        TerrainTerraceBrush.StateChanged -= OnBrushStateChanged;
        TerrainTerraceBrush.Disable();
    }

    static float ReadCornerRadius()
    {
        TerrainWorldProfile profile = TerrainWorldProfile.FindInProject();
        return profile != null ? profile.terraceCornerRadiusMetres : 0.5f;
    }

    static void WriteCornerRadius(float value)
    {
        TerrainWorldProfile profile = TerrainWorldProfile.FindInProject();
        if (profile == null)
            return;
        Undo.RecordObject(profile, "Change terrace corner radius");
        profile.terraceCornerRadiusMetres = value;
        EditorUtility.SetDirty(profile);
    }

    static void RebuildOpenTiles()
    {
        EditorApplication.ExecuteMenuItem("Stitch Punk/Terrain/Terraces/Rebuild All Open Tiles");
    }

    void SelectMode(int modeIndex)
    {
        TerrainTerraceBrush.ActiveMode = (TerrainTerraceBrushMode)modeIndex;
        RefreshControls();
    }

    void OnBrushStateChanged()
    {
        RefreshControls();
        RaiseStatusChanged();
    }

    void RefreshControls()
    {
        int modeIndex = (int)TerrainTerraceBrush.ActiveMode;
        if (modeIndex != lastSegmentedIndex)
        {
            lastSegmentedIndex = modeIndex;
            ToolkitChrome.SetSegmentedSelection(segmented, modeIndex);
        }
        modeHint.text = ModeHints[modeIndex];
        refreshBrushSize();

        bool hasProfile = TerrainWorldProfile.FindInProject() != null;
        cornerRadiusRow.style.display = hasProfile ? DisplayStyle.Flex : DisplayStyle.None;
        noProfileState.style.display = hasProfile ? DisplayStyle.None : DisplayStyle.Flex;
        if (hasProfile)
            refreshCornerRadius();
    }
}
#endif
