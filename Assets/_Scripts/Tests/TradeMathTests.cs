using NUnit.Framework;
using Unity.Mathematics;

namespace StitchPunk.Tests
{
    // F1 (Trade_System.md section 10). Gameplay code is in the global namespace, so TradeMath needs no using.
    [TestFixture]
    public sealed class TradeMathTests
    {
        private static TradeTuningBlob DefaultTuning()
        {
            return new TradeTuningBlob
            {
                corpseValue = 2f,
                scrapMetalValue = 1f,
                electricityValue = 1.5f,
                healthDifficultyWeight = 4f,
                deadlineWeight = 3f,
                dangerWeight = 2f,
                currencyPerDifficulty = 6f,
                payoutMultiplierByTier = new float4(1f, 1.1f, 1.25f, 1.4f),
            };
        }

        [Test]
        public void Difficulty_GrowsWithQuantityConditionDangerAndTightDeadlines()
        {
            TradeTuningBlob tuning = DefaultTuning();
            float baseline = TradeMath.ComputeDifficulty(2, 4.5f, 0.5f, 4, 1, in tuning);

            Assert.Greater(TradeMath.ComputeDifficulty(3, 4.5f, 0.5f, 4, 1, in tuning), baseline, "quantity");
            Assert.Greater(TradeMath.ComputeDifficulty(2, 4.5f, 0.9f, 4, 1, in tuning), baseline, "required health");
            Assert.Greater(TradeMath.ComputeDifficulty(2, 4.5f, 0.5f, 4, 3, in tuning), baseline, "danger");
            Assert.Greater(TradeMath.ComputeDifficulty(2, 4.5f, 0.5f, 2, 1, in tuning), baseline, "fewer days allowed");
        }

        [Test]
        public void Reward_ScalesWithDifficultyAndTheReputationTierMultiplier()
        {
            TradeTuningBlob tuning = DefaultTuning();

            Assert.AreEqual(60, TradeMath.ComputeCurrencyReward(10f, ReputationTier.Stranger, in tuning));
            Assert.AreEqual(75, TradeMath.ComputeCurrencyReward(10f, ReputationTier.Trusted, in tuning));
            Assert.AreEqual(84, TradeMath.ComputeCurrencyReward(10f, ReputationTier.Partner, in tuning));
        }

        [Test]
        public void Tier_ComesFromTheClientsThresholds()
        {
            int3 thresholds = new int3(25, 50, 80);

            Assert.AreEqual(ReputationTier.Stranger, TradeMath.TierFromReputation(24, thresholds));
            Assert.AreEqual(ReputationTier.Known, TradeMath.TierFromReputation(25, thresholds));
            Assert.AreEqual(ReputationTier.Trusted, TradeMath.TierFromReputation(79, thresholds));
            Assert.AreEqual(ReputationTier.Partner, TradeMath.TierFromReputation(80, thresholds));
        }

        [Test]
        public void OrderSize_OpensUpWithTier()
        {
            int2 quantityRange = new int2(2, 6);

            Assert.AreEqual(2, TradeMath.MaxQuantityForTier(quantityRange, ReputationTier.Stranger));
            Assert.AreEqual(4, TradeMath.MaxQuantityForTier(quantityRange, ReputationTier.Known));
            Assert.AreEqual(6, TradeMath.MaxQuantityForTier(quantityRange, ReputationTier.Trusted));
        }
    }
}
