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

public struct EquipRequest : IComponentData, IEnableableComponent { }

// Enabled by PlayerPickupSystem after EquipRequest. ItemAttachSystem parents the item
// to AttachedTo.socket and resets its local transform, then disables this.
public struct AttachRequest : IComponentData, IEnableableComponent { }

// Applied when a throw is initiated. ThrownItemSystem moves the item by velocity
// each frame and applies gravity until it lands.
public struct ThrownItem : IComponentData, IEnableableComponent
{
    public float3 velocity;
    public float throwSpeed;    // set per-item in ItemAuthoring
    public float throwArc;      // set per-item in ItemAuthoring
    public int throwDamage;     // set per-item in ItemAuthoring
    public Entity thrower;      // set at throw time — excluded from hit detection
}

// Stored on the item root. Points to the child entity whose transform is the
// grip/align point — i.e. the empty child created with ItemAttachPointAuthoring.
// A future positioning system uses this to snap the item to the socket.
public struct ItemGripPoint : IComponentData
{
    public Entity entity;
}