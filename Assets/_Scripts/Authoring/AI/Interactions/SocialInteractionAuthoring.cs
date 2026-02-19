using Unity.Entities;
using UnityEngine;

public class SocialInteractionAuthoring : MonoBehaviour {
    
    public int value;
    
    public class Baker : Baker<SocialInteractionAuthoring> {

        public override void Bake(SocialInteractionAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new SocialInteraction
            {
                value = authoring.value
            });
        }
    }
}

public struct SocialInteraction : IComponentData {
    public int value;
}