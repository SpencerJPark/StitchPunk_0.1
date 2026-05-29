using Unity.Entities;
using Unity.Mathematics;

public struct UnitEquipt : IComponentData // goes on parent entity
{
    public Entity equiptItemEntity;
    public Entity socketEntity; // the EquiptSocket child entity items attach to
}
public struct EquiptSocket : IComponentData // goes on Socket
{
    public Entity attachedItem;
}

public struct ItemGripPoint : IComponentData
{
    public Entity entity;
}


public struct Item : IComponentData
{
    public ItemType itemType;
}
public struct EquiptBy : IComponentData // goes on item
{
    public Entity owner;
}
public struct AttachedTo : IComponentData // goes on item
{
    public Entity socket;
}

public struct SpawnItemRequest : IComponentData, IEnableableComponent
{
    public ItemType itemType;
    public Entity socket;
}

public struct DespawnItemRequest : IComponentData, IEnableableComponent
{
    public Entity itemEntity;
}

public struct AttachItemRequest : IComponentData, IEnableableComponent
{
    public ItemType itemType;
    public Entity socket;
}

public struct UseItemRequest : IComponentData, IEnableableComponent { }

public struct ThrownItemRequest : IComponentData, IEnableableComponent
{
    public float3 velocity;
    public Entity thrower;        // excluded from hit detection
    public float3 throwOrigin;    // used to skip hits until item clears nearby units
}

