using Unity.Entities;
using UnityEngine;

public class InteractionVisualAuthoring : MonoBehaviour
{
    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<InteractionVisualAuthoring>
    {
        public override void Bake(InteractionVisualAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new InteractableVisual { value = 0f });
        }
    }
}
