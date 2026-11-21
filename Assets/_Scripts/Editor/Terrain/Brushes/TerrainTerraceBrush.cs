#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum TerrainTerraceBrushMode { Paint, Ramp }

public static class TerrainTerraceBrush
{
    private const float MinRadiusMetres = 1f;
    private const float MaxRadiusMetres = 40f;
    private const float TileSizeMetres = 100f;
    private const int CellsPerSide = TerrainTileData.TerraceCellsPerSide;
    private const double ContourRefreshSeconds = 0.1;
    private const float RampWidthMetres = 6f;
    private const float RampMinimumDragMetres = 2f;
    private const float RampMinimumRunMetres = 9f;
    private const float PreviewLiftMetres = 0.2f;

    private static readonly Color ContourColor = new Color(1f, 0.95f, 0.6f, 0.95f);
    private static readonly Color BrushColor = new Color(1f, 1f, 1f, 0.9f);
    private static readonly Color RampColor = new Color(0.4f, 0.9f, 1f, 0.95f);

    private static TerrainTerraceBrushMode activeMode = TerrainTerraceBrushMode.Paint;
    private static float brushRadiusMetres = 6f;
    private static bool isEnabled;

    private static bool strokeActive;
    private static int strokeSourceLevel;
    private static int strokeTargetLevel;
    private static bool strokeClears;
    private static int strokeMaxLevel = 3;
    private static float strokeCornerRadius = 2.5f;
    private static readonly HashSet<Vector2Int> strokeTiles = new HashSet<Vector2Int>();
    private static readonly List<Vector3> contourSegments = new List<Vector3>();
    private static double lastContourTime;
    private static bool contourDirty;
    private static float contourHeight;

    private static bool rampActive;
    private static Vector3 rampStartPoint;
    private static int rampStartLevel;
    private static Vector3 rampCurrentPoint;
    private static TerrainTile rampStartTile;

    private static bool hasHover;
    private static Vector3 hoverPoint;

    public static event Action StateChanged;

    public static TerrainTerraceBrushMode ActiveMode
    {
        get { return activeMode; }
        set
        {
            if (activeMode == value) return;
            CancelStroke();
            activeMode = value;
            RaiseStateChanged();
        }
    }

    public static float BrushRadiusMetres
    {
        get { return brushRadiusMetres; }
        set
        {
            float clamped = Mathf.Clamp(value, MinRadiusMetres, MaxRadiusMetres);
            if (Mathf.Approximately(clamped, brushRadiusMetres)) return;
            brushRadiusMetres = clamped;
            RaiseStateChanged();
        }
    }

    public static bool IsEnabled { get { return isEnabled; } }

    public static string StatusText
    {
        get
        {
            if (activeMode == TerrainTerraceBrushMode.Ramp) return rampActive ? "Ramp: level " + rampStartLevel + " up" : "Ramp";
            if (!strokeActive) return "Paint";
            if (strokeClears) return "Paint: clear";
            return "Paint: level " + strokeSourceLevel + " → " + strokeTargetLevel;
        }
    }

    public static void Enable()
    {
        if (isEnabled) return;
        isEnabled = true;
        SceneView.duringSceneGui -= OnSceneGui;
        SceneView.duringSceneGui += OnSceneGui;
        RaiseStateChanged();
    }

    public static void Disable()
    {
        if (!isEnabled) return;
        isEnabled = false;
        SceneView.duringSceneGui -= OnSceneGui;
        CancelStroke();
        hasHover = false;
        RaiseStateChanged();
    }

    private static void RaiseStateChanged()
    {
        if (StateChanged != null) StateChanged();
    }

    private static void CancelStroke()
    {
        bool wasActive = strokeActive || rampActive;
        strokeActive = false;
        rampActive = false;
        strokeTiles.Clear();
        contourSegments.Clear();
        contourDirty = false;
        if (wasActive) RaiseStateChanged();
    }

    private static void OnSceneGui(SceneView sceneView)
    {
        Event currentEvent = Event.current;
        if (currentEvent == null) return;

        if (currentEvent.type == EventType.Layout)
        {
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        }

        if (currentEvent.type == EventType.Repaint)
        {
            DrawOverlay();
            return;
        }

        if (currentEvent.alt) return;

        if (currentEvent.type == EventType.KeyDown)
        {
            if (currentEvent.keyCode == KeyCode.LeftBracket)
            {
                BrushRadiusMetres = brushRadiusMetres - 1f;
                currentEvent.Use();
            }
            else if (currentEvent.keyCode == KeyCode.RightBracket)
            {
                BrushRadiusMetres = brushRadiusMetres + 1f;
                currentEvent.Use();
            }
            return;
        }

        bool isMouseEvent = currentEvent.type == EventType.MouseDown
            || currentEvent.type == EventType.MouseDrag
            || currentEvent.type == EventType.MouseUp
            || currentEvent.type == EventType.MouseMove;
        if (!isMouseEvent) return;

        Vector3 hitPoint;
        bool hitGround = TryGetGroundPoint(currentEvent.mousePosition, out hitPoint);
        hasHover = hitGround;
        if (hitGround) hoverPoint = hitPoint;
        sceneView.Repaint();

        if (currentEvent.type == EventType.MouseMove) return;
        if (currentEvent.button != 0) return;

        if (activeMode == TerrainTerraceBrushMode.Paint) HandlePaintEvent(currentEvent, hitGround, hitPoint);
        else HandleRampEvent(currentEvent, hitGround, hitPoint);
    }

    private static bool TryGetGroundPoint(Vector2 mousePosition, out Vector3 groundPoint)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 5000f) && hit.collider.GetComponent<TerrainTile>() != null)
        {
            groundPoint = hit.point;
            return true;
        }

        return TerrainBrushOverlay.TryRaycastGround(ray, out groundPoint);
    }

    private static Vector2Int TileCoordinateAt(float worldX, float worldZ)
    {
        return new Vector2Int(
            Mathf.FloorToInt((worldX + TileSizeMetres * 0.5f) / TileSizeMetres),
            Mathf.FloorToInt((worldZ + TileSizeMetres * 0.5f) / TileSizeMetres));
    }

    private static int CellIndexAt(float worldPosition, int tileCoordinate)
    {
        float local = worldPosition - (tileCoordinate * TileSizeMetres - TileSizeMetres * 0.5f);
        return Mathf.Clamp(Mathf.FloorToInt(local), 0, CellsPerSide - 1);
    }

    private static bool TryReadLevel(Vector3 worldPoint, out int level)
    {
        level = 0;
        Vector2Int coordinate = TileCoordinateAt(worldPoint.x, worldPoint.z);
        TerrainTile tile = TerrainTileCreator.FindTileAt(coordinate);
        if (tile == null || tile.tileData == null) return false;
        level = TerraceLevelField.ResolvedLevel(tile.tileData, CellIndexAt(worldPoint.x, coordinate.x), CellIndexAt(worldPoint.z, coordinate.y));
        return true;
    }

    private static TerrainTileData LookupTileData(Vector2Int coordinate)
    {
        TerrainTile tile = TerrainTileCreator.FindTileAt(coordinate);
        return tile != null ? tile.tileData : null;
    }

    private static void HandlePaintEvent(Event currentEvent, bool hitGround, Vector3 hitPoint)
    {
        if (currentEvent.type == EventType.MouseDown)
        {
            if (!hitGround) return;
            int sourceLevel;
            if (!TryReadLevel(hitPoint, out sourceLevel)) return;

            TerrainWorldProfile profile = TerrainWorldProfile.FindInProject();
            strokeMaxLevel = profile != null ? profile.maxTerraceLevels : 3;
            strokeCornerRadius = profile != null ? profile.terraceCornerRadiusMetres : 2.5f;
            strokeSourceLevel = sourceLevel;
            strokeClears = currentEvent.control;
            if (strokeClears) strokeTargetLevel = TerrainTileData.NoHandTerraceLevel;
            else if (currentEvent.shift) strokeTargetLevel = Mathf.Max(sourceLevel - 1, 0);
            else strokeTargetLevel = Mathf.Min(sourceLevel + 1, strokeMaxLevel);

            strokeActive = true;
            strokeTiles.Clear();
            contourSegments.Clear();
            contourHeight = hitPoint.y + PreviewLiftMetres;
            StampDisc(hitPoint);
            RaiseStateChanged();
            currentEvent.Use();
        }
        else if (currentEvent.type == EventType.MouseDrag)
        {
            if (!strokeActive) return;
            if (hitGround)
            {
                contourHeight = hitPoint.y + PreviewLiftMetres;
                StampDisc(hitPoint);
            }
            currentEvent.Use();
        }
        else if (currentEvent.type == EventType.MouseUp)
        {
            if (!strokeActive) return;
            FinishPaintStroke();
            currentEvent.Use();
        }
    }

    private static void StampDisc(Vector3 centre)
    {
        float radius = brushRadiusMetres;
        Vector2Int minTile = TileCoordinateAt(centre.x - radius, centre.z - radius);
        Vector2Int maxTile = TileCoordinateAt(centre.x + radius, centre.z + radius);
        float radiusSquared = radius * radius;
        bool changedAny = false;

        for (int tileZ = minTile.y; tileZ <= maxTile.y; tileZ++)
        {
            for (int tileX = minTile.x; tileX <= maxTile.x; tileX++)
            {
                Vector2Int coordinate = new Vector2Int(tileX, tileZ);
                TerrainTile tile = TerrainTileCreator.FindTileAt(coordinate);
                if (tile == null || tile.tileData == null) continue;
                TerrainTileData data = tile.tileData;

                float originX = tileX * TileSizeMetres - TileSizeMetres * 0.5f;
                float originZ = tileZ * TileSizeMetres - TileSizeMetres * 0.5f;
                int cellMinX = Mathf.Max(0, Mathf.FloorToInt(centre.x - radius - originX));
                int cellMaxX = Mathf.Min(CellsPerSide - 1, Mathf.CeilToInt(centre.x + radius - originX));
                int cellMinZ = Mathf.Max(0, Mathf.FloorToInt(centre.z - radius - originZ));
                int cellMaxZ = Mathf.Min(CellsPerSide - 1, Mathf.CeilToInt(centre.z + radius - originZ));

                bool touchedThisTile = false;
                for (int cellZ = cellMinZ; cellZ <= cellMaxZ; cellZ++)
                {
                    float deltaZ = originZ + cellZ + 0.5f - centre.z;
                    for (int cellX = cellMinX; cellX <= cellMaxX; cellX++)
                    {
                        float deltaX = originX + cellX + 0.5f - centre.x;
                        if (deltaX * deltaX + deltaZ * deltaZ > radiusSquared) continue;

                        if (!touchedThisTile)
                        {
                            if (strokeTiles.Add(coordinate))
                            {
                                Undo.RecordObject(data, "Terrace stroke");
                            }
                            if (data.handTerraceLevels == null || data.handTerraceLevels.Length != CellsPerSide * CellsPerSide)
                            {
                                byte[] fresh = new byte[CellsPerSide * CellsPerSide];
                                for (int fillIndex = 0; fillIndex < fresh.Length; fillIndex++) fresh[fillIndex] = TerrainTileData.NoHandTerraceLevel;
                                data.handTerraceLevels = fresh;
                            }
                            touchedThisTile = true;
                        }

                        data.handTerraceLevels[cellZ * CellsPerSide + cellX] = (byte)strokeTargetLevel;
                        changedAny = true;
                    }
                }
            }
        }

        if (changedAny) contourDirty = true;
        RefreshContoursIfDue();
    }

    private static void RefreshContoursIfDue()
    {
        if (!contourDirty) return;
        double now = EditorApplication.timeSinceStartup;
        if (now - lastContourTime < ContourRefreshSeconds) return;
        lastContourTime = now;
        contourDirty = false;
        RebuildContours();
    }

    private static void RebuildContours()
    {
        contourSegments.Clear();
        const int sampleSide = CellsPerSide + 1;
        foreach (Vector2Int coordinate in strokeTiles)
        {
            TerrainTileData data = LookupTileData(coordinate);
            if (data == null) continue;
            float originX = coordinate.x * TileSizeMetres - TileSizeMetres * 0.5f;
            float originZ = coordinate.y * TileSizeMetres - TileSizeMetres * 0.5f;

            for (int threshold = 1; threshold <= strokeMaxLevel; threshold++)
            {
                float[] distances = TerraceLevelField.BuildSignedDistance(data, threshold, strokeCornerRadius, LookupTileData, 1);
                if (distances == null || distances.Length < sampleSide * sampleSide) continue;
                AppendMarchingSquares(distances, sampleSide, originX, originZ);
            }
        }
    }

    private static void AppendMarchingSquares(float[] distances, int sampleSide, float originX, float originZ)
    {
        Vector3[] edgePoints = new Vector3[4];
        bool[] edgeCrossed = new bool[4];
        for (int sampleZ = 0; sampleZ < sampleSide - 1; sampleZ++)
        {
            for (int sampleX = 0; sampleX < sampleSide - 1; sampleX++)
            {
                float bottomLeft = distances[sampleZ * sampleSide + sampleX];
                float bottomRight = distances[sampleZ * sampleSide + sampleX + 1];
                float topLeft = distances[(sampleZ + 1) * sampleSide + sampleX];
                float topRight = distances[(sampleZ + 1) * sampleSide + sampleX + 1];
                bool anyInside = bottomLeft > 0f || bottomRight > 0f || topLeft > 0f || topRight > 0f;
                bool anyOutside = bottomLeft <= 0f || bottomRight <= 0f || topLeft <= 0f || topRight <= 0f;
                if (!anyInside || !anyOutside) continue;

                float cornerX = originX + sampleX;
                float cornerZ = originZ + sampleZ;
                // Edges: 0 bottom, 1 right, 2 top, 3 left.
                edgeCrossed[0] = (bottomLeft > 0f) != (bottomRight > 0f);
                edgeCrossed[1] = (bottomRight > 0f) != (topRight > 0f);
                edgeCrossed[2] = (topLeft > 0f) != (topRight > 0f);
                edgeCrossed[3] = (bottomLeft > 0f) != (topLeft > 0f);
                if (edgeCrossed[0]) edgePoints[0] = new Vector3(cornerX + ZeroCrossing(bottomLeft, bottomRight), contourHeight, cornerZ);
                if (edgeCrossed[1]) edgePoints[1] = new Vector3(cornerX + 1f, contourHeight, cornerZ + ZeroCrossing(bottomRight, topRight));
                if (edgeCrossed[2]) edgePoints[2] = new Vector3(cornerX + ZeroCrossing(topLeft, topRight), contourHeight, cornerZ + 1f);
                if (edgeCrossed[3]) edgePoints[3] = new Vector3(cornerX, contourHeight, cornerZ + ZeroCrossing(bottomLeft, topLeft));

                int previousCrossed = -1;
                for (int edgeIndex = 0; edgeIndex < 4; edgeIndex++)
                {
                    if (!edgeCrossed[edgeIndex]) continue;
                    if (previousCrossed < 0)
                    {
                        previousCrossed = edgeIndex;
                    }
                    else
                    {
                        contourSegments.Add(edgePoints[previousCrossed]);
                        contourSegments.Add(edgePoints[edgeIndex]);
                        previousCrossed = -1;
                    }
                }
            }
        }
    }

    private static float ZeroCrossing(float fromValue, float toValue)
    {
        float span = toValue - fromValue;
        if (Mathf.Abs(span) < 1e-6f) return 0.5f;
        return Mathf.Clamp01(-fromValue / span);
    }

    private static void FinishPaintStroke()
    {
        HashSet<Vector2Int> rebuildTiles = new HashSet<Vector2Int>();
        foreach (Vector2Int coordinate in strokeTiles)
        {
            TerrainTileData data = LookupTileData(coordinate);
            if (data != null) EditorUtility.SetDirty(data);
            for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
            {
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    Vector2Int candidate = coordinate + new Vector2Int(offsetX, offsetZ);
                    if (TerrainTileCreator.FindTileAt(candidate) != null) rebuildTiles.Add(candidate);
                }
            }
        }

        strokeActive = false;
        strokeTiles.Clear();
        contourSegments.Clear();
        contourDirty = false;
        TerrainTerraceBaker.RebuildTiles(rebuildTiles);
        RaiseStateChanged();
    }

    private static void HandleRampEvent(Event currentEvent, bool hitGround, Vector3 hitPoint)
    {
        if (currentEvent.type == EventType.MouseDown)
        {
            if (!hitGround) return;
            int startLevel;
            if (!TryReadLevel(hitPoint, out startLevel)) return;
            rampStartTile = TerrainTileCreator.FindTileAt(TileCoordinateAt(hitPoint.x, hitPoint.z));
            rampStartPoint = hitPoint;
            rampCurrentPoint = hitPoint;
            rampStartLevel = startLevel;
            rampActive = true;
            RaiseStateChanged();
            currentEvent.Use();
        }
        else if (currentEvent.type == EventType.MouseDrag)
        {
            if (!rampActive) return;
            if (hitGround) rampCurrentPoint = hitPoint;
            currentEvent.Use();
        }
        else if (currentEvent.type == EventType.MouseUp)
        {
            if (!rampActive) return;
            if (hitGround) rampCurrentPoint = hitPoint;
            CreateRampFromDrag();
            rampActive = false;
            RaiseStateChanged();
            currentEvent.Use();
        }
    }

    private static void CreateRampFromDrag()
    {
        float deltaX = rampCurrentPoint.x - rampStartPoint.x;
        float deltaZ = rampCurrentPoint.z - rampStartPoint.z;
        float distance = Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        if (distance < RampMinimumDragMetres || rampStartTile == null) return;

        GameObject rampObject = new GameObject("TerraceRamp");
        TerraceRamp ramp = rampObject.AddComponent<TerraceRamp>();
        ramp.widthMetres = RampWidthMetres;
        ramp.runMetres = Mathf.Max(RampMinimumRunMetres, distance);
        ramp.lowerLevel = rampStartLevel;
        rampObject.transform.position = new Vector3(
            (rampStartPoint.x + rampCurrentPoint.x) * 0.5f,
            rampStartPoint.y,
            (rampStartPoint.z + rampCurrentPoint.z) * 0.5f);
        rampObject.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(deltaX, deltaZ) * Mathf.Rad2Deg, 0f);
        SceneManager.MoveGameObjectToScene(rampObject, rampStartTile.gameObject.scene);
        Undo.RegisterCreatedObjectUndo(rampObject, "Create terrace ramp");
    }

    private static void DrawOverlay()
    {
        if (hasHover)
        {
            Handles.color = BrushColor;
            Handles.DrawWireDisc(hoverPoint + Vector3.up * PreviewLiftMetres, Vector3.up, brushRadiusMetres);
        }

        if (strokeActive && contourSegments.Count >= 2)
        {
            Handles.color = ContourColor;
            Handles.DrawLines(contourSegments.ToArray());
        }

        if (rampActive)
        {
            DrawRampPreview();
        }
    }

    private static void DrawRampPreview()
    {
        float deltaX = rampCurrentPoint.x - rampStartPoint.x;
        float deltaZ = rampCurrentPoint.z - rampStartPoint.z;
        float distance = Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        if (distance < 0.01f) return;
        float forwardX = deltaX / distance;
        float forwardZ = deltaZ / distance;
        float halfWidth = RampWidthMetres * 0.5f;
        Vector3 side = new Vector3(forwardZ, 0f, -forwardX) * halfWidth;
        Vector3 lift = Vector3.up * PreviewLiftMetres;
        Vector3 startPoint = rampStartPoint + lift;
        Vector3 endPoint = new Vector3(rampCurrentPoint.x, rampStartPoint.y, rampCurrentPoint.z) + lift;

        Handles.color = RampColor;
        Handles.DrawLines(new Vector3[]
        {
            startPoint - side, startPoint + side,
            startPoint + side, endPoint + side,
            endPoint + side, endPoint - side,
            endPoint - side, startPoint - side
        });
    }
}
#endif
