#if UNITY_EDITOR
using Unity.Mathematics;
using UnityEngine;

// Pure function of (profile, world position). Noise is sampled in world coordinates so neighbouring
// tiles agree exactly on their shared edge; never read a neighbour tile from here.
public struct TerraceRampPlacement
{
    public Vector2 centreWorld;
    public float yawDegrees;
    public int lowerLevel;
    public TerraceRampKind kind;
    public float runMetres;
    public float widthMetres;
    public bool risesToTheRight;
    public bool isLowStepWornGap;
}

public static class TerrainProceduralGenerator
{
    // Serial on purpose: Parallel.For raced (AnimationCurve.Evaluate in the height remap), giving different masks run to run.
    private static void ForEachRowInOrder(int firstRow, int rowEndExclusive, System.Action<int> rowBody)
    {
        for (int rowIndex = firstRow; rowIndex < rowEndExclusive; rowIndex++) rowBody(rowIndex);
    }

    private const int SamplesPerSide = TerrainTileData.HeightSamplesPerSide;
    private const float TileSizeMetres = TerrainTileData.TileSizeMetres;
    private const int TerraceCellsPerSide = 100;

    public static float AnalyticHeightAt(TerrainGenerationProfile profile, float worldX, float worldZ)
    {
        return AnalyticHeightAt(profile, worldX, worldZ, Vector2Int.zero, 0);
    }

    public static float AnalyticHeightAt(TerrainGenerationProfile profile, float worldX, float worldZ, Vector2Int tileCoordinate, int rollIndex)
    {
        float2 seedOffset = CreateSeedOffset(profile.seed);
        float2 worldPosition = new float2(worldX, worldZ);

        // Roll 0 skips the blend entirely so a roll-0 regenerate reproduces today's heights bit for bit.
        bool rolled = rollIndex != 0;
        int2 tileCoordinateInt = new int2(tileCoordinate.x, tileCoordinate.y);
        float tileBlend = rolled ? TerracePlateauField.TileBlendFactor(worldPosition, tileCoordinateInt) : 0f;
        float2 tileSeedOffset = rolled ? seedOffset + TerracePlateauField.TileSeedOffset(profile.seed, tileCoordinateInt, rollIndex) : seedOffset;

        float noiseSum = 0f;
        float maxAmplitude = 0f;
        for (int layerIndex = 0; layerIndex < profile.heightLayers.Count; layerIndex++)
        {
            TerrainHeightLayer layer = profile.heightLayers[layerIndex];
            float frequency = layer.frequencyPerMetre;
            float amplitude = layer.amplitudeMetres;
            for (int octave = 0; octave < layer.octaves; octave++)
            {
                float octaveNoise = noise.snoise(worldPosition * frequency + seedOffset);
                if (rolled && tileBlend > 0f)
                {
                    float tileOctaveNoise = noise.snoise(worldPosition * frequency + tileSeedOffset);
                    octaveNoise = TerracePlateauField.BlendWorldAndTile(octaveNoise, tileOctaveNoise, tileBlend, 0f);
                }
                noiseSum += octaveNoise * amplitude;
                maxAmplitude += Mathf.Abs(amplitude);
                frequency *= layer.lacunarity;
                amplitude *= layer.gain;
            }
        }

        float noiseHeight = 0f;
        if (maxAmplitude > 0f)
        {
            float normalised = Mathf.Clamp01(noiseSum / (2f * maxAmplitude) + 0.5f);
            float remapped = profile.heightRemap != null ? profile.heightRemap.Evaluate(normalised) : normalised;
            noiseHeight = (remapped - 0.5f) * 2f * maxAmplitude;
        }

        float height = profile.baseHeightMetres + noiseHeight + profile.landLiftMetres;

        if (profile.mountainRadiusMetres > 0f)
        {
            float distanceToCentre = Vector2.Distance(new Vector2(worldX, worldZ), profile.mountainCentreWorld);
            if (distanceToCentre < profile.mountainRadiusMetres)
            {
                float closeness = 1f - distanceToCentre / profile.mountainRadiusMetres;
                float profileValue = profile.mountainProfile != null ? profile.mountainProfile.Evaluate(closeness) : closeness;
                height += profile.mountainHeightMetres * profileValue;
            }
        }

        if (profile.mountains != null)
        {
            Vector2 worldPoint = new Vector2(worldX, worldZ);
            float tallestContribution = 0f;
            for (int mountainIndex = 0; mountainIndex < profile.mountains.Count; mountainIndex++)
            {
                TerrainMountainFeature mountain = profile.mountains[mountainIndex];
                if (mountain.radiusMetres <= 0f) continue;

                float distanceToFeature = DistanceToSegment(worldPoint, mountain.startWorld, mountain.endWorld);
                if (distanceToFeature >= mountain.radiusMetres) continue;

                float closeness = 1f - distanceToFeature / mountain.radiusMetres;
                float profileValue = profile.mountainProfile != null ? profile.mountainProfile.Evaluate(closeness) : closeness;
                tallestContribution = Mathf.Max(tallestContribution, mountain.heightMetres * profileValue);
            }
            height += tallestContribution;
        }

        height = Mathf.Max(height, profile.flattenBelowMetres);

        // Last, so a flat area wins over the mountains and the sea floor clamp inside its radius.
        if (profile.flatAreas != null)
        {
            Vector2 worldPoint = new Vector2(worldX, worldZ);
            for (int areaIndex = 0; areaIndex < profile.flatAreas.Count; areaIndex++)
            {
                TerrainFlatArea flatArea = profile.flatAreas[areaIndex];
                float distanceToCentre = Vector2.Distance(worldPoint, flatArea.centreWorld);
                float weight;
                if (flatArea.blendEndRadiusMetres <= flatArea.flatRadiusMetres)
                    weight = distanceToCentre <= flatArea.flatRadiusMetres ? 0f : 1f;
                else
                    weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(flatArea.flatRadiusMetres, flatArea.blendEndRadiusMetres, distanceToCentre));
                height = Mathf.Lerp(flatArea.heightMetres, height, weight);
            }
        }

        return height;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
    {
        Vector2 segment = segmentEnd - segmentStart;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared <= 0f) return Vector2.Distance(point, segmentStart);

        float along = Mathf.Clamp01(Vector2.Dot(point - segmentStart, segment) / lengthSquared);
        return Vector2.Distance(point, segmentStart + segment * along);
    }

    public static void GenerateHeights(TerrainGenerationProfile profile, Vector2Int tileCoordinate, float[] outHeights)
    {
        GenerateHeights(profile, tileCoordinate, 0, outHeights);
    }

    public static void GenerateHeights(TerrainGenerationProfile profile, Vector2Int tileCoordinate, int rollIndex, float[] outHeights)
    {
        float minCornerX = tileCoordinate.x * TileSizeMetres - TileSizeMetres * 0.5f;
        float minCornerZ = tileCoordinate.y * TileSizeMetres - TileSizeMetres * 0.5f;
        for (int sampleZ = 0; sampleZ < SamplesPerSide; sampleZ++)
        {
            for (int sampleX = 0; sampleX < SamplesPerSide; sampleX++)
            {
                outHeights[sampleZ * SamplesPerSide + sampleX] =
                    AnalyticHeightAt(profile, minCornerX + sampleX, minCornerZ + sampleZ, tileCoordinate, rollIndex);
            }
        }
    }

    public static void GenerateBaseMask(TerrainGenerationProfile profile, Vector2Int tileCoordinate, int resolution, Color32[] outMask)
    {
        GenerateBaseMask(profile, tileCoordinate, 0, resolution, outMask);
    }

    public static void GenerateBaseMask(TerrainGenerationProfile profile, Vector2Int tileCoordinate, int rollIndex, int resolution, Color32[] outMask)
    {
        float2 seedOffset = CreateSeedOffset(profile.seed);
        float minCornerX = tileCoordinate.x * TileSizeMetres - TileSizeMetres * 0.5f;
        float minCornerZ = tileCoordinate.y * TileSizeMetres - TileSizeMetres * 0.5f;
        float texelSizeMetres = TileSizeMetres / resolution;

        ForEachRowInOrder(0, resolution, (int texelZ) =>
        {
            for (int texelX = 0; texelX < resolution; texelX++)
            {
                float worldX = minCornerX + (texelX + 0.5f) * texelSizeMetres;
                float worldZ = minCornerZ + (texelZ + 0.5f) * texelSizeMetres;

                outMask[texelZ * resolution + texelX] = TerrainSplatCodec.ColourOfLayer(BiomeLayerAt(profile, worldX, worldZ, seedOffset, tileCoordinate, rollIndex));
            }
        });
    }

    private static TerrainSplatLayer BiomeLayerAt(TerrainGenerationProfile profile, float worldX, float worldZ, float2 seedOffset, Vector2Int tileCoordinate, int rollIndex)
    {
        // A lock-band point reads the world field for every sample, or its 1 m slope taps leak the roll in from past the band.
        if (rollIndex != 0 && TerracePlateauField.TileBlendFactor(new float2(worldX, worldZ), new int2(tileCoordinate.x, tileCoordinate.y)) <= 0f) rollIndex = 0;
        float height = AnalyticHeightAt(profile, worldX, worldZ, tileCoordinate, rollIndex);
        float slopeAlongX = (AnalyticHeightAt(profile, worldX + 1f, worldZ, tileCoordinate, rollIndex) - AnalyticHeightAt(profile, worldX - 1f, worldZ, tileCoordinate, rollIndex)) * 0.5f;
        float slopeAlongZ = (AnalyticHeightAt(profile, worldX, worldZ + 1f, tileCoordinate, rollIndex) - AnalyticHeightAt(profile, worldX, worldZ - 1f, tileCoordinate, rollIndex)) * 0.5f;
        bool rolled = rollIndex != 0;
        int2 tileCoordinateInt = new int2(tileCoordinate.x, tileCoordinate.y);
        float tileBlend = rolled ? TerracePlateauField.TileBlendFactor(new float2(worldX, worldZ), tileCoordinateInt) : 0f;
        float2 tileSeedShift = rolled ? TerracePlateauField.TileSeedOffset(profile.seed, tileCoordinateInt, rollIndex) : float2.zero;
        float slopeDegrees = Mathf.Atan(Mathf.Sqrt(slopeAlongX * slopeAlongX + slopeAlongZ * slopeAlongZ)) * Mathf.Rad2Deg;

        // Warp + per-rule rotation affect the rule noise only; height and slope stay at the true position.
        float2 warpedPosition = new float2(worldX, worldZ);
        if (profile.biomeNoiseWarpMetres > 0f)
        {
            float2 warpSamplePosition = warpedPosition * profile.biomeNoiseWarpFrequency + seedOffset;
            float2 warpOffsetMetres = new float2(
                noise.snoise(warpSamplePosition + new float2(37.1f, 11.7f)),
                noise.snoise(warpSamplePosition + new float2(-19.3f, 53.9f))) * profile.biomeNoiseWarpMetres;
            warpedPosition += warpOffsetMetres;
        }

        TerrainSplatLayer chosenLayer = TerrainSplatLayer.Dirt;
        for (int ruleIndex = 0; ruleIndex < profile.biomeRules.Count; ruleIndex++)
        {
            TerrainBiomeRule rule = profile.biomeRules[ruleIndex];
            if (height < rule.heightRange.x || height > rule.heightRange.y) continue;
            if (slopeDegrees < rule.slopeRangeDegrees.x || slopeDegrees > rule.slopeRangeDegrees.y) continue;

            float2 ruleSamplePosition = warpedPosition;
            if (profile.biomeNoiseRotationDegrees != 0f)
            {
                float rotationRadians = math.radians((ruleIndex + 1) * profile.biomeNoiseRotationDegrees);
                float rotationSine = math.sin(rotationRadians);
                float rotationCosine = math.cos(rotationRadians);
                ruleSamplePosition = new float2(
                    warpedPosition.x * rotationCosine - warpedPosition.y * rotationSine,
                    warpedPosition.x * rotationSine + warpedPosition.y * rotationCosine);
            }

            float2 ruleNoisePosition = ruleSamplePosition * rule.noiseFrequency + seedOffset + new float2(ruleIndex * 17.3f, 0f);
            float ruleSnoise = noise.snoise(ruleNoisePosition);
            if (rolled && tileBlend > 0f)
                ruleSnoise = TerracePlateauField.BlendWorldAndTile(ruleSnoise, noise.snoise(ruleNoisePosition + tileSeedShift), tileBlend, 0f);
            float ruleNoise01 = ruleSnoise * 0.5f + 0.5f;
            if (ruleNoise01 * rule.weight >= rule.noiseThreshold)
                chosenLayer = rule.layer;
        }
        return chosenLayer;
    }

    // Natural Cliffs (NC-D3..D8): outLevels / outStepKinds are TerrainTileData's biased level bytes and step-kind bits.
    public static void GenerateTerraceLevels(TerrainGenerationProfile profile, Vector2Int tileCoordinate, int maxLevels,
        int rollIndex, System.Func<Vector2Int, TerrainTileData> neighbourLookup,
        byte[] outLevels, byte[] outStepKinds, System.Collections.Generic.List<TerraceRampPlacement> outRamps)
    {
        System.Array.Fill(outLevels, (byte)TerrainTileData.TerraceLevelBias);
        System.Array.Clear(outStepKinds, 0, outStepKinds.Length);
        if (profile.terraceBiomeRules == null || profile.terraceBiomeRules.Count == 0 || maxLevels <= 0)
            return;

        const int margin = 50;
        const int windowSide = TerraceCellsPerSide + 2 * margin;
        const int maxLevelMagnitude = 3;
        const float uphillProbeMetres = 6f;
        const float downhillDropMetres = 1.5f;
        const float cullProtectionBorderMetres = 6f;
        float2 biomeSeedOffset = CreateSeedOffset(profile.seed);
        float2 cliffSeedOffset = biomeSeedOffset + new float2(-307.9f, 655.1f);
        float tileMinCornerX = tileCoordinate.x * TileSizeMetres - TileSizeMetres * 0.5f;
        float tileMinCornerZ = tileCoordinate.y * TileSizeMetres - TileSizeMetres * 0.5f;
        float minCornerX = tileMinCornerX - margin;
        float minCornerZ = tileMinCornerZ - margin;

        System.Collections.Generic.List<TerracePlateauStyle> styles = profile.plateauStyles;
        if (styles == null || styles.Count == 0) styles = TerracePlateauStyleDefaults.CreateDefaultStyles();

        float minPlateauAreaSquareMetres = float.MaxValue;
        for (int ruleIndex = 0; ruleIndex < profile.terraceBiomeRules.Count; ruleIndex++)
        {
            if (!profile.terraceBiomeRules[ruleIndex].enabled) continue;
            minPlateauAreaSquareMetres = Mathf.Min(minPlateauAreaSquareMetres, profile.terraceBiomeRules[ruleIndex].minPlateauAreaSquareMetres);
        }
        if (minPlateauAreaSquareMetres == float.MaxValue) minPlateauAreaSquareMetres = 0f;

        System.Func<Vector2, bool, int> maxLevelAtWorldPointDirect = (Vector2 worldPoint, bool useRolledField) =>
        {
            int ruleIndex = FirstEnabledRuleIndexAt(profile, worldPoint.x, worldPoint.y, biomeSeedOffset, tileCoordinate, useRolledField ? rollIndex : 0);
            return ruleIndex < 0 ? 0 : Mathf.Min(profile.terraceBiomeRules[ruleIndex].maxLevel, maxLevels);
        };

        // The raster asks for every window cell centre twice (world and rolled field); precompute both grids once.
        const int rasterWindowSide = TerraceCellsPerSide + 2 * TerracePlateauRaster.WindowMarginCells;
        float rasterWindowOriginX = tileCoordinate.x * 100f - 50f - TerracePlateauRaster.WindowMarginCells;
        float rasterWindowOriginZ = tileCoordinate.y * 100f - 50f - TerracePlateauRaster.WindowMarginCells;
        int[] worldFieldCapGrid = new int[rasterWindowSide * rasterWindowSide];
        int[] rolledFieldCapGrid = new int[rasterWindowSide * rasterWindowSide];
        ForEachRowInOrder(0, rasterWindowSide, (int windowZ) =>
        {
            for (int windowX = 0; windowX < rasterWindowSide; windowX++)
            {
                Vector2 cellCentreWorld = new Vector2(rasterWindowOriginX + windowX + 0.5f, rasterWindowOriginZ + windowZ + 0.5f);
                worldFieldCapGrid[windowZ * rasterWindowSide + windowX] = maxLevelAtWorldPointDirect(cellCentreWorld, false);
                rolledFieldCapGrid[windowZ * rasterWindowSide + windowX] = maxLevelAtWorldPointDirect(cellCentreWorld, true);
            }
        });

        System.Func<Vector2, bool, int> maxLevelAtWorldPoint = (Vector2 worldPoint, bool useRolledField) =>
        {
            float windowXExact = worldPoint.x - rasterWindowOriginX - 0.5f;
            float windowZExact = worldPoint.y - rasterWindowOriginZ - 0.5f;
            int windowX = Mathf.RoundToInt(windowXExact);
            int windowZ = Mathf.RoundToInt(windowZExact);
            bool isWindowCellCentre = windowX >= 0 && windowX < rasterWindowSide && windowZ >= 0 && windowZ < rasterWindowSide
                && Mathf.Abs(windowXExact - windowX) <= 0.01f && Mathf.Abs(windowZExact - windowZ) <= 0.01f;
            if (!isWindowCellCentre) return maxLevelAtWorldPointDirect(worldPoint, useRolledField);
            return (useRolledField ? rolledFieldCapGrid : worldFieldCapGrid)[windowZ * rasterWindowSide + windowX];
        };

        int[] plateauLevels = new int[TerraceCellsPerSide * TerraceCellsPerSide];
        byte[] plateauKinds = new byte[plateauLevels.Length];
        TerracePlateauRaster.RasterizeTile(profile.seed, tileCoordinate, rollIndex, styles, maxLevels, minPlateauAreaSquareMetres,
            neighbourLookup, maxLevelAtWorldPoint, plateauLevels, plateauKinds);

        sbyte[] windowLevels = new sbyte[windowSide * windowSide];
        // Core cells are pure math and write only their own index, the halo cells call neighbourLookup and are filled below.
        ForEachRowInOrder(0, windowSide, (int cellZ) =>
        {
            for (int cellX = 0; cellX < windowSide; cellX++)
            {
                int coreX = cellX - margin;
                int coreZ = cellZ - margin;
                bool insideCore = coreX >= 0 && coreZ >= 0 && coreX < TerraceCellsPerSide && coreZ < TerraceCellsPerSide;
                int level;
                if (insideCore)
                {
                    level = plateauLevels[coreZ * TerraceCellsPerSide + coreX];
                    float worldX = minCornerX + cellX + 0.5f;
                    // Lock-band cells judge the slope cliff on the world field: the uphill probe reaches past the band, and I2 needs the band roll-independent.
                    int slopeRollIndex = Mathf.Min(Mathf.Min(coreX, coreZ), Mathf.Min(TerraceCellsPerSide - 1 - coreX, TerraceCellsPerSide - 1 - coreZ)) + 0.5f < TerracePlateauField.LockMetres ? 0 : rollIndex;
                    float worldZ = minCornerZ + cellZ + 0.5f;
                    int slopeRuleIndex = level == 0 ? FirstEnabledRuleIndexAt(profile, worldX, worldZ, biomeSeedOffset, tileCoordinate, slopeRollIndex) : -1;
                    if (slopeRuleIndex >= 0 && profile.terraceBiomeRules[slopeRuleIndex].cliffOnSlope)
                    {
                        TerraceBiomeRule rule = profile.terraceBiomeRules[slopeRuleIndex];
                        float height = AnalyticHeightAt(profile, worldX, worldZ, tileCoordinate, slopeRollIndex);
                        float slopeAlongX = (AnalyticHeightAt(profile, worldX + 1f, worldZ, tileCoordinate, slopeRollIndex) - AnalyticHeightAt(profile, worldX - 1f, worldZ, tileCoordinate, slopeRollIndex)) * 0.5f;
                        float slopeAlongZ = (AnalyticHeightAt(profile, worldX, worldZ + 1f, tileCoordinate, slopeRollIndex) - AnalyticHeightAt(profile, worldX, worldZ - 1f, tileCoordinate, slopeRollIndex)) * 0.5f;
                        float gradientLength = Mathf.Sqrt(slopeAlongX * slopeAlongX + slopeAlongZ * slopeAlongZ);
                        float slopeDegrees = Mathf.Atan(gradientLength) * Mathf.Rad2Deg;
                        float cliffNoise01 = noise.snoise(new float2(worldX, worldZ) * (rule.plateauNoiseFrequencyPerMetre * 2f) + cliffSeedOffset) * 0.5f + 0.5f;
                        if (slopeDegrees > rule.cliffSlopeDegrees && cliffNoise01 < rule.cliffChance)
                        {
                            // Steep candidate sitting well below the ground 6 m uphill is the downhill part of the patch, so it drops a level and the contour becomes a cliff.
                            float uphillX = worldX + slopeAlongX / gradientLength * uphillProbeMetres;
                            float uphillZ = worldZ + slopeAlongZ / gradientLength * uphillProbeMetres;
                            if (height < AnalyticHeightAt(profile, uphillX, uphillZ, tileCoordinate, slopeRollIndex) - downhillDropMetres) level = -1;
                        }
                    }
                    outLevels[coreZ * TerraceCellsPerSide + coreX] = (byte)(level + TerrainTileData.TerraceLevelBias);
                    outStepKinds[coreZ * TerraceCellsPerSide + coreX] = plateauKinds[coreZ * TerraceCellsPerSide + coreX];
                    windowLevels[cellZ * windowSide + cellX] = (sbyte)level;
                }
            }
        });

        for (int cellZ = 0; cellZ < windowSide; cellZ++)
        {
            for (int cellX = 0; cellX < windowSide; cellX++)
            {
                int coreX = cellX - margin;
                int coreZ = cellZ - margin;
                bool insideCore = coreX >= 0 && coreZ >= 0 && coreX < TerraceCellsPerSide && coreZ < TerraceCellsPerSide;
                if (insideCore) continue;
                windowLevels[cellZ * windowSide + cellX] = (sbyte)ResolvedNeighbourLevel(neighbourLookup, tileCoordinate, coreX, coreZ);
            }
        }

        if (outRamps == null) return;

        PlanRampsAndCullUnreachableRegions(windowLevels, windowSide, maxLevelMagnitude, minCornerX, minCornerZ, tileMinCornerX, tileMinCornerZ, cullProtectionBorderMetres, profile, tileCoordinate, rollIndex, outStepKinds, outRamps);
        for (int cellZ = 0; cellZ < TerraceCellsPerSide; cellZ++)
        {
            for (int cellX = 0; cellX < TerraceCellsPerSide; cellX++)
            {
                outLevels[cellZ * TerraceCellsPerSide + cellX] = (byte)(windowLevels[(cellZ + margin) * windowSide + cellX + margin] + TerrainTileData.TerraceLevelBias);
            }
        }
    }

    private static int FirstEnabledRuleIndexAt(TerrainGenerationProfile profile, float worldX, float worldZ, float2 biomeSeedOffset, Vector2Int tileCoordinate, int rollIndex)
    {
        TerrainSplatLayer biome = BiomeLayerAt(profile, worldX, worldZ, biomeSeedOffset, tileCoordinate, rollIndex);
        for (int ruleIndex = 0; ruleIndex < profile.terraceBiomeRules.Count; ruleIndex++)
        {
            if (profile.terraceBiomeRules[ruleIndex].enabled && profile.terraceBiomeRules[ruleIndex].biome == biome)
                return ruleIndex;
        }
        return -1;
    }

    // coreX / coreZ are tile-relative cell coordinates that may lie up to 50 cells outside the tile.
    private static int ResolvedNeighbourLevel(System.Func<Vector2Int, TerrainTileData> neighbourLookup, Vector2Int tileCoordinate, int coreX, int coreZ)
    {
        if (neighbourLookup == null) return 0;
        int tileOffsetX = coreX < 0 ? -1 : coreX >= TerraceCellsPerSide ? 1 : 0;
        int tileOffsetZ = coreZ < 0 ? -1 : coreZ >= TerraceCellsPerSide ? 1 : 0;
        TerrainTileData neighbour = neighbourLookup(new Vector2Int(tileCoordinate.x + tileOffsetX, tileCoordinate.y + tileOffsetZ));
        if (neighbour == null) return 0;
        return TerraceLevelField.ResolvedLevel(neighbour, coreX - tileOffsetX * TerraceCellsPerSide, coreZ - tileOffsetZ * TerraceCellsPerSide);
    }

    public static void GenerateTerraceLevels(TerrainGenerationProfile profile, Vector2Int tileCoordinate, int maxLevels, byte[] outLevels)
    {
        GenerateTerraceLevels(profile, tileCoordinate, maxLevels, outLevels, null);
    }

    public static void GenerateTerraceLevels(TerrainGenerationProfile profile, Vector2Int tileCoordinate, int maxLevels, byte[] outLevels, System.Collections.Generic.List<TerraceRampPlacement> outRamps)
    {
        byte[] scratchStepKinds = new byte[outLevels.Length];
        GenerateTerraceLevels(profile, tileCoordinate, maxLevels, 0, null, outLevels, scratchStepKinds, outRamps);
    }

    private const float TallRampWidthMetres = 6f;
    private const float TallRampRunMetres = 12f;
    private const float AlongCliffRunMetres = 9f;
    private const float AlongCliffWidthMetres = 4f;
    private const float LowGapWidthMetres = 8f;
    private const float LowGapRunMetres = 2.8f;
    private const float LowGapSpacingMetres = 40f;
    private const float TallRampSpacingMetres = 90f;
    private const float LowGapMinimumSeparationCells = 20f;
    private const float TallRampMinimumSeparationCells = 12f;

    private struct RampCandidate
    {
        public float edgeCellX;
        public float edgeCellZ;
        public float outwardX;
        public float outwardZ;
        public float quality;
    }

    private static void PlanRampsAndCullUnreachableRegions(sbyte[] windowLevels, int windowSide, int maxLevelMagnitude, float minCornerX, float minCornerZ, float tileMinCornerX, float tileMinCornerZ, float borderProtectionMetres, TerrainGenerationProfile profile, Vector2Int tileCoordinate, int rollIndex, byte[] stepKinds, System.Collections.Generic.List<TerraceRampPlacement> outRamps)
    {
        int tileCellMinX = Mathf.RoundToInt(tileMinCornerX - minCornerX);
        int tileCellMinZ = Mathf.RoundToInt(tileMinCornerZ - minCornerZ);
        int protectionCells = Mathf.CeilToInt(borderProtectionMetres);
        int protectedMinCellX = tileCellMinX - protectionCells;
        int protectedMaxCellX = tileCellMinX + TerraceCellsPerSide + protectionCells;
        int protectedMinCellZ = tileCellMinZ - protectionCells;
        int protectedMaxCellZ = tileCellMinZ + TerraceCellsPerSide + protectionCells;
        int tileInnerMinCellX = tileCellMinX + protectionCells;
        int tileInnerMaxCellX = tileCellMinX + TerraceCellsPerSide - protectionCells;
        int tileInnerMinCellZ = tileCellMinZ + protectionCells;
        int tileInnerMaxCellZ = tileCellMinZ + TerraceCellsPerSide - protectionCells;
        int tallBlockDepthCells = Mathf.CeilToInt(TallRampRunMetres * 0.5f + 1f);
        int alongCliffBlockDepthCells = Mathf.CeilToInt(AlongCliffWidthMetres + 1f);
        int lowGapBlockDepthCells = Mathf.CeilToInt(LowGapRunMetres * 0.5f + 1f);
        bool[] visited = new bool[windowLevels.Length];
        bool[] edgeCellIsRampable = new bool[windowLevels.Length];
        int[] stack = new int[windowLevels.Length];
        int[] regionCells = new int[windowLevels.Length];
        int[] directionStepX = { 1, -1, 0, 0 };
        int[] directionStepZ = { 0, 0, 1, -1 };
        System.Collections.Generic.List<RampCandidate> straightCandidates = new System.Collections.Generic.List<RampCandidate>();
        System.Collections.Generic.List<RampCandidate> alongCliffCandidates = new System.Collections.Generic.List<RampCandidate>();
        System.Collections.Generic.List<RampCandidate> lowGapCandidates = new System.Collections.Generic.List<RampCandidate>();
        System.Collections.Generic.List<Vector2> chosenEdgePoints = new System.Collections.Generic.List<Vector2>();
        float lockedBandMetres = TerracePlateauField.LockMetres;

        for (int magnitude = maxLevelMagnitude; magnitude >= 1; magnitude--)
        {
            System.Array.Clear(visited, 0, visited.Length);
            for (int startIndex = 0; startIndex < windowLevels.Length; startIndex++)
            {
                int regionLevel = windowLevels[startIndex];
                if (visited[startIndex] || regionLevel == 0 || Mathf.Abs(regionLevel) != magnitude) continue;

                int stackCount = 0;
                int regionCount = 0;
                bool touchesBorder = false;
                bool regionNearTileBorder = false;
                stack[stackCount++] = startIndex;
                visited[startIndex] = true;
                while (stackCount > 0)
                {
                    int cellIndex = stack[--stackCount];
                    regionCells[regionCount++] = cellIndex;
                    int cellX = cellIndex % windowSide;
                    int cellZ = cellIndex / windowSide;
                    if (cellX == 0 || cellZ == 0 || cellX == windowSide - 1 || cellZ == windowSide - 1) touchesBorder = true;
                    if (cellX >= protectedMinCellX && cellX < protectedMaxCellX && cellZ >= protectedMinCellZ && cellZ < protectedMaxCellZ
                        && (cellX < tileInnerMinCellX || cellX >= tileInnerMaxCellX || cellZ < tileInnerMinCellZ || cellZ >= tileInnerMaxCellZ))
                        regionNearTileBorder = true;
                    for (int direction = 0; direction < 4; direction++)
                    {
                        int neighbourX = cellX + directionStepX[direction];
                        int neighbourZ = cellZ + directionStepZ[direction];
                        if (neighbourX < 0 || neighbourZ < 0 || neighbourX >= windowSide || neighbourZ >= windowSide) continue;
                        int neighbourIndex = neighbourZ * windowSide + neighbourX;
                        if (visited[neighbourIndex] || windowLevels[neighbourIndex] != regionLevel) continue;
                        visited[neighbourIndex] = true;
                        stack[stackCount++] = neighbourIndex;
                    }
                }

                // A region touching the window border may continue outside it, so it is neither ramped nor culled here.
                if (touchesBorder) continue;

                int targetLevel = regionLevel - (int)Mathf.Sign(regionLevel);
                int outlineCells = 0;
                int coreCellCount = 0;
                int lowStepCellCount = 0;
                int stepKindMask = 1 << (magnitude - 1);
                for (int regionIndex = 0; regionIndex < regionCount; regionIndex++)
                {
                    int cellX = regionCells[regionIndex] % windowSide;
                    int cellZ = regionCells[regionIndex] / windowSide;
                    bool isOutlineCell = false;
                    for (int direction = 0; direction < 4; direction++)
                    {
                        int neighbourX = cellX + directionStepX[direction];
                        int neighbourZ = cellZ + directionStepZ[direction];
                        if (neighbourX < 0 || neighbourZ < 0 || neighbourX >= windowSide || neighbourZ >= windowSide || windowLevels[neighbourZ * windowSide + neighbourX] != regionLevel)
                            isOutlineCell = true;
                    }
                    if (isOutlineCell) outlineCells++;
                    int coreX = cellX - tileCellMinX;
                    int coreZ = cellZ - tileCellMinZ;
                    if (coreX < 0 || coreZ < 0 || coreX >= TerraceCellsPerSide || coreZ >= TerraceCellsPerSide) continue;
                    coreCellCount++;
                    if ((stepKinds[coreZ * TerraceCellsPerSide + coreX] & stepKindMask) != 0) lowStepCellCount++;
                }

                // Cells outside the core count as Tall, so a region is Low only when most of its core cells say so.
                bool isLowRegion = coreCellCount > 0 && lowStepCellCount * 2 > coreCellCount;
                bool wasDemotedToLow = false;
                if (!isLowRegion)
                {
                    straightCandidates.Clear();
                    CollectRampCandidates(windowLevels, windowSide, regionCells, regionCount, regionLevel, targetLevel, tallBlockDepthCells, TallRampWidthMetres, TallRampRunMetres, minCornerX, minCornerZ, profile, tileCoordinate, rollIndex, edgeCellIsRampable, directionStepX, directionStepZ, straightCandidates);
                    RemoveCandidatesTooCloseToTileBorder(straightCandidates, minCornerX, minCornerZ, tileMinCornerX, tileMinCornerZ, lockedBandMetres + TallRampRunMetres);
                    if (straightCandidates.Count == 0)
                    {
                        // NC-D8: a region near the tile border may continue in a neighbour tile, so it is kept without a ramp.
                        if (regionNearTileBorder) continue;
                        wasDemotedToLow = true;
                        isLowRegion = true;
                    }
                }

                if (isLowRegion)
                {
                    lowGapCandidates.Clear();
                    CollectRampCandidates(windowLevels, windowSide, regionCells, regionCount, regionLevel, targetLevel, lowGapBlockDepthCells, LowGapWidthMetres, LowGapRunMetres, minCornerX, minCornerZ, profile, tileCoordinate, rollIndex, edgeCellIsRampable, directionStepX, directionStepZ, lowGapCandidates);
                    RemoveCandidatesTooCloseToTileBorder(lowGapCandidates, minCornerX, minCornerZ, tileMinCornerX, tileMinCornerZ, lockedBandMetres + LowGapRunMetres);
                    if (wasDemotedToLow)
                    {
                        if (lowGapCandidates.Count == 0)
                        {
                            for (int regionIndex = 0; regionIndex < regionCount; regionIndex++)
                                windowLevels[regionCells[regionIndex]] = (sbyte)targetLevel;
                            continue;
                        }
                        for (int regionIndex = 0; regionIndex < regionCount; regionIndex++)
                        {
                            int coreX = regionCells[regionIndex] % windowSide - tileCellMinX;
                            int coreZ = regionCells[regionIndex] / windowSide - tileCellMinZ;
                            if (coreX < 0 || coreZ < 0 || coreX >= TerraceCellsPerSide || coreZ >= TerraceCellsPerSide) continue;
                            stepKinds[coreZ * TerraceCellsPerSide + coreX] |= (byte)stepKindMask;
                        }
                        Debug.Log("[Terraces] Tall region at world (" + (minCornerX + regionCells[0] % windowSide) + ", " + (minCornerZ + regionCells[0] / windowSide) + ") level " + regionLevel + " fits no caravan-grade Straight ramp; demoted to Low with worn gaps.");
                    }

                    int gapCount = Mathf.Max(1, Mathf.RoundToInt(outlineCells / LowGapSpacingMetres));
                    chosenEdgePoints.Clear();
                    for (int gapIndex = 0; gapIndex < gapCount; gapIndex++)
                    {
                        int pickedIndex = PickCandidateIndex(lowGapCandidates, chosenEdgePoints, LowGapMinimumSeparationCells);
                        if (pickedIndex < 0) break;
                        RampCandidate picked = lowGapCandidates[pickedIndex];
                        chosenEdgePoints.Add(new Vector2(picked.edgeCellX, picked.edgeCellZ));
                        outRamps.Add(CreatePlacement(picked, TerraceRampKind.Straight, LowGapRunMetres, LowGapWidthMetres, false, true, regionLevel, targetLevel, minCornerX, minCornerZ));
                    }
                    continue;
                }

                int rampCount = Mathf.Clamp(Mathf.RoundToInt(outlineCells / TallRampSpacingMetres), 1, 3);
                chosenEdgePoints.Clear();
                alongCliffCandidates.Clear();
                // AlongCliff parked: its landing notch leaves a hole (Natural Cliffs wave 5)
                // The AlongCliff collection is skipped so the 2nd and 3rd ramps fall through to Straight.

                for (int rampIndex = 0; rampIndex < rampCount; rampIndex++)
                {
                    int alongCliffIndex = rampIndex == 0 ? -1 : PickCandidateIndex(alongCliffCandidates, chosenEdgePoints, TallRampMinimumSeparationCells);
                    if (alongCliffIndex >= 0)
                    {
                        RampCandidate picked = alongCliffCandidates[alongCliffIndex];
                        chosenEdgePoints.Add(new Vector2(picked.edgeCellX, picked.edgeCellZ));
                        outRamps.Add(CreatePlacement(picked, TerraceRampKind.AlongCliff, AlongCliffRunMetres, AlongCliffWidthMetres, rampIndex % 2 == 1, false, regionLevel, targetLevel, minCornerX, minCornerZ));
                        continue;
                    }

                    int straightIndex = PickCandidateIndex(straightCandidates, chosenEdgePoints, TallRampMinimumSeparationCells);
                    if (straightIndex < 0) break;
                    RampCandidate straightPicked = straightCandidates[straightIndex];
                    chosenEdgePoints.Add(new Vector2(straightPicked.edgeCellX, straightPicked.edgeCellZ));
                    outRamps.Add(CreatePlacement(straightPicked, TerraceRampKind.Straight, TallRampRunMetres, TallRampWidthMetres, false, false, regionLevel, targetLevel, minCornerX, minCornerZ));
                }
            }
        }
    }

    // NC-D13: a candidate is an edge cell whose outline, sampled across spanMetres along the tangent, deviates at most RampChordToleranceMetres, with open lower ground in front.
    private static void CollectRampCandidates(sbyte[] windowLevels, int windowSide, int[] regionCells, int regionCount, int regionLevel, int targetLevel, int blockDepthCells, float spanMetres, float rampRunMetres, float minCornerX, float minCornerZ, TerrainGenerationProfile profile, Vector2Int tileCoordinate, int rollIndex, bool[] edgeCellIsRampable, int[] directionStepX, int[] directionStepZ, System.Collections.Generic.List<RampCandidate> outCandidates)
    {
        float halfSpanMetres = spanMetres * 0.5f;
        for (int regionIndex = 0; regionIndex < regionCount; regionIndex++)
        {
            int cellX = regionCells[regionIndex] % windowSide;
            int cellZ = regionCells[regionIndex] / windowSide;
            if (!HasLeavingNeighbour(windowLevels, windowSide, cellX, cellZ, regionLevel, directionStepX, directionStepZ)) continue;

            // Outward normal: mean of the leaving 4-neighbour directions over a 5x5 window.
            float normalSumX = 0f;
            float normalSumZ = 0f;
            int leavingCount = 0;
            for (int windowZ = cellZ - 2; windowZ <= cellZ + 2; windowZ++)
            {
                for (int windowX = cellX - 2; windowX <= cellX + 2; windowX++)
                {
                    if (SampleLevel(windowLevels, windowSide, windowX + 0.5f, windowZ + 0.5f) != regionLevel) continue;
                    for (int direction = 0; direction < 4; direction++)
                    {
                        if (SampleLevel(windowLevels, windowSide, windowX + 0.5f + directionStepX[direction], windowZ + 0.5f + directionStepZ[direction]) == regionLevel) continue;
                        normalSumX += directionStepX[direction];
                        normalSumZ += directionStepZ[direction];
                        leavingCount++;
                    }
                }
            }
            if (leavingCount == 0) continue;
            float normalLength = Mathf.Sqrt(normalSumX * normalSumX + normalSumZ * normalSumZ);
            if (normalLength < 0.3f * leavingCount) continue;
            float normalX = normalSumX / normalLength;
            float normalZ = normalSumZ / normalLength;
            float tangentX = -normalZ;
            float tangentZ = normalX;

            float insetCentreX = cellX + 0.5f - normalX * RampMarchInsetMetres;
            float insetCentreZ = cellZ + 0.5f - normalZ * RampMarchInsetMetres;
            float minimumDistance = float.MaxValue;
            float maximumDistance = float.MinValue;
            float distanceSum = 0f;
            int sampleCount = 0;
            bool isRejected = false;
            for (float offset = -halfSpanMetres; offset <= halfSpanMetres + 0.001f && !isRejected; offset += 1f)
            {
                float startX = insetCentreX + tangentX * offset;
                float startZ = insetCentreZ + tangentZ * offset;
                if (SampleLevel(windowLevels, windowSide, startX, startZ) != regionLevel) { isRejected = true; break; }
                float marched = 0f;
                int levelAhead = regionLevel;
                while (marched < RampMarchLimitMetres)
                {
                    marched += RampMarchStepMetres;
                    levelAhead = SampleLevel(windowLevels, windowSide, startX + normalX * marched, startZ + normalZ * marched);
                    if (levelAhead != regionLevel) break;
                }
                if (levelAhead != targetLevel) { isRejected = true; break; }
                minimumDistance = Mathf.Min(minimumDistance, marched);
                maximumDistance = Mathf.Max(maximumDistance, marched);
                distanceSum += marched;
                sampleCount++;
            }
            if (isRejected || sampleCount == 0) continue;
            float deviation = maximumDistance - minimumDistance;
            if (deviation > RampChordToleranceMetres) continue;

            // Footprint beyond the outline's furthest point must be open lower ground.
            float meanDistance = distanceSum / sampleCount;
            float edgeX = insetCentreX + normalX * meanDistance;
            float edgeZ = insetCentreZ + normalZ * meanDistance;
            float footprintStart = maximumDistance - meanDistance + 0.25f;
            for (float offset = -halfSpanMetres; offset <= halfSpanMetres + 0.001f && !isRejected; offset += 1f)
            {
                for (float depth = footprintStart; depth <= blockDepthCells; depth += 1f)
                {
                    if (SampleLevel(windowLevels, windowSide, edgeX + tangentX * offset + normalX * depth, edgeZ + tangentZ * offset + normalZ * depth) != targetLevel) { isRejected = true; break; }
                }
            }
            if (isRejected) continue;

            float halfRunMetres = rampRunMetres * 0.5f;
            float highEndX = minCornerX + edgeX - normalX * halfRunMetres;
            float highEndZ = minCornerZ + edgeZ - normalZ * halfRunMetres;
            float lowEndX = minCornerX + edgeX + normalX * halfRunMetres;
            float lowEndZ = minCornerZ + edgeZ + normalZ * halfRunMetres;
            float highEndHeight = AnalyticHeightAt(profile, highEndX, highEndZ, tileCoordinate, rollIndex);
            float lowEndHeight = AnalyticHeightAt(profile, lowEndX, lowEndZ, tileCoordinate, rollIndex);
            if (Mathf.Abs(highEndHeight - lowEndHeight) > MaxBaseRiseAcrossRampMetres) continue;
            float highCornerHeightA = AnalyticHeightAt(profile, highEndX + tangentX * halfSpanMetres, highEndZ + tangentZ * halfSpanMetres, tileCoordinate, rollIndex);
            float highCornerHeightB = AnalyticHeightAt(profile, highEndX - tangentX * halfSpanMetres, highEndZ - tangentZ * halfSpanMetres, tileCoordinate, rollIndex);
            if (Mathf.Abs(highCornerHeightA - highCornerHeightB) > MaxBaseRiseAcrossRampMetres) continue;

            float junctionHalfSpanMetres = halfSpanMetres + RampJunctionClearanceMetres;
            float junctionHalfRunMetres = halfRunMetres + RampJunctionClearanceMetres;
            for (float junctionOffset = -junctionHalfSpanMetres; junctionOffset <= junctionHalfSpanMetres + 0.001f && !isRejected; junctionOffset += 1f)
            {
                for (float junctionDepth = -junctionHalfRunMetres; junctionDepth <= junctionHalfRunMetres + 0.001f; junctionDepth += 1f)
                {
                    int junctionLevel = SampleLevel(windowLevels, windowSide, edgeX + tangentX * junctionOffset + normalX * junctionDepth, edgeZ + tangentZ * junctionOffset + normalZ * junctionDepth);
                    if (junctionLevel != regionLevel && junctionLevel != targetLevel) { isRejected = true; break; }
                }
            }
            if (isRejected) continue;

            float clearOutwardMetres = 0f;
            while (clearOutwardMetres < RampClearBonusLimitMetres && SampleLevel(windowLevels, windowSide, edgeX + normalX * (blockDepthCells + clearOutwardMetres), edgeZ + normalZ * (blockDepthCells + clearOutwardMetres)) == targetLevel)
                clearOutwardMetres += 1f;

            outCandidates.Add(new RampCandidate
            {
                edgeCellX = edgeX,
                edgeCellZ = edgeZ,
                outwardX = normalX,
                outwardZ = normalZ,
                quality = -deviation + 0.05f * clearOutwardMetres
            });
        }
    }

    // The ramp climbs the step, not the hill: a base slope steeper than this leaves a notch the ramp cannot fill.
    private const float MaxBaseRiseAcrossRampMetres = 1f;
    // A notch that reaches another wall cuts it with nothing to fill, leaving a hole.
    private const float RampJunctionClearanceMetres = 2f;
    private const float RampChordToleranceMetres = 1f;
    private const float RampMarchInsetMetres = 1.5f;
    private const float RampMarchStepMetres = 0.25f;
    private const float RampMarchLimitMetres = 12f;
    private const float RampClearBonusLimitMetres = 6f;
    private const int OutsideWindowLevel = int.MinValue;

    private static int SampleLevel(sbyte[] windowLevels, int windowSide, float cellX, float cellZ)
    {
        int indexX = Mathf.FloorToInt(cellX);
        int indexZ = Mathf.FloorToInt(cellZ);
        if (indexX < 0 || indexZ < 0 || indexX >= windowSide || indexZ >= windowSide) return OutsideWindowLevel;
        return windowLevels[indexZ * windowSide + indexX];
    }

    private static bool HasLeavingNeighbour(sbyte[] windowLevels, int windowSide, int cellX, int cellZ, int regionLevel, int[] directionStepX, int[] directionStepZ)
    {
        for (int direction = 0; direction < 4; direction++)
        {
            if (SampleLevel(windowLevels, windowSide, cellX + 0.5f + directionStepX[direction], cellZ + 0.5f + directionStepZ[direction]) != regionLevel) return true;
        }
        return false;
    }

    private static void RemoveCandidatesTooCloseToTileBorder(System.Collections.Generic.List<RampCandidate> candidates, float minCornerX, float minCornerZ, float tileMinCornerX, float tileMinCornerZ, float minimumDistanceMetres)
    {
        candidates.RemoveAll(candidate =>
        {
            float worldX = minCornerX + candidate.edgeCellX;
            float worldZ = minCornerZ + candidate.edgeCellZ;
            float distanceToBorder = Mathf.Min(Mathf.Min(worldX - tileMinCornerX, tileMinCornerX + TileSizeMetres - worldX), Mathf.Min(worldZ - tileMinCornerZ, tileMinCornerZ + TileSizeMetres - worldZ));
            return distanceToBorder < minimumDistanceMetres;
        });
    }

    // First pick is the best-scoring chord; later picks are the candidate farthest from everything already chosen.
    private static int PickCandidateIndex(System.Collections.Generic.List<RampCandidate> candidates, System.Collections.Generic.List<Vector2> chosenEdgePoints, float minimumSeparationCells)
    {
        int bestIndex = -1;
        float bestScore = float.MinValue;
        for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            RampCandidate candidate = candidates[candidateIndex];
            float score;
            if (chosenEdgePoints.Count == 0)
            {
                score = candidate.quality;
            }
            else
            {
                score = float.MaxValue;
                for (int chosenIndex = 0; chosenIndex < chosenEdgePoints.Count; chosenIndex++)
                    score = Mathf.Min(score, (new Vector2(candidate.edgeCellX, candidate.edgeCellZ) - chosenEdgePoints[chosenIndex]).sqrMagnitude);
                if (score < minimumSeparationCells * minimumSeparationCells) continue;
            }
            if (score <= bestScore) continue;
            bestScore = score;
            bestIndex = candidateIndex;
        }
        return bestIndex;
    }

    // Straight yaw points up the slope. AlongCliff yaw runs parallel to the face, centred on the lower ground half its width off the face.
    private static TerraceRampPlacement CreatePlacement(RampCandidate candidate, TerraceRampKind kind, float runMetres, float widthMetres, bool risesToTheRight, bool isLowStepWornGap, int regionLevel, int targetLevel, float minCornerX, float minCornerZ)
    {
        float towardHigherSign = regionLevel > 0 ? -1f : 1f;
        float forwardX = candidate.outwardX * towardHigherSign;
        float forwardZ = candidate.outwardZ * towardHigherSign;
        Vector2 centreWorld = new Vector2(minCornerX + candidate.edgeCellX, minCornerZ + candidate.edgeCellZ);
        if (kind == TerraceRampKind.AlongCliff)
        {
            // forward currently points toward the higher ground (the cliff); the ramp runs parallel to the face instead.
            centreWorld -= new Vector2(forwardX, forwardZ) * (widthMetres * 0.5f);
            Vector2 alongFaceForward = ForwardAlongFaceKeepingCliffOnSide(new Vector2(forwardX, forwardZ), risesToTheRight);
            forwardX = alongFaceForward.x;
            forwardZ = alongFaceForward.y;
        }
        return new TerraceRampPlacement
        {
            centreWorld = centreWorld,
            yawDegrees = Mathf.Atan2(forwardX, forwardZ) * Mathf.Rad2Deg,
            lowerLevel = Mathf.Min(regionLevel, targetLevel),
            kind = kind,
            runMetres = runMetres,
            widthMetres = widthMetres,
            risesToTheRight = risesToTheRight,
            isLowStepWornGap = isLowStepWornGap
        };
    }

    // Left of forward f is (-f.z, f.x); f = (c.z, -c.x) puts the cliff direction c on the left, and the negation puts it on the right.
    private static Vector2 ForwardAlongFaceKeepingCliffOnSide(Vector2 towardCliff, bool cliffIsOnTheLeft)
    {
        Vector2 forwardWithCliffOnLeft = new Vector2(towardCliff.y, -towardCliff.x);
        return cliffIsOnTheLeft ? forwardWithCliffOnLeft : -forwardWithCliffOnLeft;
    }

    private static float2 CreateSeedOffset(int seed)
    {
        return new float2((seed * 12.9898f) % 1000f, (seed * 78.233f) % 1000f);
    }
}
#endif
