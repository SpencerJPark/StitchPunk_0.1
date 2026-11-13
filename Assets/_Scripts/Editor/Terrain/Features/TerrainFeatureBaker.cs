#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Turns TerrainArea outlines into per-tile area masks and entries. Later phases add lines, rails and stamps.
[InitializeOnLoad]
public static class TerrainFeatureBaker
{
    private const double DebounceSeconds = 0.25;
    private const int ProgressBarTileThreshold = 2;
    private const int MaximumAreasPerTile = 255;

    private static readonly Dictionary<Component, Bounds> lastBounds = new Dictionary<Component, Bounds>();
    private static readonly List<Bounds> pendingAffectedBounds = new List<Bounds>();
    private static readonly HashSet<Vector2Int> staleTiles = new HashSet<Vector2Int>();
    private static double lastEventTime;
    private static bool updateHooked;

    public static IReadOnlyCollection<Vector2Int> StaleTiles => staleTiles;

    public static event Action BakeFinished;

    static TerrainFeatureBaker()
    {
        TerrainFeatureEvents.FeatureChanged -= OnFeatureChanged;
        TerrainFeatureEvents.FeatureChanged += OnFeatureChanged;
    }

    private static void OnFeatureChanged(Component feature)
    {
        TerrainArea area = feature as TerrainArea;
        if (area == null && !lastBounds.ContainsKey(feature))
        {
            return;
        }

        Bounds previousBounds;
        if (lastBounds.TryGetValue(feature, out previousBounds))
        {
            pendingAffectedBounds.Add(previousBounds);
        }

        if (area == null)
        {
            lastBounds.Remove(feature);
        }
        else
        {
            Bounds newBounds = area.WorldBounds();
            pendingAffectedBounds.Add(newBounds);
            lastBounds[feature] = newBounds;
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

    public static void RebakeAllFeatures(bool openMissingTiles)
    {
        HashSet<Vector2Int> affectedTiles = new HashSet<Vector2Int>();
        foreach (TerrainArea area in FindAllAreas())
        {
            AddTilesOverlapping(area.WorldBounds(), affectedTiles);
        }

        // A tile that already has an area mask must be re-baked so a deleted area clears it
        foreach (string guid in AssetDatabase.FindAssets("_AreaMask t:Texture2D", new string[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Vector2Int coordinate;
            if (TryCoordinateOfAreaMaskPath(path, out coordinate))
            {
                affectedTiles.Add(coordinate);
            }
        }

        BakeTiles(affectedTiles, openMissingTiles);
    }

    public static TerrainAreaStamp StampFromArea(TerrainArea area, byte areaIndex)
    {
        TerrainAreaStamp stamp = new TerrainAreaStamp();
        stamp.outlineXZ = area.OutlineWorldXZ(2f).ToArray();
        stamp.edgeNoiseMetres = area.edgeNoiseMetres;
        stamp.seed = area.seed;
        stamp.priority = area.priority;
        stamp.areaIndex = areaIndex;
        stamp.paintsGround = area.recipe.paintsGround;
        stamp.groundLayer = area.recipe.groundLayer;
        return stamp;
    }

    public static void BakeTile(TerrainTile tile)
    {
        BakeTile(tile, FindAllAreas());
    }

    private static void BakeTile(TerrainTile tile, IReadOnlyList<TerrainArea> allAreas)
    {
        TerrainTileData tileData = tile.tileData;
        if (tileData == null)
        {
            return;
        }

        Vector2Int coordinate = tile.tileCoordinate;
        int resolution = tileData.maskResolution > 0 ? tileData.maskResolution : TerrainSplatCodec.FallbackMaskResolution;

        float tileMinX = coordinate.x * TerrainTileData.TileSizeMetres - 50f;
        float tileMinZ = coordinate.y * TerrainTileData.TileSizeMetres - 50f;
        float tileMaxX = tileMinX + TerrainTileData.TileSizeMetres;
        float tileMaxZ = tileMinZ + TerrainTileData.TileSizeMetres;

        List<TerrainArea> overlappingAreas = new List<TerrainArea>();
        foreach (TerrainArea area in allAreas)
        {
            if (area == null || area.recipe == null)
            {
                continue;
            }

            Bounds areaBounds = area.WorldBounds();
            bool overlapsTile = areaBounds.max.x >= tileMinX && areaBounds.min.x <= tileMaxX
                && areaBounds.max.z >= tileMinZ && areaBounds.min.z <= tileMaxZ;
            if (overlapsTile)
            {
                overlappingAreas.Add(area);
            }
        }

        overlappingAreas = overlappingAreas
            .OrderBy(area => area.priority)
            .ThenBy(area => area.featureId, StringComparer.Ordinal)
            .ToList();
        if (overlappingAreas.Count > MaximumAreasPerTile)
        {
            Debug.LogWarning("Tile " + coordinate + " is overlapped by " + overlappingAreas.Count + " areas; only " + MaximumAreasPerTile + " fit in the area mask, the rest are dropped.");
            overlappingAreas.RemoveRange(MaximumAreasPerTile, overlappingAreas.Count - MaximumAreasPerTile);
        }

        string areaMaskPath = AreaMaskPath(coordinate);
        if (overlappingAreas.Count == 0 && !File.Exists(areaMaskPath))
        {
            tileData.areaEntries = new List<TerrainTileAreaEntry>();
            tileData.areaMask = null;
            return;
        }

        List<TerrainAreaStamp> stamps = new List<TerrainAreaStamp>();
        List<TerrainTileAreaEntry> entries = new List<TerrainTileAreaEntry>();
        for (int areaListIndex = 0; areaListIndex < overlappingAreas.Count; areaListIndex++)
        {
            TerrainArea area = overlappingAreas[areaListIndex];
            stamps.Add(StampFromArea(area, (byte)(areaListIndex + 1)));

            TerrainTileAreaEntry entry = new TerrainTileAreaEntry();
            entry.featureId = area.featureId;
            entry.recipe = area.recipe;
            entry.seed = area.seed;
            entry.cropIndex = area.cropIndex;
            entry.rowDirectionDegrees = area.rowDirectionDegrees;
            entry.densityMultiplier = area.densityMultiplier;
            entries.Add(entry);
        }

        byte[] ownerAreaIndex = new byte[resolution * resolution];
        byte[] edgeDistance = new byte[resolution * resolution];
        TerrainAreaRasterizer.RasteriseAreaMask(stamps, coordinate, resolution, ownerAreaIndex, edgeDistance);

        Color32[] pixels = new Color32[resolution * resolution];
        for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
        {
            byte owner = ownerAreaIndex[pixelIndex];
            byte alpha = owner != TerrainAreaRasterizer.NoArea && stamps[owner - 1].paintsGround ? (byte)255 : (byte)0;
            pixels[pixelIndex] = new Color32(owner, edgeDistance[pixelIndex], 0, alpha);
        }

        TerrainSplatCodec.WritePng(areaMaskPath, pixels, resolution, false);
        tileData.areaMask = AssetDatabase.LoadAssetAtPath<Texture2D>(areaMaskPath);
        tileData.areaEntries = entries;
        EditorUtility.SetDirty(tileData);

        if (TerrainCompositor.Composite(tile))
        {
            TerrainTileBuilder.Rebuild(tile);
        }

        TerrainAreaFillResult fillResult = TerrainAreaFiller.FillTile(tile, stamps);
        if (fillResult.demotedCount > 0)
        {
            Debug.Log("Tile " + coordinate + ": " + fillResult.demotedCount + " trees became cards (interactable cap " + TerrainPlatformProfile.Active.maxInteractablePropsPerTile + ").");
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
            List<TerrainArea> allAreas = FindAllAreas();
            bool showProgress = tilesToBake.Count > ProgressBarTileThreshold;
            try
            {
                for (int tileIndex = 0; tileIndex < tilesToBake.Count; tileIndex++)
                {
                    if (showProgress)
                    {
                        EditorUtility.DisplayProgressBar("Baking features", "Tile " + tilesToBake[tileIndex], tileIndex / (float)tilesToBake.Count);
                    }

                    TerrainTile tile = TerrainTileCreator.FindTileAt(tilesToBake[tileIndex]);
                    if (tile != null)
                    {
                        BakeTile(tile, allAreas);
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
            }
        }

        BakeFinished?.Invoke();
    }

    private static List<TerrainArea> FindAllAreas()
    {
        return new List<TerrainArea>(UnityEngine.Object.FindObjectsByType<TerrainArea>(FindObjectsInactive.Exclude));
    }

    private static string AreaMaskPath(Vector2Int coordinate)
    {
        return TerrainTileAssets.RoadMaskPath(coordinate).Replace("_RoadMask.png", "_AreaMask.png");
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

    private static bool TryCoordinateOfAreaMaskPath(string assetPath, out Vector2Int coordinate)
    {
        coordinate = Vector2Int.zero;
        string fileName = Path.GetFileNameWithoutExtension(assetPath);
        string[] parts = fileName.Split('_');
        // Tile_{x}_{y}_AreaMask
        if (parts.Length != 4 || parts[0] != "Tile" || parts[3] != "AreaMask")
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
        return assetPath == AreaMaskPath(coordinate);
    }
}
#endif
