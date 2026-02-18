using Unity.Entities;
using UnityEngine;

public class InteractionAuthoring : MonoBehaviour
{
    [Header("Interaction Settings")]
    [Tooltip("How close the NPC must be to start the action")]
    public float interactionRange = 1.5f;
    
    [Tooltip("Interaction ActionEnum for when performed")]
    public ActionType actionType = ActionType.Interact;

    public class Baker : Baker<InteractionAuthoring>
    {
        public override void Bake(InteractionAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new Interaction
            {
                interactionRange = authoring.interactionRange,
                actionType = authoring.actionType,
            });

            AddComponent(entity, new InteractionProvider());
            SetComponentEnabled<InteractionProvider>(entity, true);

            AddBuffer<InteractionOccupant>(entity);
        }
    }
}

public struct InteractionProvider : IComponentData, IEnableableComponent
{
}

public struct Interaction : IComponentData
{
    public float interactionRange;
    public ActionType actionType;
}

public struct InteractionOccupant : IBufferElementData
{
    public Entity entity;
    public float score;
}