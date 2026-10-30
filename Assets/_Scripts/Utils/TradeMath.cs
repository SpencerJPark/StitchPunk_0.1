using Unity.Mathematics;

// Pure difficulty/reward/seed functions (Trade_System.md TR-A9). Every weight comes from TradeTuningBlob.
public static class TradeMath
{
    public const int DaysPerWeek = 7;
    public const int OrderIdWeekStride = 100;

    public static uint RollSeed(uint worldSeed, int weekNumber)
    {
        return math.hash(new uint2(worldSeed, (uint)weekNumber));
    }

    public static int OrderId(int weekNumber, int indexInWeek)
    {
        return weekNumber * OrderIdWeekStride + indexInWeek;
    }

    // The roll and the UI both call this, so a household one-off needs no stored name index.
    public static int HouseholdNameIndex(int clientId, int nameCount)
    {
        if (nameCount <= 0)
            return 0;
        return (int)(math.hash(new int2(clientId, 7919)) % (uint)nameCount);
    }

    public static ReputationTier TierFromReputation(int reputation, int3 tierThresholds)
    {
        if (reputation >= tierThresholds.z) return ReputationTier.Partner;
        if (reputation >= tierThresholds.y) return ReputationTier.Trusted;
        if (reputation >= tierThresholds.x) return ReputationTier.Known;
        return ReputationTier.Stranger;
    }

    // Stranger gets the template minimum, Known the midpoint, Trusted and Partner the full range.
    public static int MaxQuantityForTier(int2 quantityRange, ReputationTier tier)
    {
        if (tier == ReputationTier.Stranger) return quantityRange.x;
        if (tier == ReputationTier.Known) return (quantityRange.x + quantityRange.y) / 2;
        return quantityRange.y;
    }

    // An order is overdue once its due day has passed in its own week, or its week is over (Accepted orders survive the weekly roll).
    public static bool IsOrderOverdue(int weekIssued, int dueDayOfWeek, int currentWeek, int currentDayOfWeek)
    {
        return weekIssued < currentWeek || dueDayOfWeek < currentDayOfWeek;
    }

    public static float UnitTierWeight(int corpses, int scrapMetal, int electricity, in TradeTuningBlob tuning)
    {
        return corpses * tuning.corpseValue
             + scrapMetal * tuning.scrapMetalValue
             + electricity * tuning.electricityValue;
    }

    public static float ComputeDifficulty(
        int quantity, float unitTierWeight, float minHealthFraction, int daysAllowed, int dangerRating,
        in TradeTuningBlob tuning)
    {
        float deadlineTightness = (DaysPerWeek - daysAllowed) / (float)DaysPerWeek;
        return quantity * unitTierWeight
             + minHealthFraction * tuning.healthDifficultyWeight
             + deadlineTightness * tuning.deadlineWeight
             + dangerRating * tuning.dangerWeight;
    }

    public static int ComputeCurrencyReward(float difficulty, ReputationTier tier, in TradeTuningBlob tuning)
    {
        float tierMultiplier = tuning.payoutMultiplierByTier[(int)tier];
        return (int)math.round(difficulty * tuning.currencyPerDifficulty * tierMultiplier);
    }
}
