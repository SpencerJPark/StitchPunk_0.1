using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

[BurstCompile]
[UpdateInGroup(typeof(ActionSelectionSystemGroup), OrderLast = true)]
public partial struct ActionSelectionSystem : ISystem
{
    private ComponentLookup<Interaction> interactionLookup;
    private NativeArray<FunctionPointer<ActionActivationDelegate>> _functionTable;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<InteractionLibrary>();
        
        interactionLookup = state.GetComponentLookup<Interaction>(false);

        int tableSize = (int)ActionType.Spawn + 1;
        _functionTable = new NativeArray<FunctionPointer<ActionActivationDelegate>>(tableSize, Allocator.Persistent);

        FunctionPointer<ActionActivationDelegate> nullPtr = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.NullEnable);
        for (int i = 0; i < tableSize; i++)
            _functionTable[i] = nullPtr;

        _functionTable[(int)ActionType.Idle]     = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.IdleEnable);
        _functionTable[(int)ActionType.Wander]   = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.WanderEnable);
        _functionTable[(int)ActionType.Interact] = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.InteractEnable);
        _functionTable[(int)ActionType.Flee]     = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.FleeEnable);
        _functionTable[(int)ActionType.MeleeContinuous] = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.MeleeEnable);
        _functionTable[(int)ActionType.Sit]             = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.SitEnable);
        _functionTable[(int)ActionType.Bathroom]        = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.InteractEnable);
    }
    
    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_functionTable.IsCreated)
        {
            _functionTable.Dispose();
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float time = (float)SystemAPI.Time.ElapsedTime;
        interactionLookup.Update(ref state);
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        
        
        // 1 Select Best Action
        state.Dependency = new ActionSelectionJob
        {
            time = time,
        }.ScheduleParallel(state.Dependency);

        // 2 Validate Selection on Interactions
        // Complete pending jobs (including EnvironmentalAwarenessJob's Interaction reads)
        // before this main-thread job writes to Interaction occupantCount.
        state.Dependency.Complete();
        InteractionLibrary interactionLib = SystemAPI.GetSingleton<InteractionLibrary>();
        new ValidateInteractionJob
        {
            interactionLookup  = interactionLookup,
            interactionLibrary = interactionLib.library,
        }.Run();
        
        // 3 Enable Downstream Components
        state.Dependency = new SetupActionJob
        {
            ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter(),
            functionTable = _functionTable
            
        }.ScheduleParallel(state.Dependency);
    }

    // [WithAll(typeof(NeedsAction))] is required — NeedsAction is IEnableableComponent.
    // Without it the query uses WithPresent (runs on all units) and FilterPreviousEntity
    // re-enables NeedsAction on dead/inactive units that have 0 options, creating an infinite loop.
    [BurstCompile]
    [WithAll(typeof(AIBrain), typeof(ActionRequest))]
    [WithDisabled(typeof(Dead))]
    [WithPresent(typeof(ActionSelectionValidationRequest))]
    public partial struct ActionSelectionJob : IJobEntity
    {
        public float time;

        public void Execute(
            [EntityIndexInQuery] int entityIndex,
            ref CurrentAction currentAction,
            ref DynamicBuffer<ActionOption> options,
            EnabledRefRW<ActionSelectionValidationRequest> needsValidation,
            EnabledRefRW<ActionRequest> actionRequest)
        {

            // 1. Filter out the previous target to prevent "oscillating" or getting stuck
            FilterPreviousTarget(ref options, currentAction.targetEntity);

            // 2. Hard-filter to the highest priority tier (combat/self-defence overrides idle options)
            FilterToHighestPriority(ref options);

            if (options.Length == 0)
            {
                // No options left? Stay in NeedsAction and try again next frame
                options.Clear();
                return;
            }

            // 3. Sort options by score (Descending)
            SortOptions(ref options);

            // 4. Randomly pick from Top 3
            Unity.Mathematics.Random random = new Unity.Mathematics.Random((uint)(entityIndex + 1) * (uint)(time * 1000 + 1));
            int topCount = math.min(options.Length, 3);
            int selectedIndex = random.NextInt(0, topCount);
            
            ActionOption choice = options[selectedIndex];

            // 5. Record the selection to CurrentAction
            currentAction.actionType   = choice.actionType;
            currentAction.targetEntity = choice.targetEntity;

            // 6. Handle State Transitions
            // If it's an interaction, disable ActionRequest now so ValidateInteractionJob
            // can use it as a signal, then re-enable it after validation.
            if (choice.interaction)
            {
                needsValidation.ValueRW = true;
                actionRequest.ValueRW = false;
            }
            else
            {
                // Non-interaction: leave ActionRequest enabled so SetupActionJob (which
                // filters [WithAll ActionRequest]) picks this entity up and disables it.
                needsValidation.ValueRW = false;
            }

            // 7. Cleanup
            options.Clear();
        }

        private static void FilterToHighestPriority(ref DynamicBuffer<ActionOption> options)
        {
            int maxPriority = 0;
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i].priority > maxPriority)
                    maxPriority = options[i].priority;
            }

            for (int i = options.Length - 1; i >= 0; i--)
            {
                if (options[i].priority < maxPriority)
                    options.RemoveAt(i);
            }
        }

        private static void FilterPreviousTarget(ref DynamicBuffer<ActionOption> options, Entity previousTarget)
        {
            for (int i = options.Length - 1; i >= 0; i--)
            {
                // Only filter interactions — prevents immediately returning to the same chair/NPC.
                // Combat options are never filtered; re-targeting the same hostile is correct.
                if (options[i].interaction && options[i].targetEntity == previousTarget)
                {
                    options.RemoveAt(i);
                }
            }
        }

        private static void SortOptions(ref DynamicBuffer<ActionOption> options)
        {
            // Simple Insertion Sort - efficient for small AI option buffers
            for (int i = 1; i < options.Length; i++)
            {
                ActionOption temp = options[i];
                int j = i - 1;

                while (j >= 0 && options[j].utilityScore < temp.utilityScore)
                {
                    options[j + 1] = options[j];
                    j--;
                }
                options[j + 1] = temp;
            }
        }
    }
    
    [BurstCompile]
    [WithAll(typeof(ActionSelectionValidationRequest))]
    [WithPresent(typeof(ActionRequest))]
    public partial struct ValidateInteractionJob : IJobEntity
    {
        public ComponentLookup<Interaction>                       interactionLookup;
        [ReadOnly] public BlobAssetReference<InteractionLibraryBlob> interactionLibrary;

        public void Execute(
            EnabledRefRW<ActionSelectionValidationRequest> validationTrigger,
            EnabledRefRW<ActionRequest>                    actionRequest,
            ref CurrentAction                              currentAction)
        {
            validationTrigger.ValueRW = false;

            bool validationPassed =
                interactionLookup.TryGetComponent(currentAction.targetEntity, out Interaction interaction) &&
                interactionLibrary.Value.interactions[(int)interaction.actionType].maxOccupants > interaction.currentOccupants;

            if (validationPassed)
            {
                interaction.currentOccupants++;
                interactionLookup[currentAction.targetEntity] = interaction;
            }
            else
            {
                currentAction.actionType = ActionType.None;
            }

            // Always re-enable ActionRequest so SetupActionJob runs this frame.
            actionRequest.ValueRW = true;
        }
    }
    
    [BurstCompile]
    [WithAll(typeof(ActionRequest))]
    public partial struct SetupActionJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ecb;
        [ReadOnly] public NativeArray<FunctionPointer<ActionActivationDelegate>> functionTable;

        public void Execute(
            Entity                      entity,
            [EntityIndexInQuery] int    index,
            in CurrentAction            currentAction,
            EnabledRefRW<ActionRequest> needsAction)
        {
            int actionIndex = (int)currentAction.actionType;

            // ActionType.None (0) means no valid action was selected (e.g. failed interaction
            // validation). Leave ActionRequest enabled so the unit retries next frame.
            if (actionIndex > 0 && actionIndex < functionTable.Length)
            {
                functionTable[actionIndex].Invoke(in entity, index, ref ecb, true);
                needsAction.ValueRW = false;
            }
        }
    }
}
