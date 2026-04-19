using Unity.Entities;
using UnityEngine;

public class RepairTaskAuthoring : MonoBehaviour
{
    public class Baker : Baker<RepairTaskAuthoring>
    {
        public override void Bake(RepairTaskAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new RepairTask());
            SetComponentEnabled<RepairTask>(entity, false);
        }
    }
}