using Unity.Entities;
using UnityEngine;

public class WanderAreaTaskAuthoring : MonoBehaviour
{
    public class Baker : Baker<WanderAreaTaskAuthoring>
    {
        public override void Bake(WanderAreaTaskAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new WanderInteraction());
            SetComponentEnabled<WanderInteraction>(entity, false);
        }
    }
}