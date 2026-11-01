using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;

/// <summary>
/// While an item is in flight, sweeps its last-frame path against every living, non-dodging entity
/// with Health using the shared ProjectileHitUtils test (friendly fire is on). No physics collider
/// required on the target.
/// On hit: Enqueues a source-agnostic DamageEvent (sourceEntity = thrower, damageSource = Throw) into
/// the recycled DamageBus and stops the item. This group runs earlier in the frame than
/// CombatSystemGroup, and this is a synchronous main-thread write (no scheduled job), so the value
/// is already in the raw queue before any parallel producer - DamageResolutionSystem + the consumer
/// apply it the same frame with no handle registration needed.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(ThrownItemSystemGroup))]
[UpdateAfter(typeof(ThrownItemSystem))]
public partial struct ThrownItemHitSystem : ISystem
{
    private const float THROWN_ITEM_RADIUS = 0.2f;

    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<Dead> deadLookup;
    private ComponentLookup<DodgeInvulnerable> dodgeInvulnerableLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<ItemLibrary>();
        state.RequireForUpdate<DamageBus>();
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        deadLookup = state.GetComponentLookup<Dead>(true);
        dodgeInvulnerableLookup = state.GetComponentLookup<DodgeInvulnerable>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        deadLookup.Update(ref state);
        dodgeInvulnerableLookup.Update(ref state);

        BlobAssetReference<ItemLibraryBlob> itemLibrary = SystemAPI.GetSingleton<ItemLibrary>().library;

        // Recycled DamageBus (v2) — thrown-item hits Enqueue DamageEvent values into the same raw
        // queue melee uses. Direct main-thread Enqueue: no other raw writer runs before ItemSystemGroup.
        NativeQueue<DamageEvent> damageQueue = SystemAPI.GetSingleton<DamageBus>().raw;

        // Hittable == has Health and is neither dead nor mid-dodge.
        NativeList<ProjectileHitCandidate> targets = new NativeList<ProjectileHitCandidate>(64, state.WorldUpdateAllocator);

        foreach ((RefRO<LocalTransform> transform, Entity entity) in
            SystemAPI.Query<RefRO<LocalTransform>>()
                .WithAll<Health>()
                .WithEntityAccess())
        {
            if (deadLookup.HasComponent(entity) && deadLookup.IsComponentEnabled(entity))
                continue;
            if (dodgeInvulnerableLookup.HasComponent(entity) && dodgeInvulnerableLookup.IsComponentEnabled(entity))
                continue;

            targets.Add(new ProjectileHitCandidate
            {
                entity   = entity,
                position = transform.ValueRO.Position
            });
        }

        if (targets.Length == 0)
            return;

        NativeArray<ProjectileHitCandidate> targetArray = targets.AsArray();
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach ((RefRO<LocalTransform> transform,
                  RefRO<ThrownItemRequest> thrownItem,
                  RefRO<Item> item,
                  EnabledRefRW<ThrownItemRequest> thrownItemRequestEnabled,
                  EnabledRefRW<PlayerInteractable> playerInteractableEnabled) in
            SystemAPI.Query<
                RefRO<LocalTransform>,
                RefRO<ThrownItemRequest>,
                RefRO<Item>,
                EnabledRefRW<ThrownItemRequest>,
                EnabledRefRW<PlayerInteractable>>()
            .WithPresent<PlayerInteractable>())
        {
            float3 itemPos = transform.ValueRO.Position;
            float3 horizontalVelocity = new float3(thrownItem.ValueRO.velocity.x, 0f, thrownItem.ValueRO.velocity.z);
            float3 previousPos = itemPos - horizontalVelocity * deltaTime;
            float3 throwOrigin = thrownItem.ValueRO.throwOrigin;
            Entity thrower = thrownItem.ValueRO.thrower;

            int hitIndex;
            float hitFraction;
            if (!ProjectileHitUtils.FindFirstHitAlongSegment(
                    in previousPos, in itemPos, THROWN_ITEM_RADIUS, thrower, in throwOrigin,
                    in targetArray, out hitIndex, out hitFraction))
                continue;

            ref ItemBlob itemBlob = ref itemLibrary.Value.items[(int)item.ValueRO.itemType];

            ProjectileHitCandidate target = targetArray[hitIndex];
            float3 hitPoint = math.lerp(previousPos, itemPos, hitFraction);
            float2 hitToTarget = hitPoint.xz - target.position.xz;

            damageQueue.Enqueue(new DamageEvent
            {
                targetEntity    = target.entity,
                sourceEntity    = thrower,
                damageSource    = DamageSource.Throw,
                damageAmount    = itemBlob.throwDamage,
                distance        = math.length(hitToTarget),
                ragdollForce    = itemBlob.throwRagdollForce,
                launchForceY    = itemBlob.throwLaunchForceY,
                launchForceX    = itemBlob.throwLaunchForceX,
                damageBehaviour = DamageBehaviour.SinlgeTarget,
                sourcePosition  = hitPoint,
                range           = 0f,
            });

            thrownItemRequestEnabled.ValueRW = false;
            playerInteractableEnabled.ValueRW = true;
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
