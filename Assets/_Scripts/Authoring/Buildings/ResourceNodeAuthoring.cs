using Unity.Entities;
using UnityEngine;

// Trees / wreckage the player's minions gather (CampLoop Phase 4).
public class ResourceNodeAuthoring : MonoBehaviour
{
    [SearchableEnum] public ResourceType resourceType = ResourceType.Wood;
    public int amount = 10;
    public float harvestSeconds = 3f;
    public int loadPerTrip = 1;

    public class Baker : Baker<ResourceNodeAuthoring>
    {
        public override void Bake(ResourceNodeAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new ResourceNode
            {
                resourceType = authoring.resourceType,
                remaining = authoring.amount,
                harvestSeconds = authoring.harvestSeconds,
                loadPerTrip = authoring.loadPerTrip
            });
        }
    }
}
