using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateBefore(typeof(ReviveSystem))]
public partial struct DeathSystem : ISystem
{
    private ComponentLookup<ActiveBrain> activeBrainLookup;
    private ComponentLookup<NeedsAction> needsActionLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        activeBrainLookup = state.GetComponentLookup<ActiveBrain>(false);
        needsActionLookup = state.GetComponentLookup<NeedsAction>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        activeBrainLookup.Update(ref state);
        needsActionLookup.Update(ref state);

        state.Dependency = new DeathJob
        {
            activeBrainLookup = activeBrainLookup,
            needsActionLookup = needsActionLookup,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithPresent(typeof(PathRequest))]
[WithPresent(typeof(DStarLiteFollower))]
[WithPresent(typeof(FlowFieldFollower))]
[WithPresent(typeof(HordeMembership))]
public partial struct DeathJob : IJobEntity
{
    public ComponentLookup<ActiveBrain> activeBrainLookup;
    public ComponentLookup<NeedsAction> needsActionLookup;

    public void Execute(
        Entity entity,
        in Dead dead,
        in Health health,
        in LocalTransform transform,
        ref UnitAction unitAction,
        ref Movement mover,
        EnabledRefRW<Alive> aliveEnabled,
        EnabledRefRW<PathRequest> pathRequestEnabled,
        EnabledRefRW<DStarLiteFollower> dStarEnabled,
        EnabledRefRW<FlowFieldFollower> flowFieldEnabled,
        EnabledRefRW<HordeMembership> hordeMembershipEnabled)
    {
        // 1. Flip life/death state flags on this entity
        unitAction.current = ActionType.Death;
        aliveEnabled.ValueRW = false;
        pathRequestEnabled.ValueRW = false;
        dStarEnabled.ValueRW = false;
        flowFieldEnabled.ValueRW = false;
        hordeMembershipEnabled.ValueRW = false;

        // 2. Stop movement and snap target
        mover.isMoving = false;
        mover.targetPosition = transform.Position;

        // 3. Stop AI state — lives on the same entity in the single-entity model.
        //    Use lookups so units without AI components (e.g. player) are handled safely.
        if (activeBrainLookup.HasComponent(entity))
            activeBrainLookup.SetComponentEnabled(entity, false);

        if (needsActionLookup.HasComponent(entity))
            needsActionLookup.SetComponentEnabled(entity, false);
    }
}
