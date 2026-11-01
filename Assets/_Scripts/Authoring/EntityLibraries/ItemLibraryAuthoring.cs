using Unity.Entities;
using UnityEngine;

public class ItemLibraryAuthoring : MonoBehaviour
{
    public ItemLibrarySO library;

    public class Baker : Baker<ItemLibraryAuthoring>
    {
        public override void Bake(ItemLibraryAuthoring authoring)
        {
            if (authoring.library == null) return;

            DependsOn(authoring.library);

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new ItemLibraryReference { library = authoring.library });
            AddComponent(entity, new ItemLibrary());

            DynamicBuffer<ItemProjectilePrefab> projectilePrefabs = AddBuffer<ItemProjectilePrefab>(entity);
            int itemTypeCount = BlobLibraryUtils.EnumCount<ItemType>();
            for (int index = 0; index < itemTypeCount; index++)
                projectilePrefabs.Add(new ItemProjectilePrefab { prefab = Entity.Null });

            foreach (ItemSO item in authoring.library.items)
            {
                if (item == null) continue;
                DependsOn(item);
                int slot = (int)item.itemType;
                if (item.projectilePrefab == null || slot < 0 || slot >= itemTypeCount) continue;
                projectilePrefabs[slot] = new ItemProjectilePrefab
                {
                    prefab = GetEntity(item.projectilePrefab, TransformUsageFlags.Dynamic)
                };
            }
        }
    }
}
