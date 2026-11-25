#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Rebuilds the tiles whose terrace data changed, once the edits settle.
[InitializeOnLoad]
public static class TerrainTerraceBaker
{
    private const double DebounceSeconds = 0.25;

    private static readonly HashSet<Vector2Int> pendingTiles = new HashSet<Vector2Int>();
    private static double lastEventTime;
    private static bool updateHooked;

    static TerrainTerraceBaker()
    {
        TerrainTerraceEvents.TerraceChanged -= OnTerraceChanged;
        TerrainTerraceEvents.TerraceChanged += OnTerraceChanged;
    }

    public static void RebuildTiles(IEnumerable<Vector2Int> tiles)
    {
        foreach (Vector2Int tileCoordinate in tiles)
        {
            TerrainTile tile = TerrainTileCreator.FindTileAt(tileCoordinate);
            if (tile != null) TerrainTileBuilder.Rebuild(tile);
        }
    }

    private static void OnTerraceChanged(Vector2Int tileCoordinate)
    {
        pendingTiles.Add(tileCoordinate);
        lastEventTime = EditorApplication.timeSinceStartup;
        if (!updateHooked)
        {
            updateHooked = true;
            EditorApplication.update += TickDebounce;
        }
    }

    private static void TickDebounce()
    {
        if (EditorApplication.timeSinceStartup - lastEventTime < DebounceSeconds)
        {
            return;
        }

        if (Application.isPlaying || EditorApplication.isCompiling)
        {
            return;
        }

        EditorApplication.update -= TickDebounce;
        updateHooked = false;

        List<Vector2Int> tilesToRebuild = new List<Vector2Int>(pendingTiles);
        pendingTiles.Clear();
        RebuildTiles(tilesToRebuild);
    }

    [MenuItem("Stitch Punk/Terrain/Terraces/Rebuild All Open Tiles")]
    private static void RebuildAllOpenTiles()
    {
        TerrainTile[] openTiles = Object.FindObjectsByType<TerrainTile>(FindObjectsInactive.Exclude);
        int terracedCount = 0;
        foreach (TerrainTile tile in openTiles)
        {
            TerrainTileBuilder.Rebuild(tile);
            if (TerrainTileBuilder.LastBuildWasTerraced) terracedCount++;
        }
        Debug.Log("Terraces: rebuilt " + openTiles.Length + " open tiles, " + terracedCount + " took the terraced path.");
    }
}
#endif
