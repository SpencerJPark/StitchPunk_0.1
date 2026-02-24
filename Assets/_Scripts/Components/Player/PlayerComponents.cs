using Unity.Entities;
using Unity.Mathematics;

public struct Player : IComponentData {
    
    public Entity interactableEntity; // entity player can interact with
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