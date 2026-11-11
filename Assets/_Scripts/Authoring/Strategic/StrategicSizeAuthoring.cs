using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// Put on a prefab root to make it grow in the zoomed-out view; the multiplier per category lives in the StrategicZoomProfile.
public class StrategicSizeAuthoring : MonoBehaviour
{
    public StrategicSizeCategory category = StrategicSizeCategory.Wanderer;

    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<StrategicSizeAuthoring>
    {
        public override void Bake(StrategicSizeAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new StrategicSizeTag { category = authoring.category });
            // Visual-only: PostTransformMatrix composes into LocalToWorld and its children, and never touches physics or LocalTransform.
            AddComponent(entity, new PostTransformMatrix { Value = float4x4.identity });
        }
    }
}
