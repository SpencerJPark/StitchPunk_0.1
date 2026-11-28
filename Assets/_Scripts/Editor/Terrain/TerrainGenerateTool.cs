#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TerrainGenerateTool
{
    const string GeneratedRampName = "GeneratedTerraceRamp";

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

    // Writes only the procedural layer; the owner's handTerraceLevels always win and are never touched here.
    static void GenerateBaseTerraceLevels(TerrainGenerationProfile profile, Vector2Int coordinate, TerrainTileData tileData, List<TerraceRampPlacement> generatedRamps)
    {
        if (profile.terraceBiomeRules == null || profile.terraceBiomeRules.Count == 0)
        {
            tileData.baseTerraceLevels = null;
            return;
        }
        TerrainWorldProfile worldProfile = TerrainWorldProfile.FindOrCreateInProject();
        int maxLevels = worldProfile != null ? worldProfile.maxTerraceLevels : 3;
        byte[] levels = new byte[TerrainTileData.TerraceCellsPerSide * TerrainTileData.TerraceCellsPerSide];
        byte[] stepKinds = new byte[levels.Length];
        System.Func<Vector2Int, TerrainTileData> neighbourLookup = (Vector2Int neighbourCoordinate) =>
            AssetDatabase.LoadAssetAtPath<TerrainTileData>(TerrainTileAssets.TileDataPath(neighbourCoordinate));
        TerrainProceduralGenerator.GenerateTerraceLevels(profile, coordinate, maxLevels, tileData.rollIndex, neighbourLookup, levels, stepKinds, generatedRamps);
        tileData.baseTerraceLevels = levels;
        tileData.baseTerraceStepKinds = stepKinds;
    }

    // Only objects named GeneratedTerraceRamp are replaced; hand-placed ramps keep their own names.
    // Parked: a 2.8 m worn-gap notch leaves holes where it meets a freeform wall (Natural Cliffs wave 4 hole check). Planner still plans them.
    const bool SpawnGeneratedWornGapRamps = false;

    static void ReplaceGeneratedRamps(TerrainTile tile, TerrainTileData tileData, List<TerraceRampPlacement> placements)
    {
        UnityEngine.SceneManagement.Scene tileScene = tile.gameObject.scene;
        foreach (GameObject rootObject in tileScene.GetRootGameObjects())
            if (rootObject.name == GeneratedRampName && rootObject.GetComponent<TerraceRamp>() != null)
                UnityEngine.Object.DestroyImmediate(rootObject);

        TerrainWorldProfile worldProfile = TerrainWorldProfile.FindOrCreateInProject();
        float levelHeightMetres = worldProfile != null ? worldProfile.terraceLevelHeightMetres : 3f;
        float hillStepHeightMetres = worldProfile != null ? worldProfile.hillStepHeightMetres : 0.7f;
        Vector2 tileMinCorner = tileData.WorldMinCorner();
        foreach (TerraceRampPlacement placement in placements)
        {
            if (placement.isLowStepWornGap && !SpawnGeneratedWornGapRamps) continue;
            GameObject rampObject = new GameObject(GeneratedRampName);
            TerraceRamp ramp = rampObject.AddComponent<TerraceRamp>();
            ramp.kind = placement.kind;
            ramp.widthMetres = placement.widthMetres > 0f ? placement.widthMetres : 6f;
            ramp.runMetres = placement.runMetres > 0f ? placement.runMetres : 9f;
            ramp.risesToTheRight = placement.risesToTheRight;
            ramp.lowerLevel = placement.lowerLevel;
            int centreCellX = Mathf.Clamp(Mathf.FloorToInt(placement.centreWorld.x - tileMinCorner.x), 0, TerrainTileData.TerraceCellsPerSide - 1);
            int centreCellZ = Mathf.Clamp(Mathf.FloorToInt(placement.centreWorld.y - tileMinCorner.y), 0, TerrainTileData.TerraceCellsPerSide - 1);
            int centreCellIndex = centreCellZ * TerrainTileData.TerraceCellsPerSide + centreCellX;
            byte stepKindsAtCentre = tileData.baseTerraceStepKinds != null && centreCellIndex < tileData.baseTerraceStepKinds.Length
                ? tileData.baseTerraceStepKinds[centreCellIndex]
                : (byte)0;
            float baseHeight = tileData.SampleBilinear(placement.centreWorld.x, placement.centreWorld.y);
            rampObject.transform.position = new Vector3(
                placement.centreWorld.x,
                baseHeight + TerrainTileData.TerraceLevelOffsetMetres(stepKindsAtCentre, placement.lowerLevel, levelHeightMetres, hillStepHeightMetres),
                placement.centreWorld.y);
            rampObject.transform.rotation = Quaternion.Euler(0f, placement.yawDegrees, 0f);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(rampObject, tileScene);
        }
    }

    internal static bool GenerateOneTile(TerrainRegion region, Vector2Int coordinate, bool allowHandAuthored)
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
        TerrainProceduralGenerator.GenerateHeights(region.profile, coordinate, tileData.rollIndex, tileData.baseHeights);
        List<TerraceRampPlacement> generatedRamps = new List<TerraceRampPlacement>();
        GenerateBaseTerraceLevels(region.profile, coordinate, tileData, generatedRamps);

        int resolution = tileData.maskResolution > 0 ? tileData.maskResolution : TerrainSplatCodec.FallbackMaskResolution;
        Color32[] maskPixels = new Color32[resolution * resolution];
        TerrainProceduralGenerator.GenerateBaseMask(region.profile, coordinate, tileData.rollIndex, resolution, maskPixels);
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
        ReplaceGeneratedRamps(tile, tileData, generatedRamps);
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
            TerrainProceduralGenerator.GenerateHeights(profile, tile.tileCoordinate, realData.rollIndex, temporaryData.baseHeights);

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
