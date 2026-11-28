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
    private static bool pendingOpenMissingTiles;

    public static IReadOnlyCollection<Vector2Int> StaleTiles => staleTiles;

    public static event Action BakeFinished;

    static TerrainFeatureBaker()
    {
        TerrainFeatureEvents.FeatureChanged -= OnFeatureChanged;
        TerrainFeatureEvents.FeatureChanged += OnFeatureChanged;
    }

    private static void OnFeatureChanged(Component feature)
    {
        if (TerrainRebakeGate.SecondsSinceLastLoadEvent < 30.0) Debug.Log("[TerrainRebake] startup event: feature " + (feature != null ? feature.name : "destroyed"));
        TerrainArea area = feature as TerrainArea;
        TerrainPath path = feature as TerrainPath;
        if (area == null && path == null && !lastBounds.ContainsKey(feature))
        {
            return;
        }

        Bounds previousBounds;
        if (lastBounds.TryGetValue(feature, out previousBounds))
        {
            pendingAffectedBounds.Add(previousBounds);
        }
        else
        {
            // Cold cache after a domain reload or scene load: ask the baked tile data where this feature last baked
            TerrainArea featureAsArea = feature as TerrainArea;
            TerrainPath featureAsPath = feature as TerrainPath;
            if (!ReferenceEquals(featureAsArea, null))
            {
                AddTilesLastBakedWithFeature(featureAsArea.featureId);
            }
            else if (!ReferenceEquals(featureAsPath, null))
            {
                AddOpenTilesScatteredByPath(featureAsPath.featureId);
            }
        }

        if (area != null)
        {
            Bounds newBounds = area.WorldBounds();
            pendingAffectedBounds.Add(newBounds);
            lastBounds[feature] = newBounds;
        }
        else if (path != null)
        {
            Bounds newBounds = path.WorldBounds();
            pendingAffectedBounds.Add(newBounds);
            lastBounds[feature] = newBounds;
        }
        else
        {
            lastBounds.Remove(feature);
        }

        // The feature being edited must show its result, so its closed tiles get opened; scene loads and unselected features do not
        if (feature != null && UnityEditor.Selection.activeGameObject == feature.gameObject)
        {
            pendingOpenMissingTiles = true;
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

        bool openMissingTiles = pendingOpenMissingTiles;
        pendingOpenMissingTiles = false;
        BakeTiles(affectedTiles, openMissingTiles);
    }

    public static void RebakeAllFeatures(bool openMissingTiles)
    {
        HashSet<Vector2Int> affectedTiles = new HashSet<Vector2Int>();
        foreach (TerrainArea area in FindAllAreas())
        {
            AddTilesOverlapping(area.WorldBounds(), affectedTiles);
        }

        foreach (TerrainPath path in FindAllPaths())
        {
            AddTilesOverlapping(path.WorldBounds(), affectedTiles);
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
        BakeTile(tile, FindAllAreas(), FindAllPaths());
    }

    private static void BakeTile(TerrainTile tile, IReadOnlyList<TerrainArea> allAreas, IReadOnlyList<TerrainPath> allPaths)
    {
        TerrainTileData tileData = tile.tileData;
        if (tileData == null)
        {
            return;
        }

        BakeAreaPass(tile, tileData, allAreas);
        TerrainPathBaker.BakeTile(tile, allPaths);
        EditorSceneManager.SaveScene(tile.gameObject.scene);
    }

    private static void BakeAreaPass(TerrainTile tile, TerrainTileData tileData, IReadOnlyList<TerrainArea> allAreas)
    {
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
            List<TerrainPath> allPaths = FindAllPaths();
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
                        BakeTile(tile, allAreas, allPaths);
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

    private static List<TerrainPath> FindAllPaths()
    {
        return new List<TerrainPath>(UnityEngine.Object.FindObjectsByType<TerrainPath>(FindObjectsInactive.Exclude));
    }

    private static string AreaMaskPath(Vector2Int coordinate)
    {
        return TerrainTileAssets.RoadMaskPath(coordinate).Replace("_RoadMask.png", "_AreaMask.png");
    }

    private static bool TileSceneExists(Vector2Int coordinate)
    {
        return AssetDatabase.LoadAssetAtPath<SceneAsset>(TerrainTileAssets.TileScenePath(coordinate)) != null;
    }

    private static void AddTilesLastBakedWithFeature(string featureId)
    {
        if (string.IsNullOrEmpty(featureId))
        {
            return;
        }

        foreach (string guid in AssetDatabase.FindAssets("t:TerrainTileData"))
        {
            TerrainTileData tileData = AssetDatabase.LoadAssetAtPath<TerrainTileData>(AssetDatabase.GUIDToAssetPath(guid));
            if (tileData == null || tileData.areaEntries == null)
            {
                continue;
            }

            foreach (TerrainTileAreaEntry entry in tileData.areaEntries)
            {
                if (entry.featureId != featureId)
                {
                    continue;
                }

                pendingAffectedBounds.Add(InsideTileBounds(tileData.coordinate));
                break;
            }
        }
    }

    private static Bounds InsideTileBounds(Vector2Int coordinate)
    {
        float tileMinX = coordinate.x * TerrainTileData.TileSizeMetres - 50f;
        float tileMinZ = coordinate.y * TerrainTileData.TileSizeMetres - 50f;
        float insetCentreOffset = TerrainTileData.TileSizeMetres * 0.5f;
        Vector3 insideTileCentre = new Vector3(tileMinX + insetCentreOffset, 0f, tileMinZ + insetCentreOffset);
        Vector3 insideTileSize = new Vector3(TerrainTileData.TileSizeMetres - 2f, 1f, TerrainTileData.TileSizeMetres - 2f);
        return new Bounds(insideTileCentre, insideTileSize);
    }

    private static void AddOpenTilesScatteredByPath(string featureId)
    {
        if (string.IsNullOrEmpty(featureId))
        {
            return;
        }

        foreach (TerrainTile tile in UnityEngine.Object.FindObjectsByType<TerrainTile>(FindObjectsInactive.Exclude))
        {
            Transform scatterParent = tile.transform.Find("Scatter");
            if (scatterParent == null)
            {
                continue;
            }

            foreach (TerrainScatterMarker marker in scatterParent.GetComponentsInChildren<TerrainScatterMarker>(true))
            {
                if (marker.sourceFeatureId == featureId)
                {
                    pendingAffectedBounds.Add(InsideTileBounds(tile.tileCoordinate));
                    break;
                }
            }
        }
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
