using Unity.Entities;
using UnityEngine;

public class BathroomInteractionAuthoring : MonoBehaviour {
    
    public class Baker : Baker<BathroomInteractionAuthoring> {

        public override void Bake(BathroomInteractionAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new BathroomInteraction());
        }
    }
}

public struct BathroomInteraction : IComponentData {
}