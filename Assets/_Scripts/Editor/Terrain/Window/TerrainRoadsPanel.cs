#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Splines;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.UIElements;

public sealed class TerrainRoadsPanel : TerrainToolPanel
{
    const float DefaultRoadLengthMetres = 40f;
    const string DefaultProfileFolder = "Assets/Terrain/Profiles";
    const string DefaultProfilePath = DefaultProfileFolder + "/DefaultRoadProfile.asset";

    readonly VisualElement roadListContainer = new VisualElement();
    readonly VisualElement emptyState = new VisualElement();
    readonly VisualElement detailGroup = new VisualElement();
    readonly ObjectField profileField = new ObjectField("Profile");
    readonly Label warningLabel = new Label();
    readonly Label ribbonVertexLabel = new Label();
    TerrainRoad selectedRoad;
    IVisualElementScheduledItem refreshTimer;
    int lastReportedRoadCount = -1;

    public override string DisplayName => "Roads";
    public override string StatusText => "Roads: " + Mathf.Max(lastReportedRoadCount, 0);

    public TerrainRoadsPanel()
    {
        AddToClassList("terrain-panel");

        Label title = new Label("Roads");
        title.AddToClassList("terrain-panel-title");
        Add(title);

        Label hint = new Label("Roads are splines. Drag the knots; the ground follows.");
        hint.AddToClassList("terrain-panel-hint");
        Add(hint);

        warningLabel.AddToClassList("terrain-warning");
        Add(warningLabel);

        ribbonVertexLabel.AddToClassList("terrain-panel-hint");
        ribbonVertexLabel.style.display = DisplayStyle.None;
        Add(ribbonVertexLabel);

        Add(roadListContainer);

        emptyState.AddToClassList("terrain-empty");
        Label emptyTitle = new Label("No roads");
        emptyTitle.AddToClassList("terrain-panel-title");
        Label emptyWhy = new Label("A road is a spline the terrain carves and paints along. Create one to start.");
        emptyWhy.AddToClassList("terrain-panel-hint");
        Button emptyCreateButton = new Button(CreateRoad);
        emptyCreateButton.text = "New Road";
        emptyCreateButton.AddToClassList("terrain-primary-button");
        emptyState.Add(emptyTitle);
        emptyState.Add(emptyWhy);
        emptyState.Add(emptyCreateButton);
        Add(emptyState);

        VisualElement buttonRow = new VisualElement();
        buttonRow.AddToClassList("terrain-row");
        Button selectButton = new Button(FrameSelectedRoad);
        selectButton.text = "Select";
        selectButton.AddToClassList("terrain-segment-button");
        Button deleteButton = new Button(DeleteSelectedRoad);
        deleteButton.text = "Delete";
        deleteButton.AddToClassList("terrain-segment-button");
        buttonRow.Add(selectButton);
        buttonRow.Add(deleteButton);
        detailGroup.Add(buttonRow);

        profileField.objectType = typeof(TerrainRoadProfile);
        profileField.allowSceneObjects = false;
        profileField.AddToClassList("terrain-row");
        profileField.RegisterValueChangedCallback(OnProfileChanged);
        detailGroup.Add(profileField);
        Add(detailGroup);

        Button newRoadButton = new Button(CreateRoad);
        newRoadButton.text = "New Road";
        newRoadButton.AddToClassList("terrain-primary-button");
        Add(newRoadButton);

        Button rebakeButton = new Button(() => TerrainRoadBaker.RebakeAllRoads(true));
        rebakeButton.text = "Rebake All Roads";
        rebakeButton.AddToClassList("terrain-segment-button");
        Add(rebakeButton);
    }

    public override void OnShown()
    {
        TerrainRoadBaker.BakeFinished += RefreshRoadList;
        TerrainRoadBaker.BakeFinished += RefreshRibbonVertexLabel;
        if (refreshTimer == null) refreshTimer = schedule.Execute(RefreshRoadList).Every(1000);
        else refreshTimer.Resume();
        RefreshRoadList();
        RefreshRibbonVertexLabel();
    }

    public override void OnHidden()
    {
        TerrainRoadBaker.BakeFinished -= RefreshRoadList;
        TerrainRoadBaker.BakeFinished -= RefreshRibbonVertexLabel;
        if (refreshTimer != null) refreshTimer.Pause();
    }

    void RefreshRoadList()
    {
        TerrainRoad[] roads = Object.FindObjectsByType<TerrainRoad>(FindObjectsInactive.Include);
        if (selectedRoad != null && System.Array.IndexOf(roads, selectedRoad) < 0) selectedRoad = null;
        if (selectedRoad == null && roads.Length > 0) selectedRoad = roads[0];

        roadListContainer.Clear();
        foreach (TerrainRoad road in roads)
        {
            TerrainRoad capturedRoad = road;
            Button row = new Button(() => SelectRoad(capturedRoad));
            row.text = road.name + ", " + (road.profile != null ? road.profile.name : "no profile");
            row.AddToClassList("terrain-list-row");
            if (road == selectedRoad) row.AddToClassList("terrain-list-row--selected");
            roadListContainer.Add(row);
        }

        emptyState.style.display = roads.Length == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        detailGroup.style.display = selectedRoad != null ? DisplayStyle.Flex : DisplayStyle.None;
        profileField.SetValueWithoutNotify(selectedRoad != null ? selectedRoad.profile : null);

        RefreshWarning();

        if (roads.Length != lastReportedRoadCount)
        {
            lastReportedRoadCount = roads.Length;
            RaiseStatusChanged();
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
        ribbonVertexLabel.text = string.Join("\n", lines);
        ribbonVertexLabel.style.color = anyOverBudget ? new StyleColor(new Color(0.9f, 0.25f, 0.25f)) : new StyleColor(StyleKeyword.Null);
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
            lines.Add(staleTileCount + " tiles need a rebake; open them or press Rebake All Roads");
        warningLabel.text = string.Join("\n", lines);
        warningLabel.style.display = lines.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void SelectRoad(TerrainRoad road)
    {
        selectedRoad = road;
        RefreshRoadList();
    }

    void FrameSelectedRoad()
    {
        if (selectedRoad == null) return;
        Selection.activeGameObject = selectedRoad.gameObject;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(selectedRoad.WorldBounds());
    }

    void DeleteSelectedRoad()
    {
        if (selectedRoad == null) return;
        if (!EditorUtility.DisplayDialog("Delete road", "Delete " + selectedRoad.name + "?", "Delete", "Cancel")) return;
        Undo.DestroyObjectImmediate(selectedRoad.gameObject);
        selectedRoad = null;
        RefreshRoadList();
    }

    void OnProfileChanged(ChangeEvent<Object> changeEvent)
    {
        if (selectedRoad == null) return;
        Undo.RecordObject(selectedRoad, "Set Terrain Road Profile");
        selectedRoad.profile = changeEvent.newValue as TerrainRoadProfile;
        EditorUtility.SetDirty(selectedRoad);
        RefreshRoadList();
    }

    void CreateRoad()
    {
        Vector3 center;
        TerrainTile activeTile;
        if (TerrainTilesPanel.TryGetActiveTile(out activeTile))
        {
            Vector2 tileCenter = TerrainTile.CenterOfTile(activeTile.tileCoordinate);
            center = new Vector3(tileCenter.x, 0f, tileCenter.y);
        }
        else
        {
            Vector3 pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            center = new Vector3(pivot.x, 0f, pivot.z);
        }

        GameObject roadObject = new GameObject("TerrainRoad");
        roadObject.transform.position = center;
        SplineContainer container = roadObject.AddComponent<SplineContainer>();
        TerrainRoad createdRoad = roadObject.AddComponent<TerrainRoad>();

        Spline spline = container.Spline;
        spline.Clear();
        float halfLength = DefaultRoadLengthMetres * 0.5f;
        spline.Add(new BezierKnot(new float3(-halfLength, 0f, 0f)), TangentMode.AutoSmooth);
        spline.Add(new BezierKnot(new float3(halfLength, 0f, 0f)), TangentMode.AutoSmooth);

        createdRoad.profile = FindOrCreateDefaultProfile();
        EditorUtility.SetDirty(createdRoad);

        Undo.RegisterCreatedObjectUndo(roadObject, "Create Terrain Road");
        Selection.activeGameObject = roadObject;
        selectedRoad = createdRoad;
        ToolManager.SetActiveContext<SplineToolContext>();
        RefreshRoadList();
    }

    static TerrainRoadProfile FindOrCreateDefaultProfile()
    {
        string[] guids = AssetDatabase.FindAssets("t:TerrainRoadProfile");
        if (guids.Length > 0)
            return AssetDatabase.LoadAssetAtPath<TerrainRoadProfile>(AssetDatabase.GUIDToAssetPath(guids[0]));

        if (!AssetDatabase.IsValidFolder("Assets/Terrain")) AssetDatabase.CreateFolder("Assets", "Terrain");
        if (!AssetDatabase.IsValidFolder(DefaultProfileFolder)) AssetDatabase.CreateFolder("Assets/Terrain", "Profiles");
        TerrainRoadProfile createdProfile = ScriptableObject.CreateInstance<TerrainRoadProfile>();
        AssetDatabase.CreateAsset(createdProfile, DefaultProfilePath);
        AssetDatabase.SaveAssets();
        return createdProfile;
    }
}
#endif
