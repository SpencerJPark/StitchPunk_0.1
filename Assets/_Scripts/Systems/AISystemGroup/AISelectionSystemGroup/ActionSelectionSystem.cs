using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

[BurstCompile]
[UpdateInGroup(typeof(AISelectionSystemGroup))]
public partial struct ActionSelectionSystem : ISystem
{
    private ComponentLookup<Interaction> interactionLookup;
    private NativeArray<FunctionPointer<ActionActivationDelegate>> _functionTable;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();

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
        _functionTable[(int)ActionType.Melee]    = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.MeleeEnable);
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
        new ValidateInteractionJob
        {
            interactionLookup = interactionLookup,
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
    [WithAll(typeof(ActiveBrain), typeof(NeedsAction))]
    [WithPresent(typeof(NeedsActionSelectionValidation))]
    public partial struct ActionSelectionJob : IJobEntity
    {
        public float time;

        public void Execute(
            [EntityIndexInQuery] int entityIndex,
            ref CurrentAction currentAction,
            ref DynamicBuffer<ActionOption> options,
            EnabledRefRW<NeedsActionSelectionValidation> needsValidation,
            EnabledRefRW<NeedsAction> needsAction)
        {

            // 1. Filter out the previous target to prevent "oscillating" or getting stuck
            FilterPreviousTarget(ref options, currentAction.targetEntity);

            if (options.Length == 0)
            {
                // No options left? Stay in NeedsAction and try again next frame
                options.Clear();
                return;
            }

            // 2. Sort options by score (Descending)
            SortOptions(ref options);

            // 3. Randomly pick from Top 3
            Unity.Mathematics.Random random = new Unity.Mathematics.Random((uint)(entityIndex + 1) * (uint)(time * 1000 + 1));
            int topCount = math.min(options.Length, 3);
            int selectedIndex = random.NextInt(0, topCount);
            
            ActionOption choice = options[selectedIndex];

            // 4. Record the selection to CurrentAction
            currentAction.actionType = choice.actionType;
            currentAction.attackType = choice.attackType;
            currentAction.targetEntity = choice.targetEntity;

            // 5. Handle State Transitions
            // If it's an interaction, we need the Validator to check occupancy
            if (choice.interaction) 
            {
                needsValidation.ValueRW = true;
                needsAction.ValueRW = false; // Transitioning to Validation state
            }
            else
            {
                // If it's not an interaction, we are done picking! 
                // The Logic systems (Attack, Patrol) should take over now.
                needsValidation.ValueRW = false;
                needsAction.ValueRW = false; 
            }

            // 6. Cleanup
            options.Clear();
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
    public partial struct ValidateInteractionJob : IJobEntity
    {
        public ComponentLookup<Interaction> interactionLookup;

        public void Execute(
            EnabledRefRW<NeedsActionSelectionValidation> validationTrigger,
            EnabledRefRW<NeedsAction> needsAction, 
            in CurrentAction currentAction)
        {
            validationTrigger.ValueRW = false;
            
            if (interactionLookup.TryGetComponent(currentAction.targetEntity, out Interaction interaction))
            {
                if (interaction.occupantCount >= interaction.maxOccupants)
                {
                    needsAction.ValueRW = true;
                    return;
                }
                
                interaction.occupantCount++;
                interactionLookup[currentAction.targetEntity] = interaction;
            }
            else
            {
                needsAction.ValueRW = true;
            }
        }
    }
    
    [BurstCompile]
    public partial struct SetupActionJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ecb;
        [ReadOnly] public NativeArray<FunctionPointer<ActionActivationDelegate>> functionTable;

        public void Execute(Entity entity, [EntityIndexInQuery] int index, in CurrentAction currentAction)
        {
            int actionIndex = (int)currentAction.actionType;
            
            if (actionIndex >= 0 && actionIndex < functionTable.Length)
            {
                functionTable[actionIndex].Invoke(in entity, index, ref ecb);
            }
        }
    }
}
