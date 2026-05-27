using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

// Detects ActionInterruptRequest, disables the active action tag via the shared
// function pointer table, halts pathing, and returns the unit to the decision
// pipeline (ActionRequest). Intentionally narrow — context-specific cleanup
// (e.g. SitReleaseRequest) is the responsibility of whoever enables the interrupt.
//
// Register a new action type here whenever one is added to ActionSelectionSystem.
[BurstCompile]
[UpdateInGroup(typeof(ActionSelectionSystemGroup), OrderFirst = true)]
public partial struct ActionInterruptSystem : ISystem
{
    private NativeArray<FunctionPointer<ActionActivationDelegate>> _functionTable;
    private ComponentLookup<PlayerControlled>                      playerControlledLookup;
    private ComponentLookup<TalkAction>                            talkActionLookup;
    private ComponentLookup<Dead>                                  deadLookup;
    private BufferLookup<ActionOption>                             actionOptionLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        playerControlledLookup = state.GetComponentLookup<PlayerControlled>(true);
        talkActionLookup       = state.GetComponentLookup<TalkAction>(true);
        deadLookup             = state.GetComponentLookup<Dead>(true);
        actionOptionLookup     = state.GetBufferLookup<ActionOption>(true);

        int tableSize  = (int)ActionType.Spawn + 1;
        _functionTable = new NativeArray<FunctionPointer<ActionActivationDelegate>>(tableSize, Allocator.Persistent);

        FunctionPointer<ActionActivationDelegate> nullPtr =
            BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.NullEnable);
        for (int i = 0; i < tableSize; i++)
            _functionTable[i] = nullPtr;

        _functionTable[(int)ActionType.Idle]            = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.IdleEnable);
        _functionTable[(int)ActionType.Wander]          = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.WanderEnable);
        _functionTable[(int)ActionType.Interact]        = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.InteractEnable);
        _functionTable[(int)ActionType.Flee]            = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.FleeEnable);
        _functionTable[(int)ActionType.MeleeContinuous] = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.MeleeEnable);
        _functionTable[(int)ActionType.Sit]             = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.SitEnable);
        _functionTable[(int)ActionType.Bathroom]        = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.InteractEnable);
        _functionTable[(int)ActionType.Talk]            = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.TalkEnable);
        _functionTable[(int)ActionType.Death]           = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.DeathEnable);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_functionTable.IsCreated)
            _functionTable.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        playerControlledLookup.Update(ref state);
        talkActionLookup.Update(ref state);
        deadLookup.Update(ref state);
        actionOptionLookup.Update(ref state);

        EntityCommandBuffer ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);

        state.Dependency = new ActionInterruptJob
        {
            functionTable          = _functionTable,
            playerControlledLookup = playerControlledLookup,
            talkActionLookup       = talkActionLookup,
            deadLookup             = deadLookup,
            actionOptionLookup     = actionOptionLookup,
            ecb                    = ecb.AsParallelWriter(),
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(ActionInterruptRequest))]
[WithPresent(typeof(PathRequest), typeof(ActionTimer), typeof(ActionRequest))]
public partial struct ActionInterruptJob : IJobEntity
{
    [ReadOnly] public NativeArray<FunctionPointer<ActionActivationDelegate>> functionTable;
    [ReadOnly] public ComponentLookup<PlayerControlled>                      playerControlledLookup;
    [ReadOnly] public ComponentLookup<TalkAction>                            talkActionLookup;
    [ReadOnly] public ComponentLookup<Dead>                                  deadLookup;
    [ReadOnly] public BufferLookup<ActionOption>                             actionOptionLookup;
    public EntityCommandBuffer.ParallelWriter ecb;

    public void Execute(
        Entity                               entity,
        [EntityIndexInQuery] int             index,
        in CurrentAction                     currentAction,
        ref ActionTimer                      actionTimer,
        EnabledRefRW<ActionInterruptRequest> actionInterruptRequestEnabled,
        EnabledRefRW<ActionRequest>          actionRequestEnabled,
        EnabledRefRW<ActionTimer>            actionTimerEnabled,
        EnabledRefRW<PathRequest>            pathRequestEnabled)
    {
        actionInterruptRequestEnabled.ValueRW = false;

        bool talkActive = talkActionLookup.HasComponent(entity) && talkActionLookup.IsComponentEnabled(entity);

        // Determine whether this is a high-priority interrupt (e.g. self-defence, health) or a
        // low-priority social lock (ValidateSocialJob interrupting the responder to clear their
        // previous action). Compare the highest-priority option currently in the awareness buffer
        // against the priority of the action being interrupted:
        //   - Higher priority available → something more urgent has appeared; unconditionally interrupt.
        //   - Same or no priority       → this is a social lock; preserve the existing special case
        //                                 that protects the one-frame window where the unit just
        //                                 ended a conversation and was re-locked as a new responder.
        int maxBufferPriority = 0;
        if (actionOptionLookup.HasBuffer(entity))
        {
            DynamicBuffer<ActionOption> options = actionOptionLookup[entity];
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i].priority > maxBufferPriority)
                    maxBufferPriority = options[i].priority;
            }
        }

        bool highPriority = maxBufferPriority > currentAction.priority;

        int  actionIndex = (int)currentAction.actionType;
        bool skipDisable = !highPriority && currentAction.actionType == ActionType.Talk && talkActive;
        if (!skipDisable && actionIndex >= 0 && actionIndex < functionTable.Length)
            functionTable[actionIndex].Invoke(in entity, index, ref ecb, false);

        pathRequestEnabled.ValueRW = false;
        actionTimer.time           = 0f;
        actionTimerEnabled.ValueRW = false;

        // Dead units transition to the Death action and never re-enter selection.
        // ActionRequest stays disabled until revival fires another ActionInterruptRequest.
        bool isDead = deadLookup.HasComponent(entity) && deadLookup.IsComponentEnabled(entity);
        if (isDead)
        {
            functionTable[(int)ActionType.Death].Invoke(in entity, index, ref ecb, true);
            ecb.SetComponent(index, entity, new CurrentAction
            {
                actionType   = ActionType.Death,
                targetEntity = Entity.Null,
                priority     = 0,
            });
            return;
        }

        // Return to decision pipeline unless player-owned or a low-priority social lock
        // (responder is held in TalkJob, not selection).
        bool playerOwned    = playerControlledLookup.HasComponent(entity) && playerControlledLookup.IsComponentEnabled(entity);
        bool keepInTalkLock = !highPriority && talkActive;
        if (!playerOwned && !keepInTalkLock)
            actionRequestEnabled.ValueRW = true;
    }
}
