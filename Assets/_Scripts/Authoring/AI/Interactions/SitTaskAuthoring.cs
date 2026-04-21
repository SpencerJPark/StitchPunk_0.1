using Unity.Entities;
using UnityEngine;

public class SitTaskAuthoring : MonoBehaviour
{
    public class Baker : Baker<SitTaskAuthoring>
    {
        public override void Bake(SitTaskAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new SitInteraction());
            SetComponentEnabled<SitInteraction>(entity, false);
        }
    }
}