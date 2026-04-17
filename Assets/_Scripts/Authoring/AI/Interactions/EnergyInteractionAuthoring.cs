using Unity.Entities;
using UnityEngine;

public class EnergyInteractionAuthoring : MonoBehaviour {
    
    public int value;
    
    public class Baker : Baker<EnergyInteractionAuthoring> {

        public override void Bake(EnergyInteractionAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new EnergyInteraction { value = authoring.value });
            AddComponent(entity, new InteractionValue { multiplier = authoring.value * 0.01f + 1f });
        }
    }
}

