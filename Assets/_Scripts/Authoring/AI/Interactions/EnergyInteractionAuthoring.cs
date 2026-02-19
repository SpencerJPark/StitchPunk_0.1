using Unity.Entities;
using UnityEngine;

public class EnergyInteractionAuthoring : MonoBehaviour {
    
    public int value;
    
    public class Baker : Baker<EnergyInteractionAuthoring> {

        public override void Bake(EnergyInteractionAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new EnergyInteraction
            {
                value = authoring.value
            });
        }
    }
}

public struct EnergyInteraction : IComponentData {
    public int value;
}