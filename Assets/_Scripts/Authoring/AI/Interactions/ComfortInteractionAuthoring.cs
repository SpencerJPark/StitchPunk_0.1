using Unity.Entities;
using UnityEngine;

public class ComfortInteractionAuthoring : MonoBehaviour
{
    public int value;
    
    public class Baker : Baker<ComfortInteractionAuthoring> {

        public override void Bake(ComfortInteractionAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new ComfortInteraction { value = authoring.value });
            AddComponent(entity, new InteractionValue { multiplier = authoring.value * 0.01f + 1f });
        }
    }
}

