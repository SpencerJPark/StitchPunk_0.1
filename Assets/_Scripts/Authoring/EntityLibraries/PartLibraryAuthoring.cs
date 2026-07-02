using Unity.Entities;
using UnityEngine;

// Scene GameObject holding the single _PartLibrary.asset. Bakes the reference + empty holder that
// PartLibraryBakingSystem (PostBakingSystemGroup) fills with the baked blob. Mirrors ItemLibraryAuthoring.
public class PartLibraryAuthoring : MonoBehaviour
{
    public PartLibrarySO library;

    public class Baker : Baker<PartLibraryAuthoring>
    {
        public override void Bake(PartLibraryAuthoring authoring)
        {
            if (authoring.library == null) return;

            DependsOn(authoring.library);

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new PartLibraryReference { library = authoring.library });
            AddComponent(entity, new PartLibrary());
        }
    }
}
