using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup))]
public partial struct BathroomExecutionSystem : ISystem
{
    private ComponentLookup<TargetPositionPathQueued> targetPositionLookup;
    private ComponentLookup<UnitAction> unitActionLookup;
    private ComponentLookup<BrainLink> brainLinkLookup;
    private ComponentLookup<NeedsAction> needsActionLookup;
    private ComponentLookup<LocalTransform> transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BathroomInteraction>();

        targetPositionLookup = state.GetComponentLookup<TargetPositionPathQueued>(false);
        unitActionLookup = state.GetComponentLookup<UnitAction>(false);
        brainLinkLookup = state.GetComponentLookup<BrainLink>(true);
        needsActionLookup = state.GetComponentLookup<NeedsAction>(false);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        targetPositionLookup.Update(ref state);
        unitActionLookup.Update(ref state);
        brainLinkLookup.Update(ref state);
        needsActionLookup.Update(ref state);
        transformLookup.Update(ref state);

        float deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new BathroomAssignmentJob
        {
            targetPositionLookup = targetPositionLookup,
            unitActionLookup = unitActionLookup,
            brainLinkLookup = brainLinkLookup,
            needsActionLookup = needsActionLookup
        }.Schedule(state.Dependency);

        state.Dependency = new BathroomArrivalJob
        {
            transformLookup = transformLookup,
            brainLinkLookup = brainLinkLookup
        }.Schedule(state.Dependency);

        state.Dependency = new BathroomCompletionJob
        {
            deltaTime = deltaTime,
            needsActionLookup = needsActionLookup,
            unitActionLookup = unitActionLookup,
            brainLinkLookup = brainLinkLookup,
        }.Schedule(state.Dependency);
    }

    // -------------------------------------------------------
    // ASSIGNMENT — pick a winner, reject losers, send winner walking
    // -------------------------------------------------------
    [BurstCompile]
    public partial struct BathroomAssignmentJob : IJobEntity
    {
        public ComponentLookup<TargetPositionPathQueued> targetPositionLookup;
        public ComponentLookup<UnitAction> unitActionLookup;
        [ReadOnly] public ComponentLookup<BrainLink> brainLinkLookup;
        public ComponentLookup<NeedsAction> needsActionLookup;

        public void Execute(
            in BathroomInteraction bathroomInteraction,
            in Interaction interaction,
            in LocalTransform interactionTransform,
            DynamicBuffer<InteractionOccupant> occupants,
            EnabledRefRW<InteractionProvider> interactionProviderEnabled)
        {
            if (occupants.Length == 0)
                return;

            int winnerIndex = FindHighestScorer(in occupants);
            Entity winnerBrain = occupants[winnerIndex].entity;

            RejectLosers(in occupants, winnerIndex);
            AssignWinner(winnerBrain, interactionTransform.Position, interaction.actionType);

            interactionProviderEnabled.ValueRW = false;

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

        private void AssignWinner(Entity brainEntity, float3 interactionPosition, ActionType actionType)
        {
            if (!brainLinkLookup.TryGetComponent(brainEntity, out BrainLink brainLink))
                return;

            Entity body = brainLink.body;

            if (!targetPositionLookup.HasComponent(body))
                return;

            if (!unitActionLookup.HasComponent(body))
                return;

            targetPositionLookup[body] = new TargetPositionPathQueued
            {
                targetPosition = interactionPosition
            };
            targetPositionLookup.SetComponentEnabled(body, true);

            unitActionLookup[body] = new UnitAction
            {
                current = actionType
            };
        }
    }

    // -------------------------------------------------------
    // ARRIVAL — detect when the NPC reaches the interaction, start timer
    // -------------------------------------------------------
    [BurstCompile]
    [WithDisabled(typeof(InteractionTimer))]
    [WithDisabled(typeof(InteractionProvider))]
    public partial struct BathroomArrivalJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
        [ReadOnly] public ComponentLookup<BrainLink> brainLinkLookup;

        public void Execute(
            in BathroomInteraction bathroomInteraction,
            in Interaction interaction,
            in LocalTransform interactionTransform,
            in DynamicBuffer<InteractionOccupant> occupants,
            ref InteractionTimer timer,
            EnabledRefRW<InteractionTimer> timerEnabled)
        {
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
    // COMPLETION — tick timer, release NPC when done, restore bladder
    // -------------------------------------------------------
    [BurstCompile]
    [WithDisabled(typeof(InteractionProvider))]
    public partial struct BathroomCompletionJob : IJobEntity
    {
        public float deltaTime;
        public ComponentLookup<NeedsAction> needsActionLookup;
        public ComponentLookup<UnitAction> unitActionLookup;
        [ReadOnly] public ComponentLookup<BrainLink> brainLinkLookup;

        public void Execute(
            in BathroomInteraction bathroomInteraction,
            DynamicBuffer<InteractionOccupant> occupants,
            ref InteractionTimer timer,
            EnabledRefRW<InteractionTimer> timerEnabled,
            EnabledRefRW<InteractionProvider> interactionProviderEnabled)
        {
            timer.elapsed += deltaTime;

            if (timer.elapsed < timer.duration)
                return;

            for (int i = 0; i < occupants.Length; i++)
            {
                Entity brainEntity = occupants[i].entity;

                if (brainLinkLookup.TryGetComponent(brainEntity, out BrainLink brainLink))
                {
                    if (unitActionLookup.HasComponent(brainLink.body))
                    {
                        unitActionLookup[brainLink.body] = new UnitAction
                        {
                            current = ActionType.None
                        };
                    }
                }

                needsActionLookup.SetComponentEnabled(brainEntity, true);
            }

            occupants.Clear();

            timer.elapsed = 0f;
            timerEnabled.ValueRW = false;
            interactionProviderEnabled.ValueRW = true;
        }
    }
}