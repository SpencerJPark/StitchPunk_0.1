using Unity.Entities;

// Global semantic "mood" of the world, escalating Explore → Tension → Combat. Append-only.
public enum WorldMoodState : byte
{
    Explore,
    Tension,
    Combat,
}

// Global world-mood singleton. Written idempotently through WorldMoodUtil (no write when the value
// is unchanged) so change-detection downstream is meaningful. Read by MusicStateSystem now and by
// NPC behaviours later.
public struct WorldMood : IComponentData
{
    public WorldMoodState state;
}
