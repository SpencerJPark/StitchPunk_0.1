using Unity.Entities;
using UnityEngine;

public class DrinkTaskAuthoring : MonoBehaviour
{
    public class Baker : Baker<DrinkTaskAuthoring>
    {
        public override void Bake(DrinkTaskAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new DrinkInteraction());
            SetComponentEnabled<DrinkInteraction>(entity, false);
        }
    }
}