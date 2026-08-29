using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsMovementToolkit
{
// Detects units that have an active PathRequest but are not making progress.
// Fires ActionInterruptRequest after STUCK_TIMEOUT seconds of consecutive non-movement,
// letting the existing interrupt pipeline cancel whatever action owns the path.
[BurstCompile]
[UpdateInGroup(typeof(MovementCoordinatorSystemGroup))]
public partial struct PathStuckCheckSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new PathStuckCheckJob
        {
            deltaTime = SystemAPI.Time.DeltaTime,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(PathRequest))]
[WithDisabled(typeof(ActionInterruptRequest))]
public partial struct PathStuckCheckJob : IJobEntity
{
    private const float SAMPLE_INTERVAL = 1.0f;
    private const float STUCK_MOVE_SQ   = 1.0f;
    private const float STUCK_TIMEOUT   = 4.0f;

    public float deltaTime;

    public void Execute(
        in LocalTransform                    transform,
        in PathRequest                       pathRequest,
        ref StuckDetector                    stuckDetector,
        EnabledRefRW<ActionInterruptRequest> interruptEnabled)
    {
        // Intentionally stationary (e.g. attacking in range, where the melee action halts
        // pathing every frame) — not stuck. Only units actively pathing can be stuck.
        if (pathRequest.requestedMode == PathfindingMode.Stop)
        {
            stuckDetector.stuckAccumulator = 0f;
            stuckDetector.sampleTimer      = 0f;
            return;
        }

        // New path request — reset all detector state
        if (!stuckDetector.lastTargetPosition.Equals(pathRequest.targetPosition))
        {
            stuckDetector.lastTargetPosition  = pathRequest.targetPosition;
            stuckDetector.lastSampledPosition = transform.Position;
            stuckDetector.stuckAccumulator    = 0f;
            stuckDetector.sampleTimer         = 0f;
            return;
        }

        stuckDetector.sampleTimer += deltaTime;
        if (stuckDetector.sampleTimer < SAMPLE_INTERVAL)
            return;

        float distMovedSq = math.distancesq(transform.Position, stuckDetector.lastSampledPosition);
        stuckDetector.lastSampledPosition = transform.Position;
        stuckDetector.sampleTimer         = 0f;

        if (distMovedSq >= STUCK_MOVE_SQ)
        {
            stuckDetector.stuckAccumulator = 0f;
            return;
        }

        stuckDetector.stuckAccumulator += SAMPLE_INTERVAL;
        if (stuckDetector.stuckAccumulator >= STUCK_TIMEOUT)
        {
            interruptEnabled.ValueRW       = true;
            stuckDetector.stuckAccumulator = 0f;
        }
    }
}
}
