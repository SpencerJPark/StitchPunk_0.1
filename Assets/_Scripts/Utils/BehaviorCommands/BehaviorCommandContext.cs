using DotsAnimationToolkit;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Bundles the per-entity ECS lookups, blob refs, and ECB/timing state that BehaviorCommandType
// handlers (Utils/BehaviorCommands/*) need. Built once per job in OnUpdate and copied per unit
// (only entityIndex set), then threaded through by ref — the lookup structs are pointer handles, so writes through them land in
// ECS data regardless of how this wrapper is passed; ref just avoids copying the whole bundle.
public struct BehaviorCommandContext
{
    // Read-only lookups.
    [ReadOnly] public ComponentLookup<LocalTransform>          transformLookup;
    [ReadOnly] public ComponentLookup<UnitEquip>               unitEquipLookup;
    [ReadOnly] public ComponentLookup<NavigationWaypoint>      waypointLookup;
    [ReadOnly] public ComponentLookup<Dead>                    deadLookup;
    [ReadOnly] public BufferLookup<Motivation>                 motivationLookup;
    [ReadOnly] public ComponentLookup<SocialInvite>            socialInviteLookup;
    // Aliases the StateMachine the job writes by ref. Safe: qualifier checks only read OTHER units' StateMachine, and each unit writes only its own.
    [ReadOnly] [NativeDisableContainerSafetyRestriction] public ComponentLookup<StateMachine>            stateMachineLookup;
    [ReadOnly] public ComponentLookup<UnitFacing>              unitFacingLookup;
    [ReadOnly] public NativeParallelMultiHashMap<int2, Entity> waypointCells;
    [ReadOnly] public BlobAssetReference<UnitLibraryBlob>      unitLibrary;
    [ReadOnly] public BufferLookup<AnimEventOutput>            animEventOutputLookup;
    [ReadOnly] public ComponentLookup<AnimEventsPending>       animEventsPendingLookup;
    [ReadOnly] public ComponentLookup<Item>                    itemLookup;
    [ReadOnly] public BlobAssetReference<ItemLibraryBlob>      itemLibrary; // may be uncreated - check IsCreated

    // Write lookups — each entity is owned by at most one executing behavior at a time, so no two
    // handler calls ever write the same entity's data (matches the job's own field annotations).
    [NativeDisableParallelForRestriction] public ComponentLookup<AttackRequest>           attackRequestLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<RangedAim>               rangedAimLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<PickupRequest>           pickupRequestLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<EquipBy>                 equipByLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<AttachedTo>              attachedToLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<AttachItemRequest>       attachItemRequestLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<AnimationCommandPending> animationCommandPendingLookup;
    [NativeDisableParallelForRestriction] public BufferLookup<AnimationCommand>           animationCommandLookup;

    public EntityCommandBuffer.ParallelWriter ecb;
    public int    entityIndex;
    public float  deltaTime;
    public double timestamp;
    public bool   loggingEnabled;
}
