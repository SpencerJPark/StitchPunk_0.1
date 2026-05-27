using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

[BurstCompile]
[UpdateInGroup(typeof(ActionSelectionSystemGroup), OrderLast = true)]
public partial struct ActionSelectionSystem : ISystem
{
    private ComponentLookup<Interaction>            interactionLookup;
    private ComponentLookup<TalkAction>             talkActionLookup;
    private ComponentLookup<ConversationContext>    conversationContextLookup;
    private ComponentLookup<ActionInterruptRequest> actionInterruptRequestLookup;
    private ComponentLookup<ActionRequest>          actionRequestLookup;
    private ComponentLookup<SocialAvailable>        socialAvailableLookup;
    private ComponentLookup<CurrentAction>          currentActionLookup;
    private NativeArray<FunctionPointer<ActionActivationDelegate>> _functionTable;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<InteractionLibrary>();
        
        interactionLookup            = state.GetComponentLookup<Interaction>(false);
        talkActionLookup             = state.GetComponentLookup<TalkAction>(false);
        conversationContextLookup    = state.GetComponentLookup<ConversationContext>(false);
        actionInterruptRequestLookup = state.GetComponentLookup<ActionInterruptRequest>(false);
        actionRequestLookup          = state.GetComponentLookup<ActionRequest>(false);
        socialAvailableLookup        = state.GetComponentLookup<SocialAvailable>(false);
        currentActionLookup          = state.GetComponentLookup<CurrentAction>(false);

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
        _functionTable[(int)ActionType.Talk]            = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.TalkEnable);
        _functionTable[(int)ActionType.Death]           = BurstCompiler.CompileFunctionPointer<ActionActivationDelegate>(SelectionFunctions.DeathEnable);
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
        talkActionLookup.Update(ref state);
        conversationContextLookup.Update(ref state);
        actionInterruptRequestLookup.Update(ref state);
        actionRequestLookup.Update(ref state);
        socialAvailableLookup.Update(ref state);
        currentActionLookup.Update(ref state);
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        InteractionLibrary interactionLib = SystemAPI.GetSingleton<InteractionLibrary>();

        // 1 Select Best Action
        state.Dependency = new ActionSelectionJob
        {
            time = time,
        }.ScheduleParallel(state.Dependency);

        // 2 Validate Selection
        state.Dependency = new ValidateInteractionJob
        {
            interactionLookup  = interactionLookup,
            interactionLibrary = interactionLib.library,
        }.Schedule(state.Dependency);

        state.Dependency = new ValidateSocialJob
        {
            talkActionLookup             = talkActionLookup,
            conversationContextLookup    = conversationContextLookup,
            actionInterruptRequestLookup = actionInterruptRequestLookup,
            actionRequestLookup          = actionRequestLookup,
            socialAvailableLookup        = socialAvailableLookup,
            currentActionLookup          = currentActionLookup,
        }.Schedule(state.Dependency);

        // 3 Enable Downstream Components
        state.Dependency = new SetupActionJob
        {
            ecb           = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter(),
            functionTable = _functionTable,
        }.ScheduleParallel(state.Dependency);
    }

    // [WithAll(typeof(NeedsAction))] is required — NeedsAction is IEnableableComponent.
    // Without it the query uses WithPresent (runs on all units) and FilterPreviousEntity
    // re-enables NeedsAction on dead/inactive units that have 0 options, creating an infinite loop.
    [BurstCompile]
    [WithAll(typeof(AIBrain), typeof(ActionRequest))]
    [WithDisabled(typeof(Dead))]
    [WithPresent(typeof(ActionSelectionValidationRequest), typeof(SocialValidationRequest))]
    public partial struct ActionSelectionJob : IJobEntity
    {
        public float time;

        public void Execute(
            [EntityIndexInQuery] int entityIndex,
            ref CurrentAction currentAction,
            ref DynamicBuffer<ActionOption> options,
            EnabledRefRW<ActionSelectionValidationRequest> needsValidation,
            EnabledRefRW<SocialValidationRequest>          socialValidationRequestEnabled,
            EnabledRefRW<ActionRequest>                    actionRequest)
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
            currentAction.priority     = choice.priority;

            // 6. Handle State Transitions
            if (choice.needsValidation)
            {
                if (choice.actionType == ActionType.Talk)
                {
                    // Route to social validation — locks both units and interrupts target
                    socialValidationRequestEnabled.ValueRW = true;
                    needsValidation.ValueRW                = false;
                }
                else
                {
                    // Standard interaction — checks occupant capacity
                    needsValidation.ValueRW                = true;
                    socialValidationRequestEnabled.ValueRW = false;
                }
                actionRequest.ValueRW = false;
            }
            else
            {
                needsValidation.ValueRW                = false;
                socialValidationRequestEnabled.ValueRW = false;
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
                if (options[i].needsValidation && options[i].targetEntity == previousTarget)
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
    [WithAll(typeof(SocialValidationRequest))]
    [WithPresent(typeof(ActionRequest))]
    public partial struct ValidateSocialJob : IJobEntity
    {
        public ComponentLookup<TalkAction>             talkActionLookup;
        public ComponentLookup<ConversationContext>    conversationContextLookup;
        public ComponentLookup<ActionInterruptRequest> actionInterruptRequestLookup;
        public ComponentLookup<ActionRequest>          actionRequestLookup;
        public ComponentLookup<SocialAvailable>                              socialAvailableLookup;
        [NativeDisableContainerSafetyRestriction] public ComponentLookup<CurrentAction> currentActionLookup;

        public void Execute(
            Entity                                entity,
            EnabledRefRW<SocialValidationRequest> socialValidationRequestEnabled,
            ref CurrentAction                     currentAction)
        {
            socialValidationRequestEnabled.ValueRW = false;
            Entity target = currentAction.targetEntity;

            // Target must be socially available (not in combat, not already in a conversation).
            // SocialAvailable is disabled by SelectionFunctions whenever combat/flee starts and
            // by this job when a conversation is locked — so first-write-wins prevents double-booking.
            bool valid = target != Entity.Null
                && conversationContextLookup.HasComponent(target)
                && conversationContextLookup[target].conversationPartner != entity
                && socialAvailableLookup.HasComponent(target)
                && socialAvailableLookup.IsComponentEnabled(target);

            if (valid)
            {
                conversationContextLookup[entity] = new ConversationContext
                {
                    conversationPartner = target,
                    isResponder         = false,
                };
                conversationContextLookup[target] = new ConversationContext
                {
                    conversationPartner = entity,
                    isResponder         = true,
                };

                // Disable SocialAvailable on both so no other unit can target either this frame.
                socialAvailableLookup.SetComponentEnabled(entity, false);
                socialAvailableLookup.SetComponentEnabled(target, false);

                // Enable TalkAction on BOTH directly — prevents double-booking if the partner
                // also picked Talk this same frame (their check sees initiator already locked).
                talkActionLookup.SetComponentEnabled(entity, true);
                talkActionLookup.SetComponentEnabled(target, true);

                // Update responder's CurrentAction so TalkJob's partnerLost check is correct
                // and ActionInterruptSystem reads the right priority for the responder next frame.
                currentActionLookup[target] = new CurrentAction
                {
                    actionType   = ActionType.Talk,
                    targetEntity = entity,
                    priority     = 1,
                };

                // Disable ActionRequest on responder so SetupActionJob cannot re-activate
                // their old action tag in the same frame.
                actionRequestLookup.SetComponentEnabled(target, false);
                // Interrupt responder to clear any action they were already executing.
                actionInterruptRequestLookup.SetComponentEnabled(target, true);
            }
            else
            {
                currentAction.actionType   = ActionType.None;
                currentAction.targetEntity = Entity.Null;
            }

            // Success: initiator goes straight to TalkJob (TalkAction already enabled above).
            // Failure: initiator retries selection next frame, unless we were already locked as
            // a responder by the partner's iteration of this same job (mutual-selection race).
            bool alreadyLockedAsResponder = talkActionLookup.IsComponentEnabled(entity);
            actionRequestLookup.SetComponentEnabled(entity, !valid && !alreadyLockedAsResponder);
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
            
            if (actionIndex > 0 && actionIndex < functionTable.Length)
            {
                functionTable[actionIndex].Invoke(in entity, index, ref ecb, true);
                needsAction.ValueRW = false;
            }
        }
    }
}
