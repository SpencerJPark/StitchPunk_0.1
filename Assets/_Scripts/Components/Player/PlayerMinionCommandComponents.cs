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
