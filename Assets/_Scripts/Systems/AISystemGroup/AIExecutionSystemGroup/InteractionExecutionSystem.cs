using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Universal fallback for interaction execution. Drives move → arrive → timer →
/// complete for any interaction whose InteractionKind is None — i.e. interactions
/// that don't have a dedicated per-kind execution system yet. Per-kind systems
/// (PickupSystem, BuildSystem, ...) will replace this flow for their specific
/// InteractionKind values and are responsible for the same completion contract:
/// remove the unit from InteractionOccupant, restore the driving behaviour's value,
/// re-enable InteractionProvider if a slot freed, disable their own task enableable,
/// and re-enable NeedsAction.
///
/// This system iterates UNIT entities and looks up data on the interaction entity
/// (4 lookups total, all on the single stable interaction entity).
///
/// InteractionMoveJob:
///   Unit has selected an interaction but hasn't started moving yet.
///   Enables MoveToTargetRequest — MoveToTargetSystem handles all pathing from here.
///
/// InteractionCompleteJob:
///   Unit has arrived (ArrivedAtTarget enabled). Ticks ActionTimer. On expiry:
///   restores the motivation that drove the action, removes self from the interaction's
///   occupant buffer, re-enables InteractionProvider if a slot is free, and releases
///   the unit back to AI.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup))]
public partial struct InteractionExecutionSystem : ISystem
{
    private ComponentLookup<Interaction>         interactionLookup;
    private ComponentLookup<InteractionTimer>    interactionTimerLookup;
    private BufferLookup<InteractionOccupant>    occupantBufferLookup;
    private ComponentLookup<InteractionProvider> interactionProviderLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();

        interactionLookup         = state.GetComponentLookup<Interaction>(true);
        interactionTimerLookup    = state.GetComponentLookup<InteractionTimer>(true);
        occupantBufferLookup      = state.GetBufferLookup<InteractionOccupant>(false);
        interactionProviderLookup = state.GetComponentLookup<InteractionProvider>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        interactionLookup.Update(ref state);
        interactionTimerLookup.Update(ref state);
        occupantBufferLookup.Update(ref state);
        interactionProviderLookup.Update(ref state);

        float deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new InteractionMoveJob
        {
            interactionLookup = interactionLookup,
        }.Schedule(state.Dependency);

        state.Dependency = new InteractionCompleteJob
        {
            deltaTime                 = deltaTime,
            interactionLookup         = interactionLookup,
            interactionTimerLookup    = interactionTimerLookup,
            occupantBufferLookup      = occupantBufferLookup,
            interactionProviderLookup = interactionProviderLookup,
        }.Schedule(state.Dependency);
    }
}

// ── Move setup: enable MoveToTargetRequest so MoveToTargetSystem handles pathing ──
[BurstCompile]
[WithDisabled(typeof(NeedsAction))]
[WithDisabled(typeof(MoveToTargetRequest))]
[WithDisabled(typeof(ArrivedAtTarget))]
public partial struct InteractionMoveJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<Interaction> interactionLookup;

    public void Execute(
        in SelectedAction                 selectedAction,
        ref MoveToTargetRequest           moveRequest,
        EnabledRefRW<MoveToTargetRequest> moveRequestEnabled)
    {
        if (selectedAction.category != ActionCategory.Interaction)
            return;

        Entity interactionEntity = selectedAction.targetEntity;
        if (interactionEntity == Entity.Null)
            return;

        if (!interactionLookup.TryGetComponent(interactionEntity, out Interaction interaction))
            return;

        moveRequest.targetEntity       = interactionEntity;
        moveRequest.arrivalRange       = interaction.interactionRange;
        moveRequest.lastKnownTargetPos = float3.zero;
        moveRequestEnabled.ValueRW     = true;
    }
}

// ── Complete: tick timer, restore motivation, release unit ────────────────────
[BurstCompile]
[WithDisabled(typeof(NeedsAction))]
[WithAll(typeof(ArrivedAtTarget))]
public partial struct InteractionCompleteJob : IJobEntity
{
    public float deltaTime;

    [ReadOnly] public ComponentLookup<Interaction>      interactionLookup;
    [ReadOnly] public ComponentLookup<InteractionTimer> interactionTimerLookup;
    public BufferLookup<InteractionOccupant>            occupantBufferLookup;
    public ComponentLookup<InteractionProvider>         interactionProviderLookup;

    public void Execute(
        Entity                         self,
        in SelectedAction              selectedAction,
        ref ActionTimer                actionTimer,
        ref DynamicBuffer<Behaviour>   behaviourBuffer,
        EnabledRefRW<ArrivedAtTarget>  arrivedEnabled,
        EnabledRefRW<NeedsAction>      needsActionEnabled)
    {
        if (selectedAction.category != ActionCategory.Interaction)
            return;

        Entity interactionEntity = selectedAction.targetEntity;

        // ── Start timer on first arrival frame ────────────────────────────────
        if (actionTimer.time == 0f)
        {
            float maxTime = 0f;
            if (interactionTimerLookup.TryGetComponent(interactionEntity, out InteractionTimer interTimer))
                maxTime = interTimer.maxTime;

            if (maxTime <= 0f)
            {
                // Instant interaction — complete immediately
                CompleteInteraction(self, interactionEntity, ref behaviourBuffer,
                    ref arrivedEnabled, ref needsActionEnabled);
                return;
            }

            actionTimer.time = maxTime;
            return;
        }

        // ── Tick ──────────────────────────────────────────────────────────────
        actionTimer.time -= deltaTime;
        if (actionTimer.time > 0f)
            return;

        actionTimer.time = 0f;
        CompleteInteraction(self, interactionEntity, ref behaviourBuffer,
            ref arrivedEnabled, ref needsActionEnabled);
    }

    private void CompleteInteraction(
        Entity self,
        Entity interactionEntity,
        ref DynamicBuffer<Behaviour> behaviourBuffer,
        ref EnabledRefRW<ArrivedAtTarget> arrivedEnabled,
        ref EnabledRefRW<NeedsAction> needsActionEnabled)
    {
        BehaviourType restoredType = BehaviourType.None;

        // Remove self from occupant buffer; read behaviourType stored at selection time
        if (occupantBufferLookup.TryGetBuffer(interactionEntity, out DynamicBuffer<InteractionOccupant> occupants))
        {
            for (int i = 0; i < occupants.Length; i++)
            {
                if (occupants[i].entity != self)
                    continue;

                restoredType = occupants[i].behaviourType;
                occupants.RemoveAt(i);
                break;
            }

            // Re-enable the interaction slot if capacity is now free
            if (interactionLookup.TryGetComponent(interactionEntity, out Interaction interaction))
            {
                if (occupants.Length < interaction.maxOccupants)
                    interactionProviderLookup.SetComponentEnabled(interactionEntity, true);
            }
        }

        // Restore the motivation that drove this interaction to full
        if (restoredType != BehaviourType.None)
        {
            for (int m = 0; m < behaviourBuffer.Length; m++)
            {
                Behaviour entry = behaviourBuffer[m];
                if (entry.behaviourType != restoredType)
                    continue;
                entry.value        = 100f;
                behaviourBuffer[m] = entry;
                break;
            }
        }

        arrivedEnabled.ValueRW     = false;
        needsActionEnabled.ValueRW = true;
    }
}
