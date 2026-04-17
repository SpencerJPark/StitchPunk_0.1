using Unity.Entities;
using UnityEngine;

public class SocialInteractionAuthoring : MonoBehaviour {
    
    public int value;
    
    public class Baker : Baker<SocialInteractionAuthoring> {

        public override void Bake(SocialInteractionAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new SocialInteraction { value = authoring.value });
            AddComponent(entity, new InteractionValue { multiplier = authoring.value * 0.01f + 1f });
        }
    }
}

