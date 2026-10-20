// Rebuilds the TargetOccupancy singleton each frame: reservable target -> units whose StateMachine (active or pending) targets it.
// Reservations are derived from this map, so nothing ever has to release one.
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(UtilityAwarenessSystemGroup), OrderFirst = true)]
public partial struct TargetOccupancySystem : ISystem
{
    private ComponentLookup<Interaction> interactionLookup;
    private ComponentLookup<Item>        itemLookup;
    private EntityQuery                  brainUnitQuery;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<InteractionLibrary>();
        state.EntityManager.CreateSingleton(new TargetOccupancy
        {
            occupantsByTarget = new NativeParallelMultiHashMap<Entity, Entity>(256, Allocator.Persistent),
        });

        interactionLookup = state.GetComponentLookup<Interaction>(true);
        itemLookup        = state.GetComponentLookup<Item>(true);
        brainUnitQuery    = SystemAPI.QueryBuilder().WithAll<StateMachine>().WithPresent<UtilityBrain>().Build();
    }

    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.TryGetSingleton<TargetOccupancy>(out TargetOccupancy singleton))
        {
            if (singleton.occupantsByTarget.IsCreated) singleton.occupantsByTarget.Dispose();
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        InteractionLibrary interactionLib = SystemAPI.GetSingleton<InteractionLibrary>();
        if (!interactionLib.library.IsCreated) return;

        interactionLookup.Update(ref state);
        itemLookup.Update(ref state);

        RefRW<TargetOccupancy> occupancy = SystemAPI.GetSingletonRW<TargetOccupancy>();
        NativeParallelMultiHashMap<Entity, Entity> occupantsByTarget = occupancy.ValueRO.occupantsByTarget;
        int requiredCapacity = math.max(256, brainUnitQuery.CalculateEntityCountWithoutFiltering() * 2);

        JobHandle clearHandle = new ClearTargetOccupancyJob
        {
            occupantsByTarget = occupantsByTarget,
            requiredCapacity  = requiredCapacity,
        }.Schedule(state.Dependency);

        state.Dependency = new RecordTargetOccupantsJob
        {
            occupantsWriter    = occupantsByTarget.AsParallelWriter(),
            interactionLookup  = interactionLookup,
            itemLookup         = itemLookup,
            interactionLibrary = interactionLib.library,
        }.ScheduleParallel(clearHandle);
    }

    [BurstCompile]
    private struct ClearTargetOccupancyJob : IJob
    {
        public NativeParallelMultiHashMap<Entity, Entity> occupantsByTarget;
        public int requiredCapacity;

        public void Execute()
        {
            if (occupantsByTarget.Capacity < requiredCapacity) occupantsByTarget.Capacity = requiredCapacity;
            occupantsByTarget.Clear();
        }
    }

    [BurstCompile]
    [WithPresent(typeof(UtilityBrain))]
    [WithDisabled(typeof(Dead))]
    private partial struct RecordTargetOccupantsJob : IJobEntity
    {
        public NativeParallelMultiHashMap<Entity, Entity>.ParallelWriter occupantsWriter;
        [ReadOnly] public ComponentLookup<Interaction> interactionLookup;
        [ReadOnly] public ComponentLookup<Item>        itemLookup;
        [ReadOnly] public BlobAssetReference<InteractionLibraryBlob> interactionLibrary;

        private void Execute(Entity unit, in StateMachine stateMachine)
        {
            if (stateMachine.activeBehavior != BehaviorType.None
                && stateMachine.targetEntity != Entity.Null
                && IsReservable(stateMachine.targetEntity, stateMachine.action))
            {
                occupantsWriter.Add(stateMachine.targetEntity, unit);
            }

            if (stateMachine.pendingBehavior != BehaviorType.None
                && stateMachine.pendingTarget != Entity.Null
                && stateMachine.pendingTarget != stateMachine.targetEntity
                && IsReservable(stateMachine.pendingTarget, stateMachine.pendingAction))
            {
                occupantsWriter.Add(stateMachine.pendingTarget, unit);
            }
        }

        private bool IsReservable(Entity target, ActionType unitAction)
        {
            int capacity = TargetReservationUtils.GetCapacity(target, unitAction, in interactionLookup, in itemLookup, ref interactionLibrary.Value);
            return capacity != TargetReservationUtils.UnlimitedCapacity;
        }
    }
}
