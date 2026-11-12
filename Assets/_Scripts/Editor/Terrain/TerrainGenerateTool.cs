#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TerrainGenerateTool
{
    public static List<Vector2Int> ExistingTilesInside(TerrainRegion region)
    {
        List<Vector2Int> existingTiles = new List<Vector2Int>();
        foreach (Vector2Int coordinate in region.TilesInside())
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TerrainTileAssets.TileScenePath(coordinate)) != null)
                existingTiles.Add(coordinate);
        return existingTiles;
    }

    public static int Generate(TerrainRegion region, bool allowHandAuthored, Action<float, string> reportProgress)
    {
        if (region.mode == TerrainRegionMode.Hand || region.profile == null)
        {
            Debug.LogWarning("[Terrain] Region " + region.name + " is Hand mode or has no profile; nothing generated.");
            return 0;
        }

        List<Vector2Int> tileCoordinates = ExistingTilesInside(region);
        int generatedCount = 0;
        for (int index = 0; index < tileCoordinates.Count; index++)
        {
            Vector2Int coordinate = tileCoordinates[index];
            reportProgress?.Invoke(index / (float)Mathf.Max(1, tileCoordinates.Count), "Tile (" + coordinate.x + ", " + coordinate.y + ")");
            if (GenerateOneTile(region, coordinate, allowHandAuthored)) generatedCount++;
        }
        reportProgress?.Invoke(1f, "Done");
        return generatedCount;
    }

    // Freshly created tiles take the terrain of the procedural region they landed in, so a grown grid is never a flat grass slab
    // next to generated hills. Tiles outside every region are left as created.
    public static int GenerateNewTilesInsideRegions(IReadOnlyList<Vector2Int> newTileCoordinates)
    {
        TerrainRegion[] regions = UnityEngine.Object.FindObjectsByType<TerrainRegion>(FindObjectsInactive.Exclude);
        int generatedCount = 0;
        try
        {
            for (int tileIndex = 0; tileIndex < newTileCoordinates.Count; tileIndex++)
            {
                Vector2Int coordinate = newTileCoordinates[tileIndex];
                TerrainRegion coveringRegion = ProceduralRegionCovering(regions, coordinate);
                if (coveringRegion == null) continue;

                EditorUtility.DisplayProgressBar("Generate New Tiles", "Tile " + coordinate.x + ", " + coordinate.y,
                    tileIndex / (float)newTileCoordinates.Count);
                bool wasOpen = TerrainTileCreator.FindTileAt(coordinate) != null;
                if (GenerateOneTile(coveringRegion, coordinate, false)) generatedCount++;
                // An open tile is live-baked on every reload, so only tiles the owner opened stay open.
                if (!wasOpen) TerrainTilesPanel.SaveAndCloseTile(coordinate);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        return generatedCount;
    }

    static TerrainRegion ProceduralRegionCovering(TerrainRegion[] regions, Vector2Int coordinate)
    {
        foreach (TerrainRegion region in regions)
        {
            if (region.mode == TerrainRegionMode.Hand || region.profile == null) continue;
            foreach (Vector2Int insideCoordinate in region.TilesInside())
                if (insideCoordinate == coordinate) return region;
        }
        return null;
    }

    static bool GenerateOneTile(TerrainRegion region, Vector2Int coordinate, bool allowHandAuthored)
    {
        TerrainTile tile = TerrainTileCreator.FindTileAt(coordinate);
        if (tile == null)
        {
            if (!TerrainTilesPanel.OpenTileForEdit(coordinate)) return false;
            tile = TerrainTileCreator.FindTileAt(coordinate);
            if (tile == null) return false;
        }

        TerrainTileData tileData = tile.tileData != null ? tile.tileData : TerrainTileAssets.EnsureTileData(tile);
        if (tileData == null) return false;
        if (tileData.handAuthored && !allowHandAuthored) return false;

        tileData.EnsureHeightArrays();
        TerrainProceduralGenerator.GenerateHeights(region.profile, coordinate, tileData.baseHeights);

        int resolution = tileData.maskResolution > 0 ? tileData.maskResolution : TerrainSplatCodec.FallbackMaskResolution;
        Color32[] maskPixels = new Color32[resolution * resolution];
        TerrainProceduralGenerator.GenerateBaseMask(region.profile, coordinate, resolution, maskPixels);
        string maskPath = TerrainTileAssets.MaskPath(coordinate);
        TerrainSplatCodec.WritePng(maskPath, maskPixels, resolution, false);
        Texture2D maskTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
        tile.splatMask = maskTexture;
        tileData.baseMask = maskTexture;

        if (region.paletteOverride != null)
        {
            tile.palette = region.paletteOverride;
            MeshRenderer tileRenderer = tile.GetComponent<MeshRenderer>();
            if (tileRenderer != null && tileRenderer.sharedMaterial != null)
                region.paletteOverride.ApplyToMaterial(tileRenderer.sharedMaterial);
        }

        tileData.BumpRevision();
        TerrainCompositor.Composite(tile);
        TerrainTileBuilder.Rebuild(tile);
        EditorUtility.SetDirty(tileData);
        EditorUtility.SetDirty(tile);
        EditorSceneManager.SaveScene(tile.gameObject.scene);
        return true;
    }

    // Heights only and in memory; TerrainTileBuilder.Rebuild restores the real mesh.
    public static bool PreviewOneTile(TerrainTile tile, TerrainGenerationProfile profile)
    {
        TerrainTileData realData = tile.tileData;
        if (realData == null || profile == null) return false;

        TerrainTileData temporaryData = ScriptableObject.CreateInstance<TerrainTileData>();
        try
        {
            temporaryData.coordinate = realData.coordinate;
            temporaryData.EnsureHeightArrays();
            CopyIfPresent(realData.handHeightDelta, temporaryData.handHeightDelta);
            CopyIfPresent(realData.riverHeightDelta, temporaryData.riverHeightDelta);
            CopyIfPresent(realData.roadHeightDelta, temporaryData.roadHeightDelta);
            TerrainProceduralGenerator.GenerateHeights(profile, tile.tileCoordinate, temporaryData.baseHeights);

            MeshFilter meshFilter = tile.GetComponent<MeshFilter>();
            if (meshFilter == null) return false;
            Mesh previewMesh = TerrainMeshBuilder.BuildTileMesh(temporaryData, 1, null, TerrainTileCreator.GroundTopHeight);
            previewMesh.name = "TerrainPreviewMesh";
            meshFilter.sharedMesh = previewMesh;
            return true;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(temporaryData);
        }
    }

    static void CopyIfPresent(float[] source, float[] destination)
    {
        if (source == null || destination == null) return;
        Array.Copy(source, destination, Mathf.Min(source.Length, destination.Length));
    }
}
#endif
