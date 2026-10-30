#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// Where a tile's mask, SDF and material live, and the wiring between them
public static class TerrainTileAssets
{
    public const string TileFolder = "Assets/Terrain/Tiles";
    const string MaterialTemplatePath = "Assets/PaintedGround/Materials/M_PaintedGround.mat";

    public static string MaskPath(Vector2Int coordinate)
    {
        return TileFolder + "/Tile_" + coordinate.x + "_" + coordinate.y + "_Mask.png";
    }

    public static string SignedDistanceFieldPath(Vector2Int coordinate)
    {
        return TileFolder + "/Tile_" + coordinate.x + "_" + coordinate.y + "_Splat_SDF.png";
    }

    public static string MaterialPath(Vector2Int coordinate)
    {
        return TileFolder + "/M_Ground_Tile_" + coordinate.x + "_" + coordinate.y + ".mat";
    }

    // Creates whatever is missing (an existing mask is never overwritten), re-derives the SDF and
    // points the tile's renderer at a material reading it.
    public static void EnsureAssets(TerrainTile tile, TerrainSplatLayer fillWhenMaskIsNew)
    {
        Vector2Int coordinate = tile.tileCoordinate;
        if (!Directory.Exists(TileFolder)) Directory.CreateDirectory(TileFolder);

        string maskPath = MaskPath(coordinate);
        if (!File.Exists(maskPath))
        {
            Color32[] filledMask = TerrainSplatCodec.CreateFilledMask(TerrainSplatCodec.MaskResolution, fillWhenMaskIsNew);
            TerrainSplatCodec.WritePng(maskPath, filledMask, TerrainSplatCodec.MaskResolution, false);
        }

        tile.splatMask = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
        RebuildSignedDistanceField(tile);
        ApplyToMaterial(tile);
        EditorUtility.SetDirty(tile);
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
