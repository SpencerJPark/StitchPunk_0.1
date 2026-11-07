#if UNITY_EDITOR
using System;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Stitch Punk > Terrain > Create Test World: a copy of CampLoop with its tiles swapped for a fresh N x N grid
public static class TerrainTestWorldBuilder
{
    public const string TestWorldScenePath = "Assets/Scenes/TerrainWorld.unity";
    public const string TestWorldTileFolder = "Assets/Terrain/TestWorld";
    const string CampLoopScenePath = "Assets/Scenes/CampLoop.unity";
    const string TerrainWorldRootName = "TerrainWorld";
    const string TileObjectPrefix = "Tile_";

    [MenuItem("Stitch Punk/Terrain/Create Test World (7 x 7)")]
    static void CreateSevenBySevenFromMenu()
    {
        BuildFromMenu(7);
    }

    [MenuItem("Stitch Punk/Terrain/Create Test World (11 x 11)")]
    static void CreateElevenByElevenFromMenu()
    {
        BuildFromMenu(11);
    }

    static void BuildFromMenu(int tilesPerSide)
    {
        string message = "This builds a NEW scene at " + TestWorldScenePath + " (copied from CampLoop, replacing any earlier test world) "
            + "with a " + tilesPerSide + " x " + tilesPerSide + " grid of tiles under " + TestWorldTileFolder + ".\n\nCampLoop and its tiles are never touched.";
        if (!EditorUtility.DisplayDialog("Create Test World", message, "Create", "Cancel")) return;
        string report = BuildTestWorld(tilesPerSide, TerrainTestWorldContent.PopulateTestWorld);
        Debug.Log("[Terrain] " + report);
    }

    public static Vector2Int TileRangeForSide(int tilesPerSide)
    {
        int half = tilesPerSide / 2;
        return new Vector2Int(-half, half);
    }

    public static string BuildTestWorld(int tilesPerSide, Action<int> afterTilesCreated)
    {
        if (tilesPerSide < 1 || tilesPerSide % 2 == 0) return "Tiles per side must be a positive odd number.";
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "Cancelled: the open scene was not saved.";

        TerrainTileAssets.RootFolderOverride = TestWorldTileFolder;
        int tilesCreated = 0;
        try
        {
            EditorUtility.DisplayProgressBar("Create Test World", "Copying CampLoop", 0f);
            if (AssetDatabase.DeleteAsset(TestWorldTileFolder)) AssetDatabase.Refresh();
            EnsureFolder(TestWorldTileFolder);
            if (!AssetDatabase.CopyAsset(CampLoopScenePath, TestWorldScenePath)) return "Could not copy " + CampLoopScenePath + " to " + TestWorldScenePath;
            EditorSceneManager.OpenScene(TestWorldScenePath, OpenSceneMode.Single);

            DetachCopiedTilesAndRoads();

            Vector2Int range = TileRangeForSide(tilesPerSide);
            int totalTiles = tilesPerSide * tilesPerSide;
            for (int tileZ = range.x; tileZ <= range.y; tileZ++)
            {
                for (int tileX = range.x; tileX <= range.y; tileX++)
                {
                    EditorUtility.DisplayProgressBar("Create Test World", "Tile " + tileX + ", " + tileZ, tilesCreated / (float)totalTiles);
                    SubScene created = TerrainTileCreator.CreateTileScene(new Vector2Int(tileX, tileZ), TerrainSplatLayer.Grass);
                    if (created != null) tilesCreated++;
                }
            }

            if (afterTilesCreated != null)
            {
                EditorUtility.DisplayProgressBar("Create Test World", "Adding content", 1f);
                afterTilesCreated(tilesPerSide);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }
        finally
        {
            TerrainTileAssets.RootFolderOverride = null;
            EditorUtility.ClearProgressBar();
        }
        return "Test world built: " + tilesCreated + " tiles under " + TestWorldTileFolder + ", scene " + TestWorldScenePath;
    }

    // Only the scene's SubScene objects are removed; the tile scene assets of CampLoop stay on disk
    static void DetachCopiedTilesAndRoads()
    {
        foreach (GameObject rootObject in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (rootObject.name != TerrainWorldRootName) continue;
            for (int childIndex = rootObject.transform.childCount - 1; childIndex >= 0; childIndex--)
            {
                GameObject child = rootObject.transform.GetChild(childIndex).gameObject;
                if (child.name.StartsWith(TileObjectPrefix)) UnityEngine.Object.DestroyImmediate(child);
            }
        }
        foreach (TerrainRoad road in UnityEngine.Object.FindObjectsByType<TerrainRoad>(FindObjectsInactive.Include))
            UnityEngine.Object.DestroyImmediate(road.gameObject);
    }

    static void EnsureFolder(string folderPath)
    {
        string[] segments = folderPath.Split('/');
        string builtPath = segments[0];
        for (int index = 1; index < segments.Length; index++)
        {
            string nextPath = builtPath + "/" + segments[index];
            if (!AssetDatabase.IsValidFolder(nextPath)) AssetDatabase.CreateFolder(builtPath, segments[index]);
            builtPath = nextPath;
        }
    }
}
#endif
