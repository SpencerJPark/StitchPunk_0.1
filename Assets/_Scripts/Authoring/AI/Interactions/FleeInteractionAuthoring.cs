using Unity.Entities;
using UnityEngine;

public class FleeInteractionAuthoring : MonoBehaviour {
    
    public class Baker : Baker<FleeInteractionAuthoring> {

        public override void Bake(FleeInteractionAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new FleeInteraction());
        }
    }
}

public struct FleeInteraction : IComponentData {
}