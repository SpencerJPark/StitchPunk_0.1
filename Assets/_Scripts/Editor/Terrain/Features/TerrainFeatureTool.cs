#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

[InitializeOnLoad]
public static class TerrainFeatureTool
{
    private enum DrawMode { None, Outline, Box }

    private const float KnotPickPixels = 10f;
    private const float SegmentPickPixels = 8f;
    private const float CloseOutlinePixels = 12f;
    private const float RayMarchStepMetres = 2f;
    private const float RayMarchMaximumMetres = 3000f;
    private const int BisectionIterations = 12;
    private const float TileSizeMetres = 100f;
    private const float TileHalfSizeMetres = 50f;
    private const float MinimumBoxSideMetres = 1f;
    private const int MinimumOutlinePointCount = 3;
    private const double HudMessageSeconds = 3.0;
    private const float PreviewSegmentMetres = 2f;

    private static readonly Color OutlineColour = new Color(0.95f, 0.95f, 0.9f, 1f);
    private static readonly Color RubberBandColour = new Color(1f, 1f, 1f, 0.6f);
    private static readonly Color SelectionBlue = new Color(0.24f, 0.55f, 1f, 1f);

    private static DrawMode drawMode = DrawMode.None;
    private static TerrainAreaRecipe pendingRecipe;
    private static int pendingSeed;
    private static readonly List<Vector2> pendingPointsXZ = new List<Vector2>();
    private static Vector2 boxCornerA;
    private static Vector2 boxCornerB;
    private static bool boxDragging;

    private static bool mouseGroundValid;
    private static Vector3 mouseGround;

    private static TerrainArea trackedArea;
    private static int selectedKnotIndex = -1;
    private static bool knotDragging;
    // GetControlID is order-dependent, so the id seen at mouse-down is what the drag is compared against.
    private static int dragControlId;
    private static int hoverKnotIndex = -1;

    private static string hudMessage;
    private static double hudMessageExpiry;
    private static SceneView lastSceneView;

    private static readonly Dictionary<Vector2Int, TerrainTileData> tileDataCache = new Dictionary<Vector2Int, TerrainTileData>();
    private static readonly List<Vector3> knotWorldPositions = new List<Vector3>();
    private static readonly List<Vector2> knotScreenPositions = new List<Vector2>();

    public static event Action SelectionChanged;

    static TerrainFeatureTool()
    {
        SceneView.duringSceneGui += OnSceneGui;
        Selection.selectionChanged += RefreshTrackedSelection;
    }

    public static Component SelectedFeature => trackedArea;

    public static bool IsDrawing => drawMode != DrawMode.None;

    public static void BeginDrawOutline(TerrainAreaRecipe recipe)
    {
        if (recipe == null)
        {
            return;
        }

        CancelDrawing();
        drawMode = DrawMode.Outline;
        pendingRecipe = recipe;
        pendingSeed = new System.Random().Next();
        tileDataCache.Clear();
        SceneView.RepaintAll();
    }

    public static void BeginDragBox(ScriptableObject recipe)
    {
        TerrainAreaRecipe areaRecipe = recipe as TerrainAreaRecipe;
        if (areaRecipe == null)
        {
            Debug.Log("Stamps arrive in WF-6");
            CancelDrawing();
            return;
        }

        CancelDrawing();
        drawMode = DrawMode.Box;
        pendingRecipe = areaRecipe;
        pendingSeed = new System.Random().Next();
        tileDataCache.Clear();
        SceneView.RepaintAll();
    }

    public static void CancelDrawing()
    {
        drawMode = DrawMode.None;
        pendingRecipe = null;
        pendingPointsXZ.Clear();
        boxDragging = false;
        hudMessage = null;
        TerrainFeaturePreview.Clear();
        SceneView.RepaintAll();
    }

    public static void FrameSelected()
    {
        SceneView view = lastSceneView != null ? lastSceneView : SceneView.lastActiveSceneView;
        if (trackedArea == null || view == null)
        {
            return;
        }

        view.Frame(trackedArea.WorldBounds(), false);
    }

    public static void DeleteSelectedKnot()
    {
        if (trackedArea == null || selectedKnotIndex < 0)
        {
            return;
        }

        SplineContainer container = trackedArea.Container;
        Spline spline = container != null ? container.Spline : null;
        if (spline == null || selectedKnotIndex >= spline.Count || spline.Count <= MinimumOutlinePointCount)
        {
            return;
        }

        Undo.RecordObject(container, "Delete Area Knot");
        spline.RemoveAt(selectedKnotIndex);
        selectedKnotIndex = -1;
        EditorUtility.SetDirty(container);
        TerrainFeatureEvents.Raise(trackedArea);
        SelectionChanged?.Invoke();
        SceneView.RepaintAll();
    }

    private static void RefreshTrackedSelection()
    {
        TerrainArea selectedArea = null;
        if (Selection.activeGameObject != null)
        {
            selectedArea = Selection.activeGameObject.GetComponent<TerrainArea>();
        }

        if (selectedArea == trackedArea)
        {
            return;
        }

        trackedArea = selectedArea;
        selectedKnotIndex = -1;
        knotDragging = false;
        SelectionChanged?.Invoke();
        SceneView.RepaintAll();
    }

    private static float GroundHeightAt(float worldX, float worldZ)
    {
        Vector2Int coordinate = new Vector2Int(
            Mathf.FloorToInt((worldX + TileHalfSizeMetres) / TileSizeMetres),
            Mathf.FloorToInt((worldZ + TileHalfSizeMetres) / TileSizeMetres));

        TerrainTileData tileData;
        if (!tileDataCache.TryGetValue(coordinate, out tileData))
        {
            TerrainTile openTile = TerrainTileCreator.FindTileAt(coordinate);
            tileData = openTile != null ? openTile.tileData : null;
            if (tileData == null)
            {
                tileData = AssetDatabase.LoadAssetAtPath<TerrainTileData>(TerrainTileAssets.TileDataPath(coordinate));
            }

            tileDataCache[coordinate] = tileData;
        }

        if (tileData == null)
        {
            return 0f;
        }

        return tileData.SampleBilinear(worldX, worldZ) + TerrainTileData.GroundTopHeightMetres;
    }

    private static float RayHeightAboveGround(Ray ray, float distance)
    {
        Vector3 point = ray.origin + ray.direction * distance;
        return point.y - GroundHeightAt(point.x, point.z);
    }

    private static bool TryGroundUnderMouse(Vector2 mouse, out Vector3 groundPoint)
    {
        groundPoint = Vector3.zero;
        Ray ray = HandleUtility.GUIPointToWorldRay(mouse);
        float previousDistance = 0f;
        if (RayHeightAboveGround(ray, 0f) <= 0f)
        {
            return false;
        }

        for (float distance = RayMarchStepMetres; distance <= RayMarchMaximumMetres; distance += RayMarchStepMetres)
        {
            if (RayHeightAboveGround(ray, distance) <= 0f)
            {
                float nearDistance = previousDistance;
                float farDistance = distance;
                for (int iteration = 0; iteration < BisectionIterations; iteration++)
                {
                    float middleDistance = (nearDistance + farDistance) * 0.5f;
                    if (RayHeightAboveGround(ray, middleDistance) > 0f)
                    {
                        nearDistance = middleDistance;
                    }
                    else
                    {
                        farDistance = middleDistance;
                    }
                }

                Vector3 hitPoint = ray.origin + ray.direction * ((nearDistance + farDistance) * 0.5f);
                groundPoint = new Vector3(hitPoint.x, GroundHeightAt(hitPoint.x, hitPoint.z), hitPoint.z);
                return true;
            }

            previousDistance = distance;
        }

        return false;
    }

    private static void UpdateMouseGround(Vector2 mouse)
    {
        mouseGroundValid = TryGroundUnderMouse(mouse, out mouseGround);
    }

    private static Vector3 OnGround(Vector2 pointXZ)
    {
        return new Vector3(pointXZ.x, GroundHeightAt(pointXZ.x, pointXZ.y), pointXZ.y);
    }

    private static void ShowHud(string message)
    {
        hudMessage = message;
        hudMessageExpiry = EditorApplication.timeSinceStartup + HudMessageSeconds;
    }

    private static void OnSceneGui(SceneView sceneView)
    {
        bool editingSelectedArea = drawMode == DrawMode.None && trackedArea != null;
        if (drawMode == DrawMode.None && !editingSelectedArea)
        {
            return;
        }

        lastSceneView = sceneView;
        Event currentEvent = Event.current;
        int controlId = GUIUtility.GetControlID(FocusType.Passive);

        if (currentEvent.type == EventType.Layout)
        {
            if (IsDrawing)
            {
                HandleUtility.AddDefaultControl(controlId);
            }

            return;
        }

        if (editingSelectedArea)
        {
            GatherKnots();
        }

        switch (currentEvent.type)
        {
            case EventType.MouseMove:
                UpdateMouseGround(currentEvent.mousePosition);
                if (editingSelectedArea)
                {
                    hoverKnotIndex = NearestKnotIndex(currentEvent.mousePosition);
                }

                sceneView.Repaint();
                break;
            case EventType.MouseDown:
                if (currentEvent.button == 0 && !currentEvent.alt)
                {
                    UpdateMouseGround(currentEvent.mousePosition);
                    if (IsDrawing)
                    {
                        HandleDrawMouseDown(currentEvent, controlId);
                    }
                    else
                    {
                        HandleEditMouseDown(currentEvent, controlId);
                    }
                }

                break;
            case EventType.MouseDrag:
                UpdateMouseGround(currentEvent.mousePosition);
                if (GUIUtility.hotControl == dragControlId && (knotDragging || boxDragging))
                {
                    HandleDrag();
                    currentEvent.Use();
                }

                sceneView.Repaint();
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == dragControlId && (knotDragging || boxDragging))
                {
                    HandleMouseUp();
                    GUIUtility.hotControl = 0;
                    currentEvent.Use();
                }

                break;
            case EventType.KeyDown:
                HandleKeyDown(currentEvent);
                break;
            case EventType.Repaint:
                DrawAll(editingSelectedArea);
                break;
        }

        if (IsDrawing)
        {
            sceneView.Repaint();
        }
    }

    private static void GatherKnots()
    {
        knotWorldPositions.Clear();
        knotScreenPositions.Clear();
        SplineContainer container = trackedArea.Container;
        Spline spline = container != null ? container.Spline : null;
        if (spline == null)
        {
            return;
        }

        for (int knotIndex = 0; knotIndex < spline.Count; knotIndex++)
        {
            Vector3 world = container.transform.TransformPoint((Vector3)(float3)spline[knotIndex].Position);
            knotWorldPositions.Add(world);
            knotScreenPositions.Add(HandleUtility.WorldToGUIPoint(world));
        }
    }

    private static int NearestKnotIndex(Vector2 mouse)
    {
        int bestIndex = -1;
        float bestDistance = KnotPickPixels;
        for (int knotIndex = 0; knotIndex < knotScreenPositions.Count; knotIndex++)
        {
            float distance = Vector2.Distance(knotScreenPositions[knotIndex], mouse);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                bestIndex = knotIndex;
            }
        }

        return bestIndex;
    }

    private static List<Vector2> KnotOutlineXZ()
    {
        List<Vector2> outline = new List<Vector2>(knotWorldPositions.Count);
        for (int knotIndex = 0; knotIndex < knotWorldPositions.Count; knotIndex++)
        {
            outline.Add(new Vector2(knotWorldPositions[knotIndex].x, knotWorldPositions[knotIndex].z));
        }

        return outline;
    }

    private static void HandleDrawMouseDown(Event currentEvent, int controlId)
    {
        if (!mouseGroundValid)
        {
            return;
        }

        if (drawMode == DrawMode.Box)
        {
            boxCornerA = new Vector2(mouseGround.x, mouseGround.z);
            boxCornerB = boxCornerA;
            boxDragging = true;
            GUIUtility.hotControl = controlId;
            dragControlId = controlId;
            currentEvent.Use();
            return;
        }

        if (pendingPointsXZ.Count >= 1)
        {
            Vector2 firstScreen = HandleUtility.WorldToGUIPoint(OnGround(pendingPointsXZ[0]));
            if (Vector2.Distance(firstScreen, currentEvent.mousePosition) <= CloseOutlinePixels)
            {
                TryCloseOutline();
                currentEvent.Use();
                return;
            }
        }

        pendingPointsXZ.Add(new Vector2(mouseGround.x, mouseGround.z));
        currentEvent.Use();
    }

    private static void TryCloseOutline()
    {
        if (pendingPointsXZ.Count < MinimumOutlinePointCount || TerrainFeatureEditing.IsSelfIntersecting(pendingPointsXZ))
        {
            ShowHud("Outline crosses itself — move or remove a point");
            return;
        }

        List<Vector2> finishedOutline = new List<Vector2>(pendingPointsXZ);
        TerrainAreaRecipe recipe = pendingRecipe;
        int seed = pendingSeed;
        CancelDrawing();
        CreateArea(finishedOutline, recipe, seed);
    }

    private static void HandleEditMouseDown(Event currentEvent, int controlId)
    {
        Vector2 mouse = currentEvent.mousePosition;
        int knotIndex = NearestKnotIndex(mouse);
        if (knotIndex >= 0)
        {
            selectedKnotIndex = knotIndex;
            BeginKnotDrag(controlId, "Move Area Knot");
            SelectionChanged?.Invoke();
            currentEvent.Use();
            return;
        }

        if (!currentEvent.control || !mouseGroundValid || knotWorldPositions.Count < MinimumOutlinePointCount)
        {
            return;
        }

        List<Vector2> outline = KnotOutlineXZ();
        Vector2 closestPoint;
        int segmentIndex = TerrainFeatureEditing.NearestSegmentIndex(outline, new Vector2(mouseGround.x, mouseGround.z), out closestPoint);
        Vector2 closestScreen = HandleUtility.WorldToGUIPoint(OnGround(closestPoint));
        if (segmentIndex < 0 || Vector2.Distance(closestScreen, mouse) > SegmentPickPixels)
        {
            return;
        }

        SplineContainer container = trackedArea.Container;
        Undo.IncrementCurrentGroup();
        Undo.RecordObject(container, "Insert Area Knot");
        Vector3 localPoint = container.transform.InverseTransformPoint(OnGround(closestPoint));
        int insertIndex = Mathf.Clamp(segmentIndex + 1, 1, container.Spline.Count);
        container.Spline.Insert(insertIndex, (float3)localPoint, TangentMode.Linear);
        selectedKnotIndex = insertIndex;
        EditorUtility.SetDirty(container);
        TerrainFeatureEvents.Raise(trackedArea);
        SelectionChanged?.Invoke();
        currentEvent.Use();
    }

    private static void BeginKnotDrag(int controlId, string undoName)
    {
        tileDataCache.Clear();
        Undo.IncrementCurrentGroup();
        Undo.RecordObject(trackedArea.Container, undoName);
        knotDragging = true;
        GUIUtility.hotControl = controlId;
        dragControlId = controlId;
    }

    private static void HandleDrag()
    {
        if (boxDragging)
        {
            if (mouseGroundValid)
            {
                boxCornerB = new Vector2(mouseGround.x, mouseGround.z);
                TerrainFeaturePreview.SetOutline(TerrainFeatureEditing.BoxOutline(boxCornerA, boxCornerB), pendingRecipe, pendingSeed);
            }

            return;
        }

        if (trackedArea == null || !mouseGroundValid)
        {
            return;
        }

        SplineContainer container = trackedArea.Container;
        Spline spline = container.Spline;
        if (selectedKnotIndex < 0 || selectedKnotIndex >= spline.Count)
        {
            return;
        }

        BezierKnot knot = spline[selectedKnotIndex];
        knot.Position = (float3)container.transform.InverseTransformPoint(mouseGround);
        spline.SetKnot(selectedKnotIndex, knot);
        EditorUtility.SetDirty(container);
        TerrainFeaturePreview.SetOutline(trackedArea.OutlineWorldXZ(PreviewSegmentMetres), trackedArea.recipe, trackedArea.seed);
    }

    private static void HandleMouseUp()
    {
        if (boxDragging)
        {
            boxDragging = false;
            Vector2 sizeXZ = new Vector2(Mathf.Abs(boxCornerB.x - boxCornerA.x), Mathf.Abs(boxCornerB.y - boxCornerA.y));
            if (sizeXZ.x < MinimumBoxSideMetres || sizeXZ.y < MinimumBoxSideMetres)
            {
                TerrainFeaturePreview.Clear();
                return;
            }

            List<Vector2> boxOutline = TerrainFeatureEditing.BoxOutline(boxCornerA, boxCornerB);
            TerrainAreaRecipe recipe = pendingRecipe;
            int seed = pendingSeed;
            CancelDrawing();
            CreateArea(boxOutline, recipe, seed);
            return;
        }

        knotDragging = false;
        TerrainFeaturePreview.Clear();
        if (trackedArea != null)
        {
            TerrainFeatureEvents.Raise(trackedArea);
        }
    }

    private static void HandleKeyDown(Event currentEvent)
    {
        if (IsDrawing)
        {
            if (currentEvent.keyCode == KeyCode.Escape)
            {
                CancelDrawing();
                currentEvent.Use();
            }
            else if (drawMode == DrawMode.Outline && (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter))
            {
                TryCloseOutline();
                currentEvent.Use();
            }
            else if (drawMode == DrawMode.Outline && currentEvent.keyCode == KeyCode.Backspace)
            {
                if (pendingPointsXZ.Count > 0)
                {
                    pendingPointsXZ.RemoveAt(pendingPointsXZ.Count - 1);
                }

                currentEvent.Use();
            }

            return;
        }

        if (currentEvent.keyCode == KeyCode.Delete && selectedKnotIndex >= 0)
        {
            DeleteSelectedKnot();
            currentEvent.Use();
        }
    }

    private static void CreateArea(IReadOnlyList<Vector2> outlineXZ, TerrainAreaRecipe recipe, int seed)
    {
        Vector2 centroidXZ = Vector2.zero;
        for (int pointIndex = 0; pointIndex < outlineXZ.Count; pointIndex++)
        {
            centroidXZ += outlineXZ[pointIndex];
        }

        centroidXZ /= outlineXZ.Count;
        tileDataCache.Clear();
        Vector3 centroid = OnGround(centroidXZ);

        GameObject areaObject = new GameObject("Area - " + recipe.name, typeof(SplineContainer), typeof(TerrainArea));
        SceneManager.MoveGameObjectToScene(areaObject, SceneManager.GetActiveScene());
        areaObject.transform.position = centroid;

        SplineContainer container = areaObject.GetComponent<SplineContainer>();
        Spline spline = container.Spline;
        spline.Clear();
        for (int pointIndex = 0; pointIndex < outlineXZ.Count; pointIndex++)
        {
            Vector3 local = OnGround(outlineXZ[pointIndex]) - centroid;
            spline.Add(new float3(local.x, local.y, local.z), TangentMode.Linear);
        }

        spline.Closed = true;

        TerrainArea area = areaObject.GetComponent<TerrainArea>();
        area.recipe = recipe;
        area.seed = seed;

        Undo.RegisterCreatedObjectUndo(areaObject, "Create Area");
        EditorUtility.SetDirty(area);
        Selection.activeGameObject = areaObject;
        RefreshTrackedSelection();
        TerrainFeatureEvents.Raise(area);
        SceneView.RepaintAll();
    }

    private static void DrawAll(bool editingSelectedArea)
    {
        if (IsDrawing)
        {
            DrawPending();
        }
        else if (editingSelectedArea)
        {
            DrawSelectedArea();
        }

        DrawHud();
    }

    private static void DrawPending()
    {
        if (drawMode == DrawMode.Outline)
        {
            Vector3[] fixedPoints = new Vector3[pendingPointsXZ.Count];
            for (int pointIndex = 0; pointIndex < pendingPointsXZ.Count; pointIndex++)
            {
                fixedPoints[pointIndex] = OnGround(pendingPointsXZ[pointIndex]);
            }

            Handles.color = OutlineColour;
            if (fixedPoints.Length >= 2)
            {
                Handles.DrawAAPolyLine(4f, fixedPoints);
            }

            for (int pointIndex = 0; pointIndex < fixedPoints.Length; pointIndex++)
            {
                float size = HandleUtility.GetHandleSize(fixedPoints[pointIndex]) * (pointIndex == 0 ? 0.1f : 0.06f);
                Handles.color = pointIndex == 0 ? SelectionBlue : OutlineColour;
                Handles.DotHandleCap(0, fixedPoints[pointIndex], Quaternion.identity, size, EventType.Repaint);
            }

            if (mouseGroundValid && fixedPoints.Length >= 1)
            {
                Handles.color = RubberBandColour;
                Handles.DrawAAPolyLine(2f, fixedPoints[fixedPoints.Length - 1], mouseGround);
                if (fixedPoints.Length >= 2)
                {
                    Handles.DrawAAPolyLine(2f, mouseGround, fixedPoints[0]);
                }
            }

            if (mouseGroundValid)
            {
                List<Vector2> previewPoints = new List<Vector2>(pendingPointsXZ);
                previewPoints.Add(new Vector2(mouseGround.x, mouseGround.z));
                TerrainFeaturePreview.SetOutline(previewPoints, pendingRecipe, pendingSeed);
            }
        }
        else if (boxDragging)
        {
            List<Vector2> boxOutline = TerrainFeatureEditing.BoxOutline(boxCornerA, boxCornerB);
            Vector3[] boxLoop = new Vector3[boxOutline.Count + 1];
            for (int pointIndex = 0; pointIndex < boxOutline.Count; pointIndex++)
            {
                boxLoop[pointIndex] = OnGround(boxOutline[pointIndex]);
            }

            boxLoop[boxOutline.Count] = boxLoop[0];
            Handles.color = OutlineColour;
            Handles.DrawAAPolyLine(4f, boxLoop);
        }

        TerrainFeaturePreview.Draw();
    }

    private static void DrawSelectedArea()
    {
        if (knotWorldPositions.Count < 2)
        {
            return;
        }

        Vector3[] loop = new Vector3[knotWorldPositions.Count + 1];
        for (int knotIndex = 0; knotIndex < knotWorldPositions.Count; knotIndex++)
        {
            loop[knotIndex] = knotWorldPositions[knotIndex];
        }

        loop[knotWorldPositions.Count] = knotWorldPositions[0];
        Handles.color = OutlineColour;
        Handles.DrawAAPolyLine(4f, loop);

        for (int knotIndex = 0; knotIndex < knotWorldPositions.Count; knotIndex++)
        {
            Vector3 position = knotWorldPositions[knotIndex];
            bool isSelectedKnot = knotIndex == selectedKnotIndex;
            bool isHoveredKnot = knotIndex == hoverKnotIndex;
            Handles.color = isSelectedKnot ? SelectionBlue : OutlineColour;
            float size = HandleUtility.GetHandleSize(position) * (isSelectedKnot ? 0.12f : (isHoveredKnot ? 0.1f : 0.06f));
            Handles.DotHandleCap(0, position, Quaternion.identity, size, EventType.Repaint);
        }

        if (knotDragging)
        {
            TerrainFeaturePreview.Draw();
        }
    }

    private static void DrawHud()
    {
        string line = null;
        if (hudMessage != null && EditorApplication.timeSinceStartup < hudMessageExpiry)
        {
            line = hudMessage;
        }
        else if (drawMode == DrawMode.Outline)
        {
            line = "Click to add points. Enter or click the first point to close. Backspace removes the last point. Esc cancels.";
        }
        else if (drawMode == DrawMode.Box)
        {
            line = "Drag to draw a box. Esc cancels.";
        }

        if (line == null)
        {
            return;
        }

        Handles.BeginGUI();
        GUI.Label(new Rect(12f, 12f, 900f, 22f), line, EditorStyles.boldLabel);
        Handles.EndGUI();
    }
}
#endif
