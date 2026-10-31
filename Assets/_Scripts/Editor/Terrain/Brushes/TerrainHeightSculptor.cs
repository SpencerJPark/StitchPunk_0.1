#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class TerrainHeightSculptor
{
    const int NoiseSeed = 1337;
    const float NoiseFrequencyPerMetre = 0.05f;
    const float MinimumRampLengthMetres = 2f;

    static readonly HashSet<TerrainTileData> touchedTiles = new HashSet<TerrainTileData>();
    static Vector2 strokeStartXZ;
    static Vector2 lastDragXZ;
    static bool strokeActive;
    static float flattenTargetHeight;

    static TerrainHeightSculptor()
    {
        TerrainBrushOverlay.BrushStroke += OnBrushStroke;
        TerrainBrushOverlay.BrushStrokeEnded += OnBrushStrokeEnded;
    }

    static TerrainTileData LookUpTileData(Vector2Int coordinate)
    {
        TerrainTile tile = TerrainTileCreator.FindTileAt(coordinate);
        if (tile == null) return null;
        return tile.tileData;
    }

    static void OnBrushStroke(BrushHit hit, bool isDragging)
    {
        TerrainBrushTool tool = BrushState.activeTool;
        bool isSculptTool = tool == TerrainBrushTool.Raise || tool == TerrainBrushTool.Lower
            || tool == TerrainBrushTool.Smooth || tool == TerrainBrushTool.Flatten
            || tool == TerrainBrushTool.Noise;
        if (!isSculptTool && tool != TerrainBrushTool.Ramp) return;

        Vector2 centre = new Vector2(hit.worldPoint.x, hit.worldPoint.z);
        lastDragXZ = centre;

        if (!isDragging || !strokeActive)
        {
            strokeActive = true;
            strokeStartXZ = centre;
            touchedTiles.Clear();
            if (tool == TerrainBrushTool.Flatten) flattenTargetHeight = hit.worldPoint.y - TerrainTileCreator.GroundTopHeight;
            if (isSculptTool) RecordUndoForTilesNear(hit);
        }

        if (!isSculptTool) return;

        float radius = BrushState.radiusMetres;
        float hardness = BrushState.hardness;
        float strength = BrushState.strength;
        List<HeightBrushDirtyRegion> regions = null;
        switch (tool)
        {
            case TerrainBrushTool.Raise:
                regions = TerrainHeightBrush.Raise(centre, radius, hardness, strength * 0.25f, LookUpTileData);
                break;
            case TerrainBrushTool.Lower:
                regions = TerrainHeightBrush.Lower(centre, radius, hardness, strength * 0.25f, LookUpTileData);
                break;
            case TerrainBrushTool.Smooth:
                regions = TerrainHeightBrush.Smooth(centre, radius, hardness, strength, LookUpTileData);
                break;
            case TerrainBrushTool.Flatten:
                regions = TerrainHeightBrush.Flatten(centre, radius, hardness, strength, flattenTargetHeight, LookUpTileData);
                break;
            case TerrainBrushTool.Noise:
                regions = TerrainHeightBrush.Noise(centre, radius, hardness, NoiseSeed, strength * 0.5f, NoiseFrequencyPerMetre, LookUpTileData);
                break;
        }
        ApplyRegions(regions);
    }

    static void RecordUndoForTilesNear(BrushHit hit)
    {
        float reach = BrushState.radiusMetres + 1f;
        Vector2 centre = new Vector2(hit.worldPoint.x, hit.worldPoint.z);
        List<Object> dataToRecord = new List<Object>();
        for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                Vector2Int coordinate = new Vector2Int(hit.tileCoordinate.x + offsetX, hit.tileCoordinate.y + offsetZ);
                TerrainTileData data = LookUpTileData(coordinate);
                if (data == null) continue;
                Vector2 minCorner = data.WorldMinCorner();
                float nearestX = Mathf.Clamp(centre.x, minCorner.x, minCorner.x + TerrainTileData.TileSizeMetres);
                float nearestZ = Mathf.Clamp(centre.y, minCorner.y, minCorner.y + TerrainTileData.TileSizeMetres);
                float distance = Vector2.Distance(centre, new Vector2(nearestX, nearestZ));
                if (distance <= reach) dataToRecord.Add(data);
            }
        }
        if (dataToRecord.Count > 0) Undo.RecordObjects(dataToRecord.ToArray(), "Sculpt Terrain");
    }

    static void ApplyRegions(List<HeightBrushDirtyRegion> regions)
    {
        if (regions == null) return;
        for (int index = 0; index < regions.Count; index++)
        {
            TerrainTileData data = regions[index].tile;
            if (data == null) continue;
            EditorUtility.SetDirty(data);
            touchedTiles.Add(data);
            TerrainTile tile = TerrainTileCreator.FindTileAt(data.coordinate);
            if (tile != null) TerrainTileBuilder.BuildMeshInMemory(tile);
        }
    }

    static void OnBrushStrokeEnded()
    {
        if (!strokeActive) return;
        strokeActive = false;

        if (BrushState.activeTool == TerrainBrushTool.Ramp
            && Vector2.Distance(strokeStartXZ, lastDragXZ) >= MinimumRampLengthMetres)
        {
            Vector3 startPoint = new Vector3(strokeStartXZ.x, 0f, strokeStartXZ.y);
            RecordUndoForTilesNear(new BrushHit { worldPoint = startPoint, tileCoordinate = TileCoordinateAt(strokeStartXZ) });
            RecordUndoForTilesNear(new BrushHit { worldPoint = new Vector3(lastDragXZ.x, 0f, lastDragXZ.y), tileCoordinate = TileCoordinateAt(lastDragXZ) });
            ApplyRegions(TerrainHeightBrush.Ramp(strokeStartXZ, lastDragXZ, BrushState.radiusMetres * 2f,
                BrushState.hardness, BrushState.strength, LookUpTileData));
        }

        foreach (TerrainTileData data in touchedTiles)
        {
            data.BumpRevision();
            TerrainTile tile = TerrainTileCreator.FindTileAt(data.coordinate);
            if (tile != null) TerrainTileBuilder.Rebuild(tile);
        }
        touchedTiles.Clear();
    }

    static Vector2Int TileCoordinateAt(Vector2 worldXZ)
    {
        return new Vector2Int(
            Mathf.FloorToInt((worldXZ.x + 50f) / TerrainTileData.TileSizeMetres),
            Mathf.FloorToInt((worldXZ.y + 50f) / TerrainTileData.TileSizeMetres));
    }
}
#endif
