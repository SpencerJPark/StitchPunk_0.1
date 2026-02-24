using Unity.Entities;
using UnityEngine;

public class HungerInteractionAuthoring : MonoBehaviour {
    public int value;
    
    public class Baker : Baker<HungerInteractionAuthoring> {

        public override void Bake(HungerInteractionAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new HungerInteraction
            {
                value = authoring.value
            });
        }
    }
}

