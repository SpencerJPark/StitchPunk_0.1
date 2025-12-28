using Unity.Entities;
using Unity.Rendering;
using UnityEngine;

public class ImageIndexAuthoring : MonoBehaviour {

    public int defaultIndex;
    
    public class Baker : Baker<ImageIndexAuthoring> {
        
        public override void Bake(ImageIndexAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new ImageIndex
            {
                index = authoring.defaultIndex,
                onUpdate = true
            });
            AddComponent(entity, new ImageIndexOverride
            {
                Value = 0
            });
        }
    }
}

public struct ImageIndex : IComponentData
{
    public int index;
    public bool onUpdate;
}

[MaterialProperty("_ImageIndex")]
public struct ImageIndexOverride : IComponentData
{
    public float Value;
}

// Damage color bool propertyBlock
// tint for effects
// disolve