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
    const string SharedSubSceneName = "CampLoop_Sub";
    const string SharedSubScenePath = "Assets/Scenes/SubScenes/CampLoop_Sub.unity";
    const string TestWorldSubSceneName = "TerrainWorld_Sub";
    const string TestWorldSubScenePath = "Assets/Scenes/SubScenes/TerrainWorld_Sub.unity";
    const string RequiredTileFolderPrefix = "Assets/Terrain/";

    public struct TerrainWorldBuildTarget
    {
        public string scenePath;
        public string tileFolder;
        public string subSceneName;
        public string subScenePath;
        public string progressTitle;
    }

    public static readonly TerrainWorldBuildTarget TestWorldTarget = new TerrainWorldBuildTarget
    {
        scenePath = TestWorldScenePath,
        tileFolder = TestWorldTileFolder,
        subSceneName = TestWorldSubSceneName,
        subScenePath = TestWorldSubScenePath,
        progressTitle = "Create Test World"
    };

    public static readonly TerrainWorldBuildTarget RandomWorldTarget = new TerrainWorldBuildTarget
    {
        scenePath = "Assets/Scenes/RandomWorld.unity",
        tileFolder = "Assets/Terrain/RandomWorld",
        subSceneName = "RandomWorld_Sub",
        subScenePath = "Assets/Scenes/SubScenes/RandomWorld_Sub.unity",
        progressTitle = "Create Random World"
    };

    static TerrainWorldBuildTarget activeTarget = TestWorldTarget;

    // Content steps read their folder and scene from here while a build runs
    public static TerrainWorldBuildTarget ActiveTarget
    {
        get { return activeTarget; }
        private set { activeTarget = value; }
    }

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
        return BuildWorld(TestWorldTarget, tilesPerSide, afterTilesCreated);
    }

    public static string BuildWorld(TerrainWorldBuildTarget target, int tilesPerSide, Action<int> afterTilesCreated)
    {
        if (string.IsNullOrEmpty(target.tileFolder) || target.tileFolder == "Assets" || !target.tileFolder.StartsWith(RequiredTileFolderPrefix))
            return "Refused: the tile folder '" + target.tileFolder + "' must be under " + RequiredTileFolderPrefix + " because a build deletes it.";
        if (tilesPerSide < 1 || tilesPerSide % 2 == 0) return "Tiles per side must be a positive odd number.";
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "Cancelled: the open scene was not saved.";

        ActiveTarget = target;
        TerrainTileAssets.RootFolderOverride = target.tileFolder;
        int tilesCreated = 0;
        try
        {
            EditorUtility.DisplayProgressBar(target.progressTitle, "Copying CampLoop", 0f);
            if (AssetDatabase.DeleteAsset(target.tileFolder)) AssetDatabase.Refresh();
            EnsureFolder(target.tileFolder);
            if (!AssetDatabase.CopyAsset(CampLoopScenePath, target.scenePath)) return "Could not copy " + CampLoopScenePath + " to " + target.scenePath;
            EditorSceneManager.OpenScene(target.scenePath, OpenSceneMode.Single);

            DetachCopiedTilesAndRoads();

            Vector2Int range = TileRangeForSide(tilesPerSide);
            int totalTiles = tilesPerSide * tilesPerSide;
            for (int tileZ = range.x; tileZ <= range.y; tileZ++)
            {
                for (int tileX = range.x; tileX <= range.y; tileX++)
                {
                    EditorUtility.DisplayProgressBar(target.progressTitle, "Tile " + tileX + ", " + tileZ, tilesCreated / (float)totalTiles);
                    SubScene created = TerrainTileCreator.CreateTileScene(new Vector2Int(tileX, tileZ), TerrainSplatLayer.Grass);
                    if (created != null) tilesCreated++;
                }
            }

            if (afterTilesCreated != null)
            {
                EditorUtility.DisplayProgressBar(target.progressTitle, "Adding content", 1f);
                afterTilesCreated(tilesPerSide);
            }

            ForkSharedSubScene();
            AddFastTravel();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }
        finally
        {
            TerrainTileAssets.RootFolderOverride = null;
            ActiveTarget = TestWorldTarget;
            EditorUtility.ClearProgressBar();
        }
        return "World built: " + tilesCreated + " tiles under " + target.tileFolder + ", scene " + target.scenePath;
    }

    // Fast travel = the zoomed-out miniature view: bake the always-loaded strategic layer, build the camera blend rig (assigned to the
    // caravan camera slot) and add the LOD driver that raises the world time scale.
    public static void AddFastTravel()
    {
        EditorApplication.ExecuteMenuItem("Stitch Punk/Terrain/Bake Strategic Layer");
        EditorApplication.ExecuteMenuItem("Stitch Punk/Terrain/Create Strategic Camera Rig");
        GameObject rig = GameObject.Find("StrategicZoomRig");
        if (rig == null)
        {
            Debug.LogWarning("[Terrain] Test world: the strategic camera rig was not created, fast travel is not wired.");
            return;
        }
        StrategicLodDriver lodDriver = rig.GetComponent<StrategicLodDriver>();
        if (lodDriver == null) lodDriver = rig.AddComponent<StrategicLodDriver>();
        StrategicRigDriver rigDriver = rig.GetComponent<StrategicRigDriver>();
        if (rigDriver == null) return;
        SerializedProperty rigProfile = new SerializedObject(rigDriver).FindProperty("zoomProfile");
        SerializedObject lodObject = new SerializedObject(lodDriver);
        SerializedProperty lodProfile = lodObject.FindProperty("zoomProfile");
        if (rigProfile == null || lodProfile == null) return;
        lodProfile.objectReferenceValue = rigProfile.objectReferenceValue;
        lodObject.ApplyModifiedProperties();
    }

    // The tile registry (TerrainWorldAuthoring.tiles) is serialized inside CampLoop_Sub, which CampLoop uses too: the test world gets its own copy
    // of that sub-scene, so filling in 49 tiles never changes CampLoop's nine.
    public static void ForkSharedSubScene()
    {
        SubScene subScene = null;
        foreach (SubScene candidate in UnityEngine.Object.FindObjectsByType<SubScene>(FindObjectsInactive.Include))
        {
            if (candidate.gameObject.name == SharedSubSceneName || candidate.gameObject.name == ActiveTarget.subSceneName) { subScene = candidate; break; }
        }
        if (subScene == null)
        {
            Debug.LogWarning("[Terrain] Test world: no '" + SharedSubSceneName + "' SubScene to fork; the tile registry was not updated.");
            return;
        }

        if (subScene.gameObject.name == SharedSubSceneName)
        {
            AssetDatabase.DeleteAsset(ActiveTarget.subScenePath);
            if (!AssetDatabase.CopyAsset(SharedSubScenePath, ActiveTarget.subScenePath))
            {
                Debug.LogError("[Terrain] Test world: could not copy " + SharedSubScenePath + " to " + ActiveTarget.subScenePath);
                return;
            }
            SceneAsset copy = AssetDatabase.LoadAssetAtPath<SceneAsset>(ActiveTarget.subScenePath);
            SerializedObject serializedSubScene = new SerializedObject(subScene);
            SerializedProperty sceneAssetProperty = serializedSubScene.FindProperty("_SceneAsset");
            if (sceneAssetProperty == null)
            {
                Debug.LogError("[Terrain] Test world: SubScene has no _SceneAsset property in this Entities version.");
                return;
            }
            sceneAssetProperty.objectReferenceValue = copy;
            serializedSubScene.ApplyModifiedProperties();
            subScene.gameObject.name = ActiveTarget.subSceneName;
        }

        Unity.Scenes.Editor.SubSceneUtility.EditScene(subScene);
        UnityEngine.SceneManagement.Scene editingScene = subScene.EditingScene;
        if (!editingScene.IsValid() || !editingScene.isLoaded)
        {
            Debug.LogError("[Terrain] Test world: the forked sub-scene could not be opened for editing.");
            return;
        }
        foreach (GameObject rootObject in editingScene.GetRootGameObjects())
        {
            TerrainWorldAuthoring world = rootObject.GetComponentInChildren<TerrainWorldAuthoring>(true);
            if (world != null) TerrainWorldIndexBuilder.Refresh(world);
        }
        EditorSceneManager.SaveScene(editingScene);
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
