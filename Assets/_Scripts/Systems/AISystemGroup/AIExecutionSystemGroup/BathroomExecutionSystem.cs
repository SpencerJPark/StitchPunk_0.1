using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup))]
public partial struct BathroomExecutionSystem : ISystem
{
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<TargetPositionPathQueued> targetPositionLookup;
    private ComponentLookup<UnitAction> unitActionLookup;
    private ComponentLookup<BrainLink> brainLinkLookup;
    private ComponentLookup<NeedsAction> needsActionLookup;
    private ComponentLookup<InteractionProvider> interactionProviderLookup;
    private BufferLookup<InteractionOccupant> occupantBufferLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BathroomInteraction>();

        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        targetPositionLookup = state.GetComponentLookup<TargetPositionPathQueued>(false);
        unitActionLookup = state.GetComponentLookup<UnitAction>(false);
        brainLinkLookup = state.GetComponentLookup<BrainLink>(true);
        needsActionLookup = state.GetComponentLookup<NeedsAction>(false);
        interactionProviderLookup = state.GetComponentLookup<InteractionProvider>(false);
        occupantBufferLookup = state.GetBufferLookup<InteractionOccupant>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        targetPositionLookup.Update(ref state);
        unitActionLookup.Update(ref state);
        brainLinkLookup.Update(ref state);
        needsActionLookup.Update(ref state);
        interactionProviderLookup.Update(ref state);
        occupantBufferLookup.Update(ref state);

        float deltaTime = SystemAPI.Time.DeltaTime;

        new BathroomAssignmentJob
        {
            transformLookup = transformLookup,
            targetPositionLookup = targetPositionLookup,
            unitActionLookup = unitActionLookup,
            brainLinkLookup = brainLinkLookup,
            needsActionLookup = needsActionLookup,
            interactionProviderLookup = interactionProviderLookup,
            occupantBufferLookup = occupantBufferLookup
        }.Schedule();

        new BathroomArrivalJob
        {
            transformLookup = transformLookup,
            brainLinkLookup = brainLinkLookup,
            interactionProviderLookup = interactionProviderLookup,
            occupantBufferLookup = occupantBufferLookup
        }.Schedule();

        new BathroomCompletionJob
        {
            deltaTime = deltaTime,
            needsActionLookup = needsActionLookup,
            unitActionLookup = unitActionLookup,
            brainLinkLookup = brainLinkLookup,
            interactionProviderLookup = interactionProviderLookup,
            occupantBufferLookup = occupantBufferLookup
        }.Schedule();
    }

    // -------------------------------------------------------
    // ASSIGNMENT — pick a winner, reject losers, send winner walking
    // -------------------------------------------------------
    [BurstCompile]
    public partial struct BathroomAssignmentJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
        public ComponentLookup<TargetPositionPathQueued> targetPositionLookup;
        public ComponentLookup<UnitAction> unitActionLookup;
        [ReadOnly] public ComponentLookup<BrainLink> brainLinkLookup;
        public ComponentLookup<NeedsAction> needsActionLookup;
        public ComponentLookup<InteractionProvider> interactionProviderLookup;
        public BufferLookup<InteractionOccupant> occupantBufferLookup;

        public void Execute(
            Entity interactionEntity,
            in BathroomInteraction bathroomInteraction,
            in LocalTransform interactionTransform)
        {
            if (!interactionProviderLookup.IsComponentEnabled(interactionEntity))
                return;

            if (!occupantBufferLookup.TryGetBuffer(interactionEntity, out DynamicBuffer<InteractionOccupant> occupants))
                return;

            if (occupants.Length == 0)
                return;

            int winnerIndex = FindHighestScorer(in occupants);
            Entity winnerBrain = occupants[winnerIndex].entity;

            RejectLosers(in occupants, winnerIndex);
            AssignWinner(winnerBrain, interactionTransform.Position);

            interactionProviderLookup.SetComponentEnabled(interactionEntity, false);

            InteractionOccupant winner = occupants[winnerIndex];
            occupants.Clear();
            occupants.Add(winner);
        }

        private static int FindHighestScorer(in DynamicBuffer<InteractionOccupant> occupants)
        {
            int winnerIndex = 0;
            float highestScore = occupants[0].score;

            for (int i = 1; i < occupants.Length; i++)
            {
                if (occupants[i].score > highestScore)
                {
                    highestScore = occupants[i].score;
                    winnerIndex = i;
                }
            }

            return winnerIndex;
        }

        private void RejectLosers(in DynamicBuffer<InteractionOccupant> occupants, int winnerIndex)
        {
            for (int i = 0; i < occupants.Length; i++)
            {
                if (i == winnerIndex)
                    continue;

                Entity loserBrain = occupants[i].entity;
                needsActionLookup.SetComponentEnabled(loserBrain, true);
            }
        }

        private void AssignWinner(Entity brainEntity, float3 interactionPosition)
        {
            if (!brainLinkLookup.TryGetComponent(brainEntity, out BrainLink brainLink))
                return;

            Entity body = brainLink.body;

            targetPositionLookup[body] = new TargetPositionPathQueued
            {
                targetPosition = interactionPosition
            };
            targetPositionLookup.SetComponentEnabled(body, true);

            unitActionLookup[body] = new UnitAction
            {
                current = ActionType.UseBathroom
            };
        }
    }

    // -------------------------------------------------------
    // ARRIVAL — detect when the NPC reaches the interaction, start timer
    // -------------------------------------------------------
    [BurstCompile]
    [WithDisabled(typeof(InteractionTimer))]
    public partial struct BathroomArrivalJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
        [ReadOnly] public ComponentLookup<BrainLink> brainLinkLookup;
        [ReadOnly] public ComponentLookup<InteractionProvider> interactionProviderLookup;
        [ReadOnly] public BufferLookup<InteractionOccupant> occupantBufferLookup;

        public void Execute(
            Entity interactionEntity,
            in BathroomInteraction bathroomInteraction,
            in LocalTransform interactionTransform,
            ref InteractionTimer timer,
            EnabledRefRW<InteractionTimer> timerEnabled)
        {
            if (interactionProviderLookup.IsComponentEnabled(interactionEntity))
                return;

            if (!interactionProviderLookup.TryGetComponent(interactionEntity, out InteractionProvider interaction))
                return;

            if (!occupantBufferLookup.TryGetBuffer(interactionEntity, out DynamicBuffer<InteractionOccupant> occupants))
                return;

            if (occupants.Length == 0)
                return;

            Entity brainEntity = occupants[0].entity;

            if (!brainLinkLookup.TryGetComponent(brainEntity, out BrainLink brainLink))
                return;

            if (!transformLookup.TryGetComponent(brainLink.body, out LocalTransform bodyTransform))
                return;

            float distSq = math.distancesq(bodyTransform.Position, interactionTransform.Position);
            float rangeSq = interaction.interactionRange * interaction.interactionRange;

            if (distSq > rangeSq)
                return;

            timer.elapsed = 0f;
            timerEnabled.ValueRW = true;
        }
    }

    // -------------------------------------------------------
    // COMPLETION — tick timer, release NPC when done
    // -------------------------------------------------------
    [BurstCompile]
    public partial struct BathroomCompletionJob : IJobEntity
    {
        public float deltaTime;
        public ComponentLookup<NeedsAction> needsActionLookup;
        public ComponentLookup<UnitAction> unitActionLookup;
        [ReadOnly] public ComponentLookup<BrainLink> brainLinkLookup;
        public ComponentLookup<InteractionProvider> interactionProviderLookup;
        public BufferLookup<InteractionOccupant> occupantBufferLookup;

        public void Execute(
            Entity interactionEntity,
            in BathroomInteraction bathroomInteraction,
            ref InteractionTimer timer,
            EnabledRefRW<InteractionTimer> timerEnabled)
        {
            if (interactionProviderLookup.IsComponentEnabled(interactionEntity))
                return;

            timer.elapsed += deltaTime;

            if (timer.elapsed < timer.duration)
                return;

            if (occupantBufferLookup.TryGetBuffer(interactionEntity, out DynamicBuffer<InteractionOccupant> occupants))
            {
                for (int i = 0; i < occupants.Length; i++)
                {
                    Entity brainEntity = occupants[i].entity;

                    if (brainLinkLookup.TryGetComponent(brainEntity, out BrainLink brainLink))
                    {
                        unitActionLookup[brainLink.body] = new UnitAction
                        {
                            current = ActionType.None
                        };
                    }

                    needsActionLookup.SetComponentEnabled(brainEntity, true);
                }

                occupants.Clear();
            }

            timer.elapsed = 0f;
            timerEnabled.ValueRW = false;
            interactionProviderLookup.SetComponentEnabled(interactionEntity, true);
        }
    }
}