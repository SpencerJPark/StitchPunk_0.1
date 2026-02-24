using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Brain Components
public struct IsBrain : IComponentData { }

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

public struct Hurt : IBufferElementData
{
    public Entity attackerEntity;
    public float distance;
}


// Body Components
public struct HasBrain : IComponentData { }

public struct BrainLink : IComponentData
{
    public Entity brain;
}

public struct UnitAction : IComponentData
{
    public ActionType current;    
}