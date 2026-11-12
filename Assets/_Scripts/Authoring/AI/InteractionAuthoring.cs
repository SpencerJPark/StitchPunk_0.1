using Unity.Entities;
using UnityEngine;

public class InteractionAuthoring : MonoBehaviour
{
    [Header("Interaction Settings")]
    public ActionType actionType;

    [Header("Player")]
    [Tooltip("Whether the player can directly target and interact with this entity.")]
    public bool playerInteractable;

    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<InteractionAuthoring>
    {
        public override void Bake(InteractionAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new Interaction
            {
                action = authoring.actionType,
            });
            SetComponentEnabled<Interaction>(entity, true);

            if (authoring.playerInteractable) AddComponent(entity, new PlayerInteractable());
        }
    }
}
