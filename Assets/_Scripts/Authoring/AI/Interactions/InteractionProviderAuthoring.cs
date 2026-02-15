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
    
    public class Baker : Baker<InteractionProviderAuthoring>
    {
        public override void Bake(InteractionProviderAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new InteractionProvider
            {
                interactionRange = authoring.interactionRange,
                broadcastRadius = authoring.broadcastRadius,
            });
            SetComponentEnabled<InteractionProvider>(entity, true);

            AddBuffer<InteractionOccupant>(entity);
        }
    }
}

public struct InteractionProvider : IComponentData, IEnableableComponent
{
    public float interactionRange;
    public float broadcastRadius;
}

public struct InteractionOccupant : IBufferElementData
{
    public Entity entity;
    public float score;
}
