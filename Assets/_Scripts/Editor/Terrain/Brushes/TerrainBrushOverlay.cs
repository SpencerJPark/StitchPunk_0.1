#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public enum TerrainBrushTool { None, Raise, Lower, Smooth, Flatten, Noise, Ramp, Paint, Decal, DecalErase, DecalRegion, ScatterPlace, ScatterPaint, ScatterErase }

public struct BrushHit
{
    public Vector3 worldPoint;
    public Vector3 surfaceNormal;
    public Vector2Int tileCoordinate;
}

public static class BrushState
{
    public static float radiusMetres = 8f;
    public static float hardness = 0.5f;
    public static float strength = 0.5f;
    public static TerrainBrushTool activeTool = TerrainBrushTool.None;
    public static TerrainSplatLayer paintLayer = TerrainSplatLayer.Grass;
    public static TerrainDecalSet activeDecalSet;
    public static int decalEntryIndex;
    public static TerrainScatterSet activeScatterSet;
    public static int scatterRuleIndex;

    // Held-modifier state, refreshed from every Scene-view event: Ctrl inverts (Raise <-> Lower), Shift smooths.
    public static bool invert;
    public static bool smoothHeld;

    public const float MinimumRadiusMetres = 1f;
    public const float MaximumRadiusMetres = 40f;
    const float RadiusStepMetres = 1f;
    const float StrengthStep = 0.05f;

    public static void StepRadius(int direction)
    {
        radiusMetres = Mathf.Clamp(radiusMetres + direction * RadiusStepMetres, MinimumRadiusMetres, MaximumRadiusMetres);
    }

    public static void StepStrength(int direction)
    {
        strength = Mathf.Clamp01(Mathf.Round((strength + direction * StrengthStep) * 100f) / 100f);
    }
}

[InitializeOnLoad]
public static class TerrainBrushOverlay
{
    public static event Action<BrushHit, bool> BrushStroke;
    public static event Action BrushStrokeEnded;

    const float MarchStepMetres = 1f;
    const float MaxMarchDistanceMetres = 3000f;
    const int BisectionIterations = 6;
    const double TileCacheLifetimeSeconds = 0.5;

    static readonly Dictionary<Vector2Int, TerrainTileData> tileDataByCoordinate = new Dictionary<Vector2Int, TerrainTileData>();
    static double tileCacheBuiltAt = -1000.0;
    static bool strokeInProgress;
    static bool hasHit;
    static BrushHit currentHit;

    static TerrainBrushOverlay()
    {
        SceneView.duringSceneGui += OnSceneGui;
        EditorApplication.hierarchyChanged += InvalidateTileCache;
    }

    static void InvalidateTileCache()
    {
        tileCacheBuiltAt = -1000.0;
    }

    static void RefreshTileCacheIfStale()
    {
        if (EditorApplication.timeSinceStartup - tileCacheBuiltAt < TileCacheLifetimeSeconds) return;
        tileCacheBuiltAt = EditorApplication.timeSinceStartup;
        tileDataByCoordinate.Clear();
        TerrainTile[] tiles = UnityEngine.Object.FindObjectsByType<TerrainTile>(FindObjectsInactive.Exclude);
        foreach (TerrainTile tile in tiles)
        {
            if (tile.tileData == null) continue;
            tileDataByCoordinate[tile.tileCoordinate] = tile.tileData;
        }
    }

    static bool TryGetGroundHeight(float worldX, float worldZ, out float height)
    {
        Vector2Int coordinate = TerrainTile.CoordinateContaining(new Vector3(worldX, 0f, worldZ));
        TerrainTileData data;
        if (!tileDataByCoordinate.TryGetValue(coordinate, out data))
        {
            height = 0f;
            return false;
        }
        height = data.SampleBilinear(worldX, worldZ) + TerrainTileCreator.GroundTopHeight;
        return true;
    }

    static float GroundHeightOrFallback(float worldX, float worldZ, float fallback)
    {
        float height;
        return TryGetGroundHeight(worldX, worldZ, out height) ? height : fallback;
    }

    static bool IsBelowGround(Vector3 point)
    {
        float height;
        return TryGetGroundHeight(point.x, point.z, out height) && point.y < height;
    }

    static bool TryRaycastHeightmap(Ray ray, out BrushHit hit)
    {
        hit = new BrushHit();
        if (tileDataByCoordinate.Count == 0) return false;

        for (float distance = MarchStepMetres; distance <= MaxMarchDistanceMetres; distance += MarchStepMetres)
        {
            Vector3 point = ray.origin + ray.direction * distance;
            if (IsBelowGround(point))
            {
                float nearDistance = distance - MarchStepMetres;
                float farDistance = distance;
                for (int iteration = 0; iteration < BisectionIterations; iteration++)
                {
                    float middleDistance = (nearDistance + farDistance) * 0.5f;
                    if (IsBelowGround(ray.origin + ray.direction * middleDistance)) farDistance = middleDistance;
                    else nearDistance = middleDistance;
                }
                Vector3 hitPoint = ray.origin + ray.direction * farDistance;
                float hitHeight;
                if (TryGetGroundHeight(hitPoint.x, hitPoint.z, out hitHeight)) hitPoint.y = hitHeight;
                hit.worldPoint = hitPoint;
                hit.surfaceNormal = SurfaceNormalAt(hitPoint);
                hit.tileCoordinate = TerrainTile.CoordinateContaining(hitPoint);
                return true;
            }
        }
        return false;
    }

    static Vector3 SurfaceNormalAt(Vector3 point)
    {
        float centre = point.y;
        float heightEast = GroundHeightOrFallback(point.x + 1f, point.z, centre);
        float heightWest = GroundHeightOrFallback(point.x - 1f, point.z, centre);
        float heightNorth = GroundHeightOrFallback(point.x, point.z + 1f, centre);
        float heightSouth = GroundHeightOrFallback(point.x, point.z - 1f, centre);
        float slopeX = (heightEast - heightWest) * 0.5f;
        float slopeZ = (heightNorth - heightSouth) * 0.5f;
        return new Vector3(-slopeX, 1f, -slopeZ).normalized;
    }

    static Color ColourForTool(TerrainBrushTool tool)
    {
        switch (tool)
        {
            case TerrainBrushTool.Raise: return Color.green;
            case TerrainBrushTool.Lower: return Color.red;
            case TerrainBrushTool.Smooth: return Color.cyan;
            case TerrainBrushTool.Flatten: return Color.yellow;
            case TerrainBrushTool.Noise: return Color.magenta;
            case TerrainBrushTool.Ramp: return new Color(1f, 0.5f, 0f);
            default: return Color.white;
        }
    }

    static void OnSceneGui(SceneView sceneView)
    {
        if (Application.isPlaying) return;
        if (BrushState.activeTool == TerrainBrushTool.None)
        {
            strokeInProgress = false;
            return;
        }

        Event currentEvent = Event.current;
        RefreshTileCacheIfStale();
        BrushState.invert = currentEvent.control || currentEvent.command;
        BrushState.smoothHeld = currentEvent.shift;

        if (currentEvent.type == EventType.KeyDown && HandleBrushKey(currentEvent.keyCode))
        {
            currentEvent.Use();
            SceneView.RepaintAll();
            return;
        }

        if (currentEvent.type == EventType.Layout)
        {
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            return;
        }

        bool pointerEvent = currentEvent.type == EventType.MouseMove
            || currentEvent.type == EventType.MouseDrag
            || currentEvent.type == EventType.MouseDown
            || currentEvent.type == EventType.MouseUp;
        if (pointerEvent)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
            hasHit = TryRaycastHeightmap(ray, out currentHit);
            if (currentEvent.type == EventType.MouseMove) SceneView.RepaintAll();
        }

        if (currentEvent.type == EventType.Repaint)
        {
            DrawBrush();
            return;
        }

        if (currentEvent.button != 0 || currentEvent.alt) return;

        if (currentEvent.type == EventType.MouseDown && hasHit)
        {
            strokeInProgress = true;
            BrushStroke?.Invoke(currentHit, false);
            currentEvent.Use();
        }
        else if (currentEvent.type == EventType.MouseDrag && strokeInProgress)
        {
            if (hasHit) BrushStroke?.Invoke(currentHit, true);
            currentEvent.Use();
        }
        else if (currentEvent.type == EventType.MouseUp && strokeInProgress)
        {
            strokeInProgress = false;
            BrushStrokeEnded?.Invoke();
            currentEvent.Use();
        }
    }

    // One shortcut set for every brush tool: [ ] size, - = strength.
    static bool HandleBrushKey(KeyCode keyCode)
    {
        switch (keyCode)
        {
            case KeyCode.LeftBracket: BrushState.StepRadius(-1); return true;
            case KeyCode.RightBracket: BrushState.StepRadius(1); return true;
            case KeyCode.Minus: BrushState.StepStrength(-1); return true;
            case KeyCode.Equals: BrushState.StepStrength(1); return true;
            default: return false;
        }
    }

    // What the held modifiers turn the active tool into, for the cursor tint and the sculptor.
    public static TerrainBrushTool EffectiveSculptTool(TerrainBrushTool tool, bool invert, bool smoothHeld)
    {
        bool canSmooth = tool == TerrainBrushTool.Raise || tool == TerrainBrushTool.Lower
            || tool == TerrainBrushTool.Flatten || tool == TerrainBrushTool.Noise;
        if (smoothHeld && canSmooth) return TerrainBrushTool.Smooth;
        if (invert && tool == TerrainBrushTool.Raise) return TerrainBrushTool.Lower;
        if (invert && tool == TerrainBrushTool.Lower) return TerrainBrushTool.Raise;
        return tool;
    }

    static void DrawBrush()
    {
        if (!hasHit)
        {
            TerrainSceneHud.SetCursorInfo(string.Empty);
            return;
        }
        TerrainSceneHud.SetCursorInfo("Tile (" + currentHit.tileCoordinate.x + ", " + currentHit.tileCoordinate.y + ")   height "
            + (currentHit.worldPoint.y - TerrainTileCreator.GroundTopHeight).ToString("0.0") + " m   brush "
            + BrushState.radiusMetres.ToString("0") + " m");
        Color toolColour = ColourForTool(EffectiveSculptTool(BrushState.activeTool, BrushState.invert, BrushState.smoothHeld));
        Quaternion discRotation = Quaternion.FromToRotation(Vector3.up, currentHit.surfaceNormal);
        Vector3 normal = currentHit.surfaceNormal;
        Vector3 centre = currentHit.worldPoint + normal * 0.05f;

        Handles.color = new Color(toolColour.r, toolColour.g, toolColour.b, 0.9f);
        Handles.DrawWireDisc(centre, normal, BrushState.radiusMetres);
        Handles.color = new Color(toolColour.r, toolColour.g, toolColour.b, 0.5f);
        Handles.DrawWireDisc(centre, normal, BrushState.radiusMetres * BrushState.hardness);
        Handles.DrawLine(centre, centre + discRotation * Vector3.up * 1.5f);
    }

    static void SelectTool(TerrainBrushTool tool)
    {
        BrushState.activeTool = tool;
        SceneView.RepaintAll();
    }

    static bool ToolIsActive(string menuPath, TerrainBrushTool tool)
    {
        Menu.SetChecked(menuPath, BrushState.activeTool == tool);
        return true;
    }

    const string MenuRoot = "Stitch Punk/Terrain/Brush/";

    [MenuItem(MenuRoot + "Off")] static void SelectOff() { SelectTool(TerrainBrushTool.None); }
    [MenuItem(MenuRoot + "Off", true)] static bool ValidateOff() { return ToolIsActive(MenuRoot + "Off", TerrainBrushTool.None); }
    [MenuItem(MenuRoot + "Raise")] static void SelectRaise() { SelectTool(TerrainBrushTool.Raise); }
    [MenuItem(MenuRoot + "Raise", true)] static bool ValidateRaise() { return ToolIsActive(MenuRoot + "Raise", TerrainBrushTool.Raise); }
    [MenuItem(MenuRoot + "Lower")] static void SelectLower() { SelectTool(TerrainBrushTool.Lower); }
    [MenuItem(MenuRoot + "Lower", true)] static bool ValidateLower() { return ToolIsActive(MenuRoot + "Lower", TerrainBrushTool.Lower); }
    [MenuItem(MenuRoot + "Smooth")] static void SelectSmooth() { SelectTool(TerrainBrushTool.Smooth); }
    [MenuItem(MenuRoot + "Smooth", true)] static bool ValidateSmooth() { return ToolIsActive(MenuRoot + "Smooth", TerrainBrushTool.Smooth); }
    [MenuItem(MenuRoot + "Flatten")] static void SelectFlatten() { SelectTool(TerrainBrushTool.Flatten); }
    [MenuItem(MenuRoot + "Flatten", true)] static bool ValidateFlatten() { return ToolIsActive(MenuRoot + "Flatten", TerrainBrushTool.Flatten); }
    [MenuItem(MenuRoot + "Noise")] static void SelectNoise() { SelectTool(TerrainBrushTool.Noise); }
    [MenuItem(MenuRoot + "Noise", true)] static bool ValidateNoise() { return ToolIsActive(MenuRoot + "Noise", TerrainBrushTool.Noise); }
    [MenuItem(MenuRoot + "Ramp")] static void SelectRamp() { SelectTool(TerrainBrushTool.Ramp); }
    [MenuItem(MenuRoot + "Ramp", true)] static bool ValidateRamp() { return ToolIsActive(MenuRoot + "Ramp", TerrainBrushTool.Ramp); }
    [MenuItem(MenuRoot + "Paint")] static void SelectPaint() { SelectTool(TerrainBrushTool.Paint); }
    [MenuItem(MenuRoot + "Paint", true)] static bool ValidatePaint() { return ToolIsActive(MenuRoot + "Paint", TerrainBrushTool.Paint); }

    const string PaintLayerRoot = MenuRoot + "Paint Layer/";

    static void SelectPaintLayer(TerrainSplatLayer layer)
    {
        BrushState.paintLayer = layer;
        SceneView.RepaintAll();
    }

    static bool PaintLayerIsActive(string menuPath, TerrainSplatLayer layer)
    {
        Menu.SetChecked(menuPath, BrushState.paintLayer == layer);
        return true;
    }

    [MenuItem(PaintLayerRoot + "Dirt")] static void SelectPaintLayerDirt() { SelectPaintLayer(TerrainSplatLayer.Dirt); }
    [MenuItem(PaintLayerRoot + "Dirt", true)] static bool ValidatePaintLayerDirt() { return PaintLayerIsActive(PaintLayerRoot + "Dirt", TerrainSplatLayer.Dirt); }
    [MenuItem(PaintLayerRoot + "Grass")] static void SelectPaintLayerGrass() { SelectPaintLayer(TerrainSplatLayer.Grass); }
    [MenuItem(PaintLayerRoot + "Grass", true)] static bool ValidatePaintLayerGrass() { return PaintLayerIsActive(PaintLayerRoot + "Grass", TerrainSplatLayer.Grass); }
    [MenuItem(PaintLayerRoot + "Forest")] static void SelectPaintLayerForest() { SelectPaintLayer(TerrainSplatLayer.Forest); }
    [MenuItem(PaintLayerRoot + "Forest", true)] static bool ValidatePaintLayerForest() { return PaintLayerIsActive(PaintLayerRoot + "Forest", TerrainSplatLayer.Forest); }
    [MenuItem(PaintLayerRoot + "Stone")] static void SelectPaintLayerStone() { SelectPaintLayer(TerrainSplatLayer.Stone); }
    [MenuItem(PaintLayerRoot + "Stone", true)] static bool ValidatePaintLayerStone() { return PaintLayerIsActive(PaintLayerRoot + "Stone", TerrainSplatLayer.Stone); }
}
#endif
