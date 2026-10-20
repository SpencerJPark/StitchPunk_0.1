using Unity.Mathematics;

public static class CrowdLifeBudget
{
    public const int UncrowdedVisibleHumans = 12;

    public static int Budget(int visibleHumans, int poolSize)
    {
        int safePool = math.max(0, poolSize);
        if (visibleHumans <= UncrowdedVisibleHumans)
            return safePool;
        int reduced = math.max(6, safePool - (visibleHumans - UncrowdedVisibleHumans) / 2);
        return math.min(reduced, safePool);
    }

    public static float BeatIntervalScale(int visibleHumans)
    {
        return 1f + math.max(0, visibleHumans - UncrowdedVisibleHumans) / 24f;
    }
}

public static class EmoteRules
{
    public const byte AmbientPriority = 1;
    public const byte ConversationPriority = 2;
    public const byte ArgumentPriority = 3;

    public static bool TryShow(ref EmoteState state, EmoteIcon icon, float seconds, byte priority)
    {
        if (icon == EmoteIcon.None)
            return false;
        if (state.icon != EmoteIcon.None && state.remaining > 0f && priority < state.priority)
            return false;

        state.icon = icon;
        state.priority = priority;
        state.duration = seconds;
        state.remaining = seconds;
        return true;
    }
}
