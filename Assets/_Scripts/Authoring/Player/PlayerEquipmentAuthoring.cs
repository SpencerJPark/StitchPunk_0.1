using Unity.Entities;
using UnityEngine;

public class PlayerEquipmentAuthoring : MonoBehaviour
{
    public class Baker : Baker<PlayerEquipmentAuthoring>
    {
        public override void Bake(PlayerEquipmentAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new OnPlayerReviverEquipt());
            SetComponentEnabled<OnPlayerReviverEquipt>(entity, false);
        }
    }
}
