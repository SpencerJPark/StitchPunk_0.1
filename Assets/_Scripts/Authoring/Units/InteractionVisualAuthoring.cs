using Unity.Entities;
using UnityEngine;

public class InteractionVisualAuthoring : MonoBehaviour
{
    public class Baker : Baker<InteractionVisualAuthoring>
    {
        public override void Bake(InteractionVisualAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new InteractableVisual { Value = 0f });
        }
    }
}
