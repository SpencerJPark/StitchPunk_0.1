using Unity.Entities;
using Unity.Mathematics;

public enum PathfindingMode : byte
{
    DStarLite = 0,
    FlowField = 1,
    None = 2
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
