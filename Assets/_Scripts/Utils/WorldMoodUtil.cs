using Unity.Entities;

// Idempotent setter for the WorldMood singleton: callers pass the desired state and it writes only
// when the value actually changes. Reusable by any system (music now, NPC behaviours later).
public static class WorldMoodUtil
{
    // Apply against an already-fetched component (Burst-friendly: use SystemAPI.GetSingletonRW<WorldMood>()).
    // Returns true if the value changed.
    public static bool Set(ref WorldMood mood, WorldMoodState desired)
    {
        if (mood.state == desired)
            return false;

        mood.state = desired;
        return true;
    }

    // Convenience: fetch the WorldMood singleton from the system's world and apply idempotently.
    // Returns true if the value changed (no structural change; no write when unchanged).
    public static bool Set(ref SystemState state, WorldMoodState desired)
    {
        EntityQuery query = state.GetEntityQuery(ComponentType.ReadWrite<WorldMood>());
        if (query.IsEmptyIgnoreFilter)
            return false;

        WorldMood mood = query.GetSingleton<WorldMood>();
        if (mood.state == desired)
            return false;

        mood.state = desired;
        query.SetSingleton(mood);
        return true;
    }
}
