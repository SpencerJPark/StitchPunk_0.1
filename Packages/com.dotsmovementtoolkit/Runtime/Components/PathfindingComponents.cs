using Unity.Entities;
using Unity.Mathematics;

namespace DotsMovementToolkit
{
    public enum PathfindingMode : byte
    {
        DStarLite = 0,
        FlowField = 1,
        Stop = 2
    }

    public struct PathfindingAgent : IComponentData
    {
        public PathfindingMode preferredMode;
        public PathfindingMode currentMode;
        public float repathInterval;
        public float timeSinceLastRepath;
        public int hordeFormationThreshold;
        public bool needsRepath;
        public float3 targetPosition;
        public bool isActive;
        // 0 = use coordinator default (cellSize * 0.5f). Set by requesters that
        // want to halt short of the target (e.g. combat stopping at attack range).
        public float stoppingDistance;
    }

    public struct PathRequest : IComponentData, IEnableableComponent
    {
        public float3 targetPosition;
        public PathfindingMode requestedMode;
        public float stoppingDistance;
    }

    public struct StuckDetector : IComponentData
    {
        public float3 lastSampledPosition;
        public float3 lastTargetPosition;
        public float  stuckAccumulator;
        public float  sampleTimer;
    }

    // Fired by PathStuckCheckSystem when a unit has an active PathRequest but is not making
    // progress. Package-owned so it carries no game-specific meaning; a consumer maps it to
    // whatever "cancel the current action" concept its own game uses (see the game's
    // MovementStuckBridgeSystem for this project's mapping to ActionInterruptRequest).
    public struct MovementStuck : IComponentData, IEnableableComponent
    {
    }

    public struct DStarLiteFollower : IComponentData, IEnableableComponent
    {
        public int currentNodeIndex;
        public int goalNodeIndex;
        public float3 nextWaypoint;
        public float3 targetPosition;
        public int pathDataIndex;
        public float3 lastMoveDirection;
        public int currentLayer;
    }

    public struct FlowFieldFollower : IComponentData, IEnableableComponent
    {
        public float3 targetPosition;
        public float3 lastMoveVector;
        public int flowFieldIndex;
        public int currentLayer;
    }
}
