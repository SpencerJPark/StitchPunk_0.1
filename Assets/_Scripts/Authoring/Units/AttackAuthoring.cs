using Unity.Entities;
using UnityEngine;

public class AttackAuthoring : MonoBehaviour
{
    [BakingVersion("stitchpunk", 1)]
    public class Baker : Baker<AttackAuthoring>
    {
        public override void Bake(AttackAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<AttackRequest>(entity);
            SetComponentEnabled<AttackRequest>(entity, false);
        }
    }
}
