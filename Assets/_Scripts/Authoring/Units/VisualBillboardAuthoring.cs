using Unity.Entities;
using UnityEngine;

public class VisualBillboardAuthoring : MonoBehaviour
{
    public class Baker : Baker<VisualBillboardAuthoring>
    {
        public override void Bake(VisualBillboardAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<VisualBillboard>(entity);
        }
    }
}
