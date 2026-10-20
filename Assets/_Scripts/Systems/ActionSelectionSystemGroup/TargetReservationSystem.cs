using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Arbitrates exclusive targets after WinnerSelection commits and reverts losers before execution.
// First claim in iteration order wins; player orders (int.MaxValue priority) are always accepted.
[BurstCompile]
[UpdateInGroup(typeof(ActionSelectionSystemGroup), OrderLast = true)]
[UpdateAfter(typeof(WinnerSelectionSystem))]
public partial struct TargetReservationSystem : ISystem
{
    ComponentLookup<Interaction> interactionLookup;
    ComponentLookup<Item>        itemLookup;
    ComponentLookup<ThinkRate>   thinkRateLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TargetOccupancy>();
        state.RequireForUpdate<InteractionLibrary>();
        interactionLookup = state.GetComponentLookup<Interaction>(true);
        itemLookup        = state.GetComponentLookup<Item>(true);
        thinkRateLookup   = state.GetComponentLookup<ThinkRate>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        interactionLookup.Update(ref state);
        itemLookup.Update(ref state);
        thinkRateLookup.Update(ref state);

        RefRW<TargetOccupancy> occupancy = SystemAPI.GetSingletonRW<TargetOccupancy>();
        InteractionLibrary interactionLibrary = SystemAPI.GetSingleton<InteractionLibrary>();
        if (!interactionLibrary.library.IsCreated) return;

        bool loggingEnabled = !SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg)
            || (loggingCfg.EnabledCategories & (int)LogCategory.AI) != 0;
        EntityCommandBuffer ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);

        // Single-threaded on purpose: claims must be decided in one order.
        state.Dependency = new TargetReservationJob
        {
            occupantsByTarget  = occupancy.ValueRW.occupantsByTarget,
            interactionLookup  = interactionLookup,
            itemLookup         = itemLookup,
            thinkRateLookup    = thinkRateLookup,
            interactionLibrary = interactionLibrary.library,
            ecb                = ecb,
            timestamp          = SystemAPI.Time.ElapsedTime,
            loggingEnabled     = loggingEnabled,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithPresent(typeof(UtilityBrain))]
[WithDisabled(typeof(Dead))]
[WithDisabled(typeof(CutsceneActor))]
public partial struct TargetReservationJob : IJobEntity
{
    public NativeParallelMultiHashMap<Entity, Entity> occupantsByTarget;
    [ReadOnly] public ComponentLookup<Interaction>    interactionLookup;
    [ReadOnly] public ComponentLookup<Item>           itemLookup;
    public ComponentLookup<ThinkRate>                 thinkRateLookup;
    [ReadOnly] public BlobAssetReference<InteractionLibraryBlob> interactionLibrary;
    public EntityCommandBuffer ecb;
    public double timestamp;
    public bool loggingEnabled;

    public void Execute(Entity unit, ref StateMachine stateMachine)
    {
        if (stateMachine.activeBehavior != BehaviorType.None && stateMachine.targetEntity != Entity.Null)
        {
            if (!TryClaim(unit, stateMachine.targetEntity, stateMachine.activePriority, stateMachine.action))
            {
                StateMachineTransitions.ResetToIdle(ref stateMachine);
                ForceRethink(unit);
            }
        }

        if (stateMachine.pendingBehavior != BehaviorType.None && stateMachine.pendingTarget != Entity.Null)
        {
            if (!TryClaim(unit, stateMachine.pendingTarget, stateMachine.pendingPriority, stateMachine.pendingAction))
            {
                StateMachineTransitions.ClearPending(ref stateMachine);
                ForceRethink(unit);
            }
        }
    }

    bool TryClaim(Entity unit, Entity target, int priority, ActionType unitAction)
    {
        int capacity = TargetReservationUtils.GetCapacity(
            target, unitAction, in interactionLookup, in itemLookup, ref interactionLibrary.Value);
        if (capacity == TargetReservationUtils.UnlimitedCapacity) return true;
        if (TargetReservationUtils.IsOccupiedBy(in occupantsByTarget, target, unit)) return true;

        bool isPlayerOrder = priority == int.MaxValue;
        if (isPlayerOrder
            || TargetReservationUtils.CountOccupantsOtherThan(in occupantsByTarget, target, unit) < capacity)
        {
            occupantsByTarget.Add(target, unit);
            return true;
        }

        if (loggingEnabled)
            LogUtil.Log(ref ecb,
                $"[TargetReservation] Entity {unit.Index} lost {target.Index} to an occupant (capacity {capacity:D})",
                LogLevel.Info, timestamp, category: LogCategory.AI);
        return false;
    }

    void ForceRethink(Entity unit)
    {
        if (!thinkRateLookup.HasComponent(unit)) return;
        ThinkRate rate = thinkRateLookup[unit];
        rate.timeUntilNextThink = 0f;
        thinkRateLookup[unit] = rate;
    }
}
