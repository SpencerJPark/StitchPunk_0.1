using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Rendering;

// Brain
public struct Brain : IComponentData
{
    public BrainType activeBrain;
}
public struct SwapBrainRequest : IComponentData, IEnableableComponent
{
    public BrainType newBrain;
}
public struct ActiveBrain : IComponentData, IEnableableComponent { }
public struct ActionRequest : IComponentData, IEnableableComponent { }
public struct NeedsActionSelectionValidation: IComponentData, IEnableableComponent {}
public struct CurrentAction : IComponentData
{
    public ActionType actionType;
    public AttackType attackType;
    public Entity targetEntity;
}

public struct ArrivedAtTarget : IComponentData, IEnableableComponent { }

// Context
public struct Awareness : IComponentData
{
    public float range;
}
public struct ActionTimer : IComponentData
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
    public float multiplier; // 1.0 = neutral, 1.2 = 20% boost, etc.
}
public struct ActionOption : IBufferElementData
{
    public ActionType actionType;
    public MotivationType motivationType;
    public AttackType attackType;
    public float utilityScore;
    public bool interaction;
    public Entity targetEntity;
}


public struct InteractionProvider : IComponentData, IEnableableComponent { }
public struct Interaction : IComponentData
{
    public ActionType actionType;
    public MotivationType motivationType;
    public float utilityScore;
    public int maxOccupants;
    public int occupantCount;
}
public struct Faction : IComponentData
{
    public FactionType factionType;
}
public struct FactionRegistry : IComponentData
{
    public NativeParallelMultiHashMap<byte, Entity> entities;
}
// ─── ThreatEntry ──────────────────────────────────────────────────────────────
// Buffer on any unit entity that can be attacked.
// ThreatUpdateSystem populates this from the Hurt buffer each frame before
// DamageApplicationSystem clears it. ChaseScoringSystem reads it to bias
// target selection toward attackers.
public struct ThreatEntry : IBufferElementData
{
    public Entity attackerEntity;
    public float  threatScore;       // cumulative damage received from this attacker
    public float  reactionDelay;     // counts down to 0 before fight-back activates
}
public struct CombatTarget : IComponentData
{
    public Entity     targetEntity;
}


// Actions
public struct IdleAction : IComponentData, IEnableableComponent { }
public struct MoveToAction : IComponentData, IEnableableComponent { }
public struct WanderAction : IComponentData, IEnableableComponent { }
public struct InteractAction : IComponentData, IEnableableComponent { }
public struct MeleeContinuousAction : IComponentData, IEnableableComponent { }
public struct MeleeSingleAction : IComponentData, IEnableableComponent { }
public struct FleeAction : IComponentData, IEnableableComponent { }



// Player Specific
public struct PlayerInteractable : IComponentData, IEnableableComponent { }

[MaterialProperty("_IsInteractable")]
public struct InteractableVisual : IComponentData
{
    public float value; // 0 = not interactable, 1 = interactable
}

public struct PlayerControlled : IComponentData, IEnableableComponent { }

public struct PlayerOrder : IComponentData
{
    public float3 destination;
    public Entity targetEntity;
    public CommandType commandType;
}






