using System.Collections.Generic;
using UnityEngine;

// NC-D3: one plateau recipe. Field units: macro fbm is roughly -1..1; thresholds are compared against SampleStyle.
[System.Serializable]
public struct TerracePlateauStyle
{
    public string styleName;
    public float weight;
    public float grainDegrees;
    public float grainStretch;
    public float warpMetres;
    public float warpFrequencyPerMetre;
    public float macroFrequencyPerMetre;
    public float coveAmount;
    public float wobbleAmount;
    public float bias;
    public float firstLevelThreshold;
    public float secondLevelThreshold;
    [Range(0, 1)] public float lowStepShare;
}

public static class TerracePlateauStyleDefaults
{
    // Index 0 is the world style (NC-D7 world field); the order is LedgeField, Open, Mesa, Escarpment, Stacked.
    public static List<TerracePlateauStyle> CreateDefaultStyles()
    {
        return new List<TerracePlateauStyle>
        {
            CreateStyle("LedgeField", 1.2f, 0f, 1f, 24f, 0.012f, 0.008f, 0.42f, 0.15f, -0.05f, 0.20f, 0.60f, 0.85f),
            CreateStyle("Open", 1.0f, 0f, 1f, 20f, 0.012f, 0.005f, 0.25f, 0.15f, -0.62f, 0.30f, 0.70f, 0.80f),
            CreateStyle("Mesa", 1.0f, 0f, 1f, 26f, 0.012f, 0.004f, 0.40f, 0.18f, -0.06f, 0.15f, 0.55f, 0.25f),
            CreateStyle("Escarpment", 1.0f, 35f, 4f, 24f, 0.012f, 0.006f, 0.40f, 0.15f, -0.08f, 0.10f, 0.60f, 0.35f),
            CreateStyle("Stacked", 0.8f, 0f, 1f, 22f, 0.012f, 0.005f, 0.36f, 0.15f, -0.35f, 0.05f, 0.30f, 0.30f),
        };
    }

    private static TerracePlateauStyle CreateStyle(string styleName, float weight, float grainDegrees, float grainStretch,
        float warpMetres, float warpFrequencyPerMetre, float macroFrequencyPerMetre, float coveAmount, float wobbleAmount,
        float bias, float firstLevelThreshold, float secondLevelThreshold, float lowStepShare)
    {
        return new TerracePlateauStyle
        {
            styleName = styleName,
            weight = weight,
            grainDegrees = grainDegrees,
            grainStretch = grainStretch,
            warpMetres = warpMetres,
            warpFrequencyPerMetre = warpFrequencyPerMetre,
            macroFrequencyPerMetre = macroFrequencyPerMetre,
            coveAmount = coveAmount,
            wobbleAmount = wobbleAmount,
            bias = bias,
            firstLevelThreshold = firstLevelThreshold,
            secondLevelThreshold = secondLevelThreshold,
            lowStepShare = lowStepShare,
        };
    }
}
