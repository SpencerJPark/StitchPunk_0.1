#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class TerrainStrategicBaker
{
    private const int MaximumCapturePixels = 2048;
    private const int MaximumSplatPixels = 4096;
    private const string StrategicFolder = "Assets/Terrain/Strategic";
    private const string ProfileFolder = "Assets/Terrain/Profiles";
    private const string ProfilePath = ProfileFolder + "/TerrainStrategicProfile.asset";
    private const string SharedMaterialPath = StrategicFolder + "/M_Strategic.mat";
    private const string ShaderName = "Painted/Strategic";
    private const string MeshSubAssetName = "StrategicMesh";
    private const string ColourSubAssetName = "StrategicColour";
    private const string MaterialSubAssetName = "StrategicMaterial";
    private const string TerrainWorldRootName = "TerrainWorld";
    private const string FallbackRootName = "StrategicLayer";
    private const string ColourTextureProperty = "_ColourTex";

    [MenuItem("Stitch Punk/Terrain/Bake Strategic Layer")]
    public static void BakeFromMenu()
    {
        TerrainStrategicProfile profile = FindOrCreateProfile();
        TerrainPlatformProfile platform = TerrainPlatformProfileAssets.LoadEditorPreview();
        int maxTexturePixels = platform != null ? platform.strategicTextureSizePixels : 2048;
        int chunkCount = BakeAll(profile, maxTexturePixels);
        Debug.Log("[Terrain] Baked " + chunkCount + " strategic super-chunk(s).");
    }

    // Two worlds (CampLoop and the test world) reuse the same tile coordinates, so scanning every asset in the project mixes them.
    private static Dictionary<Vector2Int, TerrainTileData> CollectTilesOfOpenScene()
    {
        Dictionary<Vector2Int, TerrainTileData> tiles = new Dictionary<Vector2Int, TerrainTileData>();
        foreach (TerrainTile tile in Object.FindObjectsByType<TerrainTile>(FindObjectsInactive.Include))
        {
            if (tile.tileData != null)
            {
                tiles[tile.tileData.coordinate] = tile.tileData;
            }
        }
        if (tiles.Count > 0)
        {
            return tiles;
        }

        foreach (string guid in AssetDatabase.FindAssets("t:TerrainTileData"))
        {
            TerrainTileData tileData = AssetDatabase.LoadAssetAtPath<TerrainTileData>(AssetDatabase.GUIDToAssetPath(guid));
            if (tileData != null)
            {
                tiles[tileData.coordinate] = tileData;
            }
        }
        return tiles;
    }

    private static Dictionary<Vector2Int, TerrainTile> tilesInOpenScene;

    public static int BakeAll(TerrainStrategicProfile profile, int maxTexturePixels)
    {
        Dictionary<Vector2Int, TerrainTileData> tiles = CollectTilesOfOpenScene();
        tilesInOpenScene = TerrainStrategicGroundProxy.CollectTilesOfOpenScene();

        HashSet<Vector2Int> superChunks = new HashSet<Vector2Int>();
        foreach (Vector2Int tileCoordinate in tiles.Keys)
        {
            superChunks.Add(new Vector2Int(
                Mathf.FloorToInt((float)tileCoordinate.x / profile.superChunkTiles),
                Mathf.FloorToInt((float)tileCoordinate.y / profile.superChunkTiles)));
        }
        if (superChunks.Count == 0)
        {
            return 0;
        }

        EnsureFolder(StrategicFolder);
        Material sharedMaterial = FindOrCreateSharedMaterial();
        if (sharedMaterial == null)
        {
            return 0;
        }

        long totalBytes = 0;
        List<SceneAsset> chunkScenes = new List<SceneAsset>();
        foreach (Vector2Int superChunk in superChunks)
        {
            TerrainStrategicChunkData chunkData;
            long chunkBytes = BakeSuperChunk(profile, superChunk, tiles, maxTexturePixels, sharedMaterial, out chunkData);
            if (chunkData == null)
            {
                continue;
            }
            totalBytes += chunkBytes;
            chunkScenes.Add(WriteChunkScene(superChunk, chunkData));
        }

        TerrainStrategicBuilder.ReleaseReadableMaskCopies();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EnsureSubScenes(chunkScenes);

        float totalMegabytes = totalBytes / (1024f * 1024f);
        TerrainPlatformProfile platform = TerrainPlatformProfileAssets.LoadEditorPreview();
        Debug.Log("[Terrain] Strategic layer: " + chunkScenes.Count + " chunk(s), " + totalMegabytes.ToString("F2") + " MB of colour textures.");
        if (platform != null && totalMegabytes > platform.maxStrategicLayerMegabytes)
        {
            Debug.LogWarning("[Terrain] Strategic layer is " + totalMegabytes.ToString("F2") + " MB, over the platform budget of " + platform.maxStrategicLayerMegabytes + " MB.");
        }
        return chunkScenes.Count;
    }

    private static long BakeSuperChunk(TerrainStrategicProfile profile, Vector2Int superChunk, Dictionary<Vector2Int, TerrainTileData> tiles, int maxTexturePixels, Material sharedMaterial, out TerrainStrategicChunkData chunkData)
    {
        chunkData = null;
        Mesh builtMesh;
        Texture2D builtTexture;
        if (!TerrainStrategicBuilder.Build(profile, superChunk, tiles, maxTexturePixels, out builtMesh, out builtTexture))
        {
            return 0;
        }

        float chunkSizeMetres = profile.superChunkTiles * TerrainTileData.TileSizeMetres;
        Vector2 chunkMinMetres = TerrainStrategicBuilder.SuperChunkWorldMin(profile, superChunk);
        Material groundTemplate = TerrainStrategicGroundProxy.FindTemplateGroundMaterial(tilesInOpenScene, chunkMinMetres + Vector2.one * (chunkSizeMetres * 0.5f));
        bool drawWithGroundMaterial = groundTemplate != null;
        if (drawWithGroundMaterial)
        {
            // Same material as the real tiles, so zoomed out is exactly zoomed in; only the splat map is stitched per chunk.
            Object.DestroyImmediate(builtTexture);
            builtTexture = TerrainStrategicGroundProxy.BuildSplatComposite(tilesInOpenScene, chunkMinMetres, chunkSizeMetres, TerrainStrategicGroundProxy.NativeSplatPixelsPerSide(tilesInOpenScene, profile.superChunkTiles, MaximumSplatPixels));
            EditorUtility.CompressTexture(builtTexture, TextureFormat.BC7, TextureCompressionQuality.Normal);
            Debug.Log("[Terrain] Strategic chunk " + superChunk + ": drawn with the real ground material '" + groundTemplate.name + "', splat " + builtTexture.width + " px.");
        }
        else
        {
            builtTexture = BuildFlatColourFallback(builtTexture, profile, superChunk, chunkSizeMetres, maxTexturePixels);
        }

        string dataPath = DataPath(superChunk);
        chunkData = AssetDatabase.LoadAssetAtPath<TerrainStrategicChunkData>(dataPath);
        if (chunkData == null)
        {
            chunkData = ScriptableObject.CreateInstance<TerrainStrategicChunkData>();
            AssetDatabase.CreateAsset(chunkData, dataPath);
        }

        Mesh persistedMesh = null;
        Texture2D persistedTexture = null;
        Material persistedMaterial = null;
        foreach (Object subAsset in AssetDatabase.LoadAllAssetsAtPath(dataPath))
        {
            if (subAsset is Mesh && subAsset.name == MeshSubAssetName) persistedMesh = (Mesh)subAsset;
            else if (subAsset is Texture2D && subAsset.name == ColourSubAssetName) persistedTexture = (Texture2D)subAsset;
            else if (subAsset is Material && subAsset.name == MaterialSubAssetName) persistedMaterial = (Material)subAsset;
        }

        persistedMesh = PersistSubAsset(builtMesh, persistedMesh, MeshSubAssetName, chunkData);
        persistedTexture = PersistSubAsset(builtTexture, persistedTexture, ColourSubAssetName, chunkData);
        Material materialSource = drawWithGroundMaterial ? groundTemplate : sharedMaterial;
        if (persistedMaterial == null)
        {
            persistedMaterial = new Material(materialSource);
            persistedMaterial.name = MaterialSubAssetName;
            AssetDatabase.AddObjectToAsset(persistedMaterial, chunkData);
        }
        else
        {
            persistedMaterial.shader = materialSource.shader;
            persistedMaterial.CopyPropertiesFromMaterial(materialSource);
        }
        persistedMaterial.shaderKeywords = materialSource.shaderKeywords;
        if (drawWithGroundMaterial)
        {
            TerrainStrategicGroundProxy.ConfigureStandInMaterial(persistedMaterial, persistedTexture, chunkMinMetres, chunkSizeMetres);
        }
        else
        {
            persistedMaterial.SetTexture(ColourTextureProperty, persistedTexture);
        }

        chunkData.superChunkCoordinate = superChunk;
        chunkData.worldMin = TerrainStrategicBuilder.SuperChunkWorldMin(profile, superChunk);
        chunkData.mesh = persistedMesh;
        chunkData.colour = persistedTexture;
        EditorUtility.SetDirty(persistedMaterial);
        EditorUtility.SetDirty(chunkData);
        AssetDatabase.SaveAssetIfDirty(chunkData);
        return UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(persistedTexture);
    }

    // Only when no tile in the open scene uses Painted/Ground: the old top-down capture, so a chunk still gets some colour.
    private static Texture2D BuildFlatColourFallback(Texture2D builtTexture, TerrainStrategicProfile profile, Vector2Int superChunk, float chunkSizeMetres, int maxTexturePixels)
    {
        int capturePixels = Mathf.Min(maxTexturePixels, MaximumCapturePixels);
        Color missingTileColour = TerrainStrategicBuilder.LoadLayerColoursFromProject()[0];
        Texture2D capturedTexture = TerrainStrategicTopDownCapture.TryCapture(
            TerrainStrategicBuilder.SuperChunkWorldMin(profile, superChunk), chunkSizeMetres, capturePixels, missingTileColour);
        if (capturedTexture != null)
        {
            TerrainStrategicTopDownCapture.MatchBrightness(capturedTexture, builtTexture, missingTileColour);
            Object.DestroyImmediate(builtTexture);
            builtTexture = capturedTexture;
        }
        EditorUtility.CompressTexture(builtTexture, TextureFormat.DXT1, TextureCompressionQuality.Normal);
        return builtTexture;
    }

    private static TAsset PersistSubAsset<TAsset>(TAsset built, TAsset existing, string subAssetName, Object owner) where TAsset : Object
    {
        if (existing != null)
        {
            EditorUtility.CopySerialized(built, existing);
            existing.name = subAssetName;
            Object.DestroyImmediate(built);
            return existing;
        }
        built.name = subAssetName;
        AssetDatabase.AddObjectToAsset(built, owner);
        return built;
    }

    private static SceneAsset WriteChunkScene(Vector2Int superChunk, TerrainStrategicChunkData chunkData)
    {
        string scenePath = ScenePath(superChunk);
        Scene chunkScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        GameObject chunkObject = new GameObject("Strategic_" + superChunk.x + "_" + superChunk.y, typeof(MeshFilter), typeof(MeshRenderer));
        chunkObject.transform.position = new Vector3(chunkData.worldMin.x, 0f, chunkData.worldMin.y);
        chunkObject.GetComponent<MeshFilter>().sharedMesh = chunkData.mesh;
        MeshRenderer meshRenderer = chunkObject.GetComponent<MeshRenderer>();
        // Casts the hill shadows of the zoomed-out view; the ground shader keeps it from casting while at ground level.
        meshRenderer.shadowCastingMode = ShadowCastingMode.On;
        meshRenderer.receiveShadows = true;
        meshRenderer.sharedMaterial = LoadChunkMaterial(chunkData);
        SceneManager.MoveGameObjectToScene(chunkObject, chunkScene);

        EditorSceneManager.SaveScene(chunkScene, scenePath);
        EditorSceneManager.CloseScene(chunkScene, true);
        return AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
    }

    private static Material LoadChunkMaterial(TerrainStrategicChunkData chunkData)
    {
        foreach (Object subAsset in AssetDatabase.LoadAllAssetsAtPath(DataPath(chunkData.superChunkCoordinate)))
        {
            if (subAsset is Material && subAsset.name == MaterialSubAssetName)
            {
                return (Material)subAsset;
            }
        }
        return null;
    }

    private static void EnsureSubScenes(List<SceneAsset> chunkScenes)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        GameObject worldRoot = null;
        GameObject fallbackRoot = null;
        foreach (GameObject rootObject in activeScene.GetRootGameObjects())
        {
            if (rootObject.name == TerrainWorldRootName) worldRoot = rootObject;
            else if (rootObject.name == FallbackRootName) fallbackRoot = rootObject;
        }
        if (worldRoot == null)
        {
            worldRoot = fallbackRoot != null ? fallbackRoot : new GameObject(FallbackRootName);
        }

        HashSet<SceneAsset> alreadyLinked = new HashSet<SceneAsset>();
        foreach (SubScene existing in worldRoot.GetComponentsInChildren<SubScene>(true))
        {
            if (existing.SceneAsset != null)
            {
                alreadyLinked.Add(existing.SceneAsset);
                existing.AutoLoadScene = true;
            }
        }

        foreach (SceneAsset chunkScene in chunkScenes)
        {
            if (chunkScene == null || alreadyLinked.Contains(chunkScene))
            {
                continue;
            }
            GameObject subSceneObject = new GameObject(chunkScene.name);
            subSceneObject.transform.SetParent(worldRoot.transform, false);
            SubScene subScene = subSceneObject.AddComponent<SubScene>();
            subScene.SceneAsset = chunkScene;
            subScene.AutoLoadScene = true;
        }
        EditorSceneManager.MarkSceneDirty(activeScene);
    }

    private static TerrainStrategicProfile FindOrCreateProfile()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:TerrainStrategicProfile"))
        {
            TerrainStrategicProfile found = AssetDatabase.LoadAssetAtPath<TerrainStrategicProfile>(AssetDatabase.GUIDToAssetPath(guid));
            if (found != null)
            {
                return found;
            }
        }
        EnsureFolder(ProfileFolder);
        TerrainStrategicProfile created = ScriptableObject.CreateInstance<TerrainStrategicProfile>();
        AssetDatabase.CreateAsset(created, ProfilePath);
        return created;
    }

    private static Material FindOrCreateSharedMaterial()
    {
        Material shared = AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
        if (shared != null)
        {
            return shared;
        }
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError("[Terrain] Shader " + ShaderName + " not found; strategic layer not baked.");
            return null;
        }
        shared = new Material(shader);
        AssetDatabase.CreateAsset(shared, SharedMaterialPath);
        return shared;
    }

    private static string DataPath(Vector2Int superChunk)
    {
        return StrategicFolder + "/Strategic_" + superChunk.x + "_" + superChunk.y + "_Data.asset";
    }

    private static string ScenePath(Vector2Int superChunk)
    {
        return StrategicFolder + "/Strategic_" + superChunk.x + "_" + superChunk.y + ".unity";
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] segments = folderPath.Split('/');
        string builtPath = segments[0];
        for (int index = 1; index < segments.Length; index++)
        {
            string nextPath = builtPath + "/" + segments[index];
            if (!AssetDatabase.IsValidFolder(nextPath))
            {
                AssetDatabase.CreateFolder(builtPath, segments[index]);
            }
            builtPath = nextPath;
        }
    }
}
#endif
