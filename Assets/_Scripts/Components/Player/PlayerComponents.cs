using Unity.Entities;
using Unity.Mathematics;

public struct Player : IComponentData
{
    public Entity interactableEntity;
}

public struct PlayerData : IComponentData
{
    public float rollTime;
    public float rollTimeMax;
    public float rollSpeed;
}

public struct PlayerInputData : IComponentData
{
    public float2 moveInput;
    public float2 lookInput;
    public float2 cursorInput;
    public float zoomInput;
    public ActionMaps activeActionMap;
    
    public bool sneakToggle;
    public bool onAttackInput;
    public bool onInteractInput;
    public bool onRollInput;
}

public struct PlayerAttackInput: IComponentData, IEnableableComponent {}

public struct PlayerInteractInput: IComponentData, IEnableableComponent {}

public struct PlayerRollInput: IComponentData, IEnableableComponent {}

public struct PlayerSneakToggle: IComponentData, IEnableableComponent {}