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

public sealed class TerrainWaterPanel : TerrainToolPanel
{
    const float DefaultRiverLengthMetres = 80f;
    const float RiverEndNearWorldEdgeMetres = 100f;
    const string DefaultProfileFolder = "Assets/Terrain/Profiles";
    const string DefaultProfilePath = DefaultProfileFolder + "/DefaultWaterProfile.asset";
    const string DefaultWaterMaterialPath = "Assets/PaintedGround/Materials/M_PaintedWater.mat";

    readonly VisualElement riverListContainer = new VisualElement();
    readonly VisualElement bodyListContainer = new VisualElement();
    readonly VisualElement crossingListContainer = new VisualElement();
    readonly VisualElement emptyState = new VisualElement();
    readonly VisualElement detailGroup = new VisualElement();
    readonly ObjectField profileField = new ObjectField("Profile");
    readonly Label warningLabel = new Label();
    Component selectedItem;
    IVisualElementScheduledItem refreshTimer;
    int lastRiverCount = -1;
    int lastBodyCount = -1;
    int lastCrossingCount = -1;

    public override string DisplayName => "Water";
    public override string StatusText =>
        "Water: " + Mathf.Max(lastRiverCount, 0) + " rivers, " + Mathf.Max(lastBodyCount, 0) + " bodies, " + Mathf.Max(lastCrossingCount, 0) + " crossings";

    public TerrainWaterPanel()
    {
        AddToClassList("terrain-panel");

        Label title = new Label("Water");
        title.AddToClassList("terrain-panel-title");
        Add(title);

        Label hint = new Label("Rivers are splines, water bodies are boxes, crossings keep a channel open for roads.");
        hint.AddToClassList("terrain-panel-hint");
        Add(hint);

        warningLabel.AddToClassList("terrain-warning");
        Add(warningLabel);

        Add(MakeSectionTitle("Rivers"));
        Add(riverListContainer);
        Add(MakeSectionTitle("Water Bodies"));
        Add(bodyListContainer);
        Add(MakeSectionTitle("Crossings"));
        Add(crossingListContainer);

        emptyState.AddToClassList("terrain-empty");
        Label emptyTitle = new Label("No water");
        emptyTitle.AddToClassList("terrain-panel-title");
        Label emptyWhy = new Label("Create a river, a lake or sea, or a crossing to start.");
        emptyWhy.AddToClassList("terrain-panel-hint");
        emptyState.Add(emptyTitle);
        emptyState.Add(emptyWhy);
        Add(emptyState);

        VisualElement buttonRow = new VisualElement();
        buttonRow.AddToClassList("terrain-row");
        Button selectButton = new Button(FrameSelectedItem);
        selectButton.text = "Select";
        selectButton.AddToClassList("terrain-segment-button");
        Button deleteButton = new Button(DeleteSelectedItem);
        deleteButton.text = "Delete";
        deleteButton.AddToClassList("terrain-segment-button");
        buttonRow.Add(selectButton);
        buttonRow.Add(deleteButton);
        detailGroup.Add(buttonRow);

        profileField.objectType = typeof(TerrainWaterProfile);
        profileField.allowSceneObjects = false;
        profileField.AddToClassList("terrain-row");
        profileField.RegisterValueChangedCallback(OnProfileChanged);
        detailGroup.Add(profileField);
        Add(detailGroup);

        Button newRiverButton = new Button(CreateRiver);
        newRiverButton.text = "New River";
        newRiverButton.AddToClassList("terrain-primary-button");
        Add(newRiverButton);

        VisualElement createRow = new VisualElement();
        createRow.AddToClassList("terrain-segment-row");
        createRow.Add(MakeSegmentButton("New Water Body", CreateWaterBody));
        createRow.Add(MakeSegmentButton("New Ford", () => CreateCrossing(TerrainCrossingKind.Ford)));
        createRow.Add(MakeSegmentButton("New Bridge", () => CreateCrossing(TerrainCrossingKind.Bridge)));
        Add(createRow);

        Add(MakeSegmentButton("Rebake All Water", () => TerrainWaterBaker.RebakeAllWater(true)));
    }

    public override void OnShown()
    {
        TerrainWaterBaker.BakeFinished += RefreshLists;
        if (refreshTimer == null) refreshTimer = schedule.Execute(RefreshLists).Every(1000);
        else refreshTimer.Resume();
        RefreshLists();
    }

    public override void OnHidden()
    {
        TerrainWaterBaker.BakeFinished -= RefreshLists;
        if (refreshTimer != null) refreshTimer.Pause();
    }

    static Label MakeSectionTitle(string text)
    {
        Label label = new Label(text);
        label.AddToClassList("terrain-panel-title");
        return label;
    }

    static Button MakeSegmentButton(string text, System.Action onClick)
    {
        Button button = new Button(onClick);
        button.text = text;
        button.AddToClassList("terrain-segment-button");
        return button;
    }

    void RefreshLists()
    {
        TerrainRiver[] rivers = Object.FindObjectsByType<TerrainRiver>(FindObjectsInactive.Include);
        TerrainWaterBody[] bodies = Object.FindObjectsByType<TerrainWaterBody>(FindObjectsInactive.Include);
        TerrainCrossing[] crossings = Object.FindObjectsByType<TerrainCrossing>(FindObjectsInactive.Include);

        if (selectedItem == null)
        {
            if (rivers.Length > 0) selectedItem = rivers[0];
            else if (bodies.Length > 0) selectedItem = bodies[0];
            else if (crossings.Length > 0) selectedItem = crossings[0];
        }

        riverListContainer.Clear();
        foreach (TerrainRiver river in rivers)
            riverListContainer.Add(MakeRow(river, river.name + ", " + (river.profile != null ? river.profile.name : "no profile")));

        bodyListContainer.Clear();
        foreach (TerrainWaterBody body in bodies)
            bodyListContainer.Add(MakeRow(body, body.name + ", level " + body.waterLevelMetres.ToString("0.##") + " m"));

        crossingListContainer.Clear();
        foreach (TerrainCrossing crossing in crossings)
            crossingListContainer.Add(MakeRow(crossing, crossing.name + ", " + crossing.kind));

        int totalCount = rivers.Length + bodies.Length + crossings.Length;
        emptyState.style.display = totalCount == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        detailGroup.style.display = selectedItem != null ? DisplayStyle.Flex : DisplayStyle.None;
        profileField.style.display = SelectedItemHasProfile() ? DisplayStyle.Flex : DisplayStyle.None;
        profileField.SetValueWithoutNotify(GetSelectedProfile());

        RefreshWarning(rivers, bodies);

        if (rivers.Length != lastRiverCount || bodies.Length != lastBodyCount || crossings.Length != lastCrossingCount)
        {
            lastRiverCount = rivers.Length;
            lastBodyCount = bodies.Length;
            lastCrossingCount = crossings.Length;
            RaiseStatusChanged();
        }
    }

    Button MakeRow(Component item, string text)
    {
        Button row = new Button(() => SelectItem(item));
        row.text = text;
        row.AddToClassList("terrain-list-row");
        if (item == selectedItem) row.AddToClassList("terrain-list-row--selected");
        return row;
    }

    bool SelectedItemHasProfile()
    {
        return selectedItem is TerrainRiver || selectedItem is TerrainWaterBody;
    }

    TerrainWaterProfile GetSelectedProfile()
    {
        TerrainRiver river = selectedItem as TerrainRiver;
        if (river != null) return river.profile;
        TerrainWaterBody body = selectedItem as TerrainWaterBody;
        return body != null ? body.profile : null;
    }

    void RefreshWarning(TerrainRiver[] rivers, TerrainWaterBody[] bodies)
    {
        List<string> lines = new List<string>();
        int missingTileCount = TerrainWaterBaker.MissingTilesUnderWater().Count;
        int staleTileCount = TerrainWaterBaker.StaleTiles.Count;
        if (missingTileCount > 0)
            lines.Add(missingTileCount + " tiles under water do not exist yet; create them in the Tiles tab");
        if (staleTileCount > 0)
            lines.Add(staleTileCount + " tiles need a rebake; open them or press Rebake All Water");

        TerrainWorldProfile worldProfile = TerrainWorldProfile.FindInProject();
        if (worldProfile != null)
        {
            float tileSize = worldProfile.tileSizeMetres;
            float worldMinX = worldProfile.worldOriginTile.x * tileSize - tileSize * 0.5f;
            float worldMaxX = (worldProfile.worldOriginTile.x + worldProfile.worldTilesX) * tileSize - tileSize * 0.5f;
            float worldMinZ = worldProfile.worldOriginTile.y * tileSize - tileSize * 0.5f;
            float worldMaxZ = (worldProfile.worldOriginTile.y + worldProfile.worldTilesZ) * tileSize - tileSize * 0.5f;
            foreach (TerrainRiver river in rivers)
            {
                SplineContainer container = river.Container;
                if (container == null || container.Splines.Count == 0) continue;
                Vector3 endPoint = container.EvaluatePosition(container.Splines.Count - 1, 1f);
                bool insideBody = false;
                foreach (TerrainWaterBody body in bodies)
                {
                    if (body.ContainsXZ(endPoint.x, endPoint.z))
                    {
                        insideBody = true;
                        break;
                    }
                }
                if (insideBody) continue;

                float nearestEdgeMetres = Mathf.Min(
                    Mathf.Min(endPoint.x - worldMinX, worldMaxX - endPoint.x),
                    Mathf.Min(endPoint.z - worldMinZ, worldMaxZ - endPoint.z));
                if (nearestEdgeMetres > RiverEndNearWorldEdgeMetres)
                    lines.Add("River " + river.name + " ends in the middle of the land with no lake or sea.");
            }
        }

        warningLabel.text = string.Join("\n", lines);
        warningLabel.style.display = lines.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void SelectItem(Component item)
    {
        selectedItem = item;
        RefreshLists();
    }

    void FrameSelectedItem()
    {
        if (selectedItem == null) return;
        Selection.activeGameObject = selectedItem.gameObject;
        if (SceneView.lastActiveSceneView == null) return;

        TerrainRiver river = selectedItem as TerrainRiver;
        TerrainWaterBody body = selectedItem as TerrainWaterBody;
        TerrainCrossing crossing = selectedItem as TerrainCrossing;
        if (river != null) SceneView.lastActiveSceneView.Frame(river.WorldBounds());
        else if (body != null) SceneView.lastActiveSceneView.Frame(body.WorldBounds());
        else if (crossing != null) SceneView.lastActiveSceneView.Frame(crossing.WorldBounds());
    }

    void DeleteSelectedItem()
    {
        if (selectedItem == null) return;
        if (!EditorUtility.DisplayDialog("Delete water", "Delete " + selectedItem.name + "?", "Delete", "Cancel")) return;
        Undo.DestroyObjectImmediate(selectedItem.gameObject);
        selectedItem = null;
        RefreshLists();
    }

    void OnProfileChanged(ChangeEvent<Object> changeEvent)
    {
        TerrainWaterProfile newProfile = changeEvent.newValue as TerrainWaterProfile;
        TerrainRiver river = selectedItem as TerrainRiver;
        TerrainWaterBody body = selectedItem as TerrainWaterBody;
        if (river != null)
        {
            Undo.RecordObject(river, "Set Terrain Water Profile");
            river.profile = newProfile;
            EditorUtility.SetDirty(river);
        }
        else if (body != null)
        {
            Undo.RecordObject(body, "Set Terrain Water Profile");
            body.profile = newProfile;
            EditorUtility.SetDirty(body);
        }
        RefreshLists();
    }

    static Vector3 ActiveTileCenterOrNull(out bool hasActiveTile)
    {
        TerrainTile activeTile;
        hasActiveTile = TerrainTilesPanel.TryGetActiveTile(out activeTile);
        if (!hasActiveTile) return Vector3.zero;
        Vector2 tileCenter = TerrainTile.CenterOfTile(activeTile.tileCoordinate);
        return new Vector3(tileCenter.x, 0f, tileCenter.y);
    }

    static Vector3 SceneViewPivotOnGround()
    {
        Vector3 pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        return new Vector3(pivot.x, 0f, pivot.z);
    }

    void CreateRiver()
    {
        bool hasActiveTile;
        Vector3 center = ActiveTileCenterOrNull(out hasActiveTile);
        if (!hasActiveTile) center = SceneViewPivotOnGround();

        GameObject riverObject = new GameObject("TerrainRiver");
        riverObject.transform.position = center;
        SplineContainer container = riverObject.AddComponent<SplineContainer>();
        TerrainRiver createdRiver = riverObject.AddComponent<TerrainRiver>();

        Spline spline = container.Spline;
        spline.Clear();
        float halfLength = DefaultRiverLengthMetres * 0.5f;
        spline.Add(new BezierKnot(new float3(-halfLength, 0f, 0f)), TangentMode.AutoSmooth);
        spline.Add(new BezierKnot(new float3(halfLength, 0f, 0f)), TangentMode.AutoSmooth);

        createdRiver.profile = FindOrCreateDefaultProfile();
        EditorUtility.SetDirty(createdRiver);

        Undo.RegisterCreatedObjectUndo(riverObject, "Create Terrain River");
        Selection.activeGameObject = riverObject;
        selectedItem = createdRiver;
        ToolManager.SetActiveContext<SplineToolContext>();
        RefreshLists();
    }

    void CreateWaterBody()
    {
        bool hasActiveTile;
        Vector3 center = ActiveTileCenterOrNull(out hasActiveTile);
        if (!hasActiveTile) center = SceneViewPivotOnGround();
        float halfSize = TerrainTile.TileSizeMetres * 0.5f;

        GameObject bodyObject = new GameObject("TerrainWaterBody");
        bodyObject.transform.position = center;
        TerrainWaterBody createdBody = bodyObject.AddComponent<TerrainWaterBody>();
        createdBody.boundsMin = new Vector3(center.x - halfSize, createdBody.boundsMin.y, center.z - halfSize);
        createdBody.boundsMax = new Vector3(center.x + halfSize, createdBody.boundsMax.y, center.z + halfSize);
        createdBody.waterLevelMetres = 0f;
        createdBody.profile = FindOrCreateDefaultProfile();
        EditorUtility.SetDirty(createdBody);

        Undo.RegisterCreatedObjectUndo(bodyObject, "Create Terrain Water Body");
        Selection.activeGameObject = bodyObject;
        selectedItem = createdBody;
        RefreshLists();
    }

    void CreateCrossing(TerrainCrossingKind kind)
    {
        GameObject crossingObject = new GameObject("Terrain" + kind);
        crossingObject.transform.position = SceneViewPivotOnGround();
        TerrainCrossing createdCrossing = crossingObject.AddComponent<TerrainCrossing>();
        createdCrossing.kind = kind;
        EditorUtility.SetDirty(createdCrossing);

        Undo.RegisterCreatedObjectUndo(crossingObject, "Create Terrain Crossing");
        Selection.activeGameObject = crossingObject;
        selectedItem = createdCrossing;
        RefreshLists();
    }

    static TerrainWaterProfile FindOrCreateDefaultProfile()
    {
        string[] guids = AssetDatabase.FindAssets("t:TerrainWaterProfile");
        if (guids.Length > 0)
            return AssetDatabase.LoadAssetAtPath<TerrainWaterProfile>(AssetDatabase.GUIDToAssetPath(guids[0]));

        if (!AssetDatabase.IsValidFolder("Assets/Terrain")) AssetDatabase.CreateFolder("Assets", "Terrain");
        if (!AssetDatabase.IsValidFolder(DefaultProfileFolder)) AssetDatabase.CreateFolder("Assets/Terrain", "Profiles");
        TerrainWaterProfile createdProfile = ScriptableObject.CreateInstance<TerrainWaterProfile>();
        createdProfile.waterMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultWaterMaterialPath);
        AssetDatabase.CreateAsset(createdProfile, DefaultProfilePath);
        AssetDatabase.SaveAssets();
        return createdProfile;
    }
}
#endif
