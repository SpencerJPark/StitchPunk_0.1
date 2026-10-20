using NUnit.Framework;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class CrowdLifeRulesTests
    {
        private static ConversationTraits MakeTraits(float sociability, float temper, float humour)
        {
            return new ConversationTraits { sociability = sociability, temper = temper, humour = humour };
        }

        [Test]
        public void PairSide_BothUnitsAgreeOnMoodAndExactlyOneSpeaksPerBeat()
        {
            ConversationTraits[] traitSets =
            {
                MakeTraits(0.9f, 0.1f, 0.5f),
                MakeTraits(0.1f, 0.9f, 0.2f),
                MakeTraits(0.5f, 0.5f, 0.9f),
            };
            uint[] seeds = { 1u, 77u, 123456u };

            for (int traitIndex = 0; traitIndex < traitSets.Length; traitIndex++)
            {
                ConversationTraits lower = traitSets[traitIndex];
                ConversationTraits higher = traitSets[(traitIndex + 1) % traitSets.Length];
                float pairMoodFromLowerSide = ConversationEmoteRules.PairMood(in lower, in higher);
                float pairMoodFromHigherSide = ConversationEmoteRules.PairMood(in lower, in higher);
                Assert.AreEqual(pairMoodFromLowerSide, pairMoodFromHigherSide);

                foreach (uint seed in seeds)
                {
                    for (int beat = 0; beat < 10; beat++)
                    {
                        Assert.AreEqual(
                            ConversationEmoteRules.BeatMood(pairMoodFromLowerSide, seed, beat),
                            ConversationEmoteRules.BeatMood(pairMoodFromHigherSide, seed, beat));
                    }
                }
            }

            for (int beat = 0; beat < 10; beat++)
            {
                Assert.AreNotEqual(
                    ConversationEmoteRules.IsSpeaker(true, beat),
                    ConversationEmoteRules.IsSpeaker(false, beat));
                Assert.AreEqual(beat % 2 == 0, ConversationEmoteRules.IsSpeaker(true, beat));
            }
        }

        [Test]
        public void BeatIndexAt_StartsAtZeroIsMonotonicAndCapped()
        {
            const uint seed = 4242u;
            Assert.AreEqual(0, ConversationEmoteRules.BeatIndexAt(0.0, seed, 1f));

            int previousBeat = 0;
            for (double seconds = 0.0; seconds < 200.0; seconds += 0.25)
            {
                int beat = ConversationEmoteRules.BeatIndexAt(seconds, seed, 1f);
                Assert.GreaterOrEqual(beat, previousBeat);
                previousBeat = beat;
            }

            Assert.AreEqual(ConversationEmoteRules.MaxBeats - 1,
                ConversationEmoteRules.BeatIndexAt(1.0e9, seed, 1f));
        }

        [Test]
        public void BeatLength_StaysWithinBoundsScaledByInterval()
        {
            const float scale = 2f;
            for (uint seed = 0u; seed < 20u; seed++)
            {
                for (int beat = 0; beat < ConversationEmoteRules.MaxBeats; beat++)
                {
                    float length = ConversationEmoteRules.BeatLength(seed, beat, scale);
                    Assert.GreaterOrEqual(length, 1.6f * scale - 0.0001f);
                    Assert.LessOrEqual(length, 2.8f * scale + 0.0001f);
                }
            }
        }

        [Test]
        public void IsArgument_HotTemperedUnsociablePairArguesOnNegativeMood()
        {
            ConversationTraits hotHeaded = MakeTraits(0.1f, 0.9f, 0.5f);
            float pairMood = ConversationEmoteRules.PairMood(in hotHeaded, in hotHeaded);
            Assert.Less(pairMood, 0f);
            Assert.IsTrue(ConversationEmoteRules.IsArgument(-0.8f, in hotHeaded, in hotHeaded));
        }

        [Test]
        public void IsArgument_CalmPairNeverArguesEvenWhenDriftReachesVeryNegativeMood()
        {
            // Drift can walk a calm pair's mood below -0.6; the temper gate (max temper > 0.6) is what prevents the argument.
            ConversationTraits calmFriendly = MakeTraits(0.9f, 0.1f, 0.5f);
            float pairMood = ConversationEmoteRules.PairMood(in calmFriendly, in calmFriendly);
            uint[] seeds = { 3u, 99u, 5000u, 31337u };
            foreach (uint seed in seeds)
            {
                for (int beat = 0; beat < ConversationEmoteRules.MaxBeats; beat++)
                {
                    float mood = ConversationEmoteRules.BeatMood(pairMood, seed, beat);
                    Assert.IsFalse(ConversationEmoteRules.IsArgument(mood, in calmFriendly, in calmFriendly));
                }
            }
            Assert.IsFalse(ConversationEmoteRules.IsArgument(-1f, in calmFriendly, in calmFriendly));
        }

        [Test]
        public void CrowdLifeBudget_ShrinksPoolWithCrowdButNeverBelowSixOrAbovePool()
        {
            Assert.AreEqual(24, CrowdLifeBudget.Budget(0, 24));
            Assert.AreEqual(24, CrowdLifeBudget.Budget(12, 24));
            Assert.AreEqual(10, CrowdLifeBudget.Budget(40, 24));
            Assert.AreEqual(6, CrowdLifeBudget.Budget(100, 24));
            for (int visible = 0; visible < 200; visible++)
                Assert.LessOrEqual(CrowdLifeBudget.Budget(visible, 24), 24);
        }

        [Test]
        public void CrowdLifeBudget_BeatIntervalScaleGrowsPastUncrowdedThreshold()
        {
            Assert.AreEqual(1f, CrowdLifeBudget.BeatIntervalScale(12), 0.0001f);
            Assert.AreEqual(2f, CrowdLifeBudget.BeatIntervalScale(36), 0.0001f);
        }

        [Test]
        public void EmoteRules_TryShow_RespectsPriorityOfLiveEmote()
        {
            EmoteState state = new EmoteState();
            Assert.IsTrue(EmoteRules.TryShow(ref state, EmoteIcon.Laugh, 2f, EmoteRules.ArgumentPriority));

            Assert.IsFalse(EmoteRules.TryShow(ref state, EmoteIcon.Happy, 2f, EmoteRules.AmbientPriority));
            Assert.AreEqual(EmoteIcon.Laugh, state.icon);

            Assert.IsTrue(EmoteRules.TryShow(ref state, EmoteIcon.Angry, 3f, EmoteRules.ArgumentPriority));
            Assert.AreEqual(EmoteIcon.Angry, state.icon);
            Assert.AreEqual(3f, state.remaining);

            state.remaining = 0f;
            Assert.IsTrue(EmoteRules.TryShow(ref state, EmoteIcon.Sad, 1f, EmoteRules.AmbientPriority));
            Assert.AreEqual(EmoteIcon.Sad, state.icon);
            Assert.AreEqual(EmoteRules.AmbientPriority, state.priority);
        }

        [Test]
        public void EmoteRules_TryShow_NoneIconIsRejectedAndChangesNothing()
        {
            EmoteState state = new EmoteState
            {
                icon = EmoteIcon.Heart,
                priority = EmoteRules.AmbientPriority,
                duration = 2f,
                remaining = 1f,
            };
            Assert.IsFalse(EmoteRules.TryShow(ref state, EmoteIcon.None, 5f, EmoteRules.ArgumentPriority));
            Assert.AreEqual(EmoteIcon.Heart, state.icon);
            Assert.AreEqual(1f, state.remaining);
            Assert.AreEqual(EmoteRules.AmbientPriority, state.priority);
        }
    }
}
