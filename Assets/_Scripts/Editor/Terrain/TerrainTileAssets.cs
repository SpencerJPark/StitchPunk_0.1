#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// Where a tile's mask, SDF and material live, and the wiring between them
public static class TerrainTileAssets
{
    public const string TileFolder = "Assets/Terrain/Tiles";
    const string MaterialTemplatePath = "Assets/PaintedGround/Materials/M_PaintedGround.mat";

    public static string FolderOf(Vector2Int coordinate)
    {
        return TileFolder + "/Tile_" + coordinate.x + "_" + coordinate.y;
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
        return TileFolder + "/Tile_" + coordinate.x + "_" + coordinate.y + "_Mask.png";
    }

    static string LegacySignedDistanceFieldPath(Vector2Int coordinate)
    {
        return TileFolder + "/Tile_" + coordinate.x + "_" + coordinate.y + "_Splat_SDF.png";
    }

    static string LegacyMaterialPath(Vector2Int coordinate)
    {
        return TileFolder + "/M_Ground_Tile_" + coordinate.x + "_" + coordinate.y + ".mat";
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
            Color32[] filledMask = TerrainSplatCodec.CreateFilledMask(TerrainSplatCodec.MaskResolution, fillWhenMaskIsNew);
            TerrainSplatCodec.WritePng(maskPath, filledMask, TerrainSplatCodec.MaskResolution, false);
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
        tileData.maskResolution = tile.splatMask != null ? tile.splatMask.width : TerrainSplatCodec.MaskResolution;
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
        EditorUtility.SetDirty(groundMaterial);
        AssetDatabase.SaveAssetIfDirty(groundMaterial);

        MeshRenderer tileRenderer = tile.GetComponent<MeshRenderer>();
        if (tileRenderer != null) tileRenderer.sharedMaterial = groundMaterial;
    }
}
#endif
