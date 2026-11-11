#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Keeps baked nav current: re-bakes stale tiles inside the scene save and before entering Play.
[InitializeOnLoad]
public static class TerrainNavAutoBaker
{
    private const string EnabledPrefKey = "StitchPunk.Terrain.AutoBakeNav";
    private const string MenuPath = "Stitch Punk/Terrain/Auto Bake Nav";

    private static bool isBakingInsideSave;

    static TerrainNavAutoBaker()
    {
        EditorSceneManager.sceneSaving += OnSceneSaving;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem(MenuPath)]
    private static void ToggleAutoBake()
    {
        EditorPrefs.SetBool(EnabledPrefKey, !IsAutoBakeEnabled());
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleAutoBakeValidate()
    {
        Menu.SetChecked(MenuPath, IsAutoBakeEnabled());
        return true;
    }

    private static bool IsAutoBakeEnabled()
    {
        return EditorPrefs.GetBool(EnabledPrefKey, true);
    }

    private static bool ShouldSkip()
    {
        return !IsAutoBakeEnabled() || Application.isPlaying || BuildPipeline.isBuildingPlayer;
    }

    // Runs before the scene bytes are written, so the bake lands in this same save. Never call SaveScene here.
    private static void OnSceneSaving(Scene scene, string path)
    {
        if (isBakingInsideSave || ShouldSkip())
        {
            return;
        }

        isBakingInsideSave = true;
        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                TerrainTile[] tiles = roots[rootIndex].GetComponentsInChildren<TerrainTile>(true);
                for (int tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
                {
                    if (TerrainNavBaker.IsNavStale(tiles[tileIndex]))
                    {
                        TerrainNavBaker.BakeTileWithoutSaving(tiles[tileIndex]);
                    }
                }
            }
        }
        finally
        {
            isBakingInsideSave = false;
        }
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.ExitingEditMode || ShouldSkip())
        {
            return;
        }

        isBakingInsideSave = true;
        try
        {
            int bakedTileCount = TerrainNavBaker.BakeStaleOpenTiles(true);
            if (bakedTileCount > 0)
            {
                Debug.Log("TerrainNavAutoBaker: re-baked nav for " + bakedTileCount + " stale tile(s) before Play.");
            }
        }
        finally
        {
            isBakingInsideSave = false;
        }
    }
}
#endif
