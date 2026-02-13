using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class InteractionProviderAuthoring : MonoBehaviour
{
    [Header("Waypoint Settings")]
    [Tooltip("How close the NPC must be to start the action")]
    public float interactionRange = 1.5f;

    [Tooltip("How far away NPCs can detect this waypoint (used by spatial hash query)")]
    public float broadcastRadius = 20f;

    [Tooltip("Max NPCs that can use this waypoint at once")]
    public int maxOccupants = 1;

    [Tooltip("Optional: a child transform the NPC walks to before starting the action")]
    public Transform approachPoint;

    [Header("Actions This Waypoint Offers")]
    public List<InteractionActionDef> actions;

    [System.Serializable]
    public struct InteractionActionDef
    {
        public ActionType actionType;
        public AnimationType animation;
        public InteractionActionBehavior behavior;

        [Tooltip("How long the action takes in seconds")]
        public float duration;

        [Tooltip("Only used for WanderArea behavior: radius to wander around the waypoint")]
        public float wanderRadius;
    }

    public class Baker : Baker<InteractionProviderAuthoring>
    {
        public override void Bake(InteractionProviderAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            Entity approachEntity = authoring.approachPoint != null
                ? GetEntity(authoring.approachPoint, TransformUsageFlags.Dynamic)
                : Entity.Null;

            AddComponent(entity, new InteractionProvider
            {
                interactionRange = authoring.interactionRange,
                broadcastRadius = authoring.broadcastRadius,
                maxOccupants = authoring.maxOccupants,
                approachPoint = approachEntity,
                currentOccupants = 0
            });

            DynamicBuffer<Interaction> actionBuffer = AddBuffer<Interaction>(entity);
            foreach (InteractionActionDef action in authoring.actions)
            {
                actionBuffer.Add(new Interaction
                {
                    actionType = action.actionType,
                    animation = action.animation,
                    behavior = action.behavior,
                    duration = action.duration,
                    wanderRadius = action.wanderRadius,
                });
            }

            AddBuffer<InteractionOccupant>(entity);
        }
    }
}

// ===============================================================
// WAYPOINT DATA TYPES
// ===============================================================

public struct InteractionProvider : IComponentData
{
    public float interactionRange;
    public float broadcastRadius;
    public int maxOccupants;
    public int currentOccupants;
    public Entity approachPoint;
}

public struct Interaction : IBufferElementData
{
    public ActionType actionType;
    public AnimationType animation;
    public InteractionActionBehavior behavior;
    public float duration;
    public float wanderRadius;
}

public struct InteractionOccupant : IBufferElementData
{
    public Entity brain;
}

// ===============================================================
// WAYPOINT ACTION BEHAVIOR
// ===============================================================

/// <summary>
/// Defines what happens when an NPC arrives at a waypoint and begins their action.
/// Each behavior is handled differently by AIExecutionSystem.
/// </summary>
public enum InteractionActionBehavior
{
    /// <summary>
    /// NPC plays an animation at the waypoint position for the specified duration.
    /// Good for: eating, sleeping, sitting, working, smoking, using bathroom.
    /// </summary>
    AnimateInPlace,

    /// <summary>
    /// NPC wanders randomly within the waypoint's wanderRadius for the specified duration.
    /// Good for: socializing in a park, exploring an area, patrolling a zone.
    /// </summary>
    WanderArea,

    /// <summary>
    /// NPC stands still at the waypoint for the specified duration.
    /// Good for: waiting, guarding, resting.
    /// </summary>
    IdleInPlace,
}