using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

public struct AttackRequest : IComponentData, IEnableableComponent
{
    public Entity targetEntity;
    public AttackType attackType;
    public float  hitTime;
    public bool   hitFired;
}
public struct AttackCooldown : IComponentData
{
    public float timer;
}
public struct AttackFaction : IBufferElementData
{
    public FactionType faction;
}

public struct CurrentAttack : IComponentData
{
    public AttackType attackType;
}
public struct AvailableAttack : IBufferElementData
{
    public AttackType attackType;
}
[MaterialProperty("_SelectionColor")]
public struct HitVisual : IComponentData
{
    public float Value;
}