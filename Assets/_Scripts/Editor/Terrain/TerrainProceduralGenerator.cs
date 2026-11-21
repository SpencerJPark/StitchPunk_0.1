#if UNITY_EDITOR
using Unity.Mathematics;
using UnityEngine;

// Pure function of (profile, world position). Noise is sampled in world coordinates so neighbouring
// tiles agree exactly on their shared edge; never read a neighbour tile from here.
public static class TerrainProceduralGenerator
{
    private const int SamplesPerSide = TerrainTileData.HeightSamplesPerSide;
    private const float TileSizeMetres = TerrainTileData.TileSizeMetres;
    private const int TerraceCellsPerSide = 100;

    public static float AnalyticHeightAt(TerrainGenerationProfile profile, float worldX, float worldZ)
    {
        float2 seedOffset = CreateSeedOffset(profile.seed);
        float2 worldPosition = new float2(worldX, worldZ);

        float noiseSum = 0f;
        float maxAmplitude = 0f;
        for (int layerIndex = 0; layerIndex < profile.heightLayers.Count; layerIndex++)
        {
            TerrainHeightLayer layer = profile.heightLayers[layerIndex];
            float frequency = layer.frequencyPerMetre;
            float amplitude = layer.amplitudeMetres;
            for (int octave = 0; octave < layer.octaves; octave++)
            {
                noiseSum += noise.snoise(worldPosition * frequency + seedOffset) * amplitude;
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
        float minCornerX = tileCoordinate.x * TileSizeMetres - TileSizeMetres * 0.5f;
        float minCornerZ = tileCoordinate.y * TileSizeMetres - TileSizeMetres * 0.5f;
        for (int sampleZ = 0; sampleZ < SamplesPerSide; sampleZ++)
        {
            for (int sampleX = 0; sampleX < SamplesPerSide; sampleX++)
            {
                outHeights[sampleZ * SamplesPerSide + sampleX] =
                    AnalyticHeightAt(profile, minCornerX + sampleX, minCornerZ + sampleZ);
            }
        }
    }

    public static void GenerateBaseMask(TerrainGenerationProfile profile, Vector2Int tileCoordinate, int resolution, Color32[] outMask)
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

                float height = AnalyticHeightAt(profile, worldX, worldZ);
                float slopeAlongX = (AnalyticHeightAt(profile, worldX + 1f, worldZ) - AnalyticHeightAt(profile, worldX - 1f, worldZ)) * 0.5f;
                float slopeAlongZ = (AnalyticHeightAt(profile, worldX, worldZ + 1f) - AnalyticHeightAt(profile, worldX, worldZ - 1f)) * 0.5f;
                float slopeDegrees = Mathf.Atan(Mathf.Sqrt(slopeAlongX * slopeAlongX + slopeAlongZ * slopeAlongZ)) * Mathf.Rad2Deg;

                TerrainSplatLayer chosenLayer = TerrainSplatLayer.Dirt;
                for (int ruleIndex = 0; ruleIndex < profile.biomeRules.Count; ruleIndex++)
                {
                    TerrainBiomeRule rule = profile.biomeRules[ruleIndex];
                    if (height < rule.heightRange.x || height > rule.heightRange.y) continue;
                    if (slopeDegrees < rule.slopeRangeDegrees.x || slopeDegrees > rule.slopeRangeDegrees.y) continue;

                    float2 ruleNoisePosition = new float2(worldX, worldZ) * rule.noiseFrequency + seedOffset + new float2(ruleIndex * 17.3f, 0f);
                    float ruleNoise01 = noise.snoise(ruleNoisePosition) * 0.5f + 0.5f;
                    if (ruleNoise01 * rule.weight >= rule.noiseThreshold)
                        chosenLayer = rule.layer;
                }

                outMask[texelZ * resolution + texelX] = TerrainSplatCodec.ColourOfLayer(chosenLayer);
            }
        }
    }

    public static void GenerateTerraceLevels(TerrainGenerationProfile profile, Vector2Int tileCoordinate, int maxLevels, byte[] outLevels)
    {
        System.Array.Clear(outLevels, 0, outLevels.Length);
        if (!profile.generateTerraces || profile.terraceLevelThresholds == null || profile.terraceLevelThresholds.Length == 0 || maxLevels <= 0)
            return;

        const int margin = 50;
        const int windowSide = TerraceCellsPerSide + 2 * margin;
        float2 seedOffset = CreateSeedOffset(profile.seed) + new float2(913.7f, -421.3f);
        float minCornerX = tileCoordinate.x * TileSizeMetres - TileSizeMetres * 0.5f - margin;
        float minCornerZ = tileCoordinate.y * TileSizeMetres - TileSizeMetres * 0.5f - margin;
        float[] thresholds = profile.terraceLevelThresholds;

        byte[] windowLevels = new byte[windowSide * windowSide];
        for (int cellZ = 0; cellZ < windowSide; cellZ++)
        {
            for (int cellX = 0; cellX < windowSide; cellX++)
            {
                float2 centre = new float2(minCornerX + cellX + 0.5f, minCornerZ + cellZ + 0.5f);
                float noise01 = noise.snoise(centre * profile.terraceNoiseFrequencyPerMetre + seedOffset) * 0.5f + 0.5f;
                int level = 0;
                for (int thresholdIndex = 0; thresholdIndex < thresholds.Length && level < maxLevels; thresholdIndex++)
                {
                    if (noise01 >= thresholds[thresholdIndex]) level++;
                }
                windowLevels[cellZ * windowSide + cellX] = (byte)level;
            }
        }

        // Window-border components may continue past the window, so they are kept; neighbouring tiles then agree.
        bool[] visited = new bool[windowLevels.Length];
        int[] stack = new int[windowLevels.Length];
        int[] componentCells = new int[windowLevels.Length];
        for (int level = 1; level <= maxLevels; level++)
        {
            System.Array.Clear(visited, 0, visited.Length);
            for (int startIndex = 0; startIndex < windowLevels.Length; startIndex++)
            {
                if (visited[startIndex] || windowLevels[startIndex] < level) continue;

                int stackCount = 0;
                int componentCount = 0;
                bool touchesBorder = false;
                stack[stackCount++] = startIndex;
                visited[startIndex] = true;
                while (stackCount > 0)
                {
                    int cellIndex = stack[--stackCount];
                    componentCells[componentCount++] = cellIndex;
                    int cellX = cellIndex % windowSide;
                    int cellZ = cellIndex / windowSide;
                    if (cellX == 0 || cellZ == 0 || cellX == windowSide - 1 || cellZ == windowSide - 1) touchesBorder = true;

                    for (int direction = 0; direction < 4; direction++)
                    {
                        int neighbourX = cellX + (direction == 0 ? 1 : direction == 1 ? -1 : 0);
                        int neighbourZ = cellZ + (direction == 2 ? 1 : direction == 3 ? -1 : 0);
                        if (neighbourX < 0 || neighbourZ < 0 || neighbourX >= windowSide || neighbourZ >= windowSide) continue;

                        int neighbourIndex = neighbourZ * windowSide + neighbourX;
                        if (visited[neighbourIndex] || windowLevels[neighbourIndex] < level) continue;

                        visited[neighbourIndex] = true;
                        stack[stackCount++] = neighbourIndex;
                    }
                }

                if (touchesBorder || componentCount >= profile.minPlateauAreaSquareMetres) continue;

                for (int componentIndex = 0; componentIndex < componentCount; componentIndex++)
                {
                    int culledIndex = componentCells[componentIndex];
                    if (windowLevels[culledIndex] >= level) windowLevels[culledIndex] = (byte)(level - 1);
                }
            }
        }

        for (int cellZ = 0; cellZ < TerraceCellsPerSide; cellZ++)
        {
            for (int cellX = 0; cellX < TerraceCellsPerSide; cellX++)
            {
                outLevels[cellZ * TerraceCellsPerSide + cellX] = windowLevels[(cellZ + margin) * windowSide + cellX + margin];
            }
        }
    }

    private static float2 CreateSeedOffset(int seed)
    {
        return new float2((seed * 12.9898f) % 1000f, (seed * 78.233f) % 1000f);
    }
}
#endif
