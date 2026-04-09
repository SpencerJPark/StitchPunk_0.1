using Unity.Entities;
using Unity.Mathematics;

// Written by UnitSelectionManager, consumed by MinionCommandSystem.
public struct OnMinionMoveCommand : IComponentData, IEnableableComponent
{
    public float3 destination;
}

public struct OnMinionInteractCommand : IComponentData, IEnableableComponent
{
    public Entity targetEntity;
}

// Right-click on a hostile entity — minion stays in attack mode even if hit.
public struct OnMinionAttackCommand : IComponentData, IEnableableComponent
{
    public Entity targetEntity;
}

// Shift + right-click on a position — hold position and auto-attack enemies within radius.
public struct OnMinionDefendCommand : IComponentData, IEnableableComponent
{
    public float3 position;
    public float radius;
}

// F key — shadow the player entity continuously.
public struct OnMinionFollowCommand : IComponentData, IEnableableComponent { }
