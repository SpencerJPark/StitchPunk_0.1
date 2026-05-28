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
        }
    }
}
