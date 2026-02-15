using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class InteractionTimerAuthoring : MonoBehaviour
{
    [Header("Waypoint Settings")]
    [Tooltip("How close the NPC must be to start the action")]
    public float interactionRange = 1.5f;

    [Tooltip("How far away NPCs can detect this waypoint (used by spatial hash query)")]
    public float broadcastRadius = 20f;
    
    public class Baker : Baker<InteractionTimerAuthoring>
    {
        public override void Bake(InteractionTimerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new InteractionTimer());
        }
    }
}

public struct InteractionTimer : IComponentData, IEnableableComponent
{
    public float duration;
    public float elapsed;
}