using Unity.Entities;

// A proximity damage zone (spikes, fire, etc.) — the v2 environmental / sourceless damage example.
// HazardZoneSystem enqueues a Hazard DamageEvent (sourceEntity = Null, so no threat) into the
// DamageBus for every alive unit within `radius`, throttled by a single whole-zone retrigger gate:
// the zone fires at most once per `retriggerInterval` seconds (lastTriggerTime stamped on each fire).
// Baked from HazardAuthoring; the zone's position comes from its LocalTransform.
public struct HazardZone : IComponentData
{
    public int          damageAmount;
    public DamageSource damageSource;        // Hazard (authored)
    public float        radius;              // XZ proximity radius
    public float        retriggerInterval;   // seconds between fires (whole-zone gate)
    public float        lastTriggerTime;     // ElapsedTime of the last fire; -inf so it fires immediately

    // Death-only knockback feel (captured into Health.kill* on a lethal hit). The DamageEvent's
    // sourcePosition is the zone's own position, so the unit tips/launches away from the hazard.
    // Flail/spin/restitution aren't authored per-hazard — the ragdoll uses its baseline defaults.
    public float ragdollForce;
    public float launchForceY;
    public float launchForceX;
}
