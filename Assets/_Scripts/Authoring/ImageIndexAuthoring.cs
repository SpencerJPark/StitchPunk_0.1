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
                index = authoring.defaultIndex
            });
            AddComponent(entity, new ImageIndexOverride
            {
                value = authoring.defaultIndex
            });
        }
    }
}

public struct ImageIndex : IComponentData
{
    public bool onUpdate;
    public int index;
}

[MaterialProperty("_ImageIndex")]
public struct ImageIndexOverride : IComponentData
{
    public float value;
}