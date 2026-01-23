using Unity.Entities;
using UnityEngine;

public class ResourceLibaryAuthoring : MonoBehaviour
{
    public ResourceTypeSO.ResourceType resourceType;

    public class Baker : Baker<ResourceLibaryAuthoring>
    {
        public override void Bake(ResourceLibaryAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new ResourceTypeSOHolder
            {
                resourceType = authoring.resourceType,
            });
        } 
    }
}

public struct ResourceTypeSOHolder : IComponentData
{
    public ResourceTypeSO.ResourceType resourceType;
}
