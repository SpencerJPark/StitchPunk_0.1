#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

[InitializeOnLoad]
public static class TerrainRoadTool
{
    private enum DragKind { None, Knot, Width }

    private const float KnotPickPixels = 10f;
    private const float LinePickPixels = 8f;
    private const float SampleStepMetres = 1f;
    private const float OtherRoadSampleStepMetres = 4f;
    private const int MaximumSamples = 600;
    private const double RoadListRefreshSeconds = 0.5;
    private const float NewRoadLengthMetres = 24f;
    private const float MinimumWidthMetres = 1f;
    private const float MaximumWidthMetres = 24f;

    private static readonly Color OtherRoadColour = new Color(1f, 1f, 1f, 0.35f);
    private static readonly Color OtherRoadHoverColour = new Color(1f, 1f, 1f, 0.8f);
    private static readonly Color SelectedLineColour = new Color(0.95f, 0.95f, 0.9f, 1f);
    private static readonly Color SelectionBlue = new Color(0.24f, 0.55f, 1f, 1f);
    private static readonly Color EdgeLineColour = new Color(0.45f, 0.9f, 0.85f, 0.9f);
    private static readonly Color WidthHandleColour = new Color(1f, 0.75f, 0.15f, 1f);
    private static readonly Color GhostColour = new Color(1f, 1f, 1f, 0.7f);

    private static bool isActive;
    private static TerrainRoad selectedRoad;
    private static int selectedKnotIndex = -1;
    private static SceneView lastSceneView;

    private static TerrainRoad[] cachedRoads = new TerrainRoad[0];
    private static double roadListRefreshTime = -1000.0;

    private static DragKind dragKind = DragKind.None;
    private static int hoverKnotIndex = -1;
    private static bool hoverLineValid;
    private static Vector3 hoverLineWorldPoint;
    private static TerrainRoad hoverOtherRoad;

    private static readonly List<Vector3> selectedSamples = new List<Vector3>();
    private static readonly List<Vector3> scratchSamples = new List<Vector3>();
    private static readonly List<Vector2> scratchScreen = new List<Vector2>();
    private static readonly List<Vector3> leftEdge = new List<Vector3>();
    private static readonly List<Vector3> rightEdge = new List<Vector3>();
    private static readonly List<Vector3> knotWorldPositions = new List<Vector3>();
    private static readonly List<Vector2> knotScreenPositions = new List<Vector2>();

    public static event Action SelectionChanged;

    static TerrainRoadTool()
    {
        SceneView.duringSceneGui += OnSceneGui;
    }

    public static bool IsActive
    {
        get => isActive;
        set
        {
            if (isActive == value)
            {
                return;
            }

            isActive = value;
            if (isActive)
            {
                BrushState.activeTool = TerrainBrushTool.None;
            }
            else
            {
                dragKind = DragKind.None;
                hoverKnotIndex = -1;
                hoverLineValid = false;
                hoverOtherRoad = null;
            }

            SceneView.RepaintAll();
        }
    }

    public static TerrainRoad SelectedRoad
    {
        get => selectedRoad;
        set
        {
            selectedRoad = value;
            selectedKnotIndex = -1;
            dragKind = DragKind.None;
            SelectionChanged?.Invoke();
            SceneView.RepaintAll();
        }
    }

    public static int SelectedKnotIndex => selectedKnotIndex;

    public static void DeleteSelectedKnot()
    {
        if (selectedRoad == null || selectedKnotIndex < 0)
        {
            return;
        }

        Spline spline = selectedRoad.Container.Spline;
        if (spline == null || selectedKnotIndex >= spline.Count || !TerrainRoadEditing.CanDeleteKnot(spline.Count, spline.Closed))
        {
            return;
        }

        Undo.RecordObject(selectedRoad.Container, "Delete Road Knot");
        spline.RemoveAt(selectedKnotIndex);
        selectedKnotIndex = -1;
        EditorUtility.SetDirty(selectedRoad.Container);
        SelectionChanged?.Invoke();
        SceneView.RepaintAll();
    }

    public static void FrameSelected()
    {
        SceneView view = lastSceneView != null ? lastSceneView : SceneView.lastActiveSceneView;
        if (selectedRoad == null || view == null)
        {
            return;
        }

        view.Frame(selectedRoad.WorldBounds(), false);
    }

    public static TerrainRoad CreateRoadInFrontOfSceneView()
    {
        SceneView view = lastSceneView != null ? lastSceneView : SceneView.lastActiveSceneView;
        Vector3 centre = view != null ? view.pivot : Vector3.zero;
        Vector3 rightFlat = Vector3.right;
        if (view != null && view.camera != null)
        {
            Transform cameraTransform = view.camera.transform;
            Vector3 flattened = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up);
            if (flattened.sqrMagnitude > 0.0001f)
            {
                rightFlat = flattened.normalized;
            }

            Ray centreRay = view.camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Vector3 groundPoint;
            if (TerrainBrushOverlay.TryRaycastGround(centreRay, out groundPoint))
            {
                centre = groundPoint;
            }
        }

        Vector3 startPoint = SnapToGroundOrKeep(centre - rightFlat * (NewRoadLengthMetres * 0.5f));
        Vector3 endPoint = SnapToGroundOrKeep(centre + rightFlat * (NewRoadLengthMetres * 0.5f));

        GameObject roadObject = new GameObject("Road", typeof(TerrainRoad));
        SceneManager.MoveGameObjectToScene(roadObject, SceneManager.GetActiveScene());
        roadObject.transform.position = Vector3.zero;
        TerrainRoad road = roadObject.GetComponent<TerrainRoad>();
        Spline spline = road.Container.Spline;
        if (spline != null)
        {
            spline.Clear();
            spline.Add(new float3(startPoint.x, startPoint.y, startPoint.z), TangentMode.AutoSmooth);
            spline.Add(new float3(endPoint.x, endPoint.y, endPoint.z), TangentMode.AutoSmooth);
        }

        string[] profileGuids = AssetDatabase.FindAssets("t:TerrainRoadProfile");
        if (profileGuids.Length > 0)
        {
            road.profile = AssetDatabase.LoadAssetAtPath<TerrainRoadProfile>(AssetDatabase.GUIDToAssetPath(profileGuids[0]));
        }

        Undo.RegisterCreatedObjectUndo(roadObject, "Create Road");
        cachedRoads = FindRoads();
        roadListRefreshTime = EditorApplication.timeSinceStartup;
        SelectedRoad = road;
        IsActive = true;
        EditorUtility.SetDirty(road);
        return road;
    }

    private static Vector3 SnapToGroundOrKeep(Vector3 point)
    {
        Ray downRay = new Ray(new Vector3(point.x, point.y + 5000f, point.z), Vector3.down);
        Vector3 groundPoint;
        return TerrainBrushOverlay.TryRaycastGround(downRay, out groundPoint) ? groundPoint : point;
    }

    private static TerrainRoad[] FindRoads()
    {
        return UnityEngine.Object.FindObjectsByType<TerrainRoad>(FindObjectsSortMode.None);
    }

    private static void RefreshRoadListIfStale()
    {
        if (EditorApplication.timeSinceStartup - roadListRefreshTime < RoadListRefreshSeconds)
        {
            return;
        }

        roadListRefreshTime = EditorApplication.timeSinceStartup;
        cachedRoads = FindRoads();
    }

    private static void SampleRoad(TerrainRoad road, float stepMetres, List<Vector3> output)
    {
        output.Clear();
        SplineContainer container = road.Container;
        Spline spline = container != null ? container.Spline : null;
        if (spline == null || spline.Count < 2)
        {
            return;
        }

        float chordLength = 0f;
        for (int knotIndex = 1; knotIndex < spline.Count; knotIndex++)
        {
            chordLength += Vector3.Distance(
                container.transform.TransformPoint((Vector3)(float3)spline[knotIndex - 1].Position),
                container.transform.TransformPoint((Vector3)(float3)spline[knotIndex].Position));
        }

        int segmentCount = Mathf.Clamp(Mathf.CeilToInt(chordLength * 1.2f / stepMetres), spline.Count * 2, MaximumSamples);
        for (int step = 0; step <= segmentCount; step++)
        {
            output.Add((Vector3)container.EvaluatePosition(0, step / (float)segmentCount));
        }
    }

    private static void ToScreen(List<Vector3> worldPoints, List<Vector2> output)
    {
        output.Clear();
        for (int index = 0; index < worldPoints.Count; index++)
        {
            output.Add(HandleUtility.WorldToGUIPoint(worldPoints[index]));
        }
    }

    private static void GatherKnots(TerrainRoad road)
    {
        knotWorldPositions.Clear();
        knotScreenPositions.Clear();
        Spline spline = road.Container.Spline;
        if (spline == null)
        {
            return;
        }

        for (int knotIndex = 0; knotIndex < spline.Count; knotIndex++)
        {
            Vector3 world = road.Container.transform.TransformPoint((Vector3)(float3)spline[knotIndex].Position);
            knotWorldPositions.Add(world);
            knotScreenPositions.Add(HandleUtility.WorldToGUIPoint(world));
        }
    }

    private static bool TryGroundUnderMouse(Vector2 mouse, out Vector3 groundPoint)
    {
        return TerrainBrushOverlay.TryRaycastGround(HandleUtility.GUIPointToWorldRay(mouse), out groundPoint);
    }

    private static void OnSceneGui(SceneView sceneView)
    {
        if (!isActive)
        {
            return;
        }

        lastSceneView = sceneView;
        Event currentEvent = Event.current;
        int controlId = GUIUtility.GetControlID(FocusType.Passive);

        if (currentEvent.type == EventType.Layout)
        {
            HandleUtility.AddDefaultControl(controlId);
            return;
        }

        RefreshRoadListIfStale();
        if (selectedRoad != null)
        {
            SampleRoad(selectedRoad, SampleStepMetres, selectedSamples);
            GatherKnots(selectedRoad);
        }

        switch (currentEvent.type)
        {
            case EventType.MouseMove:
                UpdateHover(currentEvent.mousePosition);
                sceneView.Repaint();
                break;
            case EventType.MouseDown:
                if (currentEvent.button == 0 && !currentEvent.alt)
                {
                    HandleLeftMouseDown(currentEvent, controlId);
                }
                else if (currentEvent.button == 1 && !currentEvent.alt)
                {
                    HandleRightMouseDown(currentEvent);
                }
                break;
            case EventType.MouseDrag:
                if (dragKind != DragKind.None && GUIUtility.hotControl == controlId)
                {
                    HandleDrag(currentEvent);
                    currentEvent.Use();
                }
                else
                {
                    UpdateHover(currentEvent.mousePosition);
                }
                break;
            case EventType.MouseUp:
                if (dragKind != DragKind.None && GUIUtility.hotControl == controlId)
                {
                    dragKind = DragKind.None;
                    GUIUtility.hotControl = 0;
                    currentEvent.Use();
                }
                break;
            case EventType.KeyDown:
                HandleKeyDown(currentEvent);
                break;
            case EventType.Repaint:
                DrawAll();
                break;
        }
    }

    private static void UpdateHover(Vector2 mouse)
    {
        hoverKnotIndex = -1;
        hoverLineValid = false;
        hoverOtherRoad = null;
        if (selectedRoad != null)
        {
            hoverKnotIndex = TerrainRoadEditing.NearestPointIndex(knotScreenPositions, mouse, KnotPickPixels);
            if (hoverKnotIndex < 0)
            {
                hoverLineValid = TryFindOnSelectedLine(mouse, out hoverLineWorldPoint, out float ignoredFraction);
            }
        }

        if (hoverKnotIndex < 0 && !hoverLineValid)
        {
            hoverOtherRoad = FindOtherRoadUnderMouse(mouse);
        }
    }

    private static bool TryFindOnSelectedLine(Vector2 mouse, out Vector3 worldPoint, out float splineFraction)
    {
        worldPoint = Vector3.zero;
        splineFraction = 0f;
        ToScreen(selectedSamples, scratchScreen);
        TerrainRoadEditing.PolylineHit hit;
        if (!TerrainRoadEditing.TryNearestOnPolyline(scratchScreen, mouse, LinePickPixels, out hit))
        {
            return false;
        }

        splineFraction = TerrainRoadEditing.PolylineFraction(scratchScreen.Count, hit.segmentIndex, hit.segmentFraction);
        worldPoint = Vector3.Lerp(selectedSamples[hit.segmentIndex], selectedSamples[hit.segmentIndex + 1], hit.segmentFraction);
        return true;
    }

    private static TerrainRoad FindOtherRoadUnderMouse(Vector2 mouse)
    {
        TerrainRoad best = null;
        float bestDistance = float.MaxValue;
        for (int roadIndex = 0; roadIndex < cachedRoads.Length; roadIndex++)
        {
            TerrainRoad road = cachedRoads[roadIndex];
            if (road == null || road == selectedRoad || road.Container == null)
            {
                continue;
            }

            SampleRoad(road, OtherRoadSampleStepMetres, scratchSamples);
            ToScreen(scratchSamples, scratchScreen);
            TerrainRoadEditing.PolylineHit hit;
            if (TerrainRoadEditing.TryNearestOnPolyline(scratchScreen, mouse, LinePickPixels, out hit) && hit.distancePixels < bestDistance)
            {
                bestDistance = hit.distancePixels;
                best = road;
            }
        }

        return best;
    }

    private static bool TryGetWidthHandles(out Vector3 middlePoint, out Vector2 leftScreen, out Vector2 rightScreen)
    {
        middlePoint = Vector3.zero;
        leftScreen = Vector2.zero;
        rightScreen = Vector2.zero;
        if (selectedRoad == null || selectedSamples.Count < 2)
        {
            return false;
        }

        BuildSelectedEdges();
        int middleIndex = selectedSamples.Count / 2;
        if (middleIndex >= leftEdge.Count || middleIndex >= rightEdge.Count)
        {
            return false;
        }

        middlePoint = selectedSamples[middleIndex];
        leftScreen = HandleUtility.WorldToGUIPoint(leftEdge[middleIndex]);
        rightScreen = HandleUtility.WorldToGUIPoint(rightEdge[middleIndex]);
        return true;
    }

    private static void BuildSelectedEdges()
    {
        leftEdge.Clear();
        rightEdge.Clear();
        TerrainRoadEditing.BuildEdgeLines(selectedSamples, selectedRoad.EffectiveHalfWidthMetres, leftEdge, rightEdge);
    }

    private static void HandleLeftMouseDown(Event currentEvent, int controlId)
    {
        Vector2 mouse = currentEvent.mousePosition;

        if (selectedRoad != null)
        {
            int knotIndex = TerrainRoadEditing.NearestPointIndex(knotScreenPositions, mouse, KnotPickPixels);
            if (knotIndex >= 0)
            {
                selectedKnotIndex = knotIndex;
                BeginDrag(DragKind.Knot, controlId, "Move Road Knot");
                SelectionChanged?.Invoke();
                currentEvent.Use();
                return;
            }

            Vector3 middlePoint;
            Vector2 leftScreen;
            Vector2 rightScreen;
            if (TryGetWidthHandles(out middlePoint, out leftScreen, out rightScreen)
                && (Vector2.Distance(mouse, leftScreen) <= KnotPickPixels || Vector2.Distance(mouse, rightScreen) <= KnotPickPixels))
            {
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(selectedRoad, "Change Road Width");
                dragKind = DragKind.Width;
                GUIUtility.hotControl = controlId;
                currentEvent.Use();
                return;
            }

            Vector3 linePoint;
            float splineFraction;
            if (TryFindOnSelectedLine(mouse, out linePoint, out splineFraction))
            {
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(selectedRoad.Container, "Insert Road Knot");
                selectedKnotIndex = InsertKnotAtFraction(selectedRoad, splineFraction, TangentMode.AutoSmooth);
                dragKind = DragKind.Knot;
                GUIUtility.hotControl = controlId;
                SelectionChanged?.Invoke();
                currentEvent.Use();
                return;
            }
        }

        TerrainRoad otherRoad = FindOtherRoadUnderMouse(mouse);
        if (otherRoad != null)
        {
            SelectedRoad = otherRoad;
            currentEvent.Use();
            return;
        }

        Vector3 groundPoint;
        if (selectedRoad != null && currentEvent.shift && !selectedRoad.Container.Spline.Closed && TryGroundUnderMouse(mouse, out groundPoint))
        {
            ExtendSelectedRoad(groundPoint);
            currentEvent.Use();
            return;
        }

        if (selectedRoad != null)
        {
            SelectedRoad = null;
            currentEvent.Use();
        }
    }

    private static void BeginDrag(DragKind kind, int controlId, string undoName)
    {
        Undo.IncrementCurrentGroup();
        Undo.RecordObject(selectedRoad.Container, undoName);
        dragKind = kind;
        GUIUtility.hotControl = controlId;
    }

    private static void HandleDrag(Event currentEvent)
    {
        if (selectedRoad == null)
        {
            return;
        }

        Vector3 groundPoint;
        if (!TryGroundUnderMouse(currentEvent.mousePosition, out groundPoint))
        {
            return;
        }

        if (dragKind == DragKind.Knot)
        {
            Spline spline = selectedRoad.Container.Spline;
            if (selectedKnotIndex < 0 || selectedKnotIndex >= spline.Count)
            {
                return;
            }

            BezierKnot knot = spline[selectedKnotIndex];
            knot.Position = (float3)selectedRoad.Container.transform.InverseTransformPoint(groundPoint);
            spline.SetKnot(selectedKnotIndex, knot);
            EditorUtility.SetDirty(selectedRoad.Container);
        }
        else if (dragKind == DragKind.Width && selectedSamples.Count >= 2)
        {
            Vector3 middlePoint = selectedSamples[selectedSamples.Count / 2];
            selectedRoad.widthOverrideMetres = TerrainRoadEditing.WidthFromHandleDrag(middlePoint, groundPoint, MinimumWidthMetres, MaximumWidthMetres);
            EditorUtility.SetDirty(selectedRoad);
            TerrainRoadEvents.Raise(selectedRoad);
        }

        SceneView.RepaintAll();
    }

    private static int InsertKnotAtFraction(TerrainRoad road, float splineFraction, TangentMode tangentMode)
    {
        SplineContainer container = road.Container;
        Spline spline = container.Spline;
        float curveFraction;
        int curveIndex = spline.SplineToCurveT(splineFraction, out curveFraction);
        Vector3 worldPoint = (Vector3)container.EvaluatePosition(0, splineFraction);
        int insertIndex = Mathf.Clamp(curveIndex + 1, 1, spline.Count);
        spline.Insert(insertIndex, (float3)container.transform.InverseTransformPoint(worldPoint), tangentMode);
        EditorUtility.SetDirty(container);
        return insertIndex;
    }

    private static void ExtendSelectedRoad(Vector3 groundPoint)
    {
        SplineContainer container = selectedRoad.Container;
        Spline spline = container.Spline;
        int nearestEnd = TerrainRoadEditing.NearestEndKnotIndex(knotWorldPositions, groundPoint);
        float3 localPoint = (float3)container.transform.InverseTransformPoint(groundPoint);

        Undo.RecordObject(container, "Extend Road");
        if (nearestEnd <= 0)
        {
            spline.Insert(0, localPoint, TangentMode.AutoSmooth);
            selectedKnotIndex = 0;
        }
        else
        {
            spline.Add(localPoint, TangentMode.AutoSmooth);
            selectedKnotIndex = spline.Count - 1;
        }

        EditorUtility.SetDirty(container);
        SelectionChanged?.Invoke();
        SceneView.RepaintAll();
    }

    private static void HandleKeyDown(Event currentEvent)
    {
        if (currentEvent.keyCode == KeyCode.Delete || currentEvent.keyCode == KeyCode.Backspace)
        {
            if (selectedRoad != null && selectedKnotIndex >= 0)
            {
                DeleteSelectedKnot();
                currentEvent.Use();
            }
        }
        else if (currentEvent.keyCode == KeyCode.Escape)
        {
            if (selectedKnotIndex >= 0)
            {
                selectedKnotIndex = -1;
                SelectionChanged?.Invoke();
                SceneView.RepaintAll();
                currentEvent.Use();
            }
            else if (selectedRoad != null)
            {
                SelectedRoad = null;
                currentEvent.Use();
            }
        }
    }

    private static void HandleRightMouseDown(Event currentEvent)
    {
        if (selectedRoad == null)
        {
            return;
        }

        Vector2 mouse = currentEvent.mousePosition;
        int knotIndex = TerrainRoadEditing.NearestPointIndex(knotScreenPositions, mouse, KnotPickPixels);
        Vector3 linePoint;
        float splineFraction;
        bool onLine = knotIndex < 0 && TryFindOnSelectedLine(mouse, out linePoint, out splineFraction);
        if (knotIndex < 0 && !onLine)
        {
            return;
        }

        TerrainRoad road = selectedRoad;
        GenericMenu menu = new GenericMenu();
        if (knotIndex >= 0)
        {
            int capturedKnot = knotIndex;
            Spline spline = road.Container.Spline;
            if (TerrainRoadEditing.CanDeleteKnot(spline.Count, spline.Closed))
            {
                menu.AddItem(new GUIContent("Delete Knot"), false, () =>
                {
                    selectedKnotIndex = capturedKnot;
                    DeleteSelectedKnot();
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Delete Knot"));
            }

            menu.AddItem(new GUIContent("Set Tangent/Auto Smooth"), false, () => SetKnotTangentMode(road, capturedKnot, TangentMode.AutoSmooth));
            menu.AddItem(new GUIContent("Set Tangent/Linear"), false, () => SetKnotTangentMode(road, capturedKnot, TangentMode.Linear));
        }
        else
        {
            TryFindOnSelectedLine(mouse, out linePoint, out splineFraction);
            float capturedFraction = splineFraction;
            menu.AddItem(new GUIContent("Insert Knot Here"), false, () =>
            {
                Undo.RecordObject(road.Container, "Insert Road Knot");
                selectedKnotIndex = InsertKnotAtFraction(road, capturedFraction, TangentMode.AutoSmooth);
                SelectionChanged?.Invoke();
                SceneView.RepaintAll();
            });
        }

        menu.ShowAsContext();
        currentEvent.Use();
    }

    private static void SetKnotTangentMode(TerrainRoad road, int knotIndex, TangentMode mode)
    {
        Spline spline = road.Container.Spline;
        if (knotIndex < 0 || knotIndex >= spline.Count)
        {
            return;
        }

        Undo.RecordObject(road.Container, "Set Road Tangent");
        spline.SetTangentMode(knotIndex, mode);
        EditorUtility.SetDirty(road.Container);
        SceneView.RepaintAll();
    }

    private static void DrawAll()
    {
        for (int roadIndex = 0; roadIndex < cachedRoads.Length; roadIndex++)
        {
            TerrainRoad road = cachedRoads[roadIndex];
            if (road == null || road == selectedRoad || road.Container == null)
            {
                continue;
            }

            SampleRoad(road, OtherRoadSampleStepMetres, scratchSamples);
            if (scratchSamples.Count < 2)
            {
                continue;
            }

            Handles.color = road == hoverOtherRoad ? OtherRoadHoverColour : OtherRoadColour;
            Handles.DrawAAPolyLine(road == hoverOtherRoad ? 4f : 2f, scratchSamples.ToArray());
        }

        if (selectedRoad == null || selectedSamples.Count < 2)
        {
            return;
        }

        BuildSelectedEdges();
        Handles.color = EdgeLineColour;
        Handles.DrawAAPolyLine(2f, leftEdge.ToArray());
        Handles.DrawAAPolyLine(2f, rightEdge.ToArray());

        Handles.color = SelectedLineColour;
        Handles.DrawAAPolyLine(4f, selectedSamples.ToArray());

        for (int knotIndex = 0; knotIndex < knotWorldPositions.Count; knotIndex++)
        {
            Vector3 position = knotWorldPositions[knotIndex];
            bool isSelectedKnot = knotIndex == selectedKnotIndex;
            bool isHoveredKnot = knotIndex == hoverKnotIndex;
            Handles.color = isSelectedKnot ? SelectionBlue : SelectedLineColour;
            float size = HandleUtility.GetHandleSize(position) * (isSelectedKnot ? 0.12f : (isHoveredKnot ? 0.1f : 0.06f));
            Handles.DotHandleCap(0, position, Quaternion.identity, size, EventType.Repaint);
        }

        int middleIndex = selectedSamples.Count / 2;
        if (middleIndex < leftEdge.Count && middleIndex < rightEdge.Count)
        {
            Handles.color = WidthHandleColour;
            DrawWidthHandle(leftEdge[middleIndex]);
            DrawWidthHandle(rightEdge[middleIndex]);
        }

        if (hoverLineValid && hoverKnotIndex < 0 && dragKind == DragKind.None)
        {
            Handles.color = GhostColour;
            float ghostSize = HandleUtility.GetHandleSize(hoverLineWorldPoint) * 0.08f;
            Handles.DotHandleCap(0, hoverLineWorldPoint, Quaternion.identity, ghostSize, EventType.Repaint);
            Handles.Label(hoverLineWorldPoint + Vector3.up * (ghostSize * 3f), "Click to add knot");
        }
    }

    private static void DrawWidthHandle(Vector3 position)
    {
        float size = HandleUtility.GetHandleSize(position) * 0.07f;
        Handles.CubeHandleCap(0, position, Quaternion.identity, size, EventType.Repaint);
    }
}
#endif
