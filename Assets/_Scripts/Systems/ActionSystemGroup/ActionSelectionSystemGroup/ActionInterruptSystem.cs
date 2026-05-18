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
//[BurstCompile]
[UpdateInGroup(typeof(ActionSelectionSystemGroup), OrderFirst = true)]
public partial struct ActionInterruptSystem : ISystem
{
    private NativeArray<FunctionPointer<ActionActivationDelegate>> _functionTable;
    private ComponentLookup<PlayerControlled>                      playerControlledLookup;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        playerControlledLookup = state.GetComponentLookup<PlayerControlled>(true);

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
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_functionTable.IsCreated)
            _functionTable.Dispose();
    }

    //[BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        playerControlledLookup.Update(ref state);

        EntityCommandBuffer ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);

        state.Dependency = new ActionInterruptJob
        {
            functionTable          = _functionTable,
            playerControlledLookup = playerControlledLookup,
            ecb                    = ecb.AsParallelWriter(),
        }.ScheduleParallel(state.Dependency);
    }
}

//[BurstCompile]
[WithAll(typeof(ActionInterruptRequest))]
[WithPresent(typeof(PathRequest), typeof(ActionTimer), typeof(ActionRequest))]
public partial struct ActionInterruptJob : IJobEntity
{
    [ReadOnly] public NativeArray<FunctionPointer<ActionActivationDelegate>> functionTable;
    [ReadOnly] public ComponentLookup<PlayerControlled>                      playerControlledLookup;
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
        Debug.Log("Action Interrupted");
        
        actionInterruptRequestEnabled.ValueRW = false;
        
        // Disable the active action tag via the shared function table
        int actionIndex = (int)currentAction.actionType;
        if (actionIndex >= 0 && actionIndex < functionTable.Length)
            functionTable[actionIndex].Invoke(in entity, index, ref ecb, false);

        // Halt pathing
        pathRequestEnabled.ValueRW = false;

        // Clear any leftover action timer so the incoming action runs immediately
        actionTimer.time           = 0f;
        actionTimerEnabled.ValueRW = false;

        // Return to decision pipeline via direct write (not ECB) so ActionSelectionJob
        // can pick new options in the same frame.
        bool playerOwned = playerControlledLookup.HasComponent(entity) &&
                           playerControlledLookup.IsComponentEnabled(entity);
        if (!playerOwned)
            actionRequestEnabled.ValueRW = true;
    }
}
