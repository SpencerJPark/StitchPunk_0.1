using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Brain Components
public struct IsBrain : IComponentData { }
public struct ActiveBrain : IComponentData, IEnableableComponent { }

public struct BodyLink : IComponentData
{
    public Entity body;
}

public struct Awareness : IComponentData
{
    public float range;
}

public struct SelectedAction : IComponentData
{
    public Entity current;
    public Entity previous;
}

public struct NeedsAction : IComponentData, IEnableableComponent
{
}

public struct ActionOption : IBufferElementData
{
    public Entity interactableEntity;
    public float score;
}

// When enabled, ActionSelectionSystem is bypassed — brain receives orders from MinionCommandSystem instead.
// MinionAutoCounterSystem disables this when the minion takes damage.
public struct PlayerControlled : IComponentData, IEnableableComponent { }

// Written by MinionCommandSystem while PlayerControlled is enabled.
// Drives SelectedAction / PathRequest for the brain.
public struct PlayerOrder : IComponentData
{
    public float3 destination;
    public Entity targetEntity;
    public CommandType commandType;
}


// Body Components
public struct HasBrain : IComponentData, IEnableableComponent { }

public struct BrainLink : IComponentData
{
    public Entity brain;
}

