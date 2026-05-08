using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;

/// <summary>
/// While an item is in flight, checks each frame whether any entity with a Hurt buffer
/// is within hitRadius of the item's position. No physics collider required on the target —
/// works with quad-based characters that have no PhysicsCollider.
/// On hit: applies throwDamage and stops the item.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(ItemSystemGroup))]
[UpdateAfter(typeof(ThrownItemSystem))]
public partial struct ThrownItemHitSystem : ISystem
{
    private BufferLookup<Hurt> hurtBufferLookup;
    private ComponentLookup<LocalTransform> transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        hurtBufferLookup  = state.GetBufferLookup<Hurt>(false);
        transformLookup   = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        hurtBufferLookup.Update(ref state);
        transformLookup.Update(ref state);

        // Collect all hittable entities (have both Hurt buffer and a position) into a temp list
        // so the inner loop can be a simple distance check.
        var targets = new NativeList<TargetData>(64, state.WorldUpdateAllocator);

        foreach (var (transform, entity) in
            SystemAPI.Query<RefRO<LocalTransform>>()
                .WithAll<Health>()
                .WithEntityAccess())
        {
            targets.Add(new TargetData
            {
                entity   = entity,
                position = transform.ValueRO.Position
            });
        }

        if (targets.Length == 0)
            return;

        foreach (var (transform, thrownItem, thrownEnabled, interactableEnabled) in
            SystemAPI.Query<
                RefRO<LocalTransform>,
                RefRO<ThrownItemRequest>,
                EnabledRefRW<ThrownItemRequest>,
                EnabledRefRW<PlayerInteractable>>()
            .WithPresent<PlayerInteractable>())
        {
            float3 itemPos = transform.ValueRO.Position;
            const float hitRadius = 0.6f;
            float hitRadiusSq = hitRadius * hitRadius;

            // Skip hit detection until item has traveled past nearby units.
            // Prevents standing next to a body from immediately blocking the throw.
            // Walls are not entities with Health so they are unaffected.
            const float minTravelDist = 1.2f;
            float3 originDelta = itemPos - thrownItem.ValueRO.throwOrigin;
            float travelDistSq = originDelta.x * originDelta.x + originDelta.z * originDelta.z;
            if (travelDistSq < minTravelDist * minTravelDist)
                continue;

            for (int i = 0; i < targets.Length; i++)
            {
                TargetData t = targets[i];

                if (t.entity == thrownItem.ValueRO.thrower)
                    continue;

                // 2.5D — ignore Y difference so height arc doesn't matter
                float dx = itemPos.x - t.position.x;
                float dz = itemPos.z - t.position.z;
                float distSq = dx * dx + dz * dz;

                if (distSq > hitRadiusSq)
                    continue;

                if (!hurtBufferLookup.HasBuffer(t.entity))
                    continue;

                hurtBufferLookup[t.entity].Add(new Hurt
                {
                    attackerEntity = Entity.Null,
                    attackType     = AttackType.Throw,
                    distance       = math.sqrt(distSq),
                    damageAmount   = thrownItem.ValueRO.throwDamage,
                    hitSourceX     = itemPos.x,
                    ragdollForce   = thrownItem.ValueRO.ragdollForce,
                    launchForceY   = thrownItem.ValueRO.launchForceY,
                    launchForceX   = thrownItem.ValueRO.launchForceX
                });

                thrownEnabled.ValueRW       = false;
                interactableEnabled.ValueRW = true;
                break;
            }
        }
    }

    private struct TargetData
    {
        public Entity entity;
        public float3 position;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
