using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Building Blocks
public struct InteractionProvider : IComponentData, IEnableableComponent
{
}

public struct Interaction : IComponentData
{
    public float interactionRange;
    public ActionType actionType;
    public int maxOccupants;
}

public struct InteractionTimer : IComponentData, IEnableableComponent
{
    public float maxTime;
    public float duration;
    public float elapsed;
}

public struct InteractionOccupant : IBufferElementData
{
    public Entity entity;
    public MotivationType motivationType;
    public float score;
}

public struct InteractionHandled : IComponentData, IEnableableComponent
{
}

// Interaction Types
public struct SocialInteraction : IComponentData {
    public int value;
}

public struct SafetyInteraction : IComponentData
{
    public int value;
}

public struct MovementInteraction : IComponentData
{
    public int value;
}

public struct HungerInteraction : IComponentData {
    public int value;
}

public struct FunInteraction : IComponentData
{
    public int value;
}

public struct ComfortInteraction : IComponentData
{
    public int value;
}

public struct EnergyInteraction : IComponentData {
    public int value;
}

public struct BladderInteraction : IComponentData
{
    public int value;
}