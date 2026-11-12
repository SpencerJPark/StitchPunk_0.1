#if UNITY_EDITOR
using Unity.Mathematics;
using UnityEngine;

// Scatter sampler that only accepts points inside an area outline, optionally punching noise clearings into it.
public sealed class TerrainAreaRestrictedSampler : ITerrainScatterSampler
{
    private readonly ITerrainScatterSampler inner;
    private readonly TerrainAreaStamp area;
    private readonly float clearingNoiseScaleMetres;
    private readonly float clearingThreshold01;
    private readonly float2 clearingNoiseOffset;

    public TerrainAreaRestrictedSampler(ITerrainScatterSampler inner, TerrainAreaStamp area, float clearingNoiseScaleMetres, float clearingThreshold01)
    {
        this.inner = inner;
        this.area = area;
        this.clearingNoiseScaleMetres = math.max(clearingNoiseScaleMetres, 0.001f);
        this.clearingThreshold01 = clearingThreshold01;

        // Hash-style offset so different seeds sample different parts of the noise field.
        uint hashedSeed = math.hash(new int2(area.seed, 0x5bd1e995));
        clearingNoiseOffset = new float2((hashedSeed & 0xFFFFu) * 0.01f, ((hashedSeed >> 16) & 0xFFFFu) * 0.01f);
    }

    public float EdgeDistanceMetresAt(float worldX, float worldZ)
    {
        return TerrainAreaRasterizer.SignedDistanceToEdgeMetres(area, worldX, worldZ);
    }

    public float HeightAt(float worldX, float worldZ)
    {
        return inner.HeightAt(worldX, worldZ);
    }

    public Vector3 NormalAt(float worldX, float worldZ)
    {
        return inner.NormalAt(worldX, worldZ);
    }

    public bool TryGetBiome(float worldX, float worldZ, out float dirt, out float grass, out float forest, out float stone)
    {
        if (EdgeDistanceMetresAt(worldX, worldZ) < 0f || IsInsideClearing(worldX, worldZ))
        {
            dirt = 0f;
            grass = 0f;
            forest = 0f;
            stone = 0f;
            return false;
        }

        return inner.TryGetBiome(worldX, worldZ, out dirt, out grass, out forest, out stone);
    }

    private bool IsInsideClearing(float worldX, float worldZ)
    {
        if (clearingThreshold01 <= 0f)
        {
            return false;
        }

        float2 noiseCoordinate = new float2(worldX, worldZ) / clearingNoiseScaleMetres + clearingNoiseOffset;
        float noise01 = noise.snoise(noiseCoordinate) * 0.5f + 0.5f;
        return noise01 < clearingThreshold01;
    }
}
#endif
