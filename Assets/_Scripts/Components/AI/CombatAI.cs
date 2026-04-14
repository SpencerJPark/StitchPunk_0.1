using Unity.Collections;
using Unity.Entities;

// ─── Faction ──────────────────────────────────────────────────────────────────
// Baked onto any unit entity by FactionAuthoring.
// Drives FactionRegistrySystem and combat target selection.
public struct Faction : IComponentData
{
    public FactionType factionType;
}

// ─── ThreatEntry ──────────────────────────────────────────────────────────────
// Buffer on any unit entity that can be attacked.
// ThreatUpdateSystem populates this from the Hurt buffer each frame before
// DamageApplicationSystem clears it. ChaseScoringSystem reads it to bias
// target selection toward attackers.
public struct ThreatEntry : IBufferElementData
{
    public Entity attackerEntity;
    public float threatScore;       // cumulative damage received from this attacker
}

// ─── CombatTarget ─────────────────────────────────────────────────────────────
// On the unit entity, baked disabled by BrainAuthoring.
// Disabled = no current target (ChaseScoringSystem is looking)
// Enabled  = actively pursuing targetEntity (AttackExecutionSystem is executing)
public struct CombatTarget : IComponentData, IEnableableComponent
{
    public Entity targetEntity;
    public float retargetTimer;    // counts down; 0 → re-evaluate target
}

// ─── FactionRegistry ──────────────────────────────────────────────────────────
// Singleton rebuilt every frame by FactionRegistrySystem (AIAwarenessSystemGroup).
// Keyed by faction byte, values are unit entities with that Faction.
// Scoring systems query this to find hostile targets without iterating all entities.
public struct FactionRegistry : IComponentData
{
    public NativeParallelMultiHashMap<byte, Entity> entities;
}
