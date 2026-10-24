using Unity.Entities;
using Unity.Mathematics;

namespace DotsMovementToolkit
{
    // Values are serialized on authoring components and in PathRequests, so append only.
    public enum PathfindingMode : byte
    {
        // Single-agent only: every D* Lite agent shares one search grid (see README Known Issues).
        DStarLite = 0,
        FlowField = 1,
        Stop = 2,
        // The default for individual characters: per-agent path, string-pulled into straight segments.
        AStar = 3
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
        // Set by CrowdFlowFieldSwitchSystem when it turned this agent's A* request into a shared
        // flow field, so it knows to switch the agent back once the crowd thins out.
        public bool crowdFlowFieldActive;
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

    public struct AStarFollower : IComponentData, IEnableableComponent
    {
        public int currentWaypointIndex;
        public float3 targetPosition;
        public float3 lastMoveDirection;
        public int currentLayer;
        // What the current path was planned against. A repeat request for the same goal node on an
        // unchanged cost map keeps the path instead of searching again.
        public int goalNode;
        public uint costMapVersion;
    }

    // Corner points of the agent's current A* path, start excluded, final target last.
    [InternalBufferCapacity(8)]
    public struct AStarPathWaypoint : IBufferElementData
    {
        public float3 position;
        public int layer;
    }

    public struct FlowFieldFollower : IComponentData, IEnableableComponent
    {
        public float3 targetPosition;
        public float3 lastMoveVector;
        public int flowFieldIndex;
        public int currentLayer;
    }
}
