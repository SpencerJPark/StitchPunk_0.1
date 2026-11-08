using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Publishes which tiles are drawn as real ground, one texel per tile, so the far-view stand-in fills every tile that is not
// (still loading, or outside the load radius) instead of leaving a hole down to the sky at ground-level zoom.
[UpdateInGroup(typeof(TerrainStreamingSystemGroup))]
[UpdateAfter(typeof(TerrainStreamingSystem))]
public partial class TerrainTileResidencyShaderSystem : SystemBase
{
    private static readonly int TileResidencyTextureId = Shader.PropertyToID("_TerrainTileResidency");
    // xy = min tile coordinate, zw = tile count; zw = 0 tells the shader no residency is published (stand-in follows the zoom fade only).
    private static readonly int TileResidencyGridId = Shader.PropertyToID("_TerrainTileResidencyGrid");

    private Texture2D tileResidencyTexture;
    private byte[] tileResidencyTexels;
    private int2 publishedMinimumTile;
    private int2 publishedTileCount;
    private bool uploadPending;

    protected override void OnCreate()
    {
        RequireForUpdate<TerrainTileEntry>();
    }

    protected override void OnDestroy()
    {
        Shader.SetGlobalVector(TileResidencyGridId, Vector4.zero);
        if (tileResidencyTexture != null)
            Object.Destroy(tileResidencyTexture);
    }

    protected override void OnUpdate()
    {
        Entity settingsEntity = SystemAPI.GetSingletonEntity<TerrainStreamingSettings>();
        DynamicBuffer<TerrainTileEntry> entries = SystemAPI.GetBuffer<TerrainTileEntry>(settingsEntity);
        if (entries.Length == 0)
            return;

        int2 minimumTile = entries[0].coordinate;
        int2 maximumTile = entries[0].coordinate;
        for (int index = 1; index < entries.Length; index++)
        {
            minimumTile = math.min(minimumTile, entries[index].coordinate);
            maximumTile = math.max(maximumTile, entries[index].coordinate);
        }
        int2 tileCount = maximumTile - minimumTile + 1;

        if (tileResidencyTexture == null || !publishedMinimumTile.Equals(minimumTile) || !publishedTileCount.Equals(tileCount))
            CreateResidencyTexture(minimumTile, tileCount);

        bool residencyChanged = uploadPending;
        uploadPending = false;
        for (int index = 0; index < entries.Length; index++)
        {
            int2 local = entries[index].coordinate - minimumTile;
            int texelIndex = local.y * tileCount.x + local.x;
            byte residency = entries[index].state == TerrainTileState.Loaded ? (byte)255 : (byte)0;
            if (tileResidencyTexels[texelIndex] != residency)
            {
                tileResidencyTexels[texelIndex] = residency;
                residencyChanged = true;
            }
        }

        if (residencyChanged)
        {
            tileResidencyTexture.SetPixelData(tileResidencyTexels, 0);
            tileResidencyTexture.Apply(false, false);
        }
    }

    private void CreateResidencyTexture(int2 minimumTile, int2 tileCount)
    {
        if (tileResidencyTexture != null)
            Object.Destroy(tileResidencyTexture);

        tileResidencyTexture = new Texture2D(tileCount.x, tileCount.y, TextureFormat.R8, false, true)
        {
            name = "TerrainTileResidency",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave,
        };
        // Grid cells with no tile entry have no real ground to stand in for, so they start (and stay) "drawn".
        tileResidencyTexels = new byte[tileCount.x * tileCount.y];
        System.Array.Fill(tileResidencyTexels, (byte)255);
        uploadPending = true;
        publishedMinimumTile = minimumTile;
        publishedTileCount = tileCount;

        Shader.SetGlobalTexture(TileResidencyTextureId, tileResidencyTexture);
        Shader.SetGlobalVector(TileResidencyGridId, new Vector4(minimumTile.x, minimumTile.y, tileCount.x, tileCount.y));
    }
}
