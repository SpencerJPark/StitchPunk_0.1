using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Debug system to trace the full AI + Pathfinding flow.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
public partial struct PathfindingDebugSystem : ISystem
{
    private float lastLogTime;
    
    public void OnUpdate(ref SystemState state)
    {
        float time = (float)SystemAPI.Time.ElapsedTime;
        
        if (time - lastLogTime < 3f)
            return;
            
        lastLogTime = time;
        
        // Count brain entities
        int brainCount = 0;
        int brainNeedsActionEnabled = 0;
        int brainHasOptions = 0;
        int brainHasSelectedAction = 0;
        
        foreach (var (brainLink, needsAction, options, selectedAction, entity) in 
            SystemAPI.Query<RefRO<BrainLink>, RefRO<NeedsAction>, DynamicBuffer<ActionOption>, RefRO<SelectedAction>>()
            .WithPresent<NeedsAction>()
            .WithEntityAccess())
        {
            brainCount++;
            
            if (SystemAPI.IsComponentEnabled<NeedsAction>(entity))
                brainNeedsActionEnabled++;
                
            if (options.Length > 0)
                brainHasOptions++;
                
            if (selectedAction.ValueRO.current != Entity.Null)
                brainHasSelectedAction++;
        }
        
        Debug.Log($"[BRAIN] Count:{brainCount} NeedsAction:{brainNeedsActionEnabled} HasOptions:{brainHasOptions} HasSelected:{brainHasSelectedAction}");
        
        // Count body entities with pathfinding
        int bodyCount = 0;
        int bodyPathReqEnabled = 0;
        int bodyAgentActive = 0;
        int bodyDStarEnabled = 0;
        int bodyFlowFieldEnabled = 0;
        int bodyIsMoving = 0;
        
        foreach (var (agent, mover, entity) in 
            SystemAPI.Query<RefRO<PathfindingAgent>, RefRO<UnitMover>>()
            .WithEntityAccess())
        {
            bodyCount++;
            
            if (SystemAPI.HasComponent<PathRequest>(entity) && SystemAPI.IsComponentEnabled<PathRequest>(entity))
                bodyPathReqEnabled++;
                
            if (agent.ValueRO.isActive)
                bodyAgentActive++;
                
            if (SystemAPI.HasComponent<DStarLiteFollower>(entity) && SystemAPI.IsComponentEnabled<DStarLiteFollower>(entity))
                bodyDStarEnabled++;
                
            if (SystemAPI.HasComponent<FlowFieldFollower>(entity) && SystemAPI.IsComponentEnabled<FlowFieldFollower>(entity))
                bodyFlowFieldEnabled++;
                
            if (mover.ValueRO.isMoving)
                bodyIsMoving++;
        }
        
        Debug.Log($"[BODY] Count:{bodyCount} PathReq:{bodyPathReqEnabled} AgentActive:{bodyAgentActive} DStar:{bodyDStarEnabled} FlowField:{bodyFlowFieldEnabled} Moving:{bodyIsMoving}");
        
        // Count interactions
        int interactionCount = 0;
        int interactionProviderEnabled = 0;
        int interactionHasOccupants = 0;
        
        foreach (var (interaction, occupants, entity) in 
            SystemAPI.Query<RefRO<Interaction>, DynamicBuffer<InteractionOccupant>>()
            .WithPresent<InteractionProvider>()
            .WithEntityAccess())
        {
            interactionCount++;
            
            if (SystemAPI.IsComponentEnabled<InteractionProvider>(entity))
                interactionProviderEnabled++;
                
            if (occupants.Length > 0)
                interactionHasOccupants++;
        }
        
        Debug.Log($"[INTERACTION] Count:{interactionCount} ProviderEnabled:{interactionProviderEnabled} HasOccupants:{interactionHasOccupants}");
        
        // Check spatial hash
        if (SystemAPI.HasSingleton<SpatialHashRegistry>())
        {
            var registry = SystemAPI.GetSingleton<SpatialHashRegistry>();
            Debug.Log($"[SPATIAL] InteractionCells:{registry.interactionCells.Count()} WaypointCells:{registry.waypointCells.Count()}");
        }
        else
        {
            Debug.LogWarning("[SPATIAL] No SpatialHashRegistry singleton!");
        }
        
        // Log first brain in detail
        foreach (var (brainLink, awareness, bladder, transform, entity) in 
            SystemAPI.Query<RefRO<BrainLink>, RefRO<Awareness>, RefRO<BladderMotivation>, RefRO<LocalTransform>>()
            .WithEntityAccess())
        {
            bool needsActionEnabled = SystemAPI.HasComponent<NeedsAction>(entity) && SystemAPI.IsComponentEnabled<NeedsAction>(entity);
            Entity bodyEntity = brainLink.ValueRO.body;
            
            Debug.Log($"[BRAIN {entity.Index}] Pos:{transform.ValueRO.Position} Awareness:{awareness.ValueRO.range} Bladder:{bladder.ValueRO.value} NeedsAction:{needsActionEnabled} Body:{bodyEntity.Index}");
            
            if (bodyEntity != Entity.Null && SystemAPI.HasComponent<UnitMover>(bodyEntity))
            {
                var mover = SystemAPI.GetComponent<UnitMover>(bodyEntity);
                var bodyTransform = SystemAPI.GetComponent<LocalTransform>(bodyEntity);
                
                bool hasPathReq = SystemAPI.HasComponent<PathRequest>(bodyEntity);
                bool pathReqEnabled = hasPathReq && SystemAPI.IsComponentEnabled<PathRequest>(bodyEntity);
                
                bool hasAgent = SystemAPI.HasComponent<PathfindingAgent>(bodyEntity);
                bool agentActive = hasAgent && SystemAPI.GetComponent<PathfindingAgent>(bodyEntity).isActive;
                
                bool hasDStar = SystemAPI.HasComponent<DStarLiteFollower>(bodyEntity);
                bool dstarEnabled = hasDStar && SystemAPI.IsComponentEnabled<DStarLiteFollower>(bodyEntity);
                
                float3 target = mover.targetPosition;
                float dist = math.distance(bodyTransform.Position, target);
                
                Debug.Log($"  [BODY {bodyEntity.Index}] Pos:{bodyTransform.Position} Target:{target} Dist:{dist:F2} Speed:{mover.moveSpeed} Moving:{mover.isMoving} PathReq:{pathReqEnabled} AgentActive:{agentActive} DStar:{dstarEnabled}");
                
                if (hasDStar && dstarEnabled)
                {
                    var follower = SystemAPI.GetComponent<DStarLiteFollower>(bodyEntity);
                    Debug.Log($"    [DSTAR] PathIdx:{follower.pathDataIndex} NextWP:{follower.nextWaypoint} GoalIdx:{follower.goalNodeIndex}");
                }
            }
            
            break; // Only log first
        }
    }
}