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
            
            AddComponent(entity, new InteractionHandled());
            SetComponentEnabled<InteractionHandled>(entity, false);
        }
    }
}

