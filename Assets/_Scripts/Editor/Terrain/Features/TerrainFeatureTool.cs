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
    private enum DrawMode { None, Outline, Box, Circle }

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
    private const string GapHint = "Alt+click a knot to toggle a gap (skips the segment after it).";
    private const int MinimumOpenPointCount = 2;
    private const float MinimumCircleRadiusMetres = 1f;
    private const float PreviewSegmentMetres = 2f;

    private static readonly Color OutlineColour = new Color(0.95f, 0.95f, 0.9f, 1f);
    private static readonly Color RubberBandColour = new Color(1f, 1f, 1f, 0.6f);
    private static readonly Color SelectionBlue = new Color(0.24f, 0.55f, 1f, 1f);
    private static readonly Color GapColour = new Color(1f, 0.25f, 0.2f, 1f);

    private static DrawMode drawMode = DrawMode.None;
    private static TerrainFeatureRecipe pendingRecipe;
    private static int pendingSeed;
    private static readonly List<Vector2> pendingPointsXZ = new List<Vector2>();
    private static readonly List<bool> pendingPointIsSharp = new List<bool>();
    private const int PreviewSamplesPerSegment = 8;
    private static Vector2 boxCornerA;
    private static Vector2 boxCornerB;
    private static bool boxDragging;
    private static Vector2 circleCentre;
    private static float circleRadius;
    private static bool circleDragging;
    private static TerrainFeatureShapeKind drawShape = TerrainFeatureShapeKind.Curve;

    private static bool mouseGroundValid;
    private static Vector3 mouseGround;

    private static Component trackedFeature;
    private static int selectedKnotIndex = -1;
    private static bool knotDragging;
    // GetControlID is order-dependent, so the id seen at mouse-down is what the drag is compared against.
    private static int dragControlId;
    private static int hoverKnotIndex = -1;

    private static bool wroteDrawingInstructions;
    private static SceneView lastSceneView;

    private static readonly Dictionary<Vector2Int, TerrainTileData> tileDataCache = new Dictionary<Vector2Int, TerrainTileData>();
    private static readonly List<Vector3> knotWorldPositions = new List<Vector3>();
    private static readonly List<Vector2> knotScreenPositions = new List<Vector2>();

    public static event Action SelectionChanged;
    public static event Action DrawStateChanged;

    static TerrainFeatureTool()
    {
        SceneView.duringSceneGui += OnSceneGui;
        Selection.selectionChanged += RefreshTrackedSelection;
    }

    public static Component SelectedFeature
    {
        get
        {
            RefreshTrackedSelection();
            return trackedFeature;
        }
    }

    public static void SyncWithCurrentSelection()
    {
        RefreshTrackedSelection();
    }

    public static bool IsDrawing => drawMode != DrawMode.None;

    public static TerrainFeatureShapeKind DrawShape => drawShape;

    public static TerrainFeatureRecipe DrawRecipe => IsDrawing ? pendingRecipe : null;

    public static void BeginDraw(TerrainFeatureRecipe recipe, TerrainFeatureShapeKind shape)
    {
        if (recipe == null)
        {
            return;
        }

        bool closedUnused;
        TerrainFeatureShapeKind chosenShape = shape;
        if (!TerrainFeatureShapes.IsAllowed(chosenShape, recipe.supportedShapes, out closedUnused))
        {
            chosenShape = recipe.defaultShape;
            if (!TerrainFeatureShapes.IsAllowed(chosenShape, recipe.supportedShapes, out closedUnused))
            {
                ShowHud(recipe.name + " has no drawable shape");
                return;
            }
        }

        StartDrawing(recipe, chosenShape);
    }

    public static void SetDrawShape(TerrainFeatureShapeKind shape)
    {
        TerrainFeatureRecipe featureRecipe = DrawRecipe;
        bool closedUnused;
        if (featureRecipe == null || !TerrainFeatureShapes.IsAllowed(shape, featureRecipe.supportedShapes, out closedUnused))
        {
            if (IsDrawing)
            {
                ShowHud(shape + " is not available for this recipe");
            }

            return;
        }

        if (shape == drawShape)
        {
            return;
        }

        ResetPendingShapeState();
        drawShape = shape;
        drawMode = DrawModeForShape(shape);
        PublishDrawingHint();
        DrawStateChanged?.Invoke();
        SceneView.RepaintAll();
    }

    private static void StartDrawing(TerrainFeatureRecipe recipe, TerrainFeatureShapeKind shape)
    {
        CancelDrawing();
        drawShape = shape;
        drawMode = DrawModeForShape(shape);
        pendingRecipe = recipe;
        pendingSeed = new System.Random().Next();
        tileDataCache.Clear();
        PublishDrawingHint();
        DrawStateChanged?.Invoke();
        SceneView.RepaintAll();
    }

    private static DrawMode DrawModeForShape(TerrainFeatureShapeKind shape)
    {
        if (shape == TerrainFeatureShapeKind.Box)
        {
            return DrawMode.Box;
        }

        return shape == TerrainFeatureShapeKind.Circle ? DrawMode.Circle : DrawMode.Outline;
    }

    private static void ResetPendingShapeState()
    {
        pendingPointsXZ.Clear();
        pendingPointIsSharp.Clear();
        if (boxDragging || circleDragging)
        {
            GUIUtility.hotControl = 0;
        }

        boxDragging = false;
        circleDragging = false;
        TerrainFeaturePreview.Clear();
    }

    private static bool PendingShapeIsClosed()
    {
        TerrainFeatureRecipe featureRecipe = pendingRecipe;
        bool closed;
        TerrainFeatureShapes.IsAllowed(drawShape, featureRecipe.supportedShapes, out closed);
        return closed;
    }

    private static bool PendingShapeCreatesArea()
    {
        TerrainFeatureRecipe featureRecipe = pendingRecipe;
        return PendingShapeIsClosed() && (featureRecipe.supportedShapes & TerrainFeatureShapeSupport.ClosedArea) != 0;
    }

    private static void PublishDrawingHint()
    {
        string hint;
        if (drawMode == DrawMode.Box)
        {
            hint = "Box: drag corner to corner. 1-5 shape, Esc cancels.";
        }
        else if (drawMode == DrawMode.Circle)
        {
            hint = "Circle: press at the centre, drag for radius. 1-5 shape, Esc cancels.";
        }
        else if (PendingShapeIsClosed())
        {
            hint = drawShape + ": click points; Enter or click the first point to close. 1-5 shape, Esc cancels.";
        }
        else
        {
            hint = drawShape + ": click points; Enter or double-click to finish. 1-5 shape, Esc cancels.";
        }

        TerrainSceneHud.SetCursorInfo(hint);
        wroteDrawingInstructions = true;
    }

    private static void UpdateSelectionHint()
    {
        if (IsDrawing)
        {
            return;
        }

        if (trackedFeature is TerrainPath)
        {
            TerrainSceneHud.SetCursorInfo(GapHint);
            wroteDrawingInstructions = true;
        }
        else if (wroteDrawingInstructions)
        {
            TerrainSceneHud.SetCursorInfo(string.Empty);
            wroteDrawingInstructions = false;
        }
    }

    private static SplineContainer ContainerOf(Component feature)
    {
        TerrainArea area = feature as TerrainArea;
        if (area != null)
        {
            return area.Container;
        }

        TerrainPath path = feature as TerrainPath;
        return path != null ? path.Container : null;
    }

    private static Bounds WorldBoundsOf(Component feature)
    {
        TerrainArea area = feature as TerrainArea;
        return area != null ? area.WorldBounds() : ((TerrainPath)feature).WorldBounds();
    }

    private static bool TrackedFeatureIsClosed()
    {
        SplineContainer container = ContainerOf(trackedFeature);
        return container == null || trackedFeature is TerrainArea || container.Spline.Closed;
    }

    private static int MinimumKnotCountOfTrackedFeature()
    {
        return TrackedFeatureIsClosed() ? MinimumOutlinePointCount : MinimumOpenPointCount;
    }

    public static void CancelDrawing()
    {
        bool wasDrawing = IsDrawing;
        drawMode = DrawMode.None;
        pendingRecipe = null;
        ResetPendingShapeState();
        UpdateSelectionHint();
        if (wasDrawing)
        {
            DrawStateChanged?.Invoke();
        }

        SceneView.RepaintAll();
    }

    public static void FrameSelected()
    {
        SceneView view = lastSceneView != null ? lastSceneView : SceneView.lastActiveSceneView;
        if (trackedFeature == null || view == null)
        {
            return;
        }

        view.Frame(WorldBoundsOf(trackedFeature), false);
    }

    public static void DeleteSelectedKnot()
    {
        if (trackedFeature == null || selectedKnotIndex < 0)
        {
            return;
        }

        SplineContainer container = ContainerOf(trackedFeature);
        Spline spline = container != null ? container.Spline : null;
        if (spline == null || selectedKnotIndex >= spline.Count || spline.Count <= MinimumKnotCountOfTrackedFeature())
        {
            return;
        }

        TerrainPath trackedPath = trackedFeature as TerrainPath;
        if (trackedPath != null)
        {
            Undo.RecordObject(trackedPath, "Delete Path Knot");
            ShiftGapIndicesAfterKnotRemoval(trackedPath, selectedKnotIndex);
        }

        Undo.RecordObject(container, "Delete Feature Knot");
        spline.RemoveAt(selectedKnotIndex);
        selectedKnotIndex = -1;
        EditorUtility.SetDirty(container);
        TerrainFeatureEvents.Raise(trackedFeature);
        SelectionChanged?.Invoke();
        SceneView.RepaintAll();
    }

    private static void ShiftGapIndicesAfterKnotRemoval(TerrainPath path, int removedKnotIndex)
    {
        for (int gapIndex = path.gapKnotIndices.Count - 1; gapIndex >= 0; gapIndex--)
        {
            if (path.gapKnotIndices[gapIndex] == removedKnotIndex)
            {
                path.gapKnotIndices.RemoveAt(gapIndex);
            }
            else if (path.gapKnotIndices[gapIndex] > removedKnotIndex)
            {
                path.gapKnotIndices[gapIndex]--;
            }
        }

        EditorUtility.SetDirty(path);
    }

    private static void ShiftGapIndicesAfterKnotInsert(TerrainPath path, int insertIndex)
    {
        bool splitSegmentStartIsGap = path.gapKnotIndices.Contains(insertIndex - 1);
        for (int gapIndex = 0; gapIndex < path.gapKnotIndices.Count; gapIndex++)
        {
            if (path.gapKnotIndices[gapIndex] >= insertIndex)
            {
                path.gapKnotIndices[gapIndex]++;
            }
        }

        if (splitSegmentStartIsGap && !path.gapKnotIndices.Contains(insertIndex))
        {
            path.gapKnotIndices.Add(insertIndex);
        }

        path.gapKnotIndices.Sort();
        EditorUtility.SetDirty(path);
    }

    private static void RefreshTrackedSelection()
    {
        Component selectedFeature = null;
        if (Selection.activeGameObject != null)
        {
            selectedFeature = Selection.activeGameObject.GetComponent<TerrainArea>();
            if (selectedFeature == null)
            {
                selectedFeature = Selection.activeGameObject.GetComponent<TerrainPath>();
            }
        }

        if (selectedFeature == trackedFeature)
        {
            return;
        }

        trackedFeature = selectedFeature;
        UpdateSelectionHint();
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
        TerrainSceneHud.ShowToast(message, HudMessageSeconds);
    }

    private static void OnSceneGui(SceneView sceneView)
    {
        if (!TerrainEditorVisibility.ToolsLive)
        {
            return;
        }

        bool editingSelectedArea = drawMode == DrawMode.None && trackedFeature != null;
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
                if (currentEvent.button == 0)
                {
                    UpdateMouseGround(currentEvent.mousePosition);
                    if (IsDrawing)
                    {
                        if (!currentEvent.alt)
                        {
                            HandleDrawMouseDown(currentEvent, controlId);
                        }
                    }
                    else if (!currentEvent.alt || trackedFeature is TerrainPath)
                    {
                        HandleEditMouseDown(currentEvent, controlId);
                    }
                }

                break;
            case EventType.MouseDrag:
                UpdateMouseGround(currentEvent.mousePosition);
                if (GUIUtility.hotControl == dragControlId && (knotDragging || boxDragging || circleDragging))
                {
                    HandleDrag();
                    currentEvent.Use();
                }

                sceneView.Repaint();
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == dragControlId && (knotDragging || boxDragging || circleDragging))
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
        SplineContainer container = ContainerOf(trackedFeature);
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

        if (drawMode == DrawMode.Circle)
        {
            circleCentre = new Vector2(mouseGround.x, mouseGround.z);
            circleRadius = 0f;
            circleDragging = true;
            GUIUtility.hotControl = controlId;
            dragControlId = controlId;
            currentEvent.Use();
            return;
        }

        bool closedShape = PendingShapeIsClosed();
        if (!closedShape && currentEvent.clickCount == 2)
        {
            TryFinishClickShape();
            currentEvent.Use();
            return;
        }

        if (closedShape && pendingPointsXZ.Count >= 1)
        {
            Vector2 firstScreen = HandleUtility.WorldToGUIPoint(OnGround(pendingPointsXZ[0]));
            if (Vector2.Distance(firstScreen, currentEvent.mousePosition) <= CloseOutlinePixels)
            {
                TryFinishClickShape();
                currentEvent.Use();
                return;
            }
        }

        pendingPointsXZ.Add(new Vector2(mouseGround.x, mouseGround.z));
        pendingPointIsSharp.Add(ClickPointIsSharp(currentEvent.shift));
        currentEvent.Use();
    }

    private static bool ClickPointIsSharp(bool shiftHeld)
    {
        return TerrainFeatureShapes.KnotsAreSharp(drawShape) || (drawShape == TerrainFeatureShapeKind.Curve && shiftHeld);
    }

    private static void TryFinishClickShape()
    {
        bool closedShape = PendingShapeIsClosed();
        int minimumPointCount = closedShape ? MinimumOutlinePointCount : MinimumOpenPointCount;
        if (pendingPointsXZ.Count < minimumPointCount)
        {
            ShowHud("Add at least " + minimumPointCount + " points");
            return;
        }

        if (PendingShapeCreatesArea() && TerrainFeatureEditing.IsSelfIntersecting(pendingPointsXZ))
        {
            ShowHud("Outline crosses itself — move or remove a point");
            return;
        }

        FinishShape(new List<Vector2>(pendingPointsXZ), new List<bool>(pendingPointIsSharp));
    }

    private static void FinishShape(List<Vector2> finishedKnotsXZ, List<bool> finishedSharpFlags)
    {
        TerrainAreaRecipe recipe = pendingRecipe;
        int seed = pendingSeed;
        bool closed = PendingShapeIsClosed();
        bool createsArea = PendingShapeCreatesArea();
        CancelDrawing();
        if (createsArea)
        {
            CreateArea(finishedKnotsXZ, recipe, seed, finishedSharpFlags);
        }
        else
        {
            CreatePath(finishedKnotsXZ, (TerrainFeatureRecipe)recipe, seed, finishedSharpFlags, closed);
        }
    }

    private static void HandleEditMouseDown(Event currentEvent, int controlId)
    {
        Vector2 mouse = currentEvent.mousePosition;
        int knotIndex = NearestKnotIndex(mouse);
        TerrainPath trackedPath = trackedFeature as TerrainPath;
        if (currentEvent.alt)
        {
            if (trackedPath != null && knotIndex >= 0)
            {
                ToggleGapKnot(trackedPath, knotIndex);
                currentEvent.Use();
            }

            return;
        }

        if (knotIndex >= 0)
        {
            selectedKnotIndex = knotIndex;
            BeginKnotDrag(controlId, "Move Feature Knot");
            SelectionChanged?.Invoke();
            currentEvent.Use();
            return;
        }

        if (!currentEvent.control || !mouseGroundValid || knotWorldPositions.Count < MinimumKnotCountOfTrackedFeature())
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

        SplineContainer container = ContainerOf(trackedFeature);
        if (!container.Spline.Closed && segmentIndex >= container.Spline.Count - 1)
        {
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.RecordObject(container, "Insert Feature Knot");
        Vector3 localPoint = container.transform.InverseTransformPoint(OnGround(closestPoint));
        int insertIndex = Mathf.Clamp(segmentIndex + 1, 1, container.Spline.Count);
        TerrainPath insertedIntoPath = trackedFeature as TerrainPath;
        if (insertedIntoPath != null)
        {
            Undo.RecordObject(insertedIntoPath, "Insert Feature Knot");
            ShiftGapIndicesAfterKnotInsert(insertedIntoPath, insertIndex);
        }

        container.Spline.Insert(insertIndex, (float3)localPoint, TangentMode.AutoSmooth);
        selectedKnotIndex = insertIndex;
        EditorUtility.SetDirty(container);
        TerrainFeatureEvents.Raise(trackedFeature);
        SelectionChanged?.Invoke();
        currentEvent.Use();
    }

    private static void ToggleGapKnot(TerrainPath path, int knotIndex)
    {
        Undo.RecordObject(path, "Toggle Path Gap");
        if (!path.gapKnotIndices.Remove(knotIndex))
        {
            path.gapKnotIndices.Add(knotIndex);
        }

        EditorUtility.SetDirty(path);
        TerrainFeatureEvents.Raise(path);
        SceneView.RepaintAll();
    }

    private static void BeginKnotDrag(int controlId, string undoName)
    {
        tileDataCache.Clear();
        Undo.IncrementCurrentGroup();
        Undo.RecordObject(ContainerOf(trackedFeature), undoName);
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
                if (PendingShapeCreatesArea())
                {
                    TerrainFeaturePreview.SetOutline(TerrainFeatureShapes.BoxKnots(boxCornerA, boxCornerB), pendingRecipe, pendingSeed);
                }
            }

            return;
        }

        if (circleDragging)
        {
            if (mouseGroundValid)
            {
                circleRadius = Vector2.Distance(circleCentre, new Vector2(mouseGround.x, mouseGround.z));
                if (PendingShapeCreatesArea() && circleRadius >= MinimumCircleRadiusMetres)
                {
                    TerrainFeaturePreview.SetOutline(SmoothedOutlineXZ(TerrainFeatureShapes.CircleKnots(circleCentre, circleRadius), null, true), pendingRecipe, pendingSeed);
                }
            }

            return;
        }

        if (trackedFeature == null || !mouseGroundValid)
        {
            return;
        }

        SplineContainer container = ContainerOf(trackedFeature);
        Spline spline = container.Spline;
        if (selectedKnotIndex < 0 || selectedKnotIndex >= spline.Count)
        {
            return;
        }

        BezierKnot knot = spline[selectedKnotIndex];
        TangentMode existingTangentMode = spline.GetTangentMode(selectedKnotIndex);
        knot.Position = (float3)container.transform.InverseTransformPoint(mouseGround);
        spline.SetKnot(selectedKnotIndex, knot);
        spline.SetTangentMode(selectedKnotIndex, existingTangentMode);
        EditorUtility.SetDirty(container);
        TerrainArea trackedArea = trackedFeature as TerrainArea;
        if (trackedArea != null)
        {
            TerrainFeaturePreview.SetOutline(trackedArea.OutlineWorldXZ(PreviewSegmentMetres), trackedArea.recipe, trackedArea.seed, trackedArea.densityMultiplier);
        }
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

            List<Vector2> boxKnots = TerrainFeatureShapes.BoxKnots(boxCornerA, boxCornerB);
            FinishShape(boxKnots, FilledFlags(boxKnots.Count, true));
            return;
        }

        if (circleDragging)
        {
            circleDragging = false;
            if (circleRadius < MinimumCircleRadiusMetres)
            {
                TerrainFeaturePreview.Clear();
                return;
            }

            List<Vector2> circleKnots = TerrainFeatureShapes.CircleKnots(circleCentre, circleRadius);
            FinishShape(circleKnots, FilledFlags(circleKnots.Count, false));
            return;
        }

        knotDragging = false;
        TerrainFeaturePreview.Clear();
        if (trackedFeature != null)
        {
            TerrainFeatureEvents.Raise(trackedFeature);
        }
    }

    private static List<bool> FilledFlags(int count, bool value)
    {
        List<bool> flags = new List<bool>(count);
        for (int flagIndex = 0; flagIndex < count; flagIndex++)
        {
            flags.Add(value);
        }

        return flags;
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
                TryFinishClickShape();
                currentEvent.Use();
            }
            else if (currentEvent.keyCode >= KeyCode.Alpha1 && currentEvent.keyCode <= KeyCode.Alpha5 && DrawRecipe != null)
            {
                SetDrawShape((TerrainFeatureShapeKind)(currentEvent.keyCode - KeyCode.Alpha1));
                currentEvent.Use();
            }
            else if (drawMode == DrawMode.Outline && currentEvent.keyCode == KeyCode.Backspace)
            {
                if (pendingPointsXZ.Count > 0)
                {
                    pendingPointsXZ.RemoveAt(pendingPointsXZ.Count - 1);
                    pendingPointIsSharp.RemoveAt(pendingPointIsSharp.Count - 1);
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

    private static List<Vector2> SmoothedOutlineXZ(IReadOnlyList<Vector2> pointsXZ, IReadOnlyList<bool> sharpFlags, bool closed)
    {
        List<Vector2> sampled = new List<Vector2>();
        if (pointsXZ.Count < (closed ? 3 : 2))
        {
            sampled.AddRange(pointsXZ);
            return sampled;
        }

        Spline temporarySpline = new Spline();
        for (int pointIndex = 0; pointIndex < pointsXZ.Count; pointIndex++)
        {
            bool isSharp = sharpFlags != null && pointIndex < sharpFlags.Count && sharpFlags[pointIndex];
            temporarySpline.Add(new float3(pointsXZ[pointIndex].x, 0f, pointsXZ[pointIndex].y), isSharp ? TangentMode.Linear : TangentMode.AutoSmooth);
        }

        temporarySpline.Closed = closed;
        int segmentCount = closed ? pointsXZ.Count : pointsXZ.Count - 1;
        int totalSamples = segmentCount * PreviewSamplesPerSegment;
        int sampleCount = closed ? totalSamples : totalSamples + 1;
        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            float3 position = SplineUtility.EvaluatePosition(temporarySpline, sampleIndex / (float)totalSamples);
            sampled.Add(new Vector2(position.x, position.z));
        }

        return sampled;
    }

    private static void CreateArea(IReadOnlyList<Vector2> outlineXZ, TerrainAreaRecipe recipe, int seed, IReadOnlyList<bool> sharpFlags = null)
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
            bool isSharp = sharpFlags == null || (pointIndex < sharpFlags.Count && sharpFlags[pointIndex]);
            spline.Add(new float3(local.x, local.y, local.z), isSharp ? TangentMode.Linear : TangentMode.AutoSmooth);
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

    private static void CreatePath(IReadOnlyList<Vector2> knotsXZ, TerrainFeatureRecipe recipe, int seed, IReadOnlyList<bool> sharpFlags, bool closed)
    {
        Vector2 centroidXZ = Vector2.zero;
        for (int pointIndex = 0; pointIndex < knotsXZ.Count; pointIndex++)
        {
            centroidXZ += knotsXZ[pointIndex];
        }

        centroidXZ /= knotsXZ.Count;
        tileDataCache.Clear();
        Vector3 centroid = OnGround(centroidXZ);

        GameObject pathObject = new GameObject("Path - " + recipe.name, typeof(SplineContainer), typeof(TerrainPath));
        SceneManager.MoveGameObjectToScene(pathObject, SceneManager.GetActiveScene());
        pathObject.transform.position = centroid;

        SplineContainer container = pathObject.GetComponent<SplineContainer>();
        Spline spline = container.Spline;
        spline.Clear();
        for (int pointIndex = 0; pointIndex < knotsXZ.Count; pointIndex++)
        {
            Vector3 local = OnGround(knotsXZ[pointIndex]) - centroid;
            bool isSharp = pointIndex < sharpFlags.Count && sharpFlags[pointIndex];
            spline.Add(new float3(local.x, local.y, local.z), isSharp ? TangentMode.Linear : TangentMode.AutoSmooth);
        }

        spline.Closed = closed;

        TerrainPath path = pathObject.GetComponent<TerrainPath>();
        path.recipe = recipe;
        path.seed = seed;
        path.closedLoop = closed;

        Undo.RegisterCreatedObjectUndo(pathObject, "Create Path");
        EditorUtility.SetDirty(path);
        Selection.activeGameObject = pathObject;
        RefreshTrackedSelection();
        TerrainFeatureEvents.Raise(path);
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
    }

    private static void DrawPending()
    {
        if (drawMode == DrawMode.Outline)
        {
            bool closedShape = PendingShapeIsClosed();
            Vector3[] fixedPoints = new Vector3[pendingPointsXZ.Count];
            for (int pointIndex = 0; pointIndex < pendingPointsXZ.Count; pointIndex++)
            {
                fixedPoints[pointIndex] = OnGround(pendingPointsXZ[pointIndex]);
            }

            List<Vector2> smoothedPreviewPoints = null;
            if (mouseGroundValid)
            {
                List<Vector2> previewControlPoints = new List<Vector2>(pendingPointsXZ);
                List<bool> previewSharpFlags = new List<bool>(pendingPointIsSharp);
                previewControlPoints.Add(new Vector2(mouseGround.x, mouseGround.z));
                previewSharpFlags.Add(ClickPointIsSharp(Event.current != null && Event.current.shift));
                smoothedPreviewPoints = SmoothedOutlineXZ(previewControlPoints, previewSharpFlags, closedShape);
            }

            Handles.color = OutlineColour;
            if (smoothedPreviewPoints != null && smoothedPreviewPoints.Count >= 3)
            {
                int loopLength = smoothedPreviewPoints.Count + (closedShape ? 1 : 0);
                Vector3[] smoothedLoop = new Vector3[loopLength];
                for (int sampleIndex = 0; sampleIndex < smoothedPreviewPoints.Count; sampleIndex++)
                {
                    smoothedLoop[sampleIndex] = OnGround(smoothedPreviewPoints[sampleIndex]);
                }

                if (closedShape)
                {
                    smoothedLoop[smoothedPreviewPoints.Count] = smoothedLoop[0];
                }

                Handles.DrawAAPolyLine(4f, smoothedLoop);
            }
            else if (fixedPoints.Length >= 2)
            {
                Handles.DrawAAPolyLine(4f, fixedPoints);
            }

            for (int pointIndex = 0; pointIndex < fixedPoints.Length; pointIndex++)
            {
                float size = HandleUtility.GetHandleSize(fixedPoints[pointIndex]) * (pointIndex == 0 ? 0.1f : 0.06f);
                Handles.color = pointIndex == 0 ? SelectionBlue : OutlineColour;
                Handles.DotHandleCap(0, fixedPoints[pointIndex], Quaternion.identity, size, EventType.Repaint);
            }

            if (smoothedPreviewPoints != null && PendingShapeCreatesArea())
            {
                TerrainFeaturePreview.SetOutline(smoothedPreviewPoints, pendingRecipe, pendingSeed, 1f);
            }
        }
        else if (circleDragging && circleRadius >= MinimumCircleRadiusMetres)
        {
            List<Vector2> circleOutline = SmoothedOutlineXZ(TerrainFeatureShapes.CircleKnots(circleCentre, circleRadius), null, true);
            Vector3[] circleLoop = new Vector3[circleOutline.Count + 1];
            for (int pointIndex = 0; pointIndex < circleOutline.Count; pointIndex++)
            {
                circleLoop[pointIndex] = OnGround(circleOutline[pointIndex]);
            }

            circleLoop[circleOutline.Count] = circleLoop[0];
            Handles.color = OutlineColour;
            Handles.DrawAAPolyLine(4f, circleLoop);
        }
        else if (boxDragging)
        {
            List<Vector2> boxOutline = TerrainFeatureShapes.BoxKnots(boxCornerA, boxCornerB);
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

        TerrainPath selectedPath = trackedFeature as TerrainPath;
        bool closedOutline = TrackedFeatureIsClosed();
        int segmentCount = closedOutline ? knotWorldPositions.Count : knotWorldPositions.Count - 1;
        for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            bool isGapSegment = selectedPath != null && selectedPath.gapKnotIndices.Contains(segmentIndex);
            Vector3 segmentStart = knotWorldPositions[segmentIndex];
            Vector3 segmentEnd = knotWorldPositions[(segmentIndex + 1) % knotWorldPositions.Count];
            if (isGapSegment)
            {
                Handles.color = GapColour;
                Handles.DrawDottedLine(segmentStart, segmentEnd, 6f);
            }
            else
            {
                Handles.color = OutlineColour;
                Handles.DrawAAPolyLine(4f, segmentStart, segmentEnd);
            }
        }

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
}
#endif
