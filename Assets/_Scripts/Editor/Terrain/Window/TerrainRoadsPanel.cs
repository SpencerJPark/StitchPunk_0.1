#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.UIElements;
using DotsAnimationToolkit.Editor;

public sealed class TerrainRoadsPanel : TerrainToolPanel
{
    const int RefreshIntervalMilliseconds = 500;
    const float MinimumWidthMetres = 1f;
    const float MaximumWidthMetres = 24f;
    const int MinimumKnotsPerRoad = 2;

    static readonly TerrainKeyHint[] Hints =
    {
        new TerrainKeyHint { keys = "Click line", action = "add point" },
        new TerrainKeyHint { keys = "Drag", action = "move" },
        new TerrainKeyHint { keys = "Del", action = "remove point" },
        new TerrainKeyHint { keys = "Shift+click", action = "extend" },
        new TerrainKeyHint { keys = "Esc", action = "deselect" }
    };

    readonly VisualElement emptyHost = new VisualElement();
    readonly VisualElement cardsHost = new VisualElement();
    readonly VisualElement listBody;
    readonly VisualElement roadCard;
    readonly VisualElement pointCard;
    readonly Label warningLabel = new Label();
    readonly Label ribbonVertexLabel = new Label();
    readonly Label pointLabel;
    readonly Label lengthBadge;
    readonly Label knotCountBadge;
    readonly ObjectField profileField = new ObjectField();
    readonly IntegerField priorityField = new IntegerField();
    readonly Button useProfileWidthButton;
    readonly Button deletePointButton;
    readonly System.Action widthRefresh;
    IVisualElementScheduledItem refreshTimer;
    int lastReportedRoadCount = -1;
    int lastListSignature;

    public override string DisplayName => "Roads";
    public override string StatusText => TerrainRoadTool.SelectedRoad != null ? TerrainRoadTool.SelectedRoad.name : "no road selected";
    public override IReadOnlyList<TerrainKeyHint> KeyHints => Hints;

    public TerrainRoadsPanel()
    {
        style.flexGrow = 1f;

        VisualElement actions;
        VisualElement body;
        VisualElement pane = TerrainUi.MakePane("Roads", out actions, out body);
        Add(pane);

        actions.Add(TerrainUi.MakePrimary("New Road", ToolkitIcons.Plus, "Add a road in front of the Scene view, then drag its dots or click its line to add more", CreateRoad));

        emptyHost.Add(TerrainUi.MakeEmpty("No roads yet", "Add a road, then click its line in the Scene view to add points, drag a point to move it, and drag the square handles to set the width.", "New Road", CreateRoad));

        VisualElement listCard = TerrainUi.MakeCard("In this scene", out listBody);
        cardsHost.Add(listCard);

        VisualElement roadCardBody;
        roadCard = TerrainUi.MakeCard("Road", out roadCardBody);
        profileField.objectType = typeof(TerrainRoadProfile);
        profileField.allowSceneObjects = false;
        profileField.RegisterValueChangedCallback(OnProfileChanged);
        roadCardBody.Add(ToolkitChrome.MakePropertyRow("Profile", profileField, "What this road looks like and how it carves the ground"));
        roadCardBody.Add(TerrainUi.MakeSliderRow("Width", MinimumWidthMetres, MaximumWidthMetres, ReadWidth, WriteWidth, "Full road width in metres", out widthRefresh));
        useProfileWidthButton = TerrainUi.MakeGhost("Use profile width", null, "Drop the width override and use the profile's width", UseProfileWidth);
        roadCardBody.Add(useProfileWidthButton);
        priorityField.RegisterValueChangedCallback(OnPriorityChanged);
        roadCardBody.Add(ToolkitChrome.MakePropertyRow("Priority", priorityField, "Where roads overlap, the higher priority wins"));
        VisualElement badgeRow = new VisualElement();
        badgeRow.AddToClassList("terrain-badge-row");
        lengthBadge = ToolkitChrome.MakeBadge(string.Empty, ToolkitStatusTone.Neutral);
        knotCountBadge = ToolkitChrome.MakeBadge(string.Empty, ToolkitStatusTone.Neutral);
        badgeRow.Add(lengthBadge);
        badgeRow.Add(knotCountBadge);
        roadCardBody.Add(badgeRow);
        ribbonVertexLabel.AddToClassList("toolkit-hint");
        roadCardBody.Add(ribbonVertexLabel);
        warningLabel.AddToClassList("toolkit-hint");
        roadCardBody.Add(warningLabel);
        cardsHost.Add(roadCard);

        VisualElement pointCardBody;
        pointCard = TerrainUi.MakeCard("Point", out pointCardBody);
        pointLabel = new Label();
        pointCardBody.Add(pointLabel);
        deletePointButton = TerrainUi.MakeGhost("Delete point", ToolkitIcons.Trash, "Remove the selected point (a road keeps at least two)", TerrainRoadTool.DeleteSelectedKnot);
        pointCardBody.Add(deletePointButton);
        cardsHost.Add(pointCard);

        VisualElement actionsCardBody;
        VisualElement actionsCard = TerrainUi.MakeCard("Actions", out actionsCardBody);
        actionsCardBody.Add(TerrainUi.MakeSecondary("Rebake all roads", null, "Rebake every tile under a road", () => TerrainRoadBaker.RebakeAllRoads(true)));
        actionsCardBody.Add(TerrainUi.MakeDestructive("Delete road", ToolkitIcons.Trash, "Delete the selected road", DeleteSelectedRoad));
        cardsHost.Add(actionsCard);

        body.Add(emptyHost);
        body.Add(cardsHost);
    }

    public override void OnShown()
    {
        TerrainRoadTool.IsActive = true;
        TerrainRoadTool.SelectionChanged += RefreshAll;
        TerrainRoadBaker.BakeFinished += RefreshAll;
        TerrainRoadBaker.BakeFinished += RefreshRibbonVertexLabel;
        if (refreshTimer == null) refreshTimer = schedule.Execute(RefreshAll).Every(RefreshIntervalMilliseconds);
        else refreshTimer.Resume();
        RefreshAll();
        RefreshRibbonVertexLabel();
    }

    public override void OnHidden()
    {
        TerrainRoadTool.IsActive = false;
        TerrainRoadTool.SelectionChanged -= RefreshAll;
        TerrainRoadBaker.BakeFinished -= RefreshAll;
        TerrainRoadBaker.BakeFinished -= RefreshRibbonVertexLabel;
        if (refreshTimer != null) refreshTimer.Pause();
    }

    void RefreshAll()
    {
        TerrainRoad[] roads = Object.FindObjectsByType<TerrainRoad>(FindObjectsInactive.Include);
        TerrainRoad selectedRoad = TerrainRoadTool.SelectedRoad;

        emptyHost.style.display = roads.Length == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        cardsHost.style.display = roads.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;

        RebuildListIfChanged(roads, selectedRoad);

        roadCard.style.display = selectedRoad != null ? DisplayStyle.Flex : DisplayStyle.None;
        if (selectedRoad != null)
        {
            profileField.SetValueWithoutNotify(selectedRoad.profile);
            priorityField.SetValueWithoutNotify(selectedRoad.priority);
            widthRefresh();
            useProfileWidthButton.style.display = selectedRoad.widthOverrideMetres > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            int knotCount = selectedRoad.Container != null ? selectedRoad.Container.Spline.Count : 0;
            float lengthMetres = selectedRoad.Container != null ? selectedRoad.Container.CalculateLength(0) : 0f;
            lengthBadge.text = lengthMetres.ToString("F0") + " m long";
            knotCountBadge.text = knotCount + " points";
            RefreshWarning();

            int knotIndex = TerrainRoadTool.SelectedKnotIndex;
            bool knotSelected = knotIndex >= 0 && knotIndex < knotCount;
            pointCard.style.display = knotSelected ? DisplayStyle.Flex : DisplayStyle.None;
            pointLabel.text = "Point " + (knotIndex + 1) + " of " + knotCount;
            deletePointButton.SetEnabled(knotCount > MinimumKnotsPerRoad);
        }
        else
        {
            pointCard.style.display = DisplayStyle.None;
        }

        if (roads.Length != lastReportedRoadCount)
        {
            lastReportedRoadCount = roads.Length;
            RaiseStatusChanged();
        }
    }

    // Rows are rebuilt only when names, widths or the selection change, so a double-click is not eaten by a rebuild between clicks.
    void RebuildListIfChanged(TerrainRoad[] roads, TerrainRoad selectedRoad)
    {
        int signature = selectedRoad != null ? selectedRoad.GetEntityId().GetHashCode() : 0;
        foreach (TerrainRoad road in roads)
            signature = signature * 31 + road.name.GetHashCode() + road.GetEntityId().GetHashCode() + (int)(road.EffectiveWidthMetres * 10f);
        if (signature == lastListSignature && listBody.childCount == roads.Length) return;
        lastListSignature = signature;

        listBody.Clear();
        foreach (TerrainRoad road in roads)
        {
            TerrainRoad capturedRoad = road;
            VisualElement row = new VisualElement();
            row.AddToClassList("toolkit-list-row");
            row.AddToClassList("terrain-road-row");
            row.EnableInClassList("toolkit-list-row--selected", road == selectedRoad);
            Label name = new Label(road.name);
            name.AddToClassList("terrain-road-name");
            Label meta = new Label(road.EffectiveWidthMetres.ToString("F1") + " m wide");
            meta.AddToClassList("toolkit-hint");
            row.Add(name);
            row.Add(meta);
            row.RegisterCallback<ClickEvent>((ClickEvent clickEvent) => SelectRoad(capturedRoad, clickEvent.clickCount >= 2));
            listBody.Add(row);
        }
    }

    // Counting walks every open tile's hierarchy, so this runs on bake finish and panel show only, never on the timer.
    void RefreshRibbonVertexLabel()
    {
        List<string> lines = new List<string>();
        bool anyOverBudget = false;
        foreach (TerrainTile tile in Object.FindObjectsByType<TerrainTile>(FindObjectsInactive.Include))
        {
            TerrainTileBudgetResult budget;
            if (!TerrainBudgetCheck.TryEvaluateByCoordinate(tile.tileCoordinate, out budget)) continue;
            if (budget.ribbonVertexCount <= 0) continue;
            string line = "(" + tile.tileCoordinate.x + ", " + tile.tileCoordinate.y + "): " + budget.ribbonVertexCount + " ribbon verts";
            if (budget.isOverBudget)
            {
                line += " OVER";
                anyOverBudget = true;
            }
            lines.Add(line);
        }
        ToolkitChrome.SetStatus(ribbonVertexLabel, string.Join("\n", lines), anyOverBudget ? ToolkitStatusTone.Error : ToolkitStatusTone.Neutral);
        ribbonVertexLabel.style.display = lines.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void RefreshWarning()
    {
        List<string> lines = new List<string>();
        int missingTileCount = TerrainRoadBaker.MissingTilesUnderRoads().Count;
        int staleTileCount = TerrainRoadBaker.StaleTiles.Count;
        if (missingTileCount > 0)
            lines.Add(missingTileCount + " tiles under roads do not exist yet; create them in the Tiles tab");
        if (staleTileCount > 0)
            lines.Add(staleTileCount + " tiles need a rebake; open them or press Rebake all roads");
        warningLabel.text = string.Join("\n", lines);
        warningLabel.style.display = lines.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void SelectRoad(TerrainRoad road, bool frame)
    {
        TerrainRoadTool.SelectedRoad = road;
        if (frame) TerrainRoadTool.FrameSelected();
        RefreshAll();
    }

    void CreateRoad()
    {
        TerrainRoadTool.CreateRoadInFrontOfSceneView();
        RefreshAll();
    }

    float ReadWidth()
    {
        TerrainRoad road = TerrainRoadTool.SelectedRoad;
        return road != null ? road.EffectiveWidthMetres : MinimumWidthMetres;
    }

    void WriteWidth(float widthMetres)
    {
        TerrainRoad road = TerrainRoadTool.SelectedRoad;
        if (road == null) return;
        Undo.RecordObject(road, "Set Terrain Road Width");
        road.widthOverrideMetres = widthMetres;
        EditorUtility.SetDirty(road);
        useProfileWidthButton.style.display = DisplayStyle.Flex;
    }

    void UseProfileWidth()
    {
        TerrainRoad road = TerrainRoadTool.SelectedRoad;
        if (road == null) return;
        Undo.RecordObject(road, "Use Profile Road Width");
        road.widthOverrideMetres = 0f;
        EditorUtility.SetDirty(road);
        RefreshAll();
    }

    void OnPriorityChanged(ChangeEvent<int> changeEvent)
    {
        TerrainRoad road = TerrainRoadTool.SelectedRoad;
        if (road == null) return;
        Undo.RecordObject(road, "Set Terrain Road Priority");
        road.priority = changeEvent.newValue;
        EditorUtility.SetDirty(road);
    }

    void OnProfileChanged(ChangeEvent<Object> changeEvent)
    {
        TerrainRoad road = TerrainRoadTool.SelectedRoad;
        if (road == null) return;
        Undo.RecordObject(road, "Set Terrain Road Profile");
        road.profile = changeEvent.newValue as TerrainRoadProfile;
        EditorUtility.SetDirty(road);
        RefreshAll();
    }

    void DeleteSelectedRoad()
    {
        TerrainRoad road = TerrainRoadTool.SelectedRoad;
        if (road == null) return;
        if (!EditorUtility.DisplayDialog("Delete road", "Delete " + road.name + "?", "Delete", "Cancel")) return;
        Undo.DestroyObjectImmediate(road.gameObject);
        TerrainRoadTool.SelectedRoad = null;
        RefreshAll();
    }
}
#endif
