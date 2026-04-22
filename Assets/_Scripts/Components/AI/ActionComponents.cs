using Unity.Entities;
using Unity.Mathematics;

public struct NeedsAction : IComponentData, IEnableableComponent { }

public struct CurrentAction : IComponentData
{
    public ActionType actionType;
    public MotivationType  motivationType;
    public Entity targetEntity;
}

public struct ActionTimer : IComponentData
{
    public float time;
}

public struct ActionOption : IBufferElementData
{
    public ActionType actionType;
    public MotivationType motivationType;
    public float utilityScore; // result from motivations considerations multiplied
    public bool interaction;
    public Entity targetEntity;
}

// Actions
public struct IdleAction : IComponentData, IEnableableComponent { }
public struct MoveToAction : IComponentData, IEnableableComponent { }
public struct WanderAction : IComponentData, IEnableableComponent { }
public struct InteractAction : IComponentData, IEnableableComponent { }
public struct PunchAction : IComponentData, IEnableableComponent { }
public struct FleeAction : IComponentData, IEnableableComponent { }
