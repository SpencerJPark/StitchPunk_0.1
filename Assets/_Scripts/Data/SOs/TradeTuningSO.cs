using Unity.Mathematics;
using UnityEngine;

// Every weight in TradeMath lives here (TR-A9). Tier arrays are indexed by ReputationTier.
[CreateAssetMenu(menuName = "Stitch Punk/Trade/Trade Tuning")]
public class TradeTuningSO : ScriptableObject
{
    [Header("Difficulty")]
    public float corpseValue = 2f;
    public float scrapMetalValue = 1f;
    public float electricityValue = 1.5f;
    public float healthDifficultyWeight = 4f;
    public float deadlineWeight = 3f;
    public float dangerWeight = 2f;

    [Header("Reward")]
    public float currencyPerDifficulty = 6f;
    public float[] payoutMultiplierByTier = { 1f, 1.1f, 1.25f, 1.4f };

    [Header("Offers per week, by tier")]
    public int[] minOffersByTier = { 0, 1, 1, 2 };
    public int[] maxOffersByTier = { 1, 1, 2, 2 };
    public int householdOffersPerWeek = 2;

    [Header("Relationships")]
    public int startingReputation = 10;

    public float4 PayoutMultipliers() => ToFloat4(payoutMultiplierByTier, 1f);
    public int4 MinOffers() => ToInt4(minOffersByTier);
    public int4 MaxOffers() => ToInt4(maxOffersByTier);

    private static float4 ToFloat4(float[] source, float fallback)
    {
        float4 result = new float4(fallback);
        for (int index = 0; source != null && index < 4 && index < source.Length; index++)
            result[index] = source[index];
        return result;
    }

    private static int4 ToInt4(int[] source)
    {
        int4 result = int4.zero;
        for (int index = 0; source != null && index < 4 && index < source.Length; index++)
            result[index] = source[index];
        return result;
    }
}
