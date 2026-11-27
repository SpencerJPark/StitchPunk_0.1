#if UNITY_EDITOR
using System.IO;
using Unity.Scenes;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Where a tile's mask, SDF and material live, and the wiring between them
public static class TerrainTileAssets
{
    public const string TileFolder = "Assets/Terrain/Tiles";
    // Set by the test world builder so its tiles never share assets with the real tile folder
    public static string RootFolderOverride;
    static string ActiveTileFolder { get { return string.IsNullOrEmpty(RootFolderOverride) ? TileFolderOfOpenWorld() : RootFolderOverride; } }

    static string cachedWorldScenePath;
    static string cachedWorldTileFolder;

    // Each world scene keeps its tiles in its own folder. Without this, editing tile (0,0) of the test world read and
    // wrote the masks of the camp world's tile (0,0), and the tile map showed the camp world's grid.
    static string TileFolderOfOpenWorld()
    {
        string activeScenePath = SceneManager.GetActiveScene().path;
        if (cachedWorldTileFolder != null && activeScenePath == cachedWorldScenePath) return cachedWorldTileFolder;

        string worldTileFolder = TileFolder;
        foreach (SubScene subScene in Object.FindObjectsByType<SubScene>(FindObjectsInactive.Include))
        {
            if (!subScene.name.StartsWith("Tile_") || subScene.SceneAsset == null) continue;
            string tileScenePath = AssetDatabase.GetAssetPath(subScene.SceneAsset);
            worldTileFolder = Path.GetDirectoryName(Path.GetDirectoryName(tileScenePath)).Replace('\\', '/');
            break;
        }
        cachedWorldScenePath = activeScenePath;
        cachedWorldTileFolder = worldTileFolder;
        return worldTileFolder;
    }
    const string MaterialTemplatePath = "Assets/PaintedGround/Materials/M_PaintedGround.mat";

    public static string FolderOf(Vector2Int coordinate)
    {
        return ActiveTileFolder + "/Tile_" + coordinate.x + "_" + coordinate.y;
    }

    public static string TileScenePath(Vector2Int coordinate)
    {
        return FolderOf(coordinate) + "/Tile_" + coordinate.x + "_" + coordinate.y + ".unity";
    }

    public static string TileDataPath(Vector2Int coordinate)
    {
        return FolderOf(coordinate) + "/Tile_" + coordinate.x + "_" + coordinate.y + "_Data.asset";
    }

    public static string RoadMaskPath(Vector2Int coordinate)
    {
        return FolderOf(coordinate) + "/Tile_" + coordinate.x + "_" + coordinate.y + "_RoadMask.png";
    }

    public static string SurfaceOverlayPath(Vector2Int coordinate)
    {
        return FolderOf(coordinate) + "/Tile_" + coordinate.x + "_" + coordinate.y + "_SurfaceOverlay.png";
    }

    public static string PaintOverlayPath(Vector2Int coordinate)
    {
        return FolderOf(coordinate) + "/Tile_" + coordinate.x + "_" + coordinate.y + "_PaintOverlay.png";
    }

    // An existing mask file wins so a tile never changes resolution under its own paint.
    public static int MaskResolutionFor(TerrainTile tile)
    {
        string existingMaskPath = MaskPath(tile.tileCoordinate);
        if (File.Exists(existingMaskPath))
        {
            int existingResolution;
            TerrainSplatCodec.ReadPng(existingMaskPath, out existingResolution);
            return existingResolution;
        }

        if (tile.tileData != null && tile.tileData.maskResolution > 0) return tile.tileData.maskResolution;
        TerrainWorldProfile profile = TerrainWorldProfile.FindInProject();
        if (profile != null) return profile.defaultMaskResolution;
        return TerrainSplatCodec.FallbackMaskResolution;
    }

    public static void StampExistingSignedDistanceFields(TerrainTile tile)
    {
        TerrainSplatCodec.StampSignedDistanceFieldImportSettings(SignedDistanceFieldPath(tile.tileCoordinate), MaskResolutionFor(tile));
    }

    [MenuItem("Stitch Punk/Terrain/Compress Selected Tile SDF (BC7)")]
    static void CompressSelectedTileSignedDistanceField()
    {
        GameObject selectedObject = Selection.activeGameObject;
        TerrainTile tile = selectedObject != null ? selectedObject.GetComponent<TerrainTile>() : null;
        if (tile == null)
        {
            EditorUtility.DisplayDialog("Terrain", "Select a tile with a TerrainTile component.", "OK");
            return;
        }

        StampExistingSignedDistanceFields(tile);
    }

    public static string MaskPath(Vector2Int coordinate)
    {
        return PreferNewUnlessOnlyLegacyExists(NewMaskPath(coordinate), LegacyMaskPath(coordinate));
    }

    public static string SignedDistanceFieldPath(Vector2Int coordinate)
    {
        return PreferNewUnlessOnlyLegacyExists(NewSignedDistanceFieldPath(coordinate), LegacySignedDistanceFieldPath(coordinate));
    }

    public static string MaterialPath(Vector2Int coordinate)
    {
        return PreferNewUnlessOnlyLegacyExists(NewMaterialPath(coordinate), LegacyMaterialPath(coordinate));
    }

    static string NewMaskPath(Vector2Int coordinate)
    {
        return FolderOf(coordinate) + "/Tile_" + coordinate.x + "_" + coordinate.y + "_Mask.png";
    }

    static string NewSignedDistanceFieldPath(Vector2Int coordinate)
    {
        return FolderOf(coordinate) + "/Tile_" + coordinate.x + "_" + coordinate.y + "_Splat_SDF.png";
    }

    static string NewMaterialPath(Vector2Int coordinate)
    {
        return FolderOf(coordinate) + "/M_Ground_Tile_" + coordinate.x + "_" + coordinate.y + ".mat";
    }

    static string LegacyMaskPath(Vector2Int coordinate)
    {
        return ActiveTileFolder + "/Tile_" + coordinate.x + "_" + coordinate.y + "_Mask.png";
    }

    static string LegacySignedDistanceFieldPath(Vector2Int coordinate)
    {
        return ActiveTileFolder + "/Tile_" + coordinate.x + "_" + coordinate.y + "_Splat_SDF.png";
    }

    static string LegacyMaterialPath(Vector2Int coordinate)
    {
        return ActiveTileFolder + "/M_Ground_Tile_" + coordinate.x + "_" + coordinate.y + ".mat";
    }

    // Before migration runs, a tile that only has the loose legacy file keeps using it.
    static string PreferNewUnlessOnlyLegacyExists(string newPath, string legacyPath)
    {
        if (!File.Exists(newPath) && File.Exists(legacyPath)) return legacyPath;
        return newPath;
    }

    // CopyAsset and CreateAsset need the folder to be a real imported folder, so create each level through the AssetDatabase.
    static void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath)) return;
        int lastSlash = folderPath.LastIndexOf('/');
        string parentFolder = folderPath.Substring(0, lastSlash);
        EnsureFolderExists(parentFolder);
        AssetDatabase.CreateFolder(parentFolder, folderPath.Substring(lastSlash + 1));
    }

    // Creates whatever is missing (an existing mask is never overwritten), re-derives the SDF and
    // points the tile's renderer at a material reading it.
    public static void EnsureAssets(TerrainTile tile, TerrainSplatLayer fillWhenMaskIsNew)
    {
        Vector2Int coordinate = tile.tileCoordinate;
        EnsureFolderExists(FolderOf(coordinate));

        string maskPath = MaskPath(coordinate);
        if (!File.Exists(maskPath))
        {
            int newMaskResolution = MaskResolutionFor(tile);
            Color32[] filledMask = TerrainSplatCodec.CreateFilledMask(newMaskResolution, fillWhenMaskIsNew);
            TerrainSplatCodec.WritePng(maskPath, filledMask, newMaskResolution, false);
        }

        tile.splatMask = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
        RebuildSignedDistanceField(tile);
        ApplyToMaterial(tile);
        EnsureTileData(tile);
        EditorUtility.SetDirty(tile);
    }

    public static TerrainTileData EnsureTileData(TerrainTile tile)
    {
        Vector2Int coordinate = tile.tileCoordinate;
        string dataPath = TileDataPath(coordinate);
        TerrainTileData tileData = AssetDatabase.LoadAssetAtPath<TerrainTileData>(dataPath);
        if (tileData == null)
        {
            EnsureFolderExists(FolderOf(coordinate));
            tileData = ScriptableObject.CreateInstance<TerrainTileData>();
            AssetDatabase.CreateAsset(tileData, dataPath);
        }

        tileData.coordinate = coordinate;
        tileData.maskResolution = tile.splatMask != null ? tile.splatMask.width : MaskResolutionFor(tile);
        if (tileData.baseMask == null) tileData.baseMask = tile.splatMask;
        tileData.EnsureHeightArrays();
        EditorUtility.SetDirty(tileData);

        tile.tileData = tileData;
        EditorUtility.SetDirty(tile);
        return tileData;
    }

    public static void MigrateLegacyTile(TerrainTile tile)
    {
        Vector2Int coordinate = tile.tileCoordinate;
        string[] legacyPaths = { LegacyMaskPath(coordinate), LegacySignedDistanceFieldPath(coordinate), LegacyMaterialPath(coordinate) };
        string[] newPaths = { NewMaskPath(coordinate), NewSignedDistanceFieldPath(coordinate), NewMaterialPath(coordinate) };
        for (int index = 0; index < legacyPaths.Length; index++)
        {
            if (!File.Exists(legacyPaths[index]) || File.Exists(newPaths[index])) continue;
            EnsureFolderExists(FolderOf(coordinate));
            string moveError = AssetDatabase.MoveAsset(legacyPaths[index], newPaths[index]);
            if (!string.IsNullOrEmpty(moveError))
            {
                Debug.LogWarning("Terrain tile migration stopped moving " + legacyPaths[index] + ": " + moveError);
                return;
            }
        }

        tile.splatMask = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath(coordinate));
        tile.splatSignedDistanceField = AssetDatabase.LoadAssetAtPath<Texture2D>(SignedDistanceFieldPath(coordinate));
        EnsureTileData(tile);
        ApplyToMaterial(tile);
        EditorUtility.SetDirty(tile);
    }

    [MenuItem("Stitch Punk/Terrain/Migrate Selected Tile Assets")]
    static void MigrateSelectedTile()
    {
        GameObject selectedObject = Selection.activeGameObject;
        TerrainTile tile = selectedObject != null ? selectedObject.GetComponent<TerrainTile>() : null;
        if (tile == null)
        {
            EditorUtility.DisplayDialog("Terrain", "Select a tile with a TerrainTile component.", "OK");
            return;
        }

        MigrateLegacyTile(tile);
    }

    public static void RebuildSignedDistanceField(TerrainTile tile)
    {
        Vector2Int coordinate = tile.tileCoordinate;
        int resolution;
        Color32[] mask = TerrainSplatCodec.ReadPng(MaskPath(coordinate), out resolution);
        Color32[] signedDistanceField = TerrainSplatCodec.EncodeSignedDistanceField(mask, resolution, TerrainTile.TileSizeMetres);
        string signedDistanceFieldPath = SignedDistanceFieldPath(coordinate);
        TerrainSplatCodec.WritePng(signedDistanceFieldPath, signedDistanceField, resolution, true);
        tile.splatSignedDistanceField = AssetDatabase.LoadAssetAtPath<Texture2D>(signedDistanceFieldPath);
    }

    public static void ApplyToMaterial(TerrainTile tile)
    {
        Vector2Int coordinate = tile.tileCoordinate;
        string materialPath = MaterialPath(coordinate);
        Material groundMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (groundMaterial == null)
        {
            AssetDatabase.CopyAsset(MaterialTemplatePath, materialPath);
            groundMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        }

        groundMaterial.SetTexture("_SplatMap", tile.splatSignedDistanceField);
        groundMaterial.SetVector("_SplatBounds", TerrainTile.SplatWorldRectOf(coordinate));
        groundMaterial.EnableKeyword("_SPLAT_WORLDTEXTURE");
        groundMaterial.DisableKeyword("_SPLAT_VERTEXCOLOR");
        groundMaterial.SetFloat("_Splat", 1f);
        SetTerraceCornerProperties(tile, groundMaterial);
        EditorUtility.SetDirty(groundMaterial);
        AssetDatabase.SaveAssetIfDirty(groundMaterial);

        MeshRenderer tileRenderer = tile.GetComponent<MeshRenderer>();
        if (tileRenderer != null) tileRenderer.sharedMaterial = groundMaterial;
    }

    // Call after a terrace rebuild so the ground shader's corner levels match the new terraces.
    public static void ApplyTerraceCornerLevels(TerrainTile tile)
    {
        Material groundMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(tile.tileCoordinate));
        if (groundMaterial == null) return;
        SetTerraceCornerProperties(tile, groundMaterial);
        EditorUtility.SetDirty(groundMaterial);
        AssetDatabase.SaveAssetIfDirty(groundMaterial);
    }

    public static string TerraceCornerLevelsPath(Vector2Int coordinate)
    {
        return FolderOf(coordinate) + "/Tile_" + coordinate.x + "_" + coordinate.y + "_TerraceCorners.png";
    }

    static TerrainTileData LoadTileDataForNeighbour(Vector2Int coordinate)
    {
        return AssetDatabase.LoadAssetAtPath<TerrainTileData>(TileDataPath(coordinate));
    }

    // 51 x 51 R8 texture, pixel (i, j) = terrace level + 3 at corner (i, j) of the 2 m dual grid; the shader decodes it.
    static void SetTerraceCornerProperties(TerrainTile tile, Material groundMaterial)
    {
        Vector2Int coordinate = tile.tileCoordinate;
        TerrainTileData tileData = tile.tileData != null ? tile.tileData : LoadTileDataForNeighbour(coordinate);
        if (tileData == null) return;

        const int cornersPerSide = 51;
        int[] cornerLevels = TerraceEdgeTileOutline.BuildCornerLevels(tileData, LoadTileDataForNeighbour);
        Color32[] pixels = new Color32[cornerLevels.Length];
        for (int index = 0; index < cornerLevels.Length; index++)
        {
            byte encodedLevel = (byte)Mathf.Clamp(cornerLevels[index] + 3, 0, 6);
            pixels[index] = new Color32(encodedLevel, 0, 0, 255);
        }

        string texturePath = TerraceCornerLevelsPath(coordinate);
        bool existedBefore = File.Exists(texturePath);
        Texture2D encoded = new Texture2D(cornersPerSide, cornersPerSide, TextureFormat.RGBA32, false, true);
        encoded.SetPixels32(pixels);
        encoded.Apply();
        File.WriteAllBytes(texturePath, encoded.EncodeToPNG());
        Object.DestroyImmediate(encoded);
        AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer != null && (!existedBefore || importer.textureType != TextureImporterType.SingleChannel))
        {
            importer.textureType = TextureImporterType.SingleChannel;
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        float halfTile = TerrainTile.TileSizeMetres * 0.5f;
        groundMaterial.SetTexture("_TerraceCornerLevels", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
        groundMaterial.SetVector("_TerraceCornerOrigin", new Vector4(
            coordinate.x * TerrainTile.TileSizeMetres - halfTile,
            coordinate.y * TerrainTile.TileSizeMetres - halfTile,
            2f,
            cornersPerSide));
    }
}
#endif
