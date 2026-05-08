using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Rendering;

public struct CurrentAction : IComponentData
{
    public ActionType actionType;
    public Entity     targetEntity;
}

// Selection Methods
public struct AIBrain : IComponentData, IEnableableComponent { }
public struct PlayerControlled : IComponentData, IEnableableComponent { }

// Player Context
public struct PlayerOrder : IComponentData
{
    public float3 destination;
    public Entity targetEntity;
    public CommandType commandType;
}

// Awareness Context
public struct Awareness : IComponentData
{
    public float range;
}
public struct ActionTimer : IComponentData, IEnableableComponent
{
    public float time;
}
public struct Motivation : IBufferElementData
{
    public MotivationType motivationType;  // drives curve + spatial hash key (Interaction mode)
    public float value;                    // current urgency [-100, 100]
    public float decayRate;                // units per second
    public float contextMultiplier;        // written by pre-pass systems, reset to 1.0 by decay system
}
public struct MotivationSatisfaction : IBufferElementData
{
    public MotivationType motivationType;
    public float restorationAmount; // flat units of motivation restored on action completion
}


public struct ActionOption : IBufferElementData
{
    public ActionType actionType;
    public MotivationType motivationType;
    public int priority;
    public float utilityScore;
    public bool interaction;
    public Entity targetEntity;
}

public struct Faction : IComponentData
{
    public FactionType factionType;
}
public struct FactionRegistry : IComponentData
{
    public NativeParallelMultiHashMap<byte, Entity> entities;
}
public struct AttackFaction : IBufferElementData
{
    public FactionType faction;
}
public struct ThreatEntry : IBufferElementData
{
    public Entity attackerEntity;
    public float  threatScore;       // cumulative damage received from this attacker
    public float  reactionDelay;     // counts down to 0 before fight-back activates
}



// Actions
public struct IdleAction : IComponentData, IEnableableComponent { }
public struct MoveToAction : IComponentData, IEnableableComponent { }
public struct WanderAction : IComponentData, IEnableableComponent { }
public struct InteractAction : IComponentData, IEnableableComponent { }
public struct SitAction : IComponentData, IEnableableComponent { }
public struct MeleeContinuousAction : IComponentData, IEnableableComponent { }
public struct MeleeSingleAction : IComponentData, IEnableableComponent { }
public struct FleeAction : IComponentData, IEnableableComponent { }
public struct EquipAction : IComponentData, IEnableableComponent { }


// Requests
public struct ActionRequest : IComponentData, IEnableableComponent { }
public struct ActionSelectionValidationRequest: IComponentData, IEnableableComponent {}
public struct ActionInterruptRequest : IComponentData, IEnableableComponent { }
public struct MotivationChangeRequest : IBufferElementData
{
    public MotivationType      motivationType;
    public MotivationChangeType changeType;    // Add: += value (clamped 0–100); Set: = value (clamped 0–100)
    public float               value;
}
public struct SwapBrainRequest : IComponentData, IEnableableComponent
{
    public UnitType newUnit;
}








