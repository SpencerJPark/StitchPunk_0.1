using DotsMovementToolkit;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

// Generic, so any knock (the caravan today) reuses it: a ShoveMotion slides the unit, a StunnedState pins it in
// place for its duration. Both work by pinning Movement.targetPosition to where the unit is, so it runs before
// UnitMoverSystem and the mover has nowhere to go. A stunned unit can still swing at what is in reach.
[BurstCompile]
[UpdateInGroup(typeof(MovementExecutionSystemGroup))]
[UpdateBefore(typeof(UnitMoverSystem))]
public partial struct StunAndShoveSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        if (deltaTime <= 0f) return;

        state.Dependency = new ShoveJob { deltaTime = deltaTime }.ScheduleParallel(state.Dependency);
        state.Dependency = new StunJob { deltaTime = deltaTime }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct ShoveJob : IJobEntity
{
    public float deltaTime;

    private void Execute(ref LocalTransform transform, ref Movement movement, ref ShoveMotion shoveMotion,
        EnabledRefRW<ShoveMotion> shoveMotionEnabled)
    {
        float slideSeconds = deltaTime < shoveMotion.remainingSeconds ? deltaTime : shoveMotion.remainingSeconds;
        transform.Position += shoveMotion.velocity * slideSeconds;
        movement.targetPosition = transform.Position;
        shoveMotion.remainingSeconds -= deltaTime;
        if (shoveMotion.remainingSeconds <= 0f)
            shoveMotionEnabled.ValueRW = false;
    }
}

[BurstCompile]
public partial struct StunJob : IJobEntity
{
    public float deltaTime;

    private void Execute(in LocalTransform transform, ref Movement movement, ref StunnedState stunnedState,
        EnabledRefRW<StunnedState> stunnedStateEnabled)
    {
        movement.targetPosition = transform.Position;
        stunnedState.duration -= deltaTime;
        if (stunnedState.duration <= 0f)
            stunnedStateEnabled.ValueRW = false;
    }
}
