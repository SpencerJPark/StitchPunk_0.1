using Unity.Entities;
using Unity.Mathematics;

namespace DotsMovementToolkit
{
/// <summary>
/// Static utility class for horde operations.
/// </summary>
public static class HordeUtils
{
    /// <summary>
    /// Create a new horde entity.
    /// </summary>
    public static Entity CreateHorde(EntityManager em, float3 targetPosition, Entity targetEntity = default)
    {
        var registry = em.CreateEntityQuery(typeof(HordeRegistry)).GetSingleton<HordeRegistry>();
        int hordeId = registry.nextHordeId++;
        
        // Update registry
        var registryEntity = em.CreateEntityQuery(typeof(HordeRegistry)).GetSingletonEntity();
        em.SetComponentData(registryEntity, registry);
        
        // Create horde entity
        var hordeEntity = em.CreateEntity();
        em.AddComponent<Horde>(hordeEntity);
        em.SetComponentData(hordeEntity, new Horde
        {
            hordeId = hordeId,
            targetPosition = targetPosition,
            targetEntity = targetEntity,
            flowFieldIndex = -1,
            memberCount = 0,
            isActive = true,
            needsPathUpdate = true
        });
        
        // Add member buffer
        em.AddBuffer<HordeMemberBuffer>(hordeEntity);
        
        return hordeEntity;
    }
    
    /// <summary>
    /// Add an entity to a horde.
    /// </summary>
    public static void JoinHorde(EntityManager em, Entity memberEntity, Entity hordeEntity, int priority = 0)
    {
        if (!em.HasComponent<Horde>(hordeEntity))
            return;
            
        var horde = em.GetComponentData<Horde>(hordeEntity);
        
        // Set membership on the entity
        if (em.HasComponent<HordeMembership>(memberEntity))
        {
            em.SetComponentData(memberEntity, new HordeMembership
            {
                hordeId = horde.hordeId,
                hordeEntity = hordeEntity,
                formationOffset = float2.zero,
                priority = priority
            });
            em.SetComponentEnabled<HordeMembership>(memberEntity, true);
        }
        
        // Add to horde's member buffer
        var buffer = em.GetBuffer<HordeMemberBuffer>(hordeEntity);
        buffer.Add(new HordeMemberBuffer { memberEntity = memberEntity });
        
        // Update pathfinding mode
        if (em.HasComponent<PathfindingAgent>(memberEntity))
        {
            var agent = em.GetComponentData<PathfindingAgent>(memberEntity);
            agent.currentMode = PathfindingMode.FlowField;
            em.SetComponentData(memberEntity, agent);
        }
    }
    
    /// <summary>
    /// Remove an entity from its current horde.
    /// </summary>
    public static void LeaveHorde(EntityManager em, Entity memberEntity)
    {
        if (!em.HasComponent<HordeMembership>(memberEntity))
            return;
            
        var membership = em.GetComponentData<HordeMembership>(memberEntity);
        var hordeEntity = membership.hordeEntity;
        
        // Disable membership
        em.SetComponentEnabled<HordeMembership>(memberEntity, false);
        em.SetComponentData(memberEntity, new HordeMembership
        {
            hordeId = -1,
            hordeEntity = Entity.Null,
            formationOffset = float2.zero,
            priority = int.MaxValue
        });
        
        // Remove from horde buffer
        if (hordeEntity != Entity.Null && em.HasBuffer<HordeMemberBuffer>(hordeEntity))
        {
            var buffer = em.GetBuffer<HordeMemberBuffer>(hordeEntity);
            for (int i = buffer.Length - 1; i >= 0; i--)
            {
                if (buffer[i].memberEntity == memberEntity)
                {
                    buffer.RemoveAt(i);
                    break;
                }
            }
        }
        
        // Revert to preferred pathfinding mode
        if (em.HasComponent<PathfindingAgent>(memberEntity))
        {
            var agent = em.GetComponentData<PathfindingAgent>(memberEntity);
            agent.currentMode = agent.preferredMode;
            em.SetComponentData(memberEntity, agent);
        }
        
        // Disable flow field follower
        if (em.HasComponent<FlowFieldFollower>(memberEntity))
        {
            em.SetComponentEnabled<FlowFieldFollower>(memberEntity, false);
        }
    }
    
    /// <summary>
    /// Update a horde's target position.
    /// </summary>
    public static void SetHordeTarget(EntityManager em, Entity hordeEntity, float3 targetPosition, Entity targetEntity = default)
    {
        if (!em.HasComponent<Horde>(hordeEntity))
            return;
            
        var horde = em.GetComponentData<Horde>(hordeEntity);
        horde.targetPosition = targetPosition;
        horde.targetEntity = targetEntity;
        horde.needsPathUpdate = true;
        em.SetComponentData(hordeEntity, horde);
    }
    
    /// <summary>
    /// Disband a horde, releasing all members to individual pathfinding.
    /// </summary>
    public static void DisbandHorde(EntityManager em, Entity hordeEntity)
    {
        if (!em.HasComponent<Horde>(hordeEntity))
            return;
        
        // Get all members and remove them
        if (em.HasBuffer<HordeMemberBuffer>(hordeEntity))
        {
            var buffer = em.GetBuffer<HordeMemberBuffer>(hordeEntity);
            for (int i = 0; i < buffer.Length; i++)
            {
                LeaveHorde(em, buffer[i].memberEntity);
            }
        }
        
        // Mark horde as inactive
        var horde = em.GetComponentData<Horde>(hordeEntity);
        horde.isActive = false;
        horde.memberCount = 0;
        em.SetComponentData(hordeEntity, horde);
    }
}
}