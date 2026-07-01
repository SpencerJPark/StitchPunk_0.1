using Unity.Entities;
using Unity.Mathematics;

public struct OnPlayerReviverEquip : IComponentData, IEnableableComponent
{
    public ItemType itemType;
}

public struct PlayerSelectedAttack : IComponentData
{
    public DamageSource damageSource;
}