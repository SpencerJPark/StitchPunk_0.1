using Unity.Entities;
using Unity.Mathematics;

public struct Player : IComponentData
{
    public Entity interactableEntity;
}

public struct PlayerSettings : IComponentData
{
    
}

// Controls
public struct PlayerActionMap : IComponentData
{
    public ActionMaps activeActionMap;
}

public struct MovePlayerInput : IComponentData, IEnableableComponent
{
    public float2 moveInput;
}

public struct LookPlayerInput : IComponentData, IEnableableComponent
{
    public float2 lookInput;
}

public struct CursorPlayerInput : IComponentData, IEnableableComponent
{
    public float2 cursorInput;
}

public struct ZoomPlayerInput : IComponentData, IEnableableComponent
{
    public float zoomInput;
}

public struct OnAttackPlayerInput: IComponentData, IEnableableComponent {}

public struct OnInteractPlayerInput: IComponentData, IEnableableComponent {}

public struct OnRollPlayerInput : IComponentData, IEnableableComponent
{
    public float rollTime;
}

public struct OnSneakPlayerInput: IComponentData, IEnableableComponent {}

public struct OnEquipmentSlotPlayerInput : IComponentData, IEnableableComponent
{
    public int slot;
}
public struct PlayerEquipmentSlots : IComponentData
{
    public ItemType itemSlot1;
    public ItemType itemSlot2;
    public ItemType itemSlot3;
    public ItemType itemSlot4;
}

// Left bumper — drop the currently equipped item.
public struct OnDropPlayerInput : IComponentData, IEnableableComponent { }

// Right trigger axis — held while aiming. When enabled + OnAttackPlayerInput fires, throws instead of attacks.
public struct AimPlayerInput : IComponentData, IEnableableComponent
{
    public float aimValue; // 0–1 trigger axis
}

// Current XZ direction the player is aiming (normalized). Updated each frame by PlayerAimSystem.
public struct AimDirection : IComponentData
{
    public float3 direction;
}

// Points to the child entity used as the aim arrow visual.
public struct AimIndicatorRef : IComponentData
{
    public Entity visualEntity;
}