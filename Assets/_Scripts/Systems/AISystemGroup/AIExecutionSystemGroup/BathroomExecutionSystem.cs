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
    private ComponentLookup<BladderMotivation> bladderLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BladderInteraction>();

        targetPositionLookup = state.GetComponentLookup<TargetPositionPathQueued>(false);
        unitActionLookup = state.GetComponentLookup<UnitAction>(false);
        brainLinkLookup = state.GetComponentLookup<BrainLink>(true);
        needsActionLookup = state.GetComponentLookup<NeedsAction>(false);
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        bladderLookup = state.GetComponentLookup<BladderMotivation>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        targetPositionLookup.Update(ref state);
        unitActionLookup.Update(ref state);
        brainLinkLookup.Update(ref state);
        needsActionLookup.Update(ref state);
        transformLookup.Update(ref state);
        bladderLookup.Update(ref state);

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
            bladderLookup = bladderLookup
        }.Schedule(state.Dependency);
    }

    // -------------------------------------------------------
    // ASSIGNMENT — select top N winners, reject the rest, send winners walking
    // -------------------------------------------------------
    [BurstCompile]
    public partial struct BathroomAssignmentJob : IJobEntity
    {
        public ComponentLookup<TargetPositionPathQueued> targetPositionLookup;
        public ComponentLookup<UnitAction> unitActionLookup;
        [ReadOnly] public ComponentLookup<BrainLink> brainLinkLookup;
        public ComponentLookup<NeedsAction> needsActionLookup;

        public void Execute(
            in BladderInteraction bladderInteraction,
            in Interaction interaction,
            in LocalTransform interactionTransform,
            DynamicBuffer<InteractionOccupant> occupants,
            EnabledRefRW<InteractionProvider> interactionProviderEnabled)
        {
            if (occupants.Length == 0)
                return;

            AIUtil.SelectWinners(occupants, interaction.maxOccupants, ref needsActionLookup);
            AIUtil.AssignWinners(in occupants, interactionTransform.Position, interaction.actionType,
                ref brainLinkLookup, ref targetPositionLookup, ref unitActionLookup);

            interactionProviderEnabled.ValueRW = false;
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
            in BladderInteraction bladderInteraction,
            in Interaction interaction,
            in LocalTransform interactionTransform,
            in DynamicBuffer<InteractionOccupant> occupants,
            ref InteractionTimer timer,
            EnabledRefRW<InteractionTimer> timerEnabled)
        {
            if (AIUtil.CheckArrival(in occupants, in interactionTransform, interaction.interactionRange,
                    ref brainLinkLookup, ref transformLookup))
            {
                timer.elapsed = 0f;
                timer.duration = timer.maxTime;
                timerEnabled.ValueRW = true;
            }
        }
    }
    
    // -------------------------------------------------------
    // COMPLETION — tick timer, release NPCs when done, restore bladder
    // -------------------------------------------------------
    [BurstCompile]
    [WithDisabled(typeof(InteractionProvider))]
    public partial struct BathroomCompletionJob : IJobEntity
    {
        public float deltaTime;
        public ComponentLookup<NeedsAction> needsActionLookup;
        public ComponentLookup<UnitAction> unitActionLookup;
        [ReadOnly] public ComponentLookup<BrainLink> brainLinkLookup;
        public ComponentLookup<BladderMotivation> bladderLookup;

        public void Execute(
            in BladderInteraction bladderInteraction,
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

                if (bladderLookup.HasComponent(brainEntity))
                {
                    bladderLookup[brainEntity] = new BladderMotivation { value = 100 };
                }
            }

            AIUtil.ReleaseOccupants(occupants, ref needsActionLookup, ref unitActionLookup, ref brainLinkLookup);

            timer.elapsed = 0f;
            timerEnabled.ValueRW = false;
            interactionProviderEnabled.ValueRW = true;
        }
    }
}