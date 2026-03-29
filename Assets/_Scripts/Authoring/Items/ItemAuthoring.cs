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
            AddComponent(entity, new EquiptBy());
            AddComponent(entity, new AttachedTo());
            AddComponent(entity, new EquipRequest());
            SetComponentEnabled<EquipRequest>(entity, false);

            AddComponent(entity, new AttachRequest());
            SetComponentEnabled<AttachRequest>(entity, false);

            AddComponent(entity, new ThrownItem());
            SetComponentEnabled<ThrownItem>(entity, false);

            if (authoring.gripPoint != null)
            {
                Entity gripEntity = GetEntity(authoring.gripPoint, TransformUsageFlags.Dynamic);
                AddComponent(entity, new ItemGripPoint { entity = gripEntity });
            }
        }
    }
}
