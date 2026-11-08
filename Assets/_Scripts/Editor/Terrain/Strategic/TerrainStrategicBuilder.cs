#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Mesh vertices are chunk-local in xz (origin = SuperChunkWorldMin) with world-space y; uv runs 0..1 over the chunk.
public static class TerrainStrategicBuilder
{
    private const float ShadowMarchStepMetres = 8f;
    private const int ShadowMarchStepCount = 6;
    private const float DeepWaterDepthMetres = 4f;
    private const float RoadAlphaThreshold = 0.5f;
    private const float ChannelThreshold = 0.5f;
    private const float FieldPatchSizeMetres = 40f;
    private const int MaxSixteenBitVertexCount = 65535;

    public struct StrategicTexelInput
    {
        public float worldX;
        public float worldZ;
        public float grassWeight;
        public float forestWeight;
        public float stoneWeight;
        public float roadCoverage;
        public float waterDepthMetres;
        public float lightMultiplier;
    }

    public static Vector2 SuperChunkWorldMin(TerrainStrategicProfile profile, Vector2Int superChunkCoordinate)
    {
        float tileCount = profile.superChunkTiles;
        return new Vector2(
            superChunkCoordinate.x * tileCount * TerrainTileData.TileSizeMetres - TerrainTileData.TileSizeMetres * 0.5f,
            superChunkCoordinate.y * tileCount * TerrainTileData.TileSizeMetres - TerrainTileData.TileSizeMetres * 0.5f);
    }

    public static bool Build(TerrainStrategicProfile profile, Vector2Int superChunkCoordinate, IReadOnlyDictionary<Vector2Int, TerrainTileData> tiles, int maxTexturePixels, out Mesh coarseMesh, out Texture2D colourTexture)
    {
        return Build(profile, superChunkCoordinate, tiles, maxTexturePixels, LoadLayerColoursFromProject(), out coarseMesh, out colourTexture);
    }

    public static bool Build(TerrainStrategicProfile profile, Vector2Int superChunkCoordinate, IReadOnlyDictionary<Vector2Int, TerrainTileData> tiles, int maxTexturePixels, Color[] layerColours, out Mesh coarseMesh, out Texture2D colourTexture)
    {
        coarseMesh = null;
        colourTexture = null;
        if (!AnyTileInChunk(profile, superChunkCoordinate, tiles))
        {
            return false;
        }

        float chunkSizeMetres = profile.superChunkTiles * TerrainTileData.TileSizeMetres;
        Vector2 chunkMin = SuperChunkWorldMin(profile, superChunkCoordinate);
        int cellsPerSide = Mathf.Max(1, Mathf.CeilToInt(chunkSizeMetres / profile.coarseCellMetres));
        float cellSizeMetres = chunkSizeMetres / cellsPerSide;

        float[] gridHeights = BuildCoarseHeights(chunkMin, cellsPerSide, cellSizeMetres, chunkSizeMetres, tiles);
        coarseMesh = BuildMesh(chunkMin, cellsPerSide, cellSizeMetres, gridHeights, tiles);
        colourTexture = BuildTexture(profile, chunkMin, chunkSizeMetres, cellsPerSide, cellSizeMetres, gridHeights, tiles, maxTexturePixels, layerColours);
        return true;
    }

    public static Color32 ColourAt(TerrainStrategicProfile profile, Color[] layerColours, in StrategicTexelInput input)
    {
        float lightMultiplier = input.lightMultiplier;
        if (input.waterDepthMetres > 0f)
        {
            Color waterColour = Color.Lerp(profile.waterShallowColour, profile.waterDeepColour, Mathf.Clamp01(input.waterDepthMetres / DeepWaterDepthMetres));
            return ToColour32(waterColour * Mathf.Lerp(1f, lightMultiplier, 0.5f));
        }

        float turfWeightSum = input.grassWeight + input.forestWeight + input.stoneWeight;
        float normalise = turfWeightSum > 1f ? 1f / turfWeightSum : 1f;
        float grassWeight = input.grassWeight * normalise;
        float forestWeight = input.forestWeight * normalise;
        float stoneWeight = input.stoneWeight * normalise;
        float dirtWeight = Mathf.Max(0f, 1f - grassWeight - forestWeight - stoneWeight);
        Color blended = layerColours[0] * dirtWeight + layerColours[1] * grassWeight + layerColours[2] * forestWeight + layerColours[3] * stoneWeight;

        float speckleMultiplier = 1f + CanopySpeckle(profile, input.worldX, input.worldZ) * forestWeight;
        float fieldMultiplier = 1f + FieldPatchTint(profile, input.worldX, input.worldZ) * grassWeight;
        Color shaded = blended * (speckleMultiplier * fieldMultiplier * lightMultiplier);

        if (input.roadCoverage > 0f)
        {
            Color roadColour = layerColours[0] * 1.15f * lightMultiplier;
            shaded = Color.Lerp(shaded, roadColour, Mathf.Clamp01(input.roadCoverage));
        }
        return ToColour32(shaded);
    }

    private static Color32 ToColour32(Color colour)
    {
        return new Color32(
            (byte)Mathf.RoundToInt(Mathf.Clamp01(colour.r) * 255f),
            (byte)Mathf.RoundToInt(Mathf.Clamp01(colour.g) * 255f),
            (byte)Mathf.RoundToInt(Mathf.Clamp01(colour.b) * 255f),
            255);
    }

    private static float CanopySpeckle(TerrainStrategicProfile profile, float worldX, float worldZ)
    {
        float cellMetres = profile.canopySpeckleScaleMetres * 0.5f;
        float noise = Hash01(Mathf.FloorToInt(worldX / cellMetres), Mathf.FloorToInt(worldZ / cellMetres), 1u) * 2f - 1f;
        return noise * profile.canopySpeckleContrast;
    }

    private static float FieldPatchTint(TerrainStrategicProfile profile, float worldX, float worldZ)
    {
        float noise = Hash01(Mathf.FloorToInt(worldX / FieldPatchSizeMetres), Mathf.FloorToInt(worldZ / FieldPatchSizeMetres), 2u) * 2f - 1f;
        return noise * profile.fieldPatchTintStrength * 0.25f;
    }

    private static float Hash01(int cellX, int cellZ, uint salt)
    {
        unchecked
        {
            uint hash = (uint)cellX * 73856093u ^ (uint)cellZ * 19349663u ^ salt * 83492791u;
            hash ^= hash >> 13;
            hash *= 0x5bd1e995u;
            hash ^= hash >> 15;
            return (hash & 0xFFFFFFu) / 16777216f;
        }
    }

    private static bool AnyTileInChunk(TerrainStrategicProfile profile, Vector2Int superChunkCoordinate, IReadOnlyDictionary<Vector2Int, TerrainTileData> tiles)
    {
        int firstTileX = superChunkCoordinate.x * profile.superChunkTiles;
        int firstTileZ = superChunkCoordinate.y * profile.superChunkTiles;
        for (int offsetZ = 0; offsetZ < profile.superChunkTiles; offsetZ++)
        {
            for (int offsetX = 0; offsetX < profile.superChunkTiles; offsetX++)
            {
                if (tiles.ContainsKey(new Vector2Int(firstTileX + offsetX, firstTileZ + offsetZ)))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // Floor ownership (never rounding): a point on a tile border belongs to the higher tile, identically in both neighbouring chunks.
    private static bool TryGetOwningTile(IReadOnlyDictionary<Vector2Int, TerrainTileData> tiles, float worldX, float worldZ, out TerrainTileData tile)
    {
        Vector2Int coordinate = new Vector2Int(
            Mathf.FloorToInt((worldX + TerrainTileData.TileSizeMetres * 0.5f) / TerrainTileData.TileSizeMetres),
            Mathf.FloorToInt((worldZ + TerrainTileData.TileSizeMetres * 0.5f) / TerrainTileData.TileSizeMetres));
        return tiles.TryGetValue(coordinate, out tile) && tile != null;
    }

    private static float HeightAtWorld(IReadOnlyDictionary<Vector2Int, TerrainTileData> tiles, float worldX, float worldZ)
    {
        TerrainTileData tile;
        if (TryGetOwningTile(tiles, worldX, worldZ, out tile))
        {
            return tile.SampleBilinear(worldX, worldZ) + TerrainTileCreator.GroundTopHeight;
        }
        // Chunk edge whose higher neighbour tile does not exist: borrow the tile just inside so the border stays continuous.
        if (TryGetOwningTile(tiles, worldX - 0.001f, worldZ - 0.001f, out tile))
        {
            return tile.SampleBilinear(worldX, worldZ) + TerrainTileCreator.GroundTopHeight;
        }
        return TerrainTileCreator.GroundTopHeight;
    }

    private static float[] BuildCoarseHeights(Vector2 chunkMin, int cellsPerSide, float cellSizeMetres, float chunkSizeMetres, IReadOnlyDictionary<Vector2Int, TerrainTileData> tiles)
    {
        int vertexCountPerSide = cellsPerSide + 1;
        float[] gridHeights = new float[vertexCountPerSide * vertexCountPerSide];
        for (int indexZ = 0; indexZ < vertexCountPerSide; indexZ++)
        {
            float worldZ = indexZ == cellsPerSide ? chunkMin.y + chunkSizeMetres : chunkMin.y + indexZ * cellSizeMetres;
            for (int indexX = 0; indexX < vertexCountPerSide; indexX++)
            {
                float worldX = indexX == cellsPerSide ? chunkMin.x + chunkSizeMetres : chunkMin.x + indexX * cellSizeMetres;
                gridHeights[indexZ * vertexCountPerSide + indexX] = HeightAtWorld(tiles, worldX, worldZ);
            }
        }
        return gridHeights;
    }

    private static Mesh BuildMesh(Vector2 chunkMin, int cellsPerSide, float cellSizeMetres, float[] gridHeights, IReadOnlyDictionary<Vector2Int, TerrainTileData> tiles)
    {
        int vertexCountPerSide = cellsPerSide + 1;
        int vertexCount = vertexCountPerSide * vertexCountPerSide;
        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        for (int indexZ = 0; indexZ < vertexCountPerSide; indexZ++)
        {
            for (int indexX = 0; indexX < vertexCountPerSide; indexX++)
            {
                int vertexIndex = indexZ * vertexCountPerSide + indexX;
                vertices[vertexIndex] = new Vector3(indexX * cellSizeMetres, gridHeights[vertexIndex], indexZ * cellSizeMetres);
                uvs[vertexIndex] = new Vector2(indexX / (float)cellsPerSide, indexZ / (float)cellsPerSide);
            }
        }

        List<int> triangles = new List<int>(cellsPerSide * cellsPerSide * 6);
        for (int cellZ = 0; cellZ < cellsPerSide; cellZ++)
        {
            for (int cellX = 0; cellX < cellsPerSide; cellX++)
            {
                float centreX = chunkMin.x + (cellX + 0.5f) * cellSizeMetres;
                float centreZ = chunkMin.y + (cellZ + 0.5f) * cellSizeMetres;
                TerrainTileData owningTile;
                if (!TryGetOwningTile(tiles, centreX, centreZ, out owningTile))
                {
                    continue;
                }
                int lowerLeft = cellZ * vertexCountPerSide + cellX;
                int lowerRight = lowerLeft + 1;
                int upperLeft = lowerLeft + vertexCountPerSide;
                int upperRight = upperLeft + 1;
                triangles.Add(lowerLeft);
                triangles.Add(upperLeft);
                triangles.Add(upperRight);
                triangles.Add(lowerLeft);
                triangles.Add(upperRight);
                triangles.Add(lowerRight);
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = "StrategicCoarseMesh";
        mesh.indexFormat = vertexCount > MaxSixteenBitVertexCount ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static int ChooseTexturePixelsPerSide(float chunkSizeMetres, float texelMetres, int maxTexturePixels)
    {
        int wanted = Mathf.Max(1, Mathf.CeilToInt(chunkSizeMetres / texelMetres));
        return Mathf.Clamp(wanted, 1, Mathf.Max(1, maxTexturePixels));
    }

    private static float SampleGrid(float[] gridHeights, int cellsPerSide, float cellSizeMetres, float localX, float localZ)
    {
        float gridX = Mathf.Clamp(localX / cellSizeMetres, 0f, cellsPerSide);
        float gridZ = Mathf.Clamp(localZ / cellSizeMetres, 0f, cellsPerSide);
        int lowX = Mathf.Min((int)gridX, cellsPerSide - 1);
        int lowZ = Mathf.Min((int)gridZ, cellsPerSide - 1);
        float fractionX = gridX - lowX;
        float fractionZ = gridZ - lowZ;
        int vertexCountPerSide = cellsPerSide + 1;
        float lowRow = Mathf.Lerp(gridHeights[lowZ * vertexCountPerSide + lowX], gridHeights[lowZ * vertexCountPerSide + lowX + 1], fractionX);
        float highRow = Mathf.Lerp(gridHeights[(lowZ + 1) * vertexCountPerSide + lowX], gridHeights[(lowZ + 1) * vertexCountPerSide + lowX + 1], fractionX);
        return Mathf.Lerp(lowRow, highRow, fractionZ);
    }

    private static float ComputeLightMultiplier(TerrainStrategicProfile profile, float[] gridHeights, int cellsPerSide, float cellSizeMetres, float localX, float localZ, Vector3 towardSun)
    {
        float height = SampleGrid(gridHeights, cellsPerSide, cellSizeMetres, localX, localZ);
        float slopeX = (SampleGrid(gridHeights, cellsPerSide, cellSizeMetres, localX + cellSizeMetres, localZ) - SampleGrid(gridHeights, cellsPerSide, cellSizeMetres, localX - cellSizeMetres, localZ)) / (2f * cellSizeMetres);
        float slopeZ = (SampleGrid(gridHeights, cellsPerSide, cellSizeMetres, localX, localZ + cellSizeMetres) - SampleGrid(gridHeights, cellsPerSide, cellSizeMetres, localX, localZ - cellSizeMetres)) / (2f * cellSizeMetres);
        Vector3 normal = new Vector3(-slopeX, 1f, -slopeZ).normalized;
        float flatLight = Mathf.Clamp01(towardSun.y);
        float hillShade = 1f + profile.hillShadeStrength * (Mathf.Clamp01(Vector3.Dot(normal, towardSun)) - flatLight);

        Vector2 horizontalToSun = new Vector2(towardSun.x, towardSun.z);
        float horizontalLength = horizontalToSun.magnitude;
        float shadowMultiplier = 1f;
        if (horizontalLength > 0.0001f)
        {
            Vector2 horizontalDirection = horizontalToSun / horizontalLength;
            float risePerMetre = towardSun.y / horizontalLength;
            for (int step = 1; step <= ShadowMarchStepCount; step++)
            {
                float distance = step * ShadowMarchStepMetres;
                float sampleHeight = SampleGrid(gridHeights, cellsPerSide, cellSizeMetres, localX + horizontalDirection.x * distance, localZ + horizontalDirection.y * distance);
                if (sampleHeight > height + distance * risePerMetre)
                {
                    shadowMultiplier = 1f - profile.bakedShadowStrength;
                    break;
                }
            }
        }
        return Mathf.Clamp(hillShade, 0.3f, 1.5f) * shadowMultiplier;
    }

    private static readonly Dictionary<Texture2D, Texture2D> readableMaskCopies = new Dictionary<Texture2D, Texture2D>();

    public static void ReleaseReadableMaskCopies()
    {
        foreach (Texture2D copy in readableMaskCopies.Values)
        {
            if (copy != null)
            {
                Object.DestroyImmediate(copy);
            }
        }
        readableMaskCopies.Clear();
    }

    // Tile masks import with Read/Write off, and skipping them silently turned every texel to bare dirt (all chunks baked brown),
    // so an unreadable mask is read back through the GPU into a readable copy.
    private static Texture2D GetReadableMask(Texture2D mask)
    {
        if (mask.isReadable)
        {
            return mask;
        }
        Texture2D copy;
        if (readableMaskCopies.TryGetValue(mask, out copy) && copy != null)
        {
            return copy;
        }

        RenderTextureReadWrite colourSpace = mask.isDataSRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;
        RenderTexture target = RenderTexture.GetTemporary(mask.width, mask.height, 0, RenderTextureFormat.ARGB32, colourSpace);
        RenderTexture previousActive = RenderTexture.active;
        Graphics.Blit(mask, target);
        RenderTexture.active = target;
        copy = new Texture2D(mask.width, mask.height, TextureFormat.RGBA32, false, !mask.isDataSRGB);
        copy.ReadPixels(new Rect(0f, 0f, mask.width, mask.height), 0, 0);
        copy.Apply(false, false);
        copy.wrapMode = TextureWrapMode.Clamp;
        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(target);
        readableMaskCopies[mask] = copy;
        return copy;
    }

    private static bool TryReadMask(Texture2D mask, float coordinateU, float coordinateV, out Color sample)
    {
        sample = default;
        if (mask == null)
        {
            return false;
        }
        sample = GetReadableMask(mask).GetPixelBilinear(coordinateU, coordinateV);
        return true;
    }

    private static void SampleTileSurface(TerrainTileData tile, float worldX, float worldZ, float height, ref StrategicTexelInput input)
    {
        Vector2 tileMin = tile.WorldMinCorner();
        float coordinateU = (worldX - tileMin.x) / TerrainTileData.TileSizeMetres;
        float coordinateV = (worldZ - tileMin.y) / TerrainTileData.TileSizeMetres;

        // baseMask: R grass, G forest, B stone, remainder dirt. An unreadable mask leaves all weights 0, so the dirt default shows.
        Color baseSample;
        if (TryReadMask(tile.baseMask, coordinateU, coordinateV, out baseSample))
        {
            input.grassWeight = baseSample.r;
            input.forestWeight = baseSample.g;
            input.stoneWeight = baseSample.b;
        }

        Color roadSample;
        if (TryReadMask(tile.roadMask, coordinateU, coordinateV, out roadSample) && roadSample.a > RoadAlphaThreshold)
        {
            input.roadCoverage = 1f;
        }

        float waterDepth = height < 0f ? -height : 0f;
        Color surfaceSample;
        if (TryReadMask(tile.surfaceOverlay, coordinateU, coordinateV, out surfaceSample))
        {
            if (surfaceSample.g > ChannelThreshold)
            {
                waterDepth = Mathf.Max(waterDepth, DeepWaterDepthMetres);
            }
            else if (surfaceSample.r > ChannelThreshold)
            {
                waterDepth = Mathf.Max(waterDepth, 0.5f);
            }
        }
        input.waterDepthMetres = waterDepth;
    }

    private static Texture2D BuildTexture(TerrainStrategicProfile profile, Vector2 chunkMin, float chunkSizeMetres, int cellsPerSide, float cellSizeMetres, float[] gridHeights, IReadOnlyDictionary<Vector2Int, TerrainTileData> tiles, int maxTexturePixels, Color[] layerColours)
    {
        int pixelsPerSide = ChooseTexturePixelsPerSide(chunkSizeMetres, profile.colourTexelMetres, maxTexturePixels);
        float texelMetres = chunkSizeMetres / pixelsPerSide;
        Vector3 towardSun = -(Quaternion.Euler(profile.bakedSunEuler) * Vector3.forward);
        Color32[] pixels = new Color32[pixelsPerSide * pixelsPerSide];
        Color32 missingTileColour = ToColour32(layerColours[0]);

        for (int pixelZ = 0; pixelZ < pixelsPerSide; pixelZ++)
        {
            float localZ = (pixelZ + 0.5f) * texelMetres;
            for (int pixelX = 0; pixelX < pixelsPerSide; pixelX++)
            {
                float localX = (pixelX + 0.5f) * texelMetres;
                StrategicTexelInput input = new StrategicTexelInput();
                input.worldX = chunkMin.x + localX;
                input.worldZ = chunkMin.y + localZ;
                input.lightMultiplier = ComputeLightMultiplier(profile, gridHeights, cellsPerSide, cellSizeMetres, localX, localZ, towardSun);

                TerrainTileData tile;
                if (!TryGetOwningTile(tiles, input.worldX, input.worldZ, out tile))
                {
                    pixels[pixelZ * pixelsPerSide + pixelX] = missingTileColour;
                    continue;
                }
                float height = SampleGrid(gridHeights, cellsPerSide, cellSizeMetres, localX, localZ);
                SampleTileSurface(tile, input.worldX, input.worldZ, height, ref input);
                pixels[pixelZ * pixelsPerSide + pixelX] = ColourAt(profile, layerColours, input);
            }
        }

        Texture2D texture = new Texture2D(pixelsPerSide, pixelsPerSide, TextureFormat.RGBA32, false);
        texture.name = "StrategicColour";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return texture;
    }

    // No strategicColour field exists on the palette yet, so the layer tint is used; a white (unset) tint falls back to a muted default.
    public static Color[] LoadLayerColoursFromProject()
    {
        Color[] fallback =
        {
            new Color(0.45f, 0.36f, 0.26f, 1f),
            new Color(0.36f, 0.5f, 0.22f, 1f),
            new Color(0.12f, 0.26f, 0.12f, 1f),
            new Color(0.5f, 0.5f, 0.5f, 1f),
        };
        string[] guids = AssetDatabase.FindAssets("t:TerrainPalette");
        if (guids.Length == 0)
        {
            return fallback;
        }
        TerrainPalette palette = AssetDatabase.LoadAssetAtPath<TerrainPalette>(AssetDatabase.GUIDToAssetPath(guids[0]));
        if (palette == null)
        {
            return fallback;
        }
        Color[] colours = new Color[4];
        for (int layerIndex = 0; layerIndex < colours.Length; layerIndex++)
        {
            Color tint = palette.GetLayer(layerIndex).tint;
            colours[layerIndex] = tint == Color.white ? fallback[layerIndex] : tint;
        }
        return colours;
    }
}
#endif
