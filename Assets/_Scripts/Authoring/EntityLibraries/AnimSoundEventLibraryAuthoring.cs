using Unity.Entities;
using UnityEngine;

public class AnimSoundEventLibraryAuthoring : MonoBehaviour
{
    public AnimSoundEventMappingSO library;

    public class Baker : Baker<AnimSoundEventLibraryAuthoring>
    {
        public override void Bake(AnimSoundEventLibraryAuthoring authoring)
        {
            if (authoring.library == null) return;

            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new AnimSoundEventLibraryReference { library = authoring.library });
            AddComponent(entity, new AnimSoundEventLibrary());
        }
    }
}
