using Unity.Entities;
using UnityEngine;

public class BladderInteractionAuthoring : MonoBehaviour
{
    public int value;
    
    public class Baker : Baker<BladderInteractionAuthoring> {

        public override void Bake(BladderInteractionAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new BladderInteraction { value = authoring.value });
            AddComponent(entity, new InteractionValue { multiplier = authoring.value * 0.01f + 1f });
        }
    }
}

