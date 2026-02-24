using Unity.Entities;
using UnityEngine;

public class BladderInteractionAuthoring : MonoBehaviour
{
    public int value;
    
    public class Baker : Baker<BladderInteractionAuthoring> {

        public override void Bake(BladderInteractionAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new BladderInteraction
            {
                value = authoring.value,
            });
        }
    }
}

