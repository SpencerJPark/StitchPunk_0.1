using Unity.Entities;
using UnityEngine;

public class InteractionLibraryAuthoring : MonoBehaviour
{
    public InteractionLibrarySO library;

    public class Baker : Baker<InteractionLibraryAuthoring>
    {
        public override void Bake(InteractionLibraryAuthoring authoring)
        {
            if (authoring.library == null) return;

            DependsOn(authoring.library);

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new InteractionLibraryReference { library = authoring.library });
            AddComponent(entity, new InteractionLibrary());
        }
    }
}
