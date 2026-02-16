using Unity.Entities;
using UnityEngine;

public class AIScoringLibraryAuthoring : MonoBehaviour
{
    public AIScoringLibrarySO library;

    public class Baker : Baker<AIScoringLibraryAuthoring>
    {
        public override void Bake(AIScoringLibraryAuthoring authoring)
        {
            if (authoring.library == null) return;

            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new ScoringLibraryReference
            {
                library = authoring.library
            });

            AddComponent(entity, new ScoringLibrary());
        }
    }
}

public struct ScoringLibrary : IComponentData
{
    public BlobAssetReference<AIScoringLibraryBlob> library;
}

public struct ScoringLibraryReference : IComponentData
{
    public UnityObjectRef<AIScoringLibrarySO> library;
}