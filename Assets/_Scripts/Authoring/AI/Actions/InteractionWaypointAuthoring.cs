using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class InteractionWaypointAuthoring : MonoBehaviour
{
    public AnimationType interactionAnimation;
    public float interactionTime;
    
    public class Baker : Baker<InteractionWaypointAuthoring>
    {
        public override void Bake(InteractionWaypointAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new InteractionWaypoint
            {
                interactionAnimation = authoring.interactionAnimation,
                interactionTime = authoring.interactionTime,
                interactable = true,
            });
        }
    }
}

public struct InteractionWaypoint : IComponentData
{
    public AnimationType interactionAnimation;
    public float interactionTime;
    public float timer;
    
    public bool interactable;
    public Entity interactingEntity;
}