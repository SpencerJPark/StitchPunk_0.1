using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsMovementToolkit
{
    // The package's entry points for requesting/cancelling a path. Any system may also write
    // Movement.targetPosition directly (that is how PlayerMoveSystem works and it stays
    // supported) — these two helpers are for the PathRequest-driven pathfinding pipeline.
    [BurstCompile]
    public static class MovementAPI
    {
        public static void BeginPathRequest(
            ref PathRequest            pathRequest,
            EnabledRefRW<PathRequest>  pathRequestEnabled,
            float3                     targetPosition,
            float                      stoppingDistance = 0f,
            PathfindingMode            mode             = PathfindingMode.DStarLite)
        {
            pathRequest.targetPosition   = targetPosition;
            pathRequest.requestedMode    = mode;
            pathRequest.stoppingDistance = stoppingDistance;
            pathRequestEnabled.ValueRW   = true;
        }

        public static void HaltPathing(
            ref PathRequest pathRequest,
            EnabledRefRW<PathRequest> pathRequestEnabled)
        {
            pathRequest.requestedMode  = PathfindingMode.Stop;
            pathRequestEnabled.ValueRW = true;
        }
    }
}
