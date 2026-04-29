using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

public struct AttackRequest : IComponentData, IEnableableComponent
{
    public Entity     targetEntity;
    public AttackType attackType;
    public bool       hitFired;
}
public struct AttackCooldown : IComponentData
{
    public float timer;
}
public struct CombatTarget : IComponentData
{
    public Entity     targetEntity;
}
public struct AvailableAttack : IBufferElementData
{
    public AttackType attackType;
}
