using Unity.Entities;
using UnityEngine;

public class SafetyInteractionAuthoring : MonoBehaviour
{
    public int value;
    
    public class Baker : Baker<SafetyInteractionAuthoring> {

        public override void Bake(SafetyInteractionAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new SafetyInteraction { value = authoring.value });
            AddComponent(entity, new InteractionValue { multiplier = authoring.value * 0.01f + 1f });
        }
    }
}

