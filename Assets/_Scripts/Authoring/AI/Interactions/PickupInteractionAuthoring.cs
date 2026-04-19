using Unity.Entities;
using UnityEngine;

public class FactionAuthoring : MonoBehaviour
{

    public class Baker : Baker<FactionAuthoring>
    {
        public override void Bake(FactionAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new PickupTask());
            SetComponentEnabled<PickupTask>(false);
        }
    }
}