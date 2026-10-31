#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// Layers a tile's source masks into the one signed-distance field the ground material samples.
// Order (TT-D21): base, then paint overlay, then road wins. The river/shallow layer comes in a later phase.
public static class TerrainCompositor
{
    private const byte PaintedAlphaThreshold = 127;

    private const byte WaterChannelThreshold = 127;
    private static readonly Color32 RiverBedDirtColour = new Color32(0, 0, 0, 255);

    public static void CompositeTexels(
        Color32[] baseMask,
        Color32[] paintOverlay,
        Color32[] roadMask,
        int resolution,
        RectInt texelRect,
        Color32[] compositeInOut)
    {
        CompositeTexels(baseMask, paintOverlay, null, roadMask, resolution, texelRect, compositeInOut);
    }

    // Surface overlay: R = shallow water, G = deep water. Water-covered ground paints as the Dirt river bed.
    public static void CompositeTexels(
        Color32[] baseMask,
        Color32[] paintOverlay,
        Color32[] surfaceOverlay,
        Color32[] roadMask,
        int resolution,
        RectInt texelRect,
        Color32[] compositeInOut)
    {
        int minimumX = Mathf.Max(texelRect.xMin, 0);
        int minimumY = Mathf.Max(texelRect.yMin, 0);
        int maximumXExclusive = Mathf.Min(texelRect.xMax, resolution);
        int maximumYExclusive = Mathf.Min(texelRect.yMax, resolution);

        for (int texelY = minimumY; texelY < maximumYExclusive; texelY++)
        {
            for (int texelX = minimumX; texelX < maximumXExclusive; texelX++)
            {
                int texelIndex = texelY * resolution + texelX;
                Color32 chosen = baseMask[texelIndex];
                if (paintOverlay != null && paintOverlay[texelIndex].a > PaintedAlphaThreshold)
                    chosen = paintOverlay[texelIndex];
                if (surfaceOverlay != null
                    && (surfaceOverlay[texelIndex].r > WaterChannelThreshold || surfaceOverlay[texelIndex].g > WaterChannelThreshold))
                    chosen = RiverBedDirtColour;
                if (roadMask != null && roadMask[texelIndex].a > PaintedAlphaThreshold)
                    chosen = roadMask[texelIndex];
                compositeInOut[texelIndex] = new Color32(chosen.r, chosen.g, chosen.b, 255);
            }
        }
    }

    // Returns true when the heights changed since the last composite, so the caller must rebuild the mesh
    public static bool Composite(TerrainTile tile)
    {
        if (tile.tileData == null) TerrainTileAssets.EnsureTileData(tile);
        TerrainTileData tileData = tile.tileData;
        Vector2Int coordinate = tile.tileCoordinate;

        string baseMaskPath = tileData.baseMask != null ? AssetDatabase.GetAssetPath(tileData.baseMask) : null;
        if (string.IsNullOrEmpty(baseMaskPath)) baseMaskPath = TerrainTileAssets.MaskPath(coordinate);

        int resolution;
        Color32[] baseMask = TerrainSplatCodec.ReadPng(baseMaskPath, out resolution);
        Color32[] paintOverlay = ReadOptionalSource(TerrainTileAssets.PaintOverlayPath(coordinate), resolution);
        Color32[] surfaceOverlay = ReadOptionalSource(TerrainTileAssets.SurfaceOverlayPath(coordinate), resolution);
        Color32[] roadMask = ReadOptionalSource(TerrainTileAssets.RoadMaskPath(coordinate), resolution);

        Color32[] composite = new Color32[resolution * resolution];
        CompositeTexels(baseMask, paintOverlay, surfaceOverlay, roadMask, resolution, new RectInt(0, 0, resolution, resolution), composite);

        Color32[] signedDistanceField = TerrainSplatCodec.EncodeSignedDistanceField(composite, resolution, TerrainTile.TileSizeMetres);
        string signedDistanceFieldPath = TerrainTileAssets.SignedDistanceFieldPath(coordinate);
        TerrainSplatCodec.WritePng(signedDistanceFieldPath, signedDistanceField, resolution, true);
        Texture2D signedDistanceFieldTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(signedDistanceFieldPath);
        tile.splatSignedDistanceField = signedDistanceFieldTexture;
        tileData.compositeSdf = signedDistanceFieldTexture;

        TerrainTileAssets.ApplyToMaterial(tile);

        bool heightsNeedMeshRebuild = tileData.revision != tileData.compositedRevision;
        tileData.compositedRevision = tileData.revision;
        EditorUtility.SetDirty(tile);
        EditorUtility.SetDirty(tileData);
        return heightsNeedMeshRebuild;
    }

    private static Color32[] ReadOptionalSource(string assetPath, int expectedResolution)
    {
        if (!File.Exists(assetPath)) return null;
        int sourceResolution;
        Color32[] pixels = TerrainSplatCodec.ReadPng(assetPath, out sourceResolution);
        if (sourceResolution == expectedResolution) return pixels;
        Debug.LogWarning("TerrainCompositor ignored " + assetPath + ": resolution " + sourceResolution + " differs from the base mask's " + expectedResolution);
        return null;
    }
}
#endif
