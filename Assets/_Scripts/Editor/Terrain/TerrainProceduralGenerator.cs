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
}

public static class TerrainProceduralGenerator
{
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

        for (int texelZ = 0; texelZ < resolution; texelZ++)
        {
            for (int texelX = 0; texelX < resolution; texelX++)
            {
                float worldX = minCornerX + (texelX + 0.5f) * texelSizeMetres;
                float worldZ = minCornerZ + (texelZ + 0.5f) * texelSizeMetres;

                outMask[texelZ * resolution + texelX] = TerrainSplatCodec.ColourOfLayer(BiomeLayerAt(profile, worldX, worldZ, seedOffset, tileCoordinate, rollIndex));
            }
        }
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

        System.Func<Vector2, bool, int> maxLevelAtWorldPoint = (Vector2 worldPoint, bool useRolledField) =>
        {
            int ruleIndex = FirstEnabledRuleIndexAt(profile, worldPoint.x, worldPoint.y, biomeSeedOffset, tileCoordinate, useRolledField ? rollIndex : 0);
            return ruleIndex < 0 ? 0 : Mathf.Min(profile.terraceBiomeRules[ruleIndex].maxLevel, maxLevels);
        };

        int[] plateauLevels = new int[TerraceCellsPerSide * TerraceCellsPerSide];
        byte[] plateauKinds = new byte[plateauLevels.Length];
        TerracePlateauRaster.RasterizeTile(profile.seed, tileCoordinate, rollIndex, styles, maxLevels, minPlateauAreaSquareMetres,
            neighbourLookup, maxLevelAtWorldPoint, plateauLevels, plateauKinds);

        sbyte[] windowLevels = new sbyte[windowSide * windowSide];
        for (int cellZ = 0; cellZ < windowSide; cellZ++)
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
                }
                else
                {
                    level = ResolvedNeighbourLevel(neighbourLookup, tileCoordinate, coreX, coreZ);
                }
                windowLevels[cellZ * windowSide + cellX] = (sbyte)level;
            }
        }

        if (outRamps == null) return;

        PlanRampsAndCullUnreachableRegions(windowLevels, windowSide, maxLevelMagnitude, minCornerX, minCornerZ, tileMinCornerX, tileMinCornerZ, cullProtectionBorderMetres, outRamps);
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

    private const float RampWidthMetres = 6f;
    private const float RampRunMetres = 9f;

    private static void PlanRampsAndCullUnreachableRegions(sbyte[] windowLevels, int windowSide, int maxLevelMagnitude, float minCornerX, float minCornerZ, float tileMinCornerX, float tileMinCornerZ, float borderProtectionMetres, System.Collections.Generic.List<TerraceRampPlacement> outRamps)
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
        int blockDepthCells = Mathf.CeilToInt(RampRunMetres * 0.5f + 1f);
        int minRunCells = Mathf.CeilToInt(RampWidthMetres + 2f);
        bool[] visited = new bool[windowLevels.Length];
        bool[] edgeCellIsRampable = new bool[windowLevels.Length];
        int[] stack = new int[windowLevels.Length];
        int[] regionCells = new int[windowLevels.Length];
        int[] directionStepX = { 1, -1, 0, 0 };
        int[] directionStepZ = { 0, 0, 1, -1 };

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
                int bestDirection = -1;
                int bestLength = 0;
                int bestStartX = 0;
                int bestStartZ = 0;
                for (int direction = 0; direction < 4; direction++)
                {
                    int stepX = directionStepX[direction];
                    int stepZ = directionStepZ[direction];
                    for (int regionIndex = 0; regionIndex < regionCount; regionIndex++)
                    {
                        int cellX = regionCells[regionIndex] % windowSide;
                        int cellZ = regionCells[regionIndex] / windowSide;
                        edgeCellIsRampable[regionCells[regionIndex]] = IsRampBlockClear(windowLevels, windowSide, cellX, cellZ, stepX, stepZ, blockDepthCells, regionLevel, targetLevel);
                    }

                    for (int regionIndex = 0; regionIndex < regionCount; regionIndex++)
                    {
                        int cellX = regionCells[regionIndex] % windowSide;
                        int cellZ = regionCells[regionIndex] / windowSide;
                        if (!edgeCellIsRampable[regionCells[regionIndex]]) continue;

                        int runStepX = stepX != 0 ? 0 : 1;
                        int runStepZ = stepX != 0 ? 1 : 0;
                        int previousX = cellX - runStepX;
                        int previousZ = cellZ - runStepZ;
                        bool previousIsRampable = previousX >= 0 && previousZ >= 0 && edgeCellIsRampable[previousZ * windowSide + previousX] && windowLevels[previousZ * windowSide + previousX] == regionLevel;
                        if (previousIsRampable) continue;

                        int runLength = 0;
                        int walkX = cellX;
                        int walkZ = cellZ;
                        while (walkX < windowSide && walkZ < windowSide && windowLevels[walkZ * windowSide + walkX] == regionLevel && edgeCellIsRampable[walkZ * windowSide + walkX])
                        {
                            runLength++;
                            walkX += runStepX;
                            walkZ += runStepZ;
                        }

                        if (runLength < minRunCells) continue;
                        bool isBetter = runLength > bestLength
                            || (runLength == bestLength && (cellZ < bestStartZ || (cellZ == bestStartZ && cellX < bestStartX)));
                        if (!isBetter) continue;
                        bestLength = runLength;
                        bestStartX = cellX;
                        bestStartZ = cellZ;
                        bestDirection = direction;
                    }

                    // Stale marks must not leak into the next direction's run detection.
                    for (int regionIndex = 0; regionIndex < regionCount; regionIndex++)
                        edgeCellIsRampable[regionCells[regionIndex]] = false;
                }

                if (bestDirection < 0)
                {
                    // NC-D8: a region near the tile border may continue in a neighbour tile, so it is kept without a ramp.
                    if (regionNearTileBorder) continue;
                    for (int regionIndex = 0; regionIndex < regionCount; regionIndex++)
                        windowLevels[regionCells[regionIndex]] = (sbyte)targetLevel;
                    continue;
                }

                int outwardX = directionStepX[bestDirection];
                int outwardZ = directionStepZ[bestDirection];
                float edgeX = bestStartX + (outwardX > 0 ? 1f : 0f) + (outwardX == 0 ? bestLength * 0.5f : 0f);
                float edgeZ = bestStartZ + (outwardZ > 0 ? 1f : 0f) + (outwardZ == 0 ? bestLength * 0.5f : 0f);
                Vector2 centreWorld = new Vector2(minCornerX + edgeX, minCornerZ + edgeZ);
                if (centreWorld.x < tileMinCornerX || centreWorld.x >= tileMinCornerX + TileSizeMetres) continue;
                if (centreWorld.y < tileMinCornerZ || centreWorld.y >= tileMinCornerZ + TileSizeMetres) continue;

                float towardHigherSign = regionLevel > 0 ? -1f : 1f;
                float forwardX = outwardX * towardHigherSign;
                float forwardZ = outwardZ * towardHigherSign;
                TerraceRampPlacement placement = new TerraceRampPlacement
                {
                    centreWorld = centreWorld,
                    yawDegrees = Mathf.Atan2(forwardX, forwardZ) * Mathf.Rad2Deg,
                    lowerLevel = Mathf.Min(regionLevel, targetLevel)
                };
                outRamps.Add(placement);
            }
        }
    }

    private static bool IsRampBlockClear(sbyte[] windowLevels, int windowSide, int cellX, int cellZ, int stepX, int stepZ, int blockDepthCells, int regionLevel, int targetLevel)
    {
        if (cellX + stepX < 0 || cellZ + stepZ < 0 || cellX + stepX >= windowSide || cellZ + stepZ >= windowSide) return false;
        if (windowLevels[(cellZ + stepZ) * windowSide + cellX + stepX] != targetLevel) return false;

        for (int depth = 0; depth < blockDepthCells; depth++)
        {
            int innerX = cellX - stepX * depth;
            int innerZ = cellZ - stepZ * depth;
            int outerX = cellX + stepX * (depth + 1);
            int outerZ = cellZ + stepZ * (depth + 1);
            if (innerX < 0 || innerZ < 0 || innerX >= windowSide || innerZ >= windowSide) return false;
            if (outerX < 0 || outerZ < 0 || outerX >= windowSide || outerZ >= windowSide) return false;
            if (windowLevels[innerZ * windowSide + innerX] != regionLevel) return false;
            if (windowLevels[outerZ * windowSide + outerX] != targetLevel) return false;
        }
        return true;
    }

    private static float2 CreateSeedOffset(int seed)
    {
        return new float2((seed * 12.9898f) % 1000f, (seed * 78.233f) % 1000f);
    }
}
#endif
