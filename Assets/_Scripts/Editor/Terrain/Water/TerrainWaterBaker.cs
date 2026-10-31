#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Turns TerrainRiver, TerrainWaterBody and TerrainCrossing objects into per-tile river carve, surface overlay and water meshes.
[InitializeOnLoad]
public static class TerrainWaterBaker
{
    private const double DebounceSeconds = 0.25;
    private const float SampleSpacingMetres = 1f;
    private const float HeightSmoothingMetres = 20f;
    private const float HeightChangeEpsilon = 1e-5f;
    private const float DefaultDeepBlocksBelowDepthMetres = 1.2f;
    private const int ProgressBarTileThreshold = 2;
    private const string WaterChildName = "Water";
    private const string WaterMeshPrefix = "Water_";
    private const string DefaultWaterMaterialPath = "Assets/PaintedGround/Materials/M_PaintedWater.mat";

    private static readonly Dictionary<UnityEngine.Object, Bounds> lastBounds = new Dictionary<UnityEngine.Object, Bounds>();
    private static readonly List<Bounds> pendingAffectedBounds = new List<Bounds>();
    private static readonly HashSet<Vector2Int> staleTiles = new HashSet<Vector2Int>();
    private static readonly Dictionary<Vector2Int, TerrainTileData> tileDataCache = new Dictionary<Vector2Int, TerrainTileData>();
    // Index-aligned with the stamp list handed to BakeTile; stamps carry no profile reference.
    private static List<TerrainWaterProfile> riverProfilesByStampIndex = new List<TerrainWaterProfile>();
    private static double lastEventTime;
    private static bool updateHooked;

    public static IReadOnlyCollection<Vector2Int> StaleTiles => staleTiles;

    public static event Action BakeFinished;

    static TerrainWaterBaker()
    {
        TerrainWaterEvents.RiverChanged -= OnRiverChanged;
        TerrainWaterEvents.RiverChanged += OnRiverChanged;
        TerrainWaterEvents.BodyChanged -= OnBodyChanged;
        TerrainWaterEvents.BodyChanged += OnBodyChanged;
        TerrainWaterEvents.CrossingChanged -= OnCrossingChanged;
        TerrainWaterEvents.CrossingChanged += OnCrossingChanged;
    }

    private static void OnRiverChanged(TerrainRiver river)
    {
        RegisterChange(river, river == null ? default(Bounds) : river.WorldBounds());
    }

    private static void OnBodyChanged(TerrainWaterBody body)
    {
        RegisterChange(body, body == null ? default(Bounds) : body.WorldBounds());
    }

    private static void OnCrossingChanged(TerrainCrossing crossing)
    {
        RegisterChange(crossing, crossing == null ? default(Bounds) : crossing.WorldBounds());
    }

    private static void RegisterChange(UnityEngine.Object changedObject, Bounds newBounds)
    {
        // A destroyed object compares equal to null but is still a usable dictionary key by reference
        Bounds previousBounds;
        if (changedObject != null && lastBounds.TryGetValue(changedObject, out previousBounds))
        {
            pendingAffectedBounds.Add(previousBounds);
        }

        if (changedObject == null)
        {
            // Dictionary lookups on destroyed keys still work by hash; drop stale entries lazily at bake time
            List<UnityEngine.Object> deadKeys = new List<UnityEngine.Object>();
            foreach (KeyValuePair<UnityEngine.Object, Bounds> entry in lastBounds)
            {
                if (entry.Key == null)
                {
                    pendingAffectedBounds.Add(entry.Value);
                    deadKeys.Add(entry.Key);
                }
            }
            foreach (UnityEngine.Object deadKey in deadKeys)
            {
                lastBounds.Remove(deadKey);
            }
        }
        else
        {
            pendingAffectedBounds.Add(newBounds);
            lastBounds[changedObject] = newBounds;
        }

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

        HashSet<Vector2Int> affectedTiles = new HashSet<Vector2Int>();
        foreach (Bounds affectedBounds in pendingAffectedBounds)
        {
            AddTilesOverlapping(affectedBounds, affectedTiles);
        }
        pendingAffectedBounds.Clear();

        BakeTiles(affectedTiles, false);
    }

    public static void RebakeAllWater(bool openMissingTiles)
    {
        HashSet<Vector2Int> affectedTiles = new HashSet<Vector2Int>();
        AddTilesOfEverySource(affectedTiles);

        // A tile that already has a surface overlay must be re-baked so deleted water is cleared
        foreach (string guid in AssetDatabase.FindAssets("_SurfaceOverlay t:Texture2D", new string[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Vector2Int coordinate;
            if (TryCoordinateOfOverlayPath(path, out coordinate))
            {
                affectedTiles.Add(coordinate);
            }
        }

        BakeTiles(affectedTiles, openMissingTiles);
    }

    public static List<Vector2Int> MissingTilesUnderWater()
    {
        HashSet<Vector2Int> coveredTiles = new HashSet<Vector2Int>();
        AddTilesOfEverySource(coveredTiles);

        List<Vector2Int> missingTiles = new List<Vector2Int>();
        foreach (Vector2Int coordinate in coveredTiles)
        {
            if (!TileSceneExists(coordinate))
            {
                missingTiles.Add(coordinate);
            }
        }
        return missingTiles;
    }

    private static void AddTilesOfEverySource(HashSet<Vector2Int> tilesOut)
    {
        foreach (TerrainRiver river in FindAllRivers())
        {
            if (river.profile != null)
            {
                AddTilesOverlapping(river.WorldBounds(), tilesOut);
            }
        }
        foreach (TerrainWaterBody body in FindAllBodies())
        {
            AddTilesOverlapping(body.WorldBounds(), tilesOut);
        }
        foreach (TerrainCrossing crossing in FindAllCrossings())
        {
            AddTilesOverlapping(crossing.WorldBounds(), tilesOut);
        }
    }

    public static TerrainRiverStamp StampFromRiver(TerrainRiver river)
    {
        TerrainWaterProfile profile = river.profile;
        List<Vector3> points = new List<Vector3>();
        UnityEngine.Splines.SplineContainer container = river.Container;
        for (int splineIndex = 0; splineIndex < container.Splines.Count; splineIndex++)
        {
            float length = container.CalculateLength(splineIndex);
            int steps = Mathf.Max(2, Mathf.CeilToInt(length / SampleSpacingMetres));
            for (int step = 0; step <= steps; step++)
            {
                // EvaluatePosition on the container already returns world space
                Unity.Mathematics.float3 position = container.EvaluatePosition(splineIndex, step / (float)steps);
                points.Add(new Vector3(position.x, HeightWithoutRiverAt(position.x, position.z), position.z));
            }
        }

        Vector3[] pointArray = points.ToArray();
        TerrainRoadRasterizer.SmoothPointHeights(pointArray, HeightSmoothingMetres);

        float[] smoothedHeights = new float[pointArray.Length];
        float[] cumulativeLengths = new float[pointArray.Length];
        for (int pointIndex = 0; pointIndex < pointArray.Length; pointIndex++)
        {
            smoothedHeights[pointIndex] = pointArray[pointIndex].y;
            if (pointIndex > 0)
            {
                Vector3 step = pointArray[pointIndex] - pointArray[pointIndex - 1];
                cumulativeLengths[pointIndex] = cumulativeLengths[pointIndex - 1] + new Vector2(step.x, step.z).magnitude;
            }
        }

        float totalLength = pointArray.Length > 0 ? cumulativeLengths[pointArray.Length - 1] : 0f;
        float[] widths = new float[pointArray.Length];
        for (int pointIndex = 0; pointIndex < pointArray.Length; pointIndex++)
        {
            float fraction = totalLength > 0f ? cumulativeLengths[pointIndex] / totalLength : 0f;
            widths[pointIndex] = profile.WidthAt(fraction);
        }

        TerrainRiverStamp stamp = new TerrainRiverStamp();
        stamp.points = pointArray;
        stamp.widths = widths;
        stamp.waterLevels = TerrainRiverBuilder.ComputeWaterLevels(pointArray, smoothedHeights, profile.bedDepthMetres, river.startWaterLevelMetres);
        stamp.bedDepthMetres = profile.bedDepthMetres;
        stamp.bankSlopeMetres = profile.bankSlopeMetres;
        stamp.shallowBandMetres = profile.shallowBandMetres;
        stamp.deepBlocksBelowDepthMetres = profile.deepBlocksBelowDepthMetres;
        return stamp;
    }

    public static TerrainWaterBodyStamp StampFromBody(TerrainWaterBody body)
    {
        TerrainWaterBodyStamp stamp = new TerrainWaterBodyStamp();
        stamp.boundsMin = body.boundsMin;
        stamp.boundsMax = body.boundsMax;
        stamp.waterLevelMetres = body.waterLevelMetres;
        stamp.deepBlocksBelowDepthMetres = body.profile != null ? body.profile.deepBlocksBelowDepthMetres : DefaultDeepBlocksBelowDepthMetres;
        return stamp;
    }

    private static float HeightWithoutRiverAt(float worldX, float worldZ)
    {
        Vector2Int coordinate = TerrainTile.CoordinateContaining(new Vector3(worldX, 0f, worldZ));
        TerrainTileData tileData;
        if (!tileDataCache.TryGetValue(coordinate, out tileData))
        {
            TerrainTile openTile = TerrainTileCreator.FindTileAt(coordinate);
            tileData = openTile != null && openTile.tileData != null
                ? openTile.tileData
                : AssetDatabase.LoadAssetAtPath<TerrainTileData>(TerrainTileAssets.TileDataPath(coordinate));
            tileDataCache[coordinate] = tileData;
        }

        return tileData == null ? 0f : tileData.SampleBilinearExcludingRiver(worldX, worldZ);
    }

    public static void BakeTile(
        TerrainTile tile,
        IReadOnlyList<TerrainRiverStamp> rivers,
        IReadOnlyList<(TerrainWaterBody body, TerrainWaterBodyStamp stamp)> bodies,
        IReadOnlyList<TerrainCrossing> crossings)
    {
        TerrainTileData tileData = tile.tileData;
        if (tileData == null)
        {
            return;
        }

        Vector2Int coordinate = tile.tileCoordinate;
        int resolution = tileData.maskResolution > 0 ? tileData.maskResolution : TerrainSplatCodec.FallbackMaskResolution;
        int samplesPerSide = TerrainTileData.HeightSamplesPerSide;

        tileData.EnsureHeightArrays();
        float[] terrainWithoutRiver = new float[TerrainTileData.HeightSampleCount];
        for (int sampleZ = 0; sampleZ < samplesPerSide; sampleZ++)
        {
            for (int sampleX = 0; sampleX < samplesPerSide; sampleX++)
            {
                terrainWithoutRiver[sampleZ * samplesPerSide + sampleX] = tileData.HeightExcludingRiverAt(sampleX, sampleZ);
            }
        }

        float[] newDelta = new float[TerrainTileData.HeightSampleCount];
        TerrainRiverBuilder.ApplyRiverCarve(rivers, coordinate, terrainWithoutRiver, newDelta);

        bool heightsChanged = false;
        for (int sampleIndex = 0; sampleIndex < newDelta.Length; sampleIndex++)
        {
            if (Mathf.Abs(newDelta[sampleIndex] - tileData.riverHeightDelta[sampleIndex]) > HeightChangeEpsilon)
            {
                heightsChanged = true;
                break;
            }
        }

        if (heightsChanged)
        {
            Array.Copy(newDelta, tileData.riverHeightDelta, newDelta.Length);
        }

        float[] ground = new float[TerrainTileData.HeightSampleCount];
        for (int sampleIndex = 0; sampleIndex < ground.Length; sampleIndex++)
        {
            ground[sampleIndex] = terrainWithoutRiver[sampleIndex] + newDelta[sampleIndex];
        }

        byte[] shallow = new byte[resolution * resolution];
        byte[] deep = new byte[resolution * resolution];
        TerrainRiverBuilder.RasteriseSurfaceMasks(rivers, coordinate, resolution, ground, shallow, deep);
        foreach ((TerrainWaterBody body, TerrainWaterBodyStamp stamp) bodyEntry in bodies)
        {
            TerrainWaterBodyBuilder.RasteriseSurfaceMasks(bodyEntry.stamp, coordinate, resolution, ground, shallow, deep);
        }

        for (int texelIndex = 0; texelIndex < deep.Length; texelIndex++)
        {
            if (deep[texelIndex] != 0)
            {
                shallow[texelIndex] = 0;
            }
        }

        ApplyCrossings(crossings, coordinate, resolution, shallow, deep);

        Color32[] pixels = new Color32[resolution * resolution];
        bool anyWaterTexel = false;
        for (int texelIndex = 0; texelIndex < pixels.Length; texelIndex++)
        {
            pixels[texelIndex] = new Color32(shallow[texelIndex], deep[texelIndex], 0, 255);
            if (shallow[texelIndex] != 0 || deep[texelIndex] != 0)
            {
                anyWaterTexel = true;
            }
        }

        string overlayPath = TerrainTileAssets.SurfaceOverlayPath(coordinate);
        if (anyWaterTexel || File.Exists(overlayPath))
        {
            TerrainSplatCodec.WritePng(overlayPath, pixels, resolution, false);
            tileData.surfaceOverlay = AssetDatabase.LoadAssetAtPath<Texture2D>(overlayPath);
        }

        BuildWaterMeshes(tile, tileData, rivers, bodies, ground);

        tileData.BumpRevision();
        EditorUtility.SetDirty(tileData);
        TerrainCompositor.Composite(tile);
        if (heightsChanged)
        {
            TerrainTileBuilder.Rebuild(tile);
        }

        EditorSceneManager.SaveScene(tile.gameObject.scene);
    }

    private static void ApplyCrossings(IReadOnlyList<TerrainCrossing> crossings, Vector2Int coordinate, int resolution, byte[] shallow, byte[] deep)
    {
        float tileMinX = coordinate.x * TerrainTileData.TileSizeMetres - TerrainTileData.TileSizeMetres * 0.5f;
        float tileMinZ = coordinate.y * TerrainTileData.TileSizeMetres - TerrainTileData.TileSizeMetres * 0.5f;
        float tileMaxX = tileMinX + TerrainTileData.TileSizeMetres;
        float tileMaxZ = tileMinZ + TerrainTileData.TileSizeMetres;
        float texelSizeMetres = TerrainTileData.TileSizeMetres / resolution;

        foreach (TerrainCrossing crossing in crossings)
        {
            Bounds crossingBounds = crossing.WorldBounds();
            bool touchesTile = crossingBounds.max.x >= tileMinX && crossingBounds.min.x <= tileMaxX
                && crossingBounds.max.z >= tileMinZ && crossingBounds.min.z <= tileMaxZ;
            if (!touchesTile)
            {
                continue;
            }

            for (int row = 0; row < resolution; row++)
            {
                float worldZ = tileMinZ + (row + 0.5f) * texelSizeMetres;
                for (int column = 0; column < resolution; column++)
                {
                    float worldX = tileMinX + (column + 0.5f) * texelSizeMetres;
                    if (!crossing.ContainsXZ(worldX, worldZ))
                    {
                        continue;
                    }

                    int texelIndex = row * resolution + column;
                    if (crossing.kind == TerrainCrossingKind.Bridge)
                    {
                        deep[texelIndex] = 0;
                        shallow[texelIndex] = 0;
                    }
                    else if (deep[texelIndex] != 0 || shallow[texelIndex] != 0)
                    {
                        deep[texelIndex] = 0;
                        shallow[texelIndex] = 255;
                    }
                }
            }
        }
    }

    private static void BuildWaterMeshes(
        TerrainTile tile,
        TerrainTileData tileData,
        IReadOnlyList<TerrainRiverStamp> rivers,
        IReadOnlyList<(TerrainWaterBody body, TerrainWaterBodyStamp stamp)> bodies,
        float[] ground)
    {
        Vector2Int coordinate = tile.tileCoordinate;
        List<Mesh> meshes = new List<Mesh>();
        List<Material> materials = new List<Material>();

        for (int riverIndex = 0; riverIndex < rivers.Count; riverIndex++)
        {
            TerrainWaterProfile profile = riverIndex < riverProfilesByStampIndex.Count ? riverProfilesByStampIndex[riverIndex] : null;
            float offset = profile != null ? profile.surfaceOffsetMetres : 0f;
            Mesh mesh = TerrainRiverBuilder.BuildRibbonMeshForTile(rivers[riverIndex], coordinate, offset);
            if (mesh != null)
            {
                meshes.Add(mesh);
                materials.Add(MaterialOfProfile(profile));
            }
        }

        foreach ((TerrainWaterBody body, TerrainWaterBodyStamp stamp) bodyEntry in bodies)
        {
            float offset = bodyEntry.body.profile != null ? bodyEntry.body.profile.surfaceOffsetMetres : 0f;
            Mesh mesh = TerrainWaterBodyBuilder.BuildSurfaceMeshForTile(bodyEntry.stamp, coordinate, ground, offset);
            if (mesh != null)
            {
                meshes.Add(mesh);
                materials.Add(MaterialOfProfile(bodyEntry.body.profile));
            }
        }

        string dataPath = TerrainTileAssets.TileDataPath(coordinate);
        Dictionary<string, Mesh> existingMeshes = new Dictionary<string, Mesh>();
        foreach (UnityEngine.Object subAsset in AssetDatabase.LoadAllAssetsAtPath(dataPath))
        {
            Mesh existingMesh = subAsset as Mesh;
            if (existingMesh != null && existingMesh.name.StartsWith(WaterMeshPrefix))
            {
                existingMeshes[existingMesh.name] = existingMesh;
            }
        }

        Transform waterRoot = tile.transform.Find(WaterChildName);
        if (waterRoot == null)
        {
            GameObject waterRootObject = new GameObject(WaterChildName);
            waterRootObject.transform.SetParent(tile.transform, false);
            waterRoot = waterRootObject.transform;
        }

        HashSet<string> usedNames = new HashSet<string>();
        for (int meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
        {
            string meshName = WaterMeshPrefix + meshIndex;
            usedNames.Add(meshName);

            Mesh persistedMesh;
            if (existingMeshes.TryGetValue(meshName, out persistedMesh))
            {
                EditorUtility.CopySerialized(meshes[meshIndex], persistedMesh);
                persistedMesh.name = meshName;
                UnityEngine.Object.DestroyImmediate(meshes[meshIndex]);
            }
            else
            {
                persistedMesh = meshes[meshIndex];
                persistedMesh.name = meshName;
                AssetDatabase.AddObjectToAsset(persistedMesh, tileData);
            }

            Transform child = waterRoot.Find(meshName);
            if (child == null)
            {
                GameObject childObject = new GameObject(meshName, typeof(MeshFilter), typeof(MeshRenderer));
                childObject.transform.SetParent(waterRoot, false);
                child = childObject.transform;
            }

            child.GetComponent<MeshFilter>().sharedMesh = persistedMesh;
            child.GetComponent<MeshRenderer>().sharedMaterial = materials[meshIndex];
        }

        foreach (KeyValuePair<string, Mesh> existing in existingMeshes)
        {
            if (!usedNames.Contains(existing.Key))
            {
                existing.Value.Clear();
            }
        }

        List<GameObject> obsoleteChildren = new List<GameObject>();
        foreach (Transform child in waterRoot)
        {
            if (!usedNames.Contains(child.name))
            {
                obsoleteChildren.Add(child.gameObject);
            }
        }
        foreach (GameObject obsoleteChild in obsoleteChildren)
        {
            UnityEngine.Object.DestroyImmediate(obsoleteChild);
        }

        EditorUtility.SetDirty(tileData);
    }

    private static Material MaterialOfProfile(TerrainWaterProfile profile)
    {
        if (profile != null && profile.waterMaterial != null)
        {
            return profile.waterMaterial;
        }
        return AssetDatabase.LoadAssetAtPath<Material>(DefaultWaterMaterialPath);
    }

    private static void BakeTiles(HashSet<Vector2Int> affectedTiles, bool openMissingTiles)
    {
        List<Vector2Int> tilesToBake = new List<Vector2Int>();
        foreach (Vector2Int coordinate in affectedTiles)
        {
            if (!TileSceneExists(coordinate))
            {
                continue;
            }

            if (TerrainTileCreator.FindTileAt(coordinate) == null && openMissingTiles)
            {
                TerrainTilesPanel.OpenTileForEdit(coordinate);
            }

            if (TerrainTileCreator.FindTileAt(coordinate) != null)
            {
                tilesToBake.Add(coordinate);
            }
            else
            {
                staleTiles.Add(coordinate);
            }
        }

        if (tilesToBake.Count > 0)
        {
            tileDataCache.Clear();
            List<TerrainRiverStamp> riverStamps = new List<TerrainRiverStamp>();
            riverProfilesByStampIndex = new List<TerrainWaterProfile>();
            foreach (TerrainRiver river in FindAllRivers())
            {
                if (river.profile != null)
                {
                    riverStamps.Add(StampFromRiver(river));
                    riverProfilesByStampIndex.Add(river.profile);
                }
            }

            List<(TerrainWaterBody body, TerrainWaterBodyStamp stamp)> bodyStamps = new List<(TerrainWaterBody body, TerrainWaterBodyStamp stamp)>();
            foreach (TerrainWaterBody body in FindAllBodies())
            {
                bodyStamps.Add((body, StampFromBody(body)));
            }
            List<TerrainCrossing> crossings = FindAllCrossings();

            bool showProgress = tilesToBake.Count > ProgressBarTileThreshold;
            try
            {
                for (int tileIndex = 0; tileIndex < tilesToBake.Count; tileIndex++)
                {
                    if (showProgress)
                    {
                        EditorUtility.DisplayProgressBar("Baking water", "Tile " + tilesToBake[tileIndex], tileIndex / (float)tilesToBake.Count);
                    }

                    TerrainTile tile = TerrainTileCreator.FindTileAt(tilesToBake[tileIndex]);
                    if (tile != null)
                    {
                        BakeTile(tile, riverStamps, bodyStamps, crossings);
                        staleTiles.Remove(tilesToBake[tileIndex]);
                    }
                }
            }
            finally
            {
                if (showProgress)
                {
                    EditorUtility.ClearProgressBar();
                }
                tileDataCache.Clear();
            }
        }

        BakeFinished?.Invoke();
    }

    private static List<TerrainRiver> FindAllRivers()
    {
        return new List<TerrainRiver>(UnityEngine.Object.FindObjectsByType<TerrainRiver>(FindObjectsInactive.Exclude));
    }

    private static List<TerrainWaterBody> FindAllBodies()
    {
        return new List<TerrainWaterBody>(UnityEngine.Object.FindObjectsByType<TerrainWaterBody>(FindObjectsInactive.Exclude));
    }

    private static List<TerrainCrossing> FindAllCrossings()
    {
        return new List<TerrainCrossing>(UnityEngine.Object.FindObjectsByType<TerrainCrossing>(FindObjectsInactive.Exclude));
    }

    private static bool TileSceneExists(Vector2Int coordinate)
    {
        return AssetDatabase.LoadAssetAtPath<SceneAsset>(TerrainTileAssets.TileScenePath(coordinate)) != null;
    }

    private static void AddTilesOverlapping(Bounds worldBounds, HashSet<Vector2Int> tilesOut)
    {
        int minTileX = Mathf.FloorToInt((worldBounds.min.x + 50f) / TerrainTileData.TileSizeMetres);
        int maxTileX = Mathf.FloorToInt((worldBounds.max.x + 50f) / TerrainTileData.TileSizeMetres);
        int minTileZ = Mathf.FloorToInt((worldBounds.min.z + 50f) / TerrainTileData.TileSizeMetres);
        int maxTileZ = Mathf.FloorToInt((worldBounds.max.z + 50f) / TerrainTileData.TileSizeMetres);
        for (int tileZ = minTileZ; tileZ <= maxTileZ; tileZ++)
        {
            for (int tileX = minTileX; tileX <= maxTileX; tileX++)
            {
                tilesOut.Add(new Vector2Int(tileX, tileZ));
            }
        }
    }

    private static bool TryCoordinateOfOverlayPath(string assetPath, out Vector2Int coordinate)
    {
        coordinate = Vector2Int.zero;
        string fileName = Path.GetFileNameWithoutExtension(assetPath);
        string[] parts = fileName.Split('_');
        // Tile_{x}_{y}_SurfaceOverlay
        if (parts.Length != 4 || parts[0] != "Tile" || parts[3] != "SurfaceOverlay")
        {
            return false;
        }

        int coordinateX;
        int coordinateY;
        if (!int.TryParse(parts[1], out coordinateX) || !int.TryParse(parts[2], out coordinateY))
        {
            return false;
        }

        coordinate = new Vector2Int(coordinateX, coordinateY);
        return assetPath == TerrainTileAssets.SurfaceOverlayPath(coordinate);
    }
}
#endif
