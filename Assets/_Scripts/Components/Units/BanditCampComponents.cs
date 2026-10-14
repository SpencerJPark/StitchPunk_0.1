using Unity.Entities;
using Unity.Mathematics;

// CampLoop Phase 5. A guard idles near its post, attacks intruders, and walks home when it has
// chased past the leash (GuardPostAwarenessSystem).
public struct GuardPost : IComponentData
{
    public Entity camp;
    public float3 anchorPosition;
    public float idleRadius;     // ~10 m: wanders inside this when calm
    public float leashRadius;    // ~25 m: beyond this it drops the chase and returns
}

// On the camp entity. BanditCampSystem counts living guards; at zero the camp is cleared for good.
public struct BanditCamp : IComponentData
{
    public int aliveGuards;
    public bool cleared;
}

// On loot nodes (ResourceNode) that only become haulable once their camp is cleared.
public struct CampLootLocked : IComponentData
{
    public Entity camp;
}

// On the player. Enabled = knocked out; at zero the player wakes at the workstation with full health.
public struct PlayerKnockedOut : IComponentData, IEnableableComponent
{
    public float remainingSeconds;
}
