using Unity.Entities;
using UnityEngine;

// Place on an empty child GameObject of the item root.
// This entity's transform is the grip/align point — where the item connects to the socket.
// Reference this child in ItemAuthoring.gripPoint on the item root so the system can find it.
public class ItemAttachPointAuthoring : MonoBehaviour
{
    public class Baker : Baker<ItemAttachPointAuthoring>
    {
        public override void Bake(ItemAttachPointAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new ItemAttachPoint());
        }
    }
}

public struct ItemAttachPoint : IComponentData { }
