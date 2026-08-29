using DotsAnimationToolkit;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Bundles the per-entity ECS lookups, blob refs, and ECB/timing state that BehaviorCommandType
// handlers (Utils/BehaviorCommands/*) need. Built once per BehaviorExecutionJob.Execute call and
// threaded through by ref — the lookup structs are pointer handles, so writes through them land in
// ECS data regardless of how this wrapper is passed; ref just avoids copying the whole bundle.
public struct BehaviorCommandContext
{
    // Read-only lookups.
    public ComponentLookup<LocalTransform>          transformLookup;
    public ComponentLookup<UnitEquip>               unitEquipLookup;
    public ComponentLookup<NavigationWaypoint>      waypointLookup;
    public ComponentLookup<Dead>                    deadLookup;
    public BufferLookup<Motivation>                 motivationLookup;
    public ComponentLookup<SocialInvite>            socialInviteLookup;
    public ComponentLookup<StateMachine>            stateMachineLookup;
    public NativeParallelMultiHashMap<int2, Entity> waypointCells;
    public BlobAssetReference<UnitLibraryBlob>      unitLibrary;
    public BufferLookup<AnimEventOutput>            animEventOutputLookup;
    public ComponentLookup<AnimEventsPending>       animEventsPendingLookup;

    // Write lookups — each entity is owned by at most one executing behavior at a time, so no two
    // handler calls ever write the same entity's data (matches the job's own field annotations).
    public ComponentLookup<AttackRequest>           attackRequestLookup;
    public ComponentLookup<PickupRequest>           pickupRequestLookup;
    public ComponentLookup<EquipBy>                 equipByLookup;
    public ComponentLookup<AttachedTo>              attachedToLookup;
    public ComponentLookup<AttachItemRequest>       attachItemRequestLookup;
    public ComponentLookup<AnimationCommandPending> animationCommandPendingLookup;
    public BufferLookup<AnimationCommand>           animationCommandLookup;

    public EntityCommandBuffer.ParallelWriter ecb;
    public int    entityIndex;
    public float  deltaTime;
    public double timestamp;
    public bool   loggingEnabled;
}
