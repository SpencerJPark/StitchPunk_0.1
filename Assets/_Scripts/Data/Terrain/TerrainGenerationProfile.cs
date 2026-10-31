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
}
