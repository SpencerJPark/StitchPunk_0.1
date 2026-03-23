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




// Body Components
public struct HasBrain : IComponentData, IEnableableComponent { }

public struct BrainLink : IComponentData
{
    public Entity brain;
}

