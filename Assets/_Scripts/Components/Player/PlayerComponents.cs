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

// Minion group / horde control — lives on the player entity.
public struct PlayerMinionGroupsData : IComponentData
{
    public int unlockedGroupCount;    // 1–4; future upgrade system writes this
    public int assignmentGroupIndex; // 0 = normal selection, 1–4 = assigning to this group
}

// One slot per unlocked group. Buffer index 0 = group 1, index 1 = group 2, etc.
public struct PlayerHordeSlot : IBufferElementData
{
    public Entity hordeEntity;
}

// Fired when the player cycles the active assignment group (LB / RB).
public struct OnCycleGroupInput : IComponentData, IEnableableComponent
{
    public int delta; // +1 or -1
}

// Fired when the player presses a D-pad direction to issue a quick command to the active group.
public struct OnQuickCommandInput : IComponentData, IEnableableComponent
{
    public int commandIndex; // 1 = up, 2 = right, 3 = down, 4 = left
}

// Fired when the player presses or releases the select button in ControlUnits mode.
public struct OnSelectPlayerInput : IComponentData, IEnableableComponent
{
    public bool isReleased; // false = pressed/started, true = released/canceled
}

// Fired when the player issues a move/interact command to selected minions (right-click / right trigger).
public struct OnCommandPlayerInput : IComponentData, IEnableableComponent { }

// Fired when the player snaps the control units camera back to the player.
public struct OnSnapCameraBackInput : IComponentData, IEnableableComponent { }