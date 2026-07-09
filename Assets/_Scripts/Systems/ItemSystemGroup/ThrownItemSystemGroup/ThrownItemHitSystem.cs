using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;

/// <summary>
/// While an item is in flight, checks each frame whether any entity with Health is within
/// hitRadius of the item's position. No physics collider required on the target — works with
/// quad-based characters that have no PhysicsCollider.
/// On hit: Enqueues a source-agnostic DamageEvent (sourceEntity = Null, damageSource = Throw) into
/// the recycled DamageBus and stops the item. This group runs earlier in the frame than
/// CombatSystemGroup, and this is a synchronous main-thread write (no scheduled job), so the value
/// is already in the raw queue before any parallel producer — DamageResolutionSystem + the consumer
/// apply it the same frame with no handle registration needed.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(ThrownItemSystemGroup))]
[UpdateAfter(typeof(ThrownItemSystem))]
public partial struct ThrownItemHitSystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<ItemLibrary>();
        state.RequireForUpdate<DamageBus>();
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);

        BlobAssetReference<ItemLibraryBlob> itemLibrary = SystemAPI.GetSingleton<ItemLibrary>().library;

        // Recycled DamageBus (v2) — thrown-item hits Enqueue DamageEvent values into the same raw
        // queue melee uses. Direct main-thread Enqueue: no other raw writer runs before ItemSystemGroup.
        NativeQueue<DamageEvent> damageQueue = SystemAPI.GetSingleton<DamageBus>().raw;

        // Collect all hittable entities (have Health and a position) into a temp list so the inner
        // loop can be a simple distance check. "Hittable" == has Health (the Hurt buffer is gone).
        NativeList<TargetData> targets = new NativeList<TargetData>(64, state.WorldUpdateAllocator);

        foreach ((RefRO<LocalTransform> transform, Entity entity) in
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

        foreach ((RefRO<LocalTransform> transform,
                  RefRO<ThrownItemRequest> thrownItem,
                  RefRO<Item> item,
                  EnabledRefRW<ThrownItemRequest> thrownEnabled,
                  EnabledRefRW<PlayerInteractable> interactableEnabled) in
            SystemAPI.Query<
                RefRO<LocalTransform>,
                RefRO<ThrownItemRequest>,
                RefRO<Item>,
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

            ref ItemBlob itemBlob = ref itemLibrary.Value.items[(int)item.ValueRO.itemType];

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

                damageQueue.Enqueue(new DamageEvent
                {
                    targetEntity    = t.entity,
                    sourceEntity    = Entity.Null,
                    damageSource    = DamageSource.Throw,
                    damageAmount    = itemBlob.throwDamage,
                    distance        = math.sqrt(distSq),
                    ragdollForce    = itemBlob.throwRagdollForce,
                    launchForceY    = itemBlob.throwLaunchForceY,
                    launchForceX    = itemBlob.throwLaunchForceX,
                    damageBehaviour = DamageBehaviour.SinlgeTarget,
                    sourcePosition  = itemPos,
                    range           = 0f,
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
