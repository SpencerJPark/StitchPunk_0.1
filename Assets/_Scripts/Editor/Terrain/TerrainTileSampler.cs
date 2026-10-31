#if UNITY_EDITOR
using System.IO;
using UnityEngine;

// Read-only scatter sampler over one tile: composite hard mask for biome, surface overlay for water, tile data for height.
public class TerrainTileSampler : ITerrainScatterSampler
{
    private const byte ChannelThreshold = 127;
    private const float NormalProbeMetres = 0.5f;

    private readonly TerrainTile tile;
    private readonly Color32[] compositeMask;
    private readonly Color32[] surfaceOverlay;
    private readonly int resolution;

    public Rect WorldRect { get; }

    public TerrainTileSampler(TerrainTile tile)
    {
        this.tile = tile;
        Vector2Int coordinate = tile.tileCoordinate;
        WorldRect = new Rect(coordinate.x * TerrainTile.TileSizeMetres - 50f, coordinate.y * TerrainTile.TileSizeMetres - 50f, TerrainTile.TileSizeMetres, TerrainTile.TileSizeMetres);

        string maskPath = TerrainTileAssets.MaskPath(coordinate);
        if (!File.Exists(maskPath))
        {
            return;
        }

        Color32[] baseMask = TerrainSplatCodec.ReadPng(maskPath, out resolution);
        Color32[] paintOverlay = ReadSameResolution(TerrainTileAssets.PaintOverlayPath(coordinate));
        Color32[] roadMask = ReadSameResolution(TerrainTileAssets.RoadMaskPath(coordinate));
        surfaceOverlay = ReadSameResolution(TerrainTileAssets.SurfaceOverlayPath(coordinate));

        compositeMask = new Color32[baseMask.Length];
        TerrainCompositor.CompositeTexels(baseMask, paintOverlay, null, roadMask, resolution, new RectInt(0, 0, resolution, resolution), compositeMask);
    }

    public float HeightAt(float worldX, float worldZ)
    {
        return tile.tileData.SampleBilinear(worldX, worldZ);
    }

    public Vector3 NormalAt(float worldX, float worldZ)
    {
        float slopeAlongX = (HeightAt(worldX + NormalProbeMetres, worldZ) - HeightAt(worldX - NormalProbeMetres, worldZ)) / (2f * NormalProbeMetres);
        float slopeAlongZ = (HeightAt(worldX, worldZ + NormalProbeMetres) - HeightAt(worldX, worldZ - NormalProbeMetres)) / (2f * NormalProbeMetres);
        return new Vector3(-slopeAlongX, 1f, -slopeAlongZ).normalized;
    }

    public bool TryGetBiome(float worldX, float worldZ, out float dirt, out float grass, out float forest, out float stone)
    {
        dirt = 0f;
        grass = 0f;
        forest = 0f;
        stone = 0f;
        if (compositeMask == null || !WorldRect.Contains(new Vector2(worldX, worldZ)))
        {
            return false;
        }

        int texelX = Mathf.Clamp((int)((worldX - WorldRect.xMin) / WorldRect.width * resolution), 0, resolution - 1);
        int texelY = Mathf.Clamp((int)((worldZ - WorldRect.yMin) / WorldRect.height * resolution), 0, resolution - 1);
        int texelIndex = texelY * resolution + texelX;

        if (surfaceOverlay != null)
        {
            Color32 surface = surfaceOverlay[texelIndex];
            if (surface.r > ChannelThreshold || surface.g > ChannelThreshold)
            {
                return false;
            }
        }

        Color32 composite = compositeMask[texelIndex];
        if (composite.r > ChannelThreshold) grass = 1f;
        else if (composite.g > ChannelThreshold) forest = 1f;
        else if (composite.b > ChannelThreshold) stone = 1f;
        else dirt = 1f;
        return true;
    }

    private Color32[] ReadSameResolution(string assetPath)
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
