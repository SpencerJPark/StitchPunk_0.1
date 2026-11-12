#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Bakes one terrain tile's 1 m nav cost + surface bytes into its TerrainTileNavAuthoring.
public static class TerrainNavBaker
{
    public const int CellsPerSide = 100;

    private const byte ChannelThreshold = 127;
    private const float CellSizeMetres = 1f;
    private const float SlopeProbeMetres = 0.5f;
    private const float PropProbeHeightMetres = 0.5f;
    private const float PropProbeRadiusMetres = 0.4f;
    private const string WallLayerName = "PathfindingWalls";

    [MenuItem("Stitch Punk/Terrain/Bake Nav (Open Tiles)")]
    private static void BakeAllOpenTilesMenu()
    {
        int bakedTileCount = BakeAllOpenTiles();
        Debug.Log("TerrainNavBaker: baked nav for " + bakedTileCount + " open tile(s).");
    }

    public static int BakeAllOpenTiles()
    {
        TerrainTile[] tiles = Object.FindObjectsByType<TerrainTile>(FindObjectsInactive.Include);
        for (int tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
        {
            BakeTile(tiles[tileIndex]);
        }
        return tiles.Length;
    }

    [MenuItem("Stitch Punk/Terrain/Bake Nav (Stale Open Tiles)")]
    private static void BakeStaleOpenTilesMenu()
    {
        int bakedTileCount = BakeStaleOpenTiles(true);
        Debug.Log("TerrainNavBaker: baked nav for " + bakedTileCount + " stale open tile(s).");
    }

    public static int BakeStaleOpenTiles(bool saveScenes)
    {
        TerrainTile[] tiles = Object.FindObjectsByType<TerrainTile>(FindObjectsInactive.Include);
        int bakedTileCount = 0;
        for (int tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
        {
            if (!IsNavStale(tiles[tileIndex]))
            {
                continue;
            }
            if (saveScenes)
            {
                BakeTile(tiles[tileIndex]);
            }
            else
            {
                BakeTileWithoutSaving(tiles[tileIndex]);
            }
            bakedTileCount++;
        }
        return bakedTileCount;
    }

    public static bool IsNavStale(TerrainTile tile)
    {
        TerrainTileNavAuthoring authoring = tile.GetComponent<TerrainTileNavAuthoring>();
        if (authoring == null || authoring.costs == null || authoring.costs.Length != CellsPerSide * CellsPerSide)
        {
            return true;
        }
        if (tile.tileData != null && authoring.bakedFromTileDataRevision != tile.tileData.revision)
        {
            return true;
        }
        return authoring.bakedFromBlockerHash != BlockerHashOf(tile);
    }

    // Prop scatter does not bump tileData.revision, so blockers are fingerprinted separately.
    public static int BlockerHashOf(TerrainTile tile)
    {
        int wallLayer = LayerMask.NameToLayer(WallLayerName);
        if (wallLayer < 0)
        {
            return 0;
        }
        // Bounds of a collider moved this frame are stale until the transforms sync.
        Physics.SyncTransforms();
        Collider[] colliders = tile.GetComponentsInChildren<Collider>(false);
        int combinedHash = 0;
        for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
        {
            Collider collider = colliders[colliderIndex];
            if (!collider.enabled || collider.gameObject.layer != wallLayer)
            {
                continue;
            }
            Vector3 centre = collider.bounds.center;
            Vector3 size = collider.bounds.size;
            // Not System.HashCode: it is seeded per process, so every tile would read as stale after an Editor restart.
            int colliderHash = 17;
            unchecked
            {
                colliderHash = colliderHash * 31 + Mathf.RoundToInt(centre.x * 100f);
                colliderHash = colliderHash * 31 + Mathf.RoundToInt(centre.y * 100f);
                colliderHash = colliderHash * 31 + Mathf.RoundToInt(centre.z * 100f);
                colliderHash = colliderHash * 31 + Mathf.RoundToInt(size.x * 100f);
                colliderHash = colliderHash * 31 + Mathf.RoundToInt(size.y * 100f);
                colliderHash = colliderHash * 31 + Mathf.RoundToInt(size.z * 100f);
            }
            combinedHash = unchecked(combinedHash + colliderHash);
        }
        return combinedHash;
    }

    public static void BakeTile(TerrainTile tile)
    {
        BakeTileWithoutSaving(tile);
        EditorSceneManager.SaveScene(tile.gameObject.scene);
    }

    public static void BakeTileWithoutSaving(TerrainTile tile)
    {
        int wallLayer = LayerMask.NameToLayer(WallLayerName);
        int wallLayerMask = wallLayer >= 0 ? 1 << wallLayer : 0;
        List<TerrainCrossing> crossings = new List<TerrainCrossing>(Object.FindObjectsByType<TerrainCrossing>(FindObjectsInactive.Include));

        byte[] costs = new byte[CellsPerSide * CellsPerSide];
        byte[] surface = new byte[CellsPerSide * CellsPerSide];
        ComputeTileNav(tile, crossings, wallLayerMask, costs, surface);

        TerrainTileNavAuthoring authoring = tile.GetComponent<TerrainTileNavAuthoring>();
        if (authoring == null)
        {
            authoring = tile.gameObject.AddComponent<TerrainTileNavAuthoring>();
        }
        authoring.cellsPerSide = CellsPerSide;
        authoring.layerCount = 1;
        authoring.coordinate = tile.tileCoordinate;
        authoring.costs = costs;
        authoring.surface = surface;
        authoring.bakedFromTileDataRevision = tile.tileData != null ? tile.tileData.revision : -1;
        authoring.bakedFromBlockerHash = BlockerHashOf(tile);
        EditorUtility.SetDirty(authoring);
        EditorSceneManager.MarkSceneDirty(tile.gameObject.scene);

        int blockedCount = 0;
        int heavyCount = 0;
        int shallowCount = 0;
        int roadCount = 0;
        for (int cellIndex = 0; cellIndex < costs.Length; cellIndex++)
        {
            if (costs[cellIndex] == TerrainNavCellRules.WallCost) blockedCount++;
            else if (costs[cellIndex] >= TerrainNavCellRules.HeavyCost) heavyCount++;
            if (surface[cellIndex] == (byte)TerrainSurface.Shallow) shallowCount++;
            else if (surface[cellIndex] == (byte)TerrainSurface.Road) roadCount++;
        }
        Debug.Log("TerrainNavBaker: tile " + tile.tileCoordinate + " blocked=" + blockedCount + " heavy=" + heavyCount + " shallow=" + shallowCount + " road=" + roadCount);
    }

    public static void ComputeTileNav(TerrainTile tile, IReadOnlyList<TerrainCrossing> crossings, int wallLayerMask, byte[] costsOut, byte[] surfaceOut)
    {
        Vector2Int coordinate = tile.tileCoordinate;
        float minCornerX = coordinate.x * TerrainTile.TileSizeMetres - 50f;
        float minCornerZ = coordinate.y * TerrainTile.TileSizeMetres - 50f;

        int resolution = 0;
        Color32[] compositeMask = null;
        Color32[] surfaceOverlay = null;
        Color32[] roadMask = null;
        string maskPath = TerrainTileAssets.MaskPath(coordinate);
        if (File.Exists(maskPath))
        {
            Color32[] baseMask = TerrainSplatCodec.ReadPng(maskPath, out resolution);
            Color32[] paintOverlay = ReadSameResolution(TerrainTileAssets.PaintOverlayPath(coordinate), resolution);
            roadMask = ReadSameResolution(TerrainTileAssets.RoadMaskPath(coordinate), resolution);
            surfaceOverlay = ReadSameResolution(TerrainTileAssets.SurfaceOverlayPath(coordinate), resolution);
            compositeMask = new Color32[baseMask.Length];
            TerrainCompositor.CompositeTexels(baseMask, paintOverlay, null, roadMask, resolution, new RectInt(0, 0, resolution, resolution), compositeMask);
        }

        if (wallLayerMask != 0)
        {
            Physics.SyncTransforms();
        }

        for (int cellZ = 0; cellZ < CellsPerSide; cellZ++)
        {
            for (int cellX = 0; cellX < CellsPerSide; cellX++)
            {
                float worldX = minCornerX + (cellX + 0.5f) * CellSizeMetres;
                float worldZ = minCornerZ + (cellZ + 0.5f) * CellSizeMetres;

                TerrainNavCellInput input = new TerrainNavCellInput();
                input.slopeDegrees = SlopeDegreesAt(tile, worldX, worldZ);
                input.roadCost = TerrainNavCellRules.LeaveGroundCost;
                input.groundCost = 1;

                if (compositeMask != null)
                {
                    int texelX = Mathf.Clamp((int)((worldX - minCornerX) / TerrainTile.TileSizeMetres * resolution), 0, resolution - 1);
                    int texelZ = Mathf.Clamp((int)((worldZ - minCornerZ) / TerrainTile.TileSizeMetres * resolution), 0, resolution - 1);
                    int texelIndex = texelZ * resolution + texelX;

                    if (surfaceOverlay != null)
                    {
                        input.shallowWater = surfaceOverlay[texelIndex].r > ChannelThreshold;
                        input.deepWater = surfaceOverlay[texelIndex].g > ChannelThreshold;
                    }
                    if (roadMask != null)
                    {
                        input.onRoad = roadMask[texelIndex].a > ChannelThreshold;
                    }

                    Color32 composite = compositeMask[texelIndex];
                    int layerIndex = composite.r > ChannelThreshold ? 1 : composite.g > ChannelThreshold ? 2 : composite.b > ChannelThreshold ? 3 : 0;
                    input.groundCost = GroundCostForLayer(tile, layerIndex);
                }

                TerrainNavCell cell = TerrainNavCellRules.Classify(input);

                if (wallLayerMask != 0)
                {
                    float groundHeight = tile.tileData.SampleBilinear(worldX, worldZ);
                    Vector3 probeCentre = new Vector3(worldX, groundHeight + PropProbeHeightMetres, worldZ);
                    if (Physics.CheckSphere(probeCentre, PropProbeRadiusMetres, wallLayerMask, QueryTriggerInteraction.Ignore))
                    {
                        cell.cost = TerrainNavCellRules.WallCost;
                    }
                }

                for (int crossingIndex = 0; crossingIndex < crossings.Count; crossingIndex++)
                {
                    TerrainCrossing crossing = crossings[crossingIndex];
                    if (crossing != null && crossing.ContainsXZ(worldX, worldZ))
                    {
                        cell = TerrainNavCellRules.ApplyCrossing(cell, crossing.kind == TerrainCrossingKind.Bridge, input.groundCost);
                    }
                }

                int outputIndex = cellZ * CellsPerSide + cellX;
                costsOut[outputIndex] = cell.cost;
                surfaceOut[outputIndex] = (byte)cell.surface;
            }
        }
    }

    private static float SlopeDegreesAt(TerrainTile tile, float worldX, float worldZ)
    {
        float slopeAlongX = (tile.tileData.SampleBilinear(worldX + SlopeProbeMetres, worldZ) - tile.tileData.SampleBilinear(worldX - SlopeProbeMetres, worldZ)) / (2f * SlopeProbeMetres);
        float slopeAlongZ = (tile.tileData.SampleBilinear(worldX, worldZ + SlopeProbeMetres) - tile.tileData.SampleBilinear(worldX, worldZ - SlopeProbeMetres)) / (2f * SlopeProbeMetres);
        return Mathf.Atan(Mathf.Sqrt(slopeAlongX * slopeAlongX + slopeAlongZ * slopeAlongZ)) * Mathf.Rad2Deg;
    }

    private static byte GroundCostForLayer(TerrainTile tile, int layerIndex)
    {
        if (tile.palette == null)
        {
            return 1;
        }
        byte paletteCost = tile.palette.GetLayer(layerIndex).navCostByte;
        return paletteCost == 0 ? (byte)1 : paletteCost;
    }

    private static Color32[] ReadSameResolution(string assetPath, int resolution)
    {
        if (!File.Exists(assetPath))
        {
            return null;
        }

        int sourceResolution;
        Color32[] pixels = TerrainSplatCodec.ReadPng(assetPath, out sourceResolution);
        return sourceResolution == resolution ? pixels : null;
    }
}
#endif
