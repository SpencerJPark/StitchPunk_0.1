using DotsMovementToolkit;
using Unity.Burst;
using Unity.Entities;

// Maps the package's generic MovementStuck signal onto this game's ActionInterruptRequest —
// the single teardown path BehaviorInterruptSystem consumes. Runs same-frame, right after
// the package sets MovementStuck, so the interrupt is visible to StateMachineSystemGroup
// on the next frame the same as any other interrupt source.
[BurstCompile]
[UpdateInGroup(typeof(MovementCoordinatorSystemGroup))]
[UpdateAfter(typeof(PathStuckCheckSystem))]
public partial struct MovementStuckBridgeSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new MovementStuckBridgeJob().ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithPresent(typeof(ActionInterruptRequest))]
public partial struct MovementStuckBridgeJob : IJobEntity
{
    public void Execute(
        EnabledRefRW<MovementStuck>          movementStuckEnabled,
        EnabledRefRW<ActionInterruptRequest> interruptEnabled)
    {
        interruptEnabled.ValueRW    = true;
        movementStuckEnabled.ValueRW = false;
    }
}
