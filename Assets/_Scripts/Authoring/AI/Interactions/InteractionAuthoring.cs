using Unity.Entities;
using UnityEngine;

public class InteractionAuthoring : MonoBehaviour
{
    [Header("Interaction Settings")]
    [Tooltip("How close the NPC must be to start the action")]
    public float interactionRange = 1.5f;

    public int maxOccupant = 1;

    public float maxTime = 0.5f;
    
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
                maxOccupants = authoring.maxOccupant
            });
            
            AddComponent(entity, new InteractionTimer
            {
                maxTime = authoring.maxTime,
            });
            SetComponentEnabled<InteractionTimer>(entity, false);

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
    public int maxOccupants;
}

public struct InteractionTimer : IComponentData, IEnableableComponent
{
    public float maxTime;
    public float duration;
    public float elapsed;
}

public struct InteractionOccupant : IBufferElementData
{
    public Entity entity;
    public float score;
}