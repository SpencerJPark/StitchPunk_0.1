using Unity.Entities;

// Currently shown emote on a unit; higher priority replaces lower.
public struct EmoteState : IComponentData
{
    public EmoteIcon icon;
    public float remaining;
    public float duration;
    public byte priority;
}

// Set by ConversationSystem while two units Talk to each other; both sides derive the same beats from startTime + pairSeed of the lower-index entity.
public struct Conversation : IComponentData, IEnableableComponent
{
    public Entity partner;
    public double startTime;
    public uint pairSeed;
    public int lastBeatHandled;
}

// 0..1, decays over time; tiers in LivingHumans LH-D13.
public struct Intoxication : IComponentData, IEnableableComponent
{
    public float level;
    public float baseMoveSpeed;
    public float brawlCooldown;
    public float nextDrunkEmoteIn;
    // DrunkStumbleSystem removes last frame's wobble before adding this frame's, so the offset never accumulates on a target the follower didn't rewrite.
    public Unity.Mathematics.float3 appliedWobble;
}

// Singleton written by EmotePresentationSystem: how crowded the view is, read to thin emotes.
public struct CrowdLifeLod : IComponentData
{
    public int visibleHumans;
    public int emoteBudget;
    public float beatIntervalScale;
}

// Per-unit seed for deterministic personality rolls.
public struct PersonalitySeed : IComponentData
{
    public uint value;
}

// One pooled emote quad; owner = unit currently shown.
public struct EmotePoolSlot : IComponentData
{
    public int slotIndex;
    public float shownFor;
    public Entity owner;
}

// Singleton config for the emote quad pool and its view-distance cull.
public struct EmotePoolConfig : IComponentData
{
    public int poolSize;
    public float viewDistance;
    public float headHeight;
    public int firstIconMaterialIndex;
}
