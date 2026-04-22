using Unity.Entities;
using Unity.Mathematics;

public struct Brain : IComponentData
{
    public BrainType activeBrain;
}
public struct SwapBrainRequest : IComponentData, IEnableableComponent
{
    public BrainType newBrain;
}

public struct ActiveBrain : IComponentData, IEnableableComponent { }

// Context
public struct Awareness : IComponentData
{
    public float range;
}
public struct Motivation : IBufferElementData
{
    public MotivationType motivationType;  // drives curve + spatial hash key (Interaction mode)
    public float value;                    // current urgency [-100, 100]
    public float decayRate;                // units per second
    public float contextMultiplier;        // written by pre-pass systems, reset to 1.0 by decay system
}
public struct TempMotivation : IBufferElementData
{
    public MotivationType motivationType;  // drives curve + spatial hash key (Interaction mode)
    public float value;                    // current urgency [-100, 100]
    public float decayRate;                // units per second
    public float contextMultiplier;        // written by pre-pass systems, reset to 1.0 by decay system
}
public struct InteractionProvider : IComponentData, IEnableableComponent
{
}
public struct Interaction : IComponentData
{
    public ActionType actionType;
    public MotivationType motivationType;
    public float utilityScore;
    public int maxOccupants;
    public int occupantCount;
}

public struct NeedsInteractionSelectionValidation: IComponentData, IEnableableComponent {}






public struct PlayerControlled : IComponentData, IEnableableComponent { }

public struct PlayerOrder : IComponentData
{
    public float3 destination;
    public Entity targetEntity;
    public CommandType commandType;
}


public struct Target : IComponentData, IEnableableComponent
{
    public Entity entity;
}
public struct ArrivedAtTarget : IComponentData, IEnableableComponent { }




