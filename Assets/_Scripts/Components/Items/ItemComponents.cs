using Unity.Entities;
using Unity.Mathematics;


public struct UnitEquip : IComponentData // goes on parent entity
{
    public Entity equipItemEntity;
    public Entity socketEntity; // the EquipSocket child entity items attach to
}
public struct EquipSocket : IComponentData // goes on Socket
{
    public Entity attachedItem;
}
public struct ItemGripPoint : IComponentData
{
    public Entity entity;
}


public struct Item : IComponentData
{
    public ItemType itemType; // will remove
}

public struct ItemTag : IComponentData { }
public struct ItemAvailable: IComponentData, IEnableableComponent { }

public struct Weapon : IComponentData
{
    public WeaponType weaponType;
}

public struct Hat : IComponentData
{
    // Type
}

public struct Ammo : IComponentData
{
    // Type
    public int value;
}

public struct MedKit : IComponentData
{
    public int value;
}

public struct Elixir : IComponentData
{
    
}

public struct Wood : IComponentData
{
    public int value;
}

public struct MetalScrap : IComponentData
{
    public int value;
}

public struct Coins : IComponentData
{
    public int value;
}


public struct EquipBy : IComponentData // goes on item
{
    public Entity owner;
}
public struct AttachedTo : IComponentData // goes on item
{
    public Entity socket;
}

public struct AttachItemRequest : IComponentData, IEnableableComponent
{
    public ItemType itemType;
    public Entity socket;
}

// Enabled on an item to request that it be picked up and linked to its EquipBy owner.
// Consumed by ItemEquipSystem; callers set EquipBy + AttachedTo before enabling this.
public struct PickupRequest : IComponentData, IEnableableComponent { }

public struct ThrownItemRequest : IComponentData, IEnableableComponent
{
    public float3 velocity;
    public Entity thrower;        // excluded from hit detection
    public float3 throwOrigin;    // used to skip hits until item clears nearby units
}

