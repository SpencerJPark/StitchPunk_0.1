using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

public class FlipbookAuthoring : MonoBehaviour
{
    public int baseImageIndex;
    
    public class Baker : Baker<FlipbookAuthoring>
    {
        public override void Bake(FlipbookAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic | TransformUsageFlags.NonUniformScale);
            
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