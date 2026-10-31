#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Unity.Scenes;
using UnityEditor;
using UnityEngine;

// Fills TerrainWorldAuthoring.tiles from the tile SubScenes open in the editor, since a baker cannot read other scenes.
public static class TerrainWorldIndexBuilder
{
    private static readonly Regex TileNamePattern = new Regex(@"^Tile_(-?\d+)_(-?\d+)$");

    public static int Refresh(TerrainWorldAuthoring world)
    {
        List<TerrainTileSceneRef> collectedTiles = new List<TerrainTileSceneRef>();
        foreach (SubScene subScene in Object.FindObjectsByType<SubScene>(FindObjectsInactive.Include))
        {
            Match nameMatch = TileNamePattern.Match(subScene.gameObject.name);
            if (!nameMatch.Success) continue;

            // Streaming owns loading; an auto-loaded tile would never unload.
            if (subScene.AutoLoadScene)
            {
                subScene.AutoLoadScene = false;
                EditorUtility.SetDirty(subScene);
            }

            collectedTiles.Add(new TerrainTileSceneRef
            {
                coordinate = new Vector2Int(int.Parse(nameMatch.Groups[1].Value), int.Parse(nameMatch.Groups[2].Value)),
                sceneGuid = subScene.SceneGUID.ToString()
            });
        }

        collectedTiles.Sort((TerrainTileSceneRef left, TerrainTileSceneRef right) =>
            left.coordinate.y != right.coordinate.y
                ? left.coordinate.y.CompareTo(right.coordinate.y)
                : left.coordinate.x.CompareTo(right.coordinate.x));

        Undo.RecordObject(world, "Refresh Tile Index");
        world.tiles = collectedTiles;
        EditorUtility.SetDirty(world);
        Debug.Log("[Terrain] Tile index refreshed: " + collectedTiles.Count + " tiles on '" + world.name + "'.", world);
        return collectedTiles.Count;
    }

    [MenuItem("Stitch Punk/Terrain/Refresh Tile Index")]
    static void RefreshMenu()
    {
        TerrainWorldAuthoring world = Object.FindFirstObjectByType<TerrainWorldAuthoring>(FindObjectsInactive.Include);
        if (world == null)
        {
            Debug.LogWarning("[Terrain] No TerrainWorldAuthoring in the loaded scenes; add one to a GameObject in the persistent subscene.");
            return;
        }
        Refresh(world);
    }
}
#endif

