using DotsAnimationToolkit;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Detects freshly dead units and drops the toolkit ragdoll. Reads Health.kill* (captured by
/// DamageEventSystem on the lethal DamageEvent) to build the RagdollLaunch impulse, then enables
/// RagdollActor to start the drop — the toolkit's own solver takes it from there (Planar2D falls in
/// whatever frame BillboardResolveSystem resolved this frame; see Documentation~/ragdoll.md).
/// worldTorque is about the actor's own forward (the Planar2D plane normal), and worldImpulse is
/// scaled by the root body's mass so the SOs' launchForceX/Y read as velocities, not raw impulses.
/// </summary>
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateAfter(typeof(DeathSystem))]
public partial struct RagdollLaunchInitSystem : ISystem
{
    private ComponentLookup<RagdollActor> ragdollActorLookup;
    private ComponentLookup<RagdollLaunch> ragdollLaunchLookup;
    private BufferLookup<RagdollBody> ragdollBodyLookup;

    public void OnCreate(ref SystemState state)
    {
        ragdollActorLookup  = state.GetComponentLookup<RagdollActor>(false);
        ragdollLaunchLookup = state.GetComponentLookup<RagdollLaunch>(false);
        ragdollBodyLookup   = state.GetBufferLookup<RagdollBody>(true);
    }

    public void OnUpdate(ref SystemState state)
    {
        ragdollActorLookup.Update(ref state);
        ragdollLaunchLookup.Update(ref state);
        ragdollBodyLookup.Update(ref state);

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
            float3 launchVelocity = new float3(
                horizontalDirection.x * health.ValueRO.killLaunchForceX,
                health.ValueRO.killLaunchForceY,
                horizontalDirection.y * health.ValueRO.killLaunchForceX) * ragdollForce;

            // launchForceX/Y are authored as velocities; impulse = velocity / invMass so the root
            // body actually reaches that speed (RagdollSolver.ApplyLaunchImpulse: velocity += impulse * invMass).
            float3 worldImpulse = launchVelocity;
            if (ragdollBodyLookup.HasBuffer(entity))
            {
                DynamicBuffer<RagdollBody> ragdollBodies = ragdollBodyLookup[entity];
                if (ragdollBodies.Length > 0 && ragdollBodies[0].parameters.invMass > 0f)
                {
                    worldImpulse = launchVelocity / ragdollBodies[0].parameters.invMass;
                }
            }

            float3 worldTorque = math.mul(transform.ValueRO.Rotation, math.forward())
                * health.ValueRO.killSpin * ragdollForce;

            if (ragdollLaunchLookup.HasComponent(entity))
            {
                ragdollLaunchLookup.GetRefRW(entity).ValueRW = new RagdollLaunch
                {
                    worldImpulse = worldImpulse,
                    worldPoint   = unitPosition,
                    worldTorque  = worldTorque,
                };
                ragdollLaunchLookup.SetComponentEnabled(entity, true);
            }

            ragdollActorLookup.SetComponentEnabled(entity, true);
        }
    }
}
