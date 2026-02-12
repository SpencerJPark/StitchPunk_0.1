using Unity.Entities;
using UnityEngine;

public class EnergyInteractionAuthoring : MonoBehaviour {
    
    public class Baker : Baker<EnergyInteractionAuthoring> {

        public override void Bake(EnergyInteractionAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new EnergyInteraction());
        }
    }
}

public struct EnergyInteraction : IComponentData {
}