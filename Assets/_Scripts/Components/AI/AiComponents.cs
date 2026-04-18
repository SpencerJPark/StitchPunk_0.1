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
public struct NeedsAction : IComponentData, IEnableableComponent { }

public struct Awareness : IComponentData
{
    public float range;
}

public struct ActionTimer : IComponentData
{
    public float time;
}

public struct Behaviour : IBufferElementData
{
    public BehaviourType behaviourType;  // drives curve + spatial hash key (Interaction mode)
    public float value;                    // current urgency [-100, 100]
    public float decayRate;                // units per second
    public float contextMultiplier;        // written by pre-pass systems, reset to 1.0 by decay system
    public ScoringMode scoringMode;        // Interaction (spatial query) or Action (score directly)
    public ActionCategory actionCategory;  // the ActionOption category this entry emits
}

public struct AttackFaction : IBufferElementData
{
    public FactionType faction;
}

public struct ActionOption : IBufferElementData
{
    public float         score;
    public ActionCategory category;
    public BehaviourType  behaviourType;
    public Entity        targetEntity;
    public float3        targetPosition;
}

public struct SelectedAction : IComponentData
{
    public ActionCategory category;
    public Entity targetEntity; // interaction waypoint, enemy, flee-from source
    public float3 targetPosition; // destination to navigate toward
}

public struct PlayerControlled : IComponentData, IEnableableComponent { }

public struct PlayerOrder : IComponentData
{
    public float3 destination;
    public Entity targetEntity;
    public CommandType commandType;
}

// ─── Shared Move-To-Target ────────────────────────────────────────────────────
// Enable MoveToTargetRequest to ask MoveToTargetSystem to walk this unit toward
// targetEntity. When distance < arrivalRange the system disables this component
// and enables ArrivedAtTarget. Execution systems (e.g. CombatAttackExecutionSystem)
// check ArrivedAtTarget to know when to begin their action.
public struct Target : IComponentData, IEnableableComponent
{
    public Entity entity;
}

public struct MoveToTargetRequest : IComponentData, IEnableableComponent
{
    public Entity targetEntity;
    public float  arrivalRange;
    public float3 lastKnownTargetPos;
    public PathfindingMode requestedMode;
}

public struct ArrivedAtTarget : IComponentData, IEnableableComponent { }




