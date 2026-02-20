using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;


public class PathfindingAuthoring : MonoBehaviour
{
    [Header("Pathfinding Settings")]
    [Tooltip("Preferred algorithm when not in a horde")]
    public PathfindingMode preferredMode = PathfindingMode.DStarLite;
    
    [Header("D* Lite Settings (for individual pathfinding)")]
    public float repathInterval = 0.5f;
    
    [Header("Horde Settings")]
    [Tooltip("If this many entities share the same target, consider forming/joining a horde")]
    public int hordeFormationThreshold = 10;

    public class Baker : Baker<PathfindingAuthoring>
    {
        public override void Bake(PathfindingAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new PathfindingAgent
            {
                preferredMode = authoring.preferredMode,
                currentMode = authoring.preferredMode,
                repathInterval = authoring.repathInterval,
                timeSinceLastRepath = 0f,
                hordeFormationThreshold = authoring.hordeFormationThreshold
            });
            
            // Add D* Lite component (disabled by default)
            AddComponent(entity, new DStarLiteFollower());
            SetComponentEnabled<DStarLiteFollower>(entity, false);
            
            // Add Flow Field component (disabled by default) 
            AddComponent(entity, new FlowFieldFollower());
            SetComponentEnabled<FlowFieldFollower>(entity, false);
            
            // Add horde membership (disabled by default - not in a horde)
            AddComponent(entity, new HordeMembership
            {
                hordeId = -1,
                hordeEntity = Entity.Null,
                formationOffset = float2.zero,
                priority = int.MaxValue
            });
            SetComponentEnabled<HordeMembership>(entity, false);
            
            // Add path request component
            AddComponent(entity, new PathRequest());
            SetComponentEnabled<PathRequest>(entity, false);
        }
    }
}

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

public struct HordeMembership : IComponentData, IEnableableComponent
{
    public int hordeId;
    public Entity hordeEntity;
    public float2 formationOffset;
    public int priority;
}