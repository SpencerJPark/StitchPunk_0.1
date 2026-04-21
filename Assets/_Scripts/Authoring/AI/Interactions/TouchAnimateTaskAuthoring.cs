using Unity.Entities;
using UnityEngine;

public class TouchAnimateTaskAuthoring : MonoBehaviour
{
    public class Baker : Baker<TouchAnimateTaskAuthoring>
    {
        public override void Bake(TouchAnimateTaskAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new TouchInteraction());
            SetComponentEnabled<TouchInteraction>(entity, false);
        }
    }
}