using Unity.Burst;
using Unity.Entities;

// The locomotion bridge: syncs StateMachine.currentStance (written by the Approach and
// FleeFromTarget behavior commands) into Movement.isRunning (read by UnitMoverSystem to
// pick runSpeed over moveSpeed) and LocomotionStance.stance (read by
// UnitAnimationAssignmentSystem to pick stance idle/moving animations).
[UpdateInGroup(typeof(MovementExecutionSystemGroup))]
[UpdateBefore(typeof(UnitMoverSystem))]
public partial struct LocomotionStanceSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new LocomotionStanceSyncJob().ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct LocomotionStanceSyncJob : IJobEntity
{
    public void Execute(
        in StateMachine      stateMachine,
        ref Movement         movement,
        ref LocomotionStance locomotionStance)
    {
        bool shouldRun = stateMachine.currentStance == StanceType.Running;
        if (movement.isRunning != shouldRun)
            movement.isRunning = shouldRun;

        if (locomotionStance.stance != stateMachine.currentStance)
            locomotionStance.stance = stateMachine.currentStance;
    }
}
