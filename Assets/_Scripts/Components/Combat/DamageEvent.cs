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
    // RagdollLaunchInitSystem. Works even when sourceEntity is Null (thrown items / hazards).
    // The ragdoll launch direction comes from sourcePosition (below), which every producer sets.
    public float ragdollForce;           // scales ragdoll violence. 1 = baseline
    public float launchForceY;           // upward launch velocity. 0 = no arc (character just tips over)
    public float launchForceX;           // horizontal launch velocity, away from sourcePosition
    public float flailIntensity;         // joint flail scale. 0 = baseline (treated as 1 at init)
    public float spin;                   // deg/s airborne tumble. 0 = none
    public float restitution;            // bounce energy kept. 0 = use RagdollSimConfig default

    // Set by EVERY producer: the world position of whatever caused the hit (attacker, thrown item,
    // hazard, AOE origin). Drives both AOE expansion and the ragdoll launch direction.
    public DamageBehaviour damageBehaviour;
    public float3          sourcePosition;
    public float           range;            // AOE radius
}
