using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

public struct AttackRequest : IComponentData, IEnableableComponent
{
    public Entity     targetEntity;
    public AttackType attackType;
    public bool       hitFired;
}
public struct AvailableAttack : IBufferElementData
{
    public ActionType actionType;
    public AttackType attackType;
}
