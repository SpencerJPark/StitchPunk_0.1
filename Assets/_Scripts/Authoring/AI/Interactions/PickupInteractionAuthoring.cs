using Unity.Entities;
using UnityEngine;

public class PickupInteractionAuthoring : MonoBehaviour
{
    public class Baker : Baker<PickupInteractionAuthoring>
    {
        public override void Bake(PickupInteractionAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new PickupTask());
            SetComponentEnabled<PickupTask>(entity, false);
        }
    }
}