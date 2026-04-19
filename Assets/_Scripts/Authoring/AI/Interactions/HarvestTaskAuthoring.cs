using Unity.Entities;
using UnityEngine;

public class HarvestTaskAuthoring : MonoBehaviour
{
    public class Baker : Baker<HarvestTaskAuthoring>
    {
        public override void Bake(HarvestTaskAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new HarvestTask());
            SetComponentEnabled<HarvestTask>(entity, false);
        }
    }
}