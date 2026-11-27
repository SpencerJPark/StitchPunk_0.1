using System.Collections.Generic;
using Unity.Mathematics;

// NC-D2 / NC-D7: pure plateau field maths; no UnityEngine objects, EditMode-testable.
public static class TerracePlateauField
{
    public const float LockMetres = 6f;
    public const float BlendMetres = 24f;

    private const float TileSizeMetres = 100f;
    private const float CoveFrequencyPerMetre = 0.02f;
    private const float WobbleFrequencyPerMetre = 0.07f;
    private const int MacroOctaveCount = 3;
    private const float SeedOffsetRangeMetres = 5000f;

    private static readonly float2 WarpTapOffsetX = new float2(137.3f, 71.9f);
    private static readonly float2 WarpTapOffsetZ = new float2(-52.7f, 214.1f);
    private static readonly float2 MacroTapOffset = new float2(311.7f, -128.4f);
    private static readonly float2 CoveTapOffset = new float2(-409.2f, 263.5f);
    private static readonly float2 WobbleTapOffset = new float2(87.6f, -377.8f);

    public static float SampleStyle(float2 worldXZ, float2 seedOffset, in TerracePlateauStyle style)
    {
        float grainRadians = math.radians(style.grainDegrees);
        float grainSine = math.sin(grainRadians);
        float grainCosine = math.cos(grainRadians);
        float2 rotated = new float2(
            worldXZ.x * grainCosine + worldXZ.y * grainSine,
            -worldXZ.x * grainSine + worldXZ.y * grainCosine);
        rotated.x /= math.max(style.grainStretch, 0.01f);

        float2 warpPoint = rotated * style.warpFrequencyPerMetre;
        float2 warped = rotated + style.warpMetres * new float2(
            noise.snoise(warpPoint + seedOffset + WarpTapOffsetX),
            noise.snoise(warpPoint + seedOffset + WarpTapOffsetZ));

        float macro = 0f;
        float amplitude = 1f;
        float amplitudeSum = 0f;
        float frequency = style.macroFrequencyPerMetre;
        for (int octave = 0; octave < MacroOctaveCount; octave++)
        {
            macro += amplitude * noise.snoise(warped * frequency + seedOffset + MacroTapOffset + new float2(octave * 17.3f, octave * 41.1f));
            amplitudeSum += amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }
        macro /= amplitudeSum;

        // Coves and wobble keep their metre scale on a stretched grain, or an escarpment's sides run dead straight for 50 m.
        float2 warpedAtMetreScale = new float2(warped.x * math.max(style.grainStretch, 0.01f), warped.y);
        float coves = noise.snoise(warpedAtMetreScale * CoveFrequencyPerMetre + seedOffset + CoveTapOffset) * style.coveAmount;
        float wobble = noise.snoise(warpedAtMetreScale * WobbleFrequencyPerMetre + seedOffset + WobbleTapOffset) * style.wobbleAmount;
        return macro + coves + wobble + style.bias;
    }

    // 0 in the lock band and outside the tile, 1 from LockMetres + BlendMetres inside.
    public static float TileBlendFactor(float2 worldXZ, int2 tileCoordinate)
    {
        float2 localFromTileCentre = worldXZ - (float2)tileCoordinate * TileSizeMetres;
        float distanceToNearestBorder = TileSizeMetres * 0.5f - math.max(math.abs(localFromTileCentre.x), math.abs(localFromTileCentre.y));
        if (distanceToNearestBorder <= 0f)
        {
            return 0f;
        }
        return math.smoothstep(LockMetres, LockMetres + BlendMetres, distanceToNearestBorder);
    }

    public static float BlendWorldAndTile(float worldValue, float tileValue, float blendFactor, float mean)
    {
        if (blendFactor <= 0f)
        {
            return worldValue;
        }
        float blended = math.lerp(worldValue, tileValue, blendFactor);
        float varianceRestore = math.sqrt(blendFactor * blendFactor + (1f - blendFactor) * (1f - blendFactor));
        return mean + (blended - mean) / varianceRestore;
    }

    public static float2 WorldSeedOffset(int worldSeed)
    {
        return SeedOffsetFromHash(math.hash(new int2(worldSeed, 0x5EED)));
    }

    public static float2 TileSeedOffset(int worldSeed, int2 tileCoordinate, int rollIndex)
    {
        return SeedOffsetFromHash(math.hash(new int4(worldSeed, tileCoordinate.x, tileCoordinate.y, rollIndex)));
    }

    public static int StyleIndexFor(int worldSeed, int2 tileCoordinate, int rollIndex, IReadOnlyList<TerracePlateauStyle> styles)
    {
        if (styles == null || styles.Count == 0)
        {
            return 0;
        }
        float totalWeight = 0f;
        for (int styleIndex = 0; styleIndex < styles.Count; styleIndex++)
        {
            totalWeight += math.max(styles[styleIndex].weight, 0f);
        }
        if (totalWeight <= 0f)
        {
            return 0;
        }

        uint pickHash = math.hash(new int4(rollIndex ^ 0x7A11C0DE, tileCoordinate.y, tileCoordinate.x, worldSeed ^ 0x3C6EF372));
        Random pickRandom = new Random(pickHash | 1u);
        float pick = pickRandom.NextFloat() * totalWeight;
        float accumulated = 0f;
        int lastPickableIndex = 0;
        for (int styleIndex = 0; styleIndex < styles.Count; styleIndex++)
        {
            float weight = math.max(styles[styleIndex].weight, 0f);
            if (weight <= 0f)
            {
                continue;
            }
            lastPickableIndex = styleIndex;
            accumulated += weight;
            if (pick < accumulated)
            {
                return styleIndex;
            }
        }
        return lastPickableIndex;
    }

    // The full NC-D7 value at one point: world field (style 0, world seed) blended with the tile's rolled style and seed.
    // Thresholds blend by the same factor. Outside the tile (generation margin) this is the world field exactly.
    public static float SampleTileField(int worldSeed, int2 tileCoordinate, int rollIndex, IReadOnlyList<TerracePlateauStyle> styles,
        float2 worldXZ, out float firstLevelThreshold, out float secondLevelThreshold)
    {
        TerracePlateauStyle worldStyle = styles[0];
        float worldValue = SampleStyle(worldXZ, WorldSeedOffset(worldSeed), worldStyle);
        float blendFactor = TileBlendFactor(worldXZ, tileCoordinate);
        if (blendFactor <= 0f)
        {
            firstLevelThreshold = worldStyle.firstLevelThreshold;
            secondLevelThreshold = worldStyle.secondLevelThreshold;
            return worldValue;
        }

        TerracePlateauStyle tileStyle = styles[StyleIndexFor(worldSeed, tileCoordinate, rollIndex, styles)];
        float tileValue = SampleStyle(worldXZ, TileSeedOffset(worldSeed, tileCoordinate, rollIndex), tileStyle);
        firstLevelThreshold = math.lerp(worldStyle.firstLevelThreshold, tileStyle.firstLevelThreshold, blendFactor);
        secondLevelThreshold = math.lerp(worldStyle.secondLevelThreshold, tileStyle.secondLevelThreshold, blendFactor);
        float mean = math.lerp(worldStyle.bias, tileStyle.bias, blendFactor);
        return BlendWorldAndTile(worldValue, tileValue, blendFactor, mean);
    }

    private static float2 SeedOffsetFromHash(uint hash)
    {
        Random seedRandom = new Random(hash | 1u);
        return seedRandom.NextFloat2(new float2(-SeedOffsetRangeMetres), new float2(SeedOffsetRangeMetres));
    }
}
