using Unity.Entities;
using UnityEngine;

public class AnimationLibraryAuthoring : MonoBehaviour
{
    public AnimationLibrarySO library;
    
    public class Baker : Baker<AnimationLibraryAuthoring>
    {
        public override void Bake(AnimationLibraryAuthoring authoring)
        {
            if (authoring.library == null) return;
            
            Entity entity = GetEntity(TransformUsageFlags.None);
            
            // Store reference for baking system to process
            AddComponent(entity, new AnimationLibraryReference
            {
                library = authoring.library
            });
            
            AddComponent(entity, new AnimationLibrary());
        }
    }
}

public struct AnimationLibrary : IComponentData {
    public BlobAssetReference<AnimationLibraryBlob> library;
}

public struct AnimationLibraryReference : IComponentData
{
    public UnityObjectRef<AnimationLibrarySO> library;
}