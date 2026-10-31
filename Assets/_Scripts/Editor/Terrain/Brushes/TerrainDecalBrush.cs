#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class TerrainDecalBrush
{
    public static int seed = 1337;

    static System.Random strokeRandom;
    static int strokeCounter;
    static Vector2 lastStampXZ;
    static bool strokeActive;
    static Vector2 regionStartXZ;
    static Vector2 regionCurrentXZ;
    static float regionHeight;

    static TerrainDecalBrush()
    {
        TerrainBrushOverlay.BrushStroke += OnBrushStroke;
        TerrainBrushOverlay.BrushStrokeEnded += OnBrushStrokeEnded;
        SceneView.duringSceneGui += DrawRegionRectangle;
    }

    static bool IsDecalTool(TerrainBrushTool tool)
    {
        return tool == TerrainBrushTool.Decal || tool == TerrainBrushTool.DecalErase || tool == TerrainBrushTool.DecalRegion;
    }

    static void OnBrushStroke(BrushHit hit, bool isDragging)
    {
        TerrainBrushTool tool = BrushState.activeTool;
        if (!IsDecalTool(tool)) return;

        Vector2 hitXZ = new Vector2(hit.worldPoint.x, hit.worldPoint.z);
        bool isPress = !isDragging || !strokeActive;
        if (isPress)
        {
            strokeActive = true;
            strokeCounter++;
            strokeRandom = new System.Random(seed + strokeCounter);
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(GroupNameFor(tool));
        }

        if (tool == TerrainBrushTool.DecalRegion)
        {
            if (isPress) regionStartXZ = hitXZ;
            regionCurrentXZ = hitXZ;
            regionHeight = hit.worldPoint.y;
            return;
        }

        TerrainTile tile = TerrainTileCreator.FindTileAt(hit.tileCoordinate);
        if (tile == null) return;

        if (tool == TerrainBrushTool.DecalErase)
        {
            TerrainDecalScatterer.EraseInRadius(tile, hitXZ, BrushState.radiusMetres, true);
            return;
        }

        TerrainDecalSet set = BrushState.activeDecalSet;
        if (set == null) return;

        float minimumSpacing = Mathf.Max(1f, BrushState.radiusMetres * 0.5f);
        if (!isPress && Vector2.Distance(hitXZ, lastStampXZ) < minimumSpacing) return;

        Vector2 stampXZ = hitXZ;
        if (!isPress)
        {
            float offsetX = (float)(strokeRandom.NextDouble() * 2.0 - 1.0);
            float offsetZ = (float)(strokeRandom.NextDouble() * 2.0 - 1.0);
            Vector2 offset = new Vector2(offsetX, offsetZ);
            if (offset.sqrMagnitude > 1f) offset.Normalize();
            stampXZ += offset * (BrushState.radiusMetres * 0.5f);
        }

        TerrainDecalScatterer.PlaceOne(tile, set, BrushState.decalEntryIndex, stampXZ, strokeRandom);
        lastStampXZ = hitXZ;
    }

    static string GroupNameFor(TerrainBrushTool tool)
    {
        if (tool == TerrainBrushTool.DecalErase) return "Erase Decals";
        if (tool == TerrainBrushTool.DecalRegion) return "Scatter Decals Region";
        return "Stamp Decals";
    }

    static void OnBrushStrokeEnded()
    {
        bool wasActive = strokeActive;
        strokeActive = false;
        if (!wasActive || BrushState.activeTool != TerrainBrushTool.DecalRegion) return;

        TerrainDecalSet set = BrushState.activeDecalSet;
        if (set == null) return;

        Rect region = RegionRect();
        Vector2Int minimumCoordinate = TerrainTile.CoordinateContaining(new Vector3(region.xMin, 0f, region.yMin));
        Vector2Int maximumCoordinate = TerrainTile.CoordinateContaining(new Vector3(region.xMax, 0f, region.yMax));
        for (int coordinateX = minimumCoordinate.x; coordinateX <= maximumCoordinate.x; coordinateX++)
        {
            for (int coordinateZ = minimumCoordinate.y; coordinateZ <= maximumCoordinate.y; coordinateZ++)
            {
                TerrainTile tile = TerrainTileCreator.FindTileAt(new Vector2Int(coordinateX, coordinateZ));
                if (tile != null) TerrainDecalScatterer.Scatter(tile, set, seed, region);
            }
        }
        SceneView.RepaintAll();
    }

    static Rect RegionRect()
    {
        Vector2 minimum = Vector2.Min(regionStartXZ, regionCurrentXZ);
        Vector2 maximum = Vector2.Max(regionStartXZ, regionCurrentXZ);
        return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
    }

    static void DrawRegionRectangle(SceneView sceneView)
    {
        if (!strokeActive || BrushState.activeTool != TerrainBrushTool.DecalRegion) return;

        Rect region = RegionRect();
        Vector3[] corners =
        {
            new Vector3(region.xMin, regionHeight + 0.1f, region.yMin),
            new Vector3(region.xMin, regionHeight + 0.1f, region.yMax),
            new Vector3(region.xMax, regionHeight + 0.1f, region.yMax),
            new Vector3(region.xMax, regionHeight + 0.1f, region.yMin)
        };
        Handles.DrawSolidRectangleWithOutline(corners, new Color(1f, 0.8f, 0.2f, 0.18f), new Color(1f, 0.8f, 0.2f, 0.9f));
    }
}
#endif
