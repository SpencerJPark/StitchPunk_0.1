using Unity.Entities;
using Unity.Mathematics;

public struct Brain : IComponentData
{
    public BrainType activeBrain;
}

public struct ActiveBrain : IComponentData, IEnableableComponent { }

public struct SwapBrainRequest : IComponentData, IEnableableComponent
{
    public BrainType newBrain;
}

// Motivations will be scored in a system group, 
public struct Motivation : IBufferElementData
{
    public MotivationType motivationType;
    public int value;
}

public struct Behaviour : IBufferElementData // equipped items add to buffer also
{
    public BehaviourType  behaviourType;
    public MotivationType targetMotivation; // which motivation this behaviour satisfies
    public ActionType     actionType;
    public int            value;            // satisfaction multiplier (0–100)
    public AttackType     attackType;
    public FactionType    hostileFaction;   // for Chase: which faction to pursue
    public float          range;            // for MeleeAttack: attack range in world units
}

public struct Awareness : IComponentData
{
    public float range;
}

public struct NeedsAction : IComponentData, IEnableableComponent { }

public struct ActionOption : IBufferElementData
{
    public float score;
    public ActionCategory category;
    public Entity targetEntity;
    public float3 targetPosition;
}

public struct SelectedAction : IComponentData
{
    public ActionCategory category;
    public Entity targetEntity;       // interaction waypoint, enemy, flee-from source
    public float3 targetPosition;     // destination to navigate toward
}


public struct PlayerControlled : IComponentData, IEnableableComponent { }

public struct PlayerOrder : IComponentData
{
    public float3 destination;
    public Entity targetEntity;
    public CommandType commandType;
}

public struct HungerRegistry : IBufferElementData
{
    public Entity motivationEntity;
}
