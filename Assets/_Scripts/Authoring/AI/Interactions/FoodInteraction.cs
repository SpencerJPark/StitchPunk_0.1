using Unity.Entities;
using UnityEngine;

public class FoodInteractionAuthoring : MonoBehaviour {
    
    public class Baker : Baker<FoodInteractionAuthoring> {

        public override void Bake(FoodInteractionAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new FoodInteraction());
        }
    }
}

public struct FoodInteraction : IComponentData {
}