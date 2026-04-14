using Unity.Entities;
using UnityEngine;

public class FactoryLibraryAuthoring : MonoBehaviour
{
    public FactoryLibrarySO library;

    public class Baker : Baker<FactoryLibraryAuthoring>
    {
        public override void Bake(FactoryLibraryAuthoring authoring)
        {
            if (authoring.library == null) return;

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new FactoryLibraryReference { library = authoring.library });
            AddComponent(entity, new FactoryLibrary());
        }
    }
}
