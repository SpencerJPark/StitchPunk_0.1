using Unity.Entities;
using UnityEngine;

public class SafetyInteractionAuthoring : MonoBehaviour
{
    public int value;
    
    public class Baker : Baker<SafetyInteractionAuthoring> {

        public override void Bake(SafetyInteractionAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new SafetyInteraction
            {
                value = authoring.value,
            });
        }
    }
}

public struct SafetyInteraction : IComponentData
{
    public int value;
}