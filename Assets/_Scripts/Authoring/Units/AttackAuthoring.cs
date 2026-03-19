using Unity.Entities;
using UnityEngine;

public class AttackAuthoring : MonoBehaviour
{
    public AttackType baseAttack;

    public class Baker : Baker<AttackAuthoring>
    {
        public override void Bake(AttackAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Attack { attackType = authoring.baseAttack});
        }
    }
}
