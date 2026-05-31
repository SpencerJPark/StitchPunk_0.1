using Unity.Entities;
using UnityEngine;

public class AIScoringLibraryAuthoring : MonoBehaviour
{
    public ConsiderationLibrarySO library;

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

