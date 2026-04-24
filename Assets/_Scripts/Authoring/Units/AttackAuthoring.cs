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
            AddComponent<Attack>(entity);
            SetComponentEnabled<Attack>(entity, false);
            AddComponent(entity, new AttackData { attackType = authoring.baseAttack });
            AddComponent(entity, new AttackCooldown { timer = 0f });
            AddComponent<CombatTarget>(entity);
        }
    }
}
