using Unity.Entities;
using Unity.Mathematics;

public struct ConversationTraits
{
    public float sociability;
    public float temper;
    public float humour;
}

public static class PersonalityTraitLookup
{
    public static float Get(in DynamicBuffer<PersonalityAttributes> traits, PersonalityTypes trait)
    {
        for (int index = 0; index < traits.Length; index++)
        {
            if (traits[index].personality == trait)
                return traits[index].value;
        }
        return 0.5f;
    }

    public static ConversationTraits ConversationTraitsOf(in DynamicBuffer<PersonalityAttributes> traits)
    {
        return new ConversationTraits
        {
            sociability = Get(in traits, PersonalityTypes.Friendly),
            temper = Get(in traits, PersonalityTypes.Anger),
            humour = Get(in traits, PersonalityTypes.Comedy),
        };
    }
}

// Both sides of a pair must compute identical values, so everything here is a pure function of pairSeed.
public static class ConversationEmoteRules
{
    public const int MaxBeats = 64;
    const int MoodDriftSalt = 1000;

    public static uint Hash(uint pairSeed, int beatIndex)
    {
        return math.hash(new uint2(pairSeed, (uint)beatIndex));
    }

    public static float Hash01(uint pairSeed, int beatIndex)
    {
        return (Hash(pairSeed, beatIndex) & 0xFFFFFF) / (float)0x1000000;
    }

    public static float PairMood(in ConversationTraits traitsA, in ConversationTraits traitsB)
    {
        float mood = 0.6f * (traitsA.sociability * traitsB.sociability)
                   - 0.5f * math.max(traitsA.temper, traitsB.temper)
                   + 0.2f * (traitsA.humour + traitsB.humour) * 0.5f;
        return math.clamp(mood, -1f, 1f);
    }

    public static float BeatMood(float pairMood, uint pairSeed, int beatIndex)
    {
        float mood = pairMood;
        for (int beat = 0; beat <= beatIndex; beat++)
            mood += Hash01(pairSeed, beat + MoodDriftSalt) * 0.3f - 0.15f;
        return math.clamp(mood, -1f, 1f);
    }

    public static float BeatLength(uint pairSeed, int beatIndex, float beatIntervalScale)
    {
        return math.lerp(1.6f, 2.8f, Hash01(pairSeed, beatIndex)) * beatIntervalScale;
    }

    public static int BeatIndexAt(double secondsSinceStart, uint pairSeed, float beatIntervalScale)
    {
        if (secondsSinceStart <= 0.0)
            return 0;

        double runningTotal = 0.0;
        for (int beat = 0; beat < MaxBeats - 1; beat++)
        {
            runningTotal += BeatLength(pairSeed, beat, beatIntervalScale);
            if (runningTotal > secondsSinceStart)
                return beat;
        }
        return MaxBeats - 1;
    }

    public static bool IsSpeaker(bool isLowerIndexOfPair, int beatIndex)
    {
        return isLowerIndexOfPair == (beatIndex % 2 == 0);
    }

    public static EmoteIcon PickEmote(float mood, in ConversationTraits speaker, float intoxication, uint hash)
    {
        float roll = (hash & 0xFFFF) / 65536f;

        if (intoxication > 0.3f && roll < 0.25f)
            return EmoteIcon.Drunk;

        if (mood > 0.5f)
        {
            if (roll < 0.3f + 0.5f * speaker.humour)
                return EmoteIcon.Laugh;
            return roll < 0.85f ? EmoteIcon.Heart : EmoteIcon.Happy;
        }

        if (mood >= -0.2f)
        {
            if (speaker.humour > 0.7f && roll < 0.2f)
                return EmoteIcon.Laugh;
            if (roll < 0.25f) return EmoteIcon.Happy;
            if (roll < 0.5f) return EmoteIcon.Question;
            if (roll < 0.75f) return EmoteIcon.Idea;
            return EmoteIcon.Exclaim;
        }

        if (speaker.temper > 0.5f || roll < 0.5f)
            return EmoteIcon.Angry;
        return EmoteIcon.Sad;
    }

    public static EmoteIcon PickReaction(float mood, in ConversationTraits listener, uint hash)
    {
        float roll = (hash & 0xFFFF) / 65536f;
        if (roll < 0.5f)
            return EmoteIcon.None;

        if (mood > 0.3f)
            return listener.humour > 0.6f ? EmoteIcon.Laugh : EmoteIcon.Happy;
        if (mood < -0.3f)
            return listener.temper < 0.3f ? EmoteIcon.Sweat : EmoteIcon.Angry;
        return EmoteIcon.Question;
    }

    public static bool IsArgument(float mood, in ConversationTraits traitsA, in ConversationTraits traitsB)
    {
        return mood < -0.6f && math.max(traitsA.temper, traitsB.temper) > 0.6f;
    }
}
