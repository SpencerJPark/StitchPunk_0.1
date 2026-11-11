using Unity.Entities;
using UnityEngine;

public class SoundLibraryAuthoring : MonoBehaviour
{
    public SoundLibrarySO library;

    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<SoundLibraryAuthoring>
    {
        public override void Bake(SoundLibraryAuthoring authoring)
        {
            if (authoring.library == null) return;

            DependsOn(authoring.library);

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new SoundLibraryReference { library = authoring.library });
            AddComponent(entity, new SoundLibrary());
        }
    }
}
