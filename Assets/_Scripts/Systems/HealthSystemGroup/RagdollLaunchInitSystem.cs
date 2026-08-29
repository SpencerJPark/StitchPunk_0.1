using DotsAnimationToolkit;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Detects freshly dead units and drops the toolkit ragdoll. Reads Health.kill* (captured by
/// DamageEventSystem on the lethal DamageEvent) to build the RagdollLaunch impulse, then enables
/// RagdollActor to start the drop — the toolkit's own solver takes it from there (Planar2D falls in
/// whatever frame BillboardResolveSystem resolved this frame; see Documentation~/ragdoll.md).
/// Replaces Ragdoll2DInitSystem's bespoke pendulum-flail: there is no per-joint landing-zone concept
/// to seed any more — hinge ranges are authored once on the rig, not rolled per kill.
/// </summary>
/// <remarks>
/// worldPoint approximates the hit location as the unit's own position — this game tracks no precise
/// hit-location, only the kill source's position (used for launch direction). worldTorque approximates
/// the legacy "spin" (an in-plane tumble) as a torque about world up. Both are reasonable starting
/// points, not verified against the real solver — tune visually once a rig with authored ragdoll
/// bodies exists to play-test against.
/// </remarks>
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateAfter(typeof(DeathSystem))]
public partial struct RagdollLaunchInitSystem : ISystem
{
    private ComponentLookup<RagdollActor> ragdollActorLookup;
    private ComponentLookup<RagdollLaunch> ragdollLaunchLookup;

    public void OnCreate(ref SystemState state)
    {
        ragdollActorLookup  = state.GetComponentLookup<RagdollActor>(false);
        ragdollLaunchLookup = state.GetComponentLookup<RagdollLaunch>(false);
    }

    public void OnUpdate(ref SystemState state)
    {
        ragdollActorLookup.Update(ref state);
        ragdollLaunchLookup.Update(ref state);

        foreach ((RefRO<Health> health, RefRO<LocalTransform> transform, Entity entity) in
            SystemAPI.Query<RefRO<Health>, RefRO<LocalTransform>>()
                .WithAll<Dead>()
                .WithPresent<RagdollActor>()
                .WithEntityAccess())
        {
            if (!ragdollActorLookup.HasComponent(entity)) continue;

            // Skip if already ragdolling (Dead stays enabled until revived).
            if (ragdollActorLookup.IsComponentEnabled(entity)) continue;

            float3 unitPosition = transform.ValueRO.Position;
            float2 horizontalDelta = new float2(
                unitPosition.x - health.ValueRO.killSourcePosition.x,
                unitPosition.z - health.ValueRO.killSourcePosition.z);
            float horizontalDistance = math.length(horizontalDelta);
            float2 horizontalDirection = horizontalDistance > 1e-4f
                ? horizontalDelta / horizontalDistance
                : new float2(1f, 0f);

            float ragdollForce = math.max(0.1f, health.ValueRO.killRagdollForce);
            float3 worldImpulse = new float3(
                horizontalDirection.x * health.ValueRO.killLaunchForceX,
                health.ValueRO.killLaunchForceY,
                horizontalDirection.y * health.ValueRO.killLaunchForceX) * ragdollForce;

            if (ragdollLaunchLookup.HasComponent(entity))
            {
                ragdollLaunchLookup.GetRefRW(entity).ValueRW = new RagdollLaunch
                {
                    worldImpulse = worldImpulse,
                    worldPoint   = unitPosition,
                    worldTorque  = new float3(0f, health.ValueRO.killSpin, 0f) * ragdollForce,
                };
                ragdollLaunchLookup.SetComponentEnabled(entity, true);
            }

            ragdollActorLookup.SetComponentEnabled(entity, true);
        }
    }
}
