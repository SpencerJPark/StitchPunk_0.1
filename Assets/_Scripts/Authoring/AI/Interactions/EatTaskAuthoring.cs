using Unity.Entities;
using UnityEngine;

public class EatTaskAuthoring : MonoBehaviour
{
    public class Baker : Baker<EatTaskAuthoring>
    {
        public override void Bake(EatTaskAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new EatTask());
            SetComponentEnabled<EatTask>(entity, false);
        }
    }
}