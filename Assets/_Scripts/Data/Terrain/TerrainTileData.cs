using UnityEngine;

[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Tile Data", fileName = "TerrainTileData")]
public class TerrainTileData : ScriptableObject
{
    public const float TileSizeMetres = 100f;
    public const int HeightSamplesPerSide = 101;
    public const int HeightSampleCount = 101 * 101;

    public Vector2Int coordinate;
    public int maskResolution = 256;

    [Tooltip("Generator-owned base terrain. Never hand-edited.")]
    public float[] baseHeights;
    [Tooltip("Hand sculpt delta. The generator never writes this.")]
    public float[] handHeightDelta;
    public float[] riverHeightDelta;
    public float[] roadHeightDelta;

    public Texture2D baseMask;
    [Tooltip("RGBA paint overlay, A = painted.")]
    public Texture2D paintOverlay;
    public Texture2D roadMask;
    [Tooltip("R shallow water, G deep water, B mud.")]
    public Texture2D surfaceOverlay;
    public Texture2D compositeSdf;

    public bool handAuthored;
    public int revision;

    [Tooltip("revision the compositor last built compositeSdf from; differs from revision when the tile needs a re-composite")]
    public int compositedRevision = -1;

    public void EnsureHeightArrays()
    {
        if (baseHeights == null || baseHeights.Length != HeightSampleCount)
        {
            baseHeights = new float[HeightSampleCount];
        }
        if (handHeightDelta == null || handHeightDelta.Length != HeightSampleCount)
        {
            handHeightDelta = new float[HeightSampleCount];
        }
        if (riverHeightDelta == null || riverHeightDelta.Length != HeightSampleCount)
        {
            riverHeightDelta = new float[HeightSampleCount];
        }
        if (roadHeightDelta == null || roadHeightDelta.Length != HeightSampleCount)
        {
            roadHeightDelta = new float[HeightSampleCount];
        }
    }

    public float HeightAt(int sampleX, int sampleZ)
    {
        int clampedX = Mathf.Clamp(sampleX, 0, HeightSamplesPerSide - 1);
        int clampedZ = Mathf.Clamp(sampleZ, 0, HeightSamplesPerSide - 1);
        int index = clampedZ * HeightSamplesPerSide + clampedX;
        return ValueOrZero(baseHeights, index)
            + ValueOrZero(handHeightDelta, index)
            + ValueOrZero(riverHeightDelta, index)
            + ValueOrZero(roadHeightDelta, index);
    }

    public float SampleBilinear(float worldX, float worldZ)
    {
        Vector2 minCorner = WorldMinCorner();
        float maxLocal = HeightSamplesPerSide - 1;
        float localX = Mathf.Clamp(worldX - minCorner.x, 0f, maxLocal);
        float localZ = Mathf.Clamp(worldZ - minCorner.y, 0f, maxLocal);
        int lowX = Mathf.Min((int)localX, HeightSamplesPerSide - 2);
        int lowZ = Mathf.Min((int)localZ, HeightSamplesPerSide - 2);
        float fractionX = localX - lowX;
        float fractionZ = localZ - lowZ;
        float lowRow = Mathf.Lerp(HeightAt(lowX, lowZ), HeightAt(lowX + 1, lowZ), fractionX);
        float highRow = Mathf.Lerp(HeightAt(lowX, lowZ + 1), HeightAt(lowX + 1, lowZ + 1), fractionX);
        return Mathf.Lerp(lowRow, highRow, fractionZ);
    }

    // Terrain as the road baker needs it: roads carve a delta on top of this, so it must not read that delta back
    public float HeightExcludingRoadAt(int sampleX, int sampleZ)
    {
        int clampedX = Mathf.Clamp(sampleX, 0, HeightSamplesPerSide - 1);
        int clampedZ = Mathf.Clamp(sampleZ, 0, HeightSamplesPerSide - 1);
        int index = clampedZ * HeightSamplesPerSide + clampedX;
        return ValueOrZero(baseHeights, index) + ValueOrZero(handHeightDelta, index) + ValueOrZero(riverHeightDelta, index);
    }

    public float SampleBilinearExcludingRoad(float worldX, float worldZ)
    {
        Vector2 minCorner = WorldMinCorner();
        float maxLocal = HeightSamplesPerSide - 1;
        float localX = Mathf.Clamp(worldX - minCorner.x, 0f, maxLocal);
        float localZ = Mathf.Clamp(worldZ - minCorner.y, 0f, maxLocal);
        int lowX = Mathf.Min((int)localX, HeightSamplesPerSide - 2);
        int lowZ = Mathf.Min((int)localZ, HeightSamplesPerSide - 2);
        float fractionX = localX - lowX;
        float fractionZ = localZ - lowZ;
        float lowRow = Mathf.Lerp(HeightExcludingRoadAt(lowX, lowZ), HeightExcludingRoadAt(lowX + 1, lowZ), fractionX);
        float highRow = Mathf.Lerp(HeightExcludingRoadAt(lowX, lowZ + 1), HeightExcludingRoadAt(lowX + 1, lowZ + 1), fractionX);
        return Mathf.Lerp(lowRow, highRow, fractionZ);
    }

    // Terrain as the river baker needs it: the river carves a delta on top, so it must not read its own delta back
    public float HeightExcludingRiverAt(int sampleX, int sampleZ)
    {
        int clampedX = Mathf.Clamp(sampleX, 0, HeightSamplesPerSide - 1);
        int clampedZ = Mathf.Clamp(sampleZ, 0, HeightSamplesPerSide - 1);
        int index = clampedZ * HeightSamplesPerSide + clampedX;
        return ValueOrZero(baseHeights, index) + ValueOrZero(handHeightDelta, index) + ValueOrZero(roadHeightDelta, index);
    }

    public float SampleBilinearExcludingRiver(float worldX, float worldZ)
    {
        Vector2 minCorner = WorldMinCorner();
        float maxLocal = HeightSamplesPerSide - 1;
        float localX = Mathf.Clamp(worldX - minCorner.x, 0f, maxLocal);
        float localZ = Mathf.Clamp(worldZ - minCorner.y, 0f, maxLocal);
        int lowX = Mathf.Min((int)localX, HeightSamplesPerSide - 2);
        int lowZ = Mathf.Min((int)localZ, HeightSamplesPerSide - 2);
        float fractionX = localX - lowX;
        float fractionZ = localZ - lowZ;
        float lowRow = Mathf.Lerp(HeightExcludingRiverAt(lowX, lowZ), HeightExcludingRiverAt(lowX + 1, lowZ), fractionX);
        float highRow = Mathf.Lerp(HeightExcludingRiverAt(lowX, lowZ + 1), HeightExcludingRiverAt(lowX + 1, lowZ + 1), fractionX);
        return Mathf.Lerp(lowRow, highRow, fractionZ);
    }

    public Vector2 WorldMinCorner()
    {
        return new Vector2(coordinate.x * TileSizeMetres - 50f, coordinate.y * TileSizeMetres - 50f);
    }

    public void SetHandHeightDelta(int sampleX, int sampleZ, float value)
    {
        if (sampleX < 0 || sampleX >= HeightSamplesPerSide || sampleZ < 0 || sampleZ >= HeightSamplesPerSide)
        {
            return;
        }
        EnsureHeightArrays();
        handHeightDelta[sampleZ * HeightSamplesPerSide + sampleX] = value;
    }

    public void BumpRevision()
    {
        revision++;
    }

    private static float ValueOrZero(float[] array, int index)
    {
        if (array == null || index >= array.Length)
        {
            return 0f;
        }
        return array[index];
    }
}
