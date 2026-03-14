using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

public class ImageIndexAuthoring : MonoBehaviour
{
    public int baseImageIndex;
    
    public class Baker : Baker<ImageIndexAuthoring>
    {
        public override void Bake(ImageIndexAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new ImageIndex
            {
                index = authoring.baseImageIndex,
                onUpdate = true
            });
            AddComponent(entity, new ImageIndexOverride
            {
                Value = 0
            });
        }
    }
}