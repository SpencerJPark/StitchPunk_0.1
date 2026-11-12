using Unity.Entities;
using UnityEngine;

public class PlayerEquipmentAuthoring : MonoBehaviour
{
    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<PlayerEquipmentAuthoring>
    {
        public override void Bake(PlayerEquipmentAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new OnPlayerReviverEquip());
            SetComponentEnabled<OnPlayerReviverEquip>(entity, false);
        }
    }
}
