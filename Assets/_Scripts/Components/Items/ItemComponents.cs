using Unity.Entities;
using Unity.Mathematics;

public struct UnitEquipt : IComponentData // goes on parent entity
{
    public Entity equiptItemEntity;
}
public struct EquiptSocket : IComponentData // goes on Socket
{
    public Entity attachedItem;
}


public struct Item : IComponentData { }
public struct EquiptBy : IComponentData // goes on item
{
    public Entity owner;
}
public struct AttachedTo : IComponentData // goes on item
{
    public Entity socket;
}