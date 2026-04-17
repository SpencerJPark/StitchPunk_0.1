using Unity.Entities;
using UnityEngine;

public class MovementInteractionAuthoring : MonoBehaviour
{
    public int value;
    
    public class Baker : Baker<MovementInteractionAuthoring> {

        public override void Bake(MovementInteractionAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new MovementInteraction { value = authoring.value });
            AddComponent(entity, new InteractionValue { multiplier = authoring.value * 0.01f + 1f });
        }
    }
}

