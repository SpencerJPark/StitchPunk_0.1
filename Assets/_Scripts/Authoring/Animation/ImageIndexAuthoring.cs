using Unity.Entities;
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

