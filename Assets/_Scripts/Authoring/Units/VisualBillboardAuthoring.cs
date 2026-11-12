using Unity.Entities;
using UnityEngine;

public class VisualBillboardAuthoring : MonoBehaviour
{
    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<VisualBillboardAuthoring>
    {
        public override void Bake(VisualBillboardAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<VisualBillboard>(entity);
        }
    }
}
