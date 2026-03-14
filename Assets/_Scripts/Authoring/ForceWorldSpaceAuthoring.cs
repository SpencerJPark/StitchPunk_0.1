using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

public class ForceWorldSpaceAuthoring : MonoBehaviour
{
    public class Baker : Baker<ForceWorldSpaceAuthoring>
    {
        public override void Bake(ForceWorldSpaceAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.WorldSpace);
        }
    }
}