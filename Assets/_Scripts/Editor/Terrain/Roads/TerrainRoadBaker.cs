#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Turns TerrainRoad splines into per-tile road masks and height deltas. Decal scattering along roads is phase P6.
[InitializeOnLoad]
public static class TerrainRoadBaker
{
    private const double DebounceSeconds = 0.25;
    private const float SampleSpacingMetres = 0.5f;
    private const float HeightChangeEpsilon = 1e-5f;
    private const int ProgressBarTileThreshold = 2;

    private static readonly Dictionary<TerrainRoad, Bounds> lastBounds = new Dictionary<TerrainRoad, Bounds>();
    private static readonly List<Bounds> pendingAffectedBounds = new List<Bounds>();
    private static readonly HashSet<Vector2Int> staleTiles = new HashSet<Vector2Int>();
    private static readonly Dictionary<Vector2Int, TerrainTileData> tileDataCache = new Dictionary<Vector2Int, TerrainTileData>();
    private static double lastEventTime;
    private static bool updateHooked;

    public static IReadOnlyCollection<Vector2Int> StaleTiles => staleTiles;

    public static event Action BakeFinished;

    static TerrainRoadBaker()
    {
        TerrainRoadEvents.RoadChanged -= OnRoadChanged;
        TerrainRoadEvents.RoadChanged += OnRoadChanged;
    }

    private static void OnRoadChanged(TerrainRoad road)
    {
        Bounds previousBounds;
        if (lastBounds.TryGetValue(road, out previousBounds))
        {
            pendingAffectedBounds.Add(previousBounds);
        }

        if (road == null)
        {
            lastBounds.Remove(road);
        }
        else
        {
            Bounds newBounds = road.WorldBounds();
            pendingAffectedBounds.Add(newBounds);
            lastBounds[road] = newBounds;
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

    public static void RebakeAllRoads(bool openMissingTiles)
    {
        HashSet<Vector2Int> affectedTiles = new HashSet<Vector2Int>();
        foreach (TerrainRoad road in FindAllRoads())
        {
            AddTilesOverlapping(road.WorldBounds(), affectedTiles);
        }

        // A tile that already has a road mask must be re-baked so a deleted road clears it
        foreach (string guid in AssetDatabase.FindAssets("_RoadMask t:Texture2D", new string[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Vector2Int coordinate;
            if (TryCoordinateOfRoadMaskPath(path, out coordinate))
            {
                affectedTiles.Add(coordinate);
            }
        }

        BakeTiles(affectedTiles, openMissingTiles);
    }

    public static List<Vector2Int> MissingTilesUnderRoads()
    {
        HashSet<Vector2Int> coveredTiles = new HashSet<Vector2Int>();
        foreach (TerrainRoad road in FindAllRoads())
        {
            AddTilesOverlapping(road.WorldBounds(), coveredTiles);
        }

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

    public static TerrainRoadStamp StampFromRoad(TerrainRoad road)
    {
        TerrainRoadProfile profile = road.profile;
        List<Vector3> points = new List<Vector3>();
        UnityEngine.Splines.SplineContainer container = road.Container;
        for (int splineIndex = 0; splineIndex < container.Splines.Count; splineIndex++)
        {
            float length = container.CalculateLength(splineIndex);
            int steps = Mathf.Max(2, Mathf.CeilToInt(length / SampleSpacingMetres));
            for (int step = 0; step <= steps; step++)
            {
                // EvaluatePosition on the container already returns world space
                Unity.Mathematics.float3 position = container.EvaluatePosition(splineIndex, step / (float)steps);
                points.Add(new Vector3(position.x, HeightWithoutRoadAt(position.x, position.z), position.z));
            }
        }

        Vector3[] pointArray = points.ToArray();
        TerrainRoadRasterizer.SmoothPointHeights(pointArray, profile.heightSmoothingMetres);

        TerrainRoadStamp stamp = new TerrainRoadStamp();
        stamp.points = pointArray;
        stamp.widthMetres = road.EffectiveWidthMetres;
        stamp.shoulderMetres = profile.shoulderMetres;
        stamp.edgeNoiseMetres = profile.edgeNoiseMetres;
        stamp.carveShoulderMetres = profile.carveShoulderMetres;
        stamp.carveHeight = profile.carveHeight;
        stamp.carveBlendCurve = profile.carveBlendCurve;
        stamp.surfaceLayer = profile.surfaceLayer;
        stamp.priority = road.priority;
        return stamp;
    }

    private static float HeightWithoutRoadAt(float worldX, float worldZ)
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

        return tileData == null ? 0f : tileData.SampleBilinearExcludingRoad(worldX, worldZ);
    }

    public static void BakeTile(TerrainTile tile, IReadOnlyList<TerrainRoadStamp> stamps)
    {
        TerrainTileData tileData = tile.tileData;
        if (tileData == null)
        {
            return;
        }

        Vector2Int coordinate = tile.tileCoordinate;
        int resolution = tileData.maskResolution > 0 ? tileData.maskResolution : TerrainSplatCodec.FallbackMaskResolution;

        List<TerrainRoadStamp> touchingStamps = new List<TerrainRoadStamp>();
        float tileMinX = coordinate.x * TerrainTileData.TileSizeMetres - 50f;
        float tileMinZ = coordinate.y * TerrainTileData.TileSizeMetres - 50f;
        float tileMaxX = tileMinX + TerrainTileData.TileSizeMetres;
        float tileMaxZ = tileMinZ + TerrainTileData.TileSizeMetres;
        foreach (TerrainRoadStamp stamp in stamps)
        {
            Bounds stampBounds = TerrainRoadRasterizer.BoundsOf(stamp);
            bool overlapsTile = stampBounds.max.x >= tileMinX && stampBounds.min.x <= tileMaxX
                && stampBounds.max.z >= tileMinZ && stampBounds.min.z <= tileMaxZ;
            if (overlapsTile)
            {
                touchingStamps.Add(stamp);
            }
        }

        byte[] coverage = new byte[resolution * resolution];
        byte[] owner = new byte[resolution * resolution];
        TerrainRoadRasterizer.RasteriseRoadMask(touchingStamps, coordinate, resolution, coverage, owner);

        Color32[] pixels = new Color32[resolution * resolution];
        bool anyTexelCovered = false;
        for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
        {
            if (owner[pixelIndex] != 255)
            {
                pixels[pixelIndex] = TerrainSplatCodec.ColourOfLayer(touchingStamps[owner[pixelIndex]].surfaceLayer);
                anyTexelCovered = true;
            }
        }

        string roadMaskPath = TerrainTileAssets.RoadMaskPath(coordinate);
        if (anyTexelCovered || File.Exists(roadMaskPath))
        {
            TerrainSplatCodec.WritePng(roadMaskPath, pixels, resolution, false);
            tileData.roadMask = AssetDatabase.LoadAssetAtPath<Texture2D>(roadMaskPath);
        }

        tileData.EnsureHeightArrays();
        float[] terrainWithoutRoad = new float[TerrainTileData.HeightSampleCount];
        for (int sampleZ = 0; sampleZ < TerrainTileData.HeightSamplesPerSide; sampleZ++)
        {
            for (int sampleX = 0; sampleX < TerrainTileData.HeightSamplesPerSide; sampleX++)
            {
                terrainWithoutRoad[sampleZ * TerrainTileData.HeightSamplesPerSide + sampleX] = tileData.HeightExcludingRoadAt(sampleX, sampleZ);
            }
        }

        float[] newDelta = new float[TerrainTileData.HeightSampleCount];
        TerrainRoadRasterizer.ApplyHeightCarve(touchingStamps, coordinate, terrainWithoutRoad, newDelta);

        bool heightsChanged = false;
        for (int sampleIndex = 0; sampleIndex < newDelta.Length; sampleIndex++)
        {
            if (Mathf.Abs(newDelta[sampleIndex] - tileData.roadHeightDelta[sampleIndex]) > HeightChangeEpsilon)
            {
                heightsChanged = true;
                break;
            }
        }

        if (heightsChanged)
        {
            Array.Copy(newDelta, tileData.roadHeightDelta, newDelta.Length);
        }

        tileData.BumpRevision();
        EditorUtility.SetDirty(tileData);
        TerrainCompositor.Composite(tile);
        if (heightsChanged)
        {
            TerrainTileBuilder.Rebuild(tile);
        }

        EditorSceneManager.SaveScene(tile.gameObject.scene);
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
            List<TerrainRoadStamp> stamps = new List<TerrainRoadStamp>();
            foreach (TerrainRoad road in FindAllRoads())
            {
                if (road.profile != null)
                {
                    stamps.Add(StampFromRoad(road));
                }
            }

            bool showProgress = tilesToBake.Count > ProgressBarTileThreshold;
            try
            {
                for (int tileIndex = 0; tileIndex < tilesToBake.Count; tileIndex++)
                {
                    if (showProgress)
                    {
                        EditorUtility.DisplayProgressBar("Baking roads", "Tile " + tilesToBake[tileIndex], tileIndex / (float)tilesToBake.Count);
                    }

                    TerrainTile tile = TerrainTileCreator.FindTileAt(tilesToBake[tileIndex]);
                    if (tile != null)
                    {
                        BakeTile(tile, stamps);
                        staleTiles.Remove(tilesToBake[tileIndex]);
                    }
                }

                // Second pass: ribbon heights sample neighbour tiles, whose roadHeightDelta is stale until they are baked.
                foreach (Vector2Int bakedCoordinate in tilesToBake)
                {
                    TerrainTile bakedTile = TerrainTileCreator.FindTileAt(bakedCoordinate);
                    if (bakedTile != null)
                    {
                        TerrainRoadRibbonBaker.BakeTileRibbons(bakedTile);
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

    private static List<TerrainRoad> FindAllRoads()
    {
        return new List<TerrainRoad>(UnityEngine.Object.FindObjectsByType<TerrainRoad>(FindObjectsInactive.Exclude));
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

    private static bool TryCoordinateOfRoadMaskPath(string assetPath, out Vector2Int coordinate)
    {
        coordinate = Vector2Int.zero;
        string fileName = Path.GetFileNameWithoutExtension(assetPath);
        string[] parts = fileName.Split('_');
        // Tile_{x}_{y}_RoadMask
        if (parts.Length != 4 || parts[0] != "Tile" || parts[3] != "RoadMask")
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
        return assetPath == TerrainTileAssets.RoadMaskPath(coordinate);
    }
}
#endif
