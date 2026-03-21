using Unity.Entities;
using UnityEngine;

public class ItemAttackAuthoring : MonoBehaviour
{
    public AttackType attackType;

    public class Baker : Baker<ItemAttackAuthoring>
    {
        public override void Bake(ItemAttackAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new AttackData { attackType = authoring.attackType });
        }
    }
}
