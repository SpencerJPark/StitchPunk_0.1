using Unity.Entities;
using UnityEngine;

public class BrainLibraryAuthoring : MonoBehaviour
{
    public BrainLibrarySO library;

    public class Baker : Baker<BrainLibraryAuthoring>
    {
        public override void Bake(BrainLibraryAuthoring authoring)
        {
            if (authoring.library == null) return;

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new BrainLibraryReference { library = authoring.library });
            AddComponent(entity, new BrainLibrary());
        }
    }
}
