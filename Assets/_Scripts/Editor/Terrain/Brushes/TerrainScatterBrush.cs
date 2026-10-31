#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class TerrainScatterBrush
{
    public static int seed = 4242;

    static System.Random strokeRandom;
    static int strokeCounter;
    static Vector2 lastPaintPoint;

    static TerrainScatterBrush()
    {
        TerrainBrushOverlay.BrushStroke += OnBrushStroke;
        TerrainBrushOverlay.BrushStrokeEnded += OnBrushStrokeEnded;
    }

    static bool IsScatterTool(TerrainBrushTool tool)
    {
        return tool == TerrainBrushTool.ScatterPlace || tool == TerrainBrushTool.ScatterPaint || tool == TerrainBrushTool.ScatterErase;
    }

    static void OnBrushStroke(BrushHit hit, bool isDragging)
    {
        TerrainBrushTool tool = BrushState.activeTool;
        if (!IsScatterTool(tool)) return;

        if (!isDragging)
        {
            strokeCounter++;
            strokeRandom = new System.Random(unchecked(seed * 31 + strokeCounter));
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(tool == TerrainBrushTool.ScatterErase ? "Erase Props" : "Place Props");
        }
        if (strokeRandom == null) strokeRandom = new System.Random(seed);

        TerrainScatterSet set = BrushState.activeScatterSet;
        TerrainTile tile = TerrainTileCreator.FindTileAt(hit.tileCoordinate);
        if (tile == null) return;

        Vector2 hitXZ = new Vector2(hit.worldPoint.x, hit.worldPoint.z);

        if (tool == TerrainBrushTool.ScatterErase)
        {
            TerrainScatterGenerator.EraseInRadius(tile, hitXZ, BrushState.radiusMetres, true);
            return;
        }
        if (set == null) return;

        if (tool == TerrainBrushTool.ScatterPlace)
        {
            if (!isDragging) TerrainScatterGenerator.PlaceOne(tile, set, BrushState.scatterRuleIndex, hitXZ, strokeRandom);
            return;
        }

        float minimumStepMetres = Mathf.Max(1f, BrushState.radiusMetres * 0.6f);
        if (isDragging && Vector2.Distance(hitXZ, lastPaintPoint) < minimumStepMetres) return;
        lastPaintPoint = hitXZ;

        int count = Mathf.Max(1, Mathf.RoundToInt(BrushState.strength * 4f));
        for (int index = 0; index < count; index++)
        {
            float angleRadians = (float)(strokeRandom.NextDouble() * Mathf.PI * 2.0);
            float distanceMetres = Mathf.Sqrt((float)strokeRandom.NextDouble()) * BrushState.radiusMetres;
            Vector2 point = hitXZ + new Vector2(Mathf.Cos(angleRadians), Mathf.Sin(angleRadians)) * distanceMetres;
            TerrainScatterGenerator.PlaceOne(tile, set, BrushState.scatterRuleIndex, point, strokeRandom);
        }
    }

    static void OnBrushStrokeEnded()
    {
        strokeRandom = null;
    }
}
#endif
