using Unity.Entities;
using UnityEngine;

public class ComfortInteractionAuthoring : MonoBehaviour
{
    public int value;
    
    public class Baker : Baker<ComfortInteractionAuthoring> {

        public override void Bake(ComfortInteractionAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new ComfortInteraction
            {
                value = authoring.value,
            });
        }
    }
}

public struct ComfortInteraction : IComponentData
{
    public int value;
}