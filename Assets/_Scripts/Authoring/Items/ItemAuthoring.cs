using Unity.Entities;
using UnityEngine;

public class ItemAuthoring : MonoBehaviour
{
    public ItemType itemType;

    [Tooltip("Drag the child GameObject that has ItemAttachPointAuthoring here.")]
    public GameObject gripPoint;

    public class Baker : Baker<ItemAuthoring>
    {
        public override void Bake(ItemAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new Item { itemType = authoring.itemType });
            AddComponent(entity, new EquipBy());
            AddComponent(entity, new AttachedTo());
            AddComponent(entity, new EquipAction());
            SetComponentEnabled<EquipAction>(entity, false);

            AddComponent(entity, new AttachItemRequest());
            SetComponentEnabled<AttachItemRequest>(entity, false);

            AddComponent(entity, new ThrownItemRequest());
            SetComponentEnabled<ThrownItemRequest>(entity, false);

            if (authoring.gripPoint != null)
            {
                Entity gripEntity = GetEntity(authoring.gripPoint, TransformUsageFlags.Dynamic);
                AddComponent(entity, new ItemGripPoint { entity = gripEntity });
            }
        }
    }
}
