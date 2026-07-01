using Unity.Entities;
using Unity.Mathematics;

// Source-agnostic damage signal (v2). No longer an IComponentData — it's a value queued in the
// DamageBus, not a component on an entity (so it never shows in the Entities window; use a debug
// counter/log instead). Producers Enqueue into DamageBus.raw; DamageResolutionSystem expands AOE
// into DamageBus.resolved; DamageEventSystem drains resolved and applies damage + threat + death.
//
// "source" replaces v1's attacker: sourceEntity is Entity.Null for environmental / sourceless
// damage (fall, spikes, burning, drowning) and damageSource spans attacks AND hazards.
public struct DamageEvent   // queued value, not a component
{
    public Entity       targetEntity;    // resolved victim (set at enqueue for SingleTarget, per-target after AOE expand)
    public Entity       sourceEntity;    // was attackerEntity. Entity.Null for environmental / sourceless
    public DamageSource damageSource;    // was attackType — attack OR hazard origin
    public int          damageAmount;
    public float        distance;        // for logging/effects

    // Death-only knockback — captured into Health.kill* on the lethal event, read by
    // Ragdoll2DInitSystem. Works even when sourceEntity is Null (thrown items / hazards).
    public float hitSourceX;             // world-X of the hit source -> ragdoll fall direction
    public float ragdollForce;           // scales ragdoll violence. 1 = baseline
    public float launchForceY;           // launch arc forces. 0 = no arc (character just tips over)
    public float launchForceX;

    // AOE — DamageResolutionSystem reads these when damageBehaviour == AreaOfEffect.
    public DamageBehaviour damageBehaviour;
    public float3          sourcePosition;   // AOE origin
    public float           range;            // AOE radius
}
