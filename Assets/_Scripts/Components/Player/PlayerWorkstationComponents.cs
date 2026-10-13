using Unity.Entities;
using Unity.Mathematics;

// Player-side workstation state (CampLoop_System.md §4).
public struct WorkstationPrefabReference : IComponentData
{
    public Entity prefab;
}

// Entity.Null = no workstation deployed.
public struct PlayerWorkstationLink : IComponentData
{
    public Entity workstation;
}

// Deploy key pressed this frame; toggles the placement preview.
public struct OnDeployWorkstationInput : IComponentData, IEnableableComponent { }

// Enabled = previewing a placement.
public struct PlayerWorkstationPlacement : IComponentData, IEnableableComponent
{
    public float3 position;
    public quaternion rotation;
    public bool isValid;
}

// Enabled = mounted. PlayerInputManager watches this bit to switch the action map and camera.
public struct WorkstationMounted : IComponentData, IEnableableComponent
{
    public Entity workstation;
    public int healthWhenMounted;
}

public struct WorkstationDismountRequest : IComponentData, IEnableableComponent { }

// Singleton baked disabled by CampLoopRulesAuthoring; enabled when the player's station is destroyed.
public struct CampLoopGameOver : IComponentData, IEnableableComponent { }
