using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct TerrainHeightLayer
{
    public float frequencyPerMetre;
    public float amplitudeMetres;
    public int octaves;
    public float lacunarity;
    public float gain;
}

// A rule passes when noise01 * weight >= noiseThreshold with height and slope inside their ranges; a passing later rule overrides an earlier one.
[System.Serializable]
public struct TerrainBiomeRule
{
    public TerrainSplatLayer layer;
    public Vector2 heightRange;
    public Vector2 slopeRangeDegrees;
    public float noiseFrequency;
    public float noiseThreshold;
    public float weight;
}

[System.Serializable]
public struct TerraceBiomeRule
{
    public TerrainSplatLayer biome;
    public bool enabled;
    [Tooltip("Unused since Natural Cliffs (plateauStyles); deleted in the cleanup wave.")] public float[] plateauThresholds;
    [Tooltip("Unused since Natural Cliffs (plateauStyles); deleted in the cleanup wave.")] public float plateauNoiseFrequencyPerMetre;
    [Range(0, 3)] public int maxLevel;
    [Tooltip("Plateaus smaller than this that do not touch the generation window border are removed.")]
    public float minPlateauAreaSquareMetres;
    [Tooltip("Where the ground slope exceeds cliffSlopeDegrees and cliff noise passes, the downhill side gets level -1.")]
    public bool cliffOnSlope;
    public float cliffSlopeDegrees;
    [Range(0, 1)] public float cliffChance;
}

[System.Serializable]
public struct TerrainMountainFeature
{
    [Tooltip("World X,Z. Same as the end point for a single peak; different for a ridge between the two.")]
    public Vector2 startWorld;
    public Vector2 endWorld;
    public float radiusMetres;
    public float heightMetres;
}

[System.Serializable]
public struct TerrainFlatArea
{
    [Tooltip("World X,Z.")]
    public Vector2 centreWorld;
    [Tooltip("Ground is exactly heightMetres inside this radius.")]
    public float flatRadiusMetres;
    [Tooltip("Generated terrain is untouched beyond this radius.")]
    public float blendEndRadiusMetres;
    public float heightMetres;
}

[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Generation Profile", fileName = "TerrainGenerationProfile")]
public class TerrainGenerationProfile : ScriptableObject
{
    public int seed = 1;

    public List<TerrainHeightLayer> heightLayers = new List<TerrainHeightLayer>
    {
        new TerrainHeightLayer { frequencyPerMetre = 0.01f, amplitudeMetres = 3f, octaves = 4, lacunarity = 2f, gain = 0.5f }
    };

    [Tooltip("0..1 normalised noise in, 0..1 out.")]
    public AnimationCurve heightRemap = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    public float baseHeightMetres = 0f;

    [Tooltip("Heights under this are raised to it, keeps river plains flat.")]
    public float flattenBelowMetres = -1000f;

    [Tooltip("World X,Z.")]
    public Vector2 mountainCentreWorld;

    [Tooltip("0 disables the mountain.")]
    public float mountainRadiusMetres = 0f;

    public float mountainHeightMetres = 0f;

    [Tooltip("x: 0 at the foot, 1 at the peak.")]
    public AnimationCurve mountainProfile = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Tooltip("Extra peaks and ridges, shaped by mountainProfile. Overlapping features take the highest, they do not stack.")]
    public List<TerrainMountainFeature> mountains = new List<TerrainMountainFeature>();

    [Tooltip("Added to the noise before mountains, so signed noise does not read as sea everywhere.")]
    public float landLiftMetres = 0f;

    [Tooltip("Level ground for camps and towns, blended into the terrain around it.")]
    public List<TerrainFlatArea> flatAreas = new List<TerrainFlatArea>();

    [Header("Biome noise shape")]
    [Tooltip("Metres the biome noise lookup is pushed around by a second noise. 0 = off (old behaviour). ~8-14 gives organic, non-grid borders.")]
    public float biomeNoiseWarpMetres = 0f;

    [Tooltip("Frequency of the warp noise, per metre.")]
    public float biomeNoiseWarpFrequency = 0.04f;

    [Tooltip("Each biome rule's noise is sampled in a grid rotated by this much more than the previous rule, so no rule lines up with the world axes.")]
    public float biomeNoiseRotationDegrees = 0f;

    public List<TerrainBiomeRule> biomeRules = new List<TerrainBiomeRule>
    {
        new TerrainBiomeRule
        {
            layer = TerrainSplatLayer.Grass,
            heightRange = new Vector2(-1000f, 1000f),
            slopeRangeDegrees = new Vector2(0f, 90f),
            noiseFrequency = 0.01f,
            noiseThreshold = 0f,
            weight = 1f
        },
        new TerrainBiomeRule
        {
            layer = TerrainSplatLayer.Forest,
            heightRange = new Vector2(-1000f, 1000f),
            slopeRangeDegrees = new Vector2(0f, 30f),
            noiseFrequency = 0.02f,
            noiseThreshold = 0.62f,
            weight = 1f
        },
        new TerrainBiomeRule
        {
            layer = TerrainSplatLayer.Stone,
            heightRange = new Vector2(-1000f, 1000f),
            slopeRangeDegrees = new Vector2(35f, 90f),
            noiseFrequency = 0.05f,
            noiseThreshold = 0f,
            weight = 1f
        }
    };

    [Header("Terraces")]
    [Tooltip("NC-D3 plateau recipes; a tile picks one by weighted hash of (seed, tile, roll). Index 0 is the world style used in every tile's border band.")]
    public List<TerracePlateauStyle> plateauStyles = TerracePlateauStyleDefaults.CreateDefaultStyles();

    [Tooltip("A biome with no enabled rule gets no terraces.")]
    public List<TerraceBiomeRule> terraceBiomeRules = new List<TerraceBiomeRule>
    {
        new TerraceBiomeRule
        {
            biome = TerrainSplatLayer.Grass,
            enabled = true,
            plateauThresholds = new float[] { 0.6f, 0.78f },
            plateauNoiseFrequencyPerMetre = 0.006f,
            maxLevel = 2,
            minPlateauAreaSquareMetres = 150f,
            cliffOnSlope = true,
            cliffSlopeDegrees = 12f,
            cliffChance = 0.5f
        },
        new TerraceBiomeRule
        {
            biome = TerrainSplatLayer.Stone,
            enabled = true,
            plateauThresholds = new float[] { 0.5f },
            plateauNoiseFrequencyPerMetre = 0.01f,
            maxLevel = 1,
            minPlateauAreaSquareMetres = 100f,
            cliffOnSlope = true,
            cliffSlopeDegrees = 8f,
            cliffChance = 0.8f
        }
    };
}
