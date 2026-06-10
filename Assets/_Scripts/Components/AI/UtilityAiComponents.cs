using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Rendering;


public struct UtilityBrain : IComponentData, IEnableableComponent
{
    public UnitType unitType;
}
public struct PlayerUnitBrain : IComponentData, IEnableableComponent { }


public struct ActionRequest : IComponentData, IEnableableComponent { }
public struct ActionInterruptRequest : IComponentData, IEnableableComponent { }
public struct ActionSelectionValidationRequest: IComponentData, IEnableableComponent {}


public struct UtilityActions : IBufferElementData
{
    public Entity targetEntity;
    public ActionType actionType;
    public int priority;
    public float totalUtility;
    public int actionDefIndex;
    public bool needsValidation;
    public bool isPlayerOrdered;
}


public struct CurrentAction : IComponentData
{
    public Entity targetEntity;
    public ActionType action;
    public int priority;
}
public struct StateMachine : IComponentData
{
    public ActionType    action;
    public BehaviorType  activeBehavior;
    public Entity        targetEntity;
    public BehaviorPhase currentPhase;
    public int           CurrentCommandIndex;
    public float         CommandTimer;
    public StanceType    currentStance;    // written by Approach command; read by locomotion system
    public float         LoopTimer;        // wall time since behavior start; survives blocking commands (CommandTimer doesn't)
    public ushort        LoopIterations;   // LoopUntil jump-back count; hard cap guard

    // Preemption — WinnerSelection never clobbers a live behavior; it writes pending fields and
    // BehaviorInterruptSystem (same frame, before execution) runs interruptionCleanup then swaps.
    public int           activePriority;   // priority of the selection that started activeBehavior; player orders = int.MaxValue
    public ActionType    pendingAction;    // pendingBehavior != None marks a pending preemption
    public BehaviorType  pendingBehavior;
    public Entity        pendingTarget;
    public int           pendingPriority;
}

// Internal Context

public struct Awareness : IComponentData
{
    public float range;
}
public struct PersonalityAttributes : IBufferElementData // random per unit
{
    public PersonalityTypes personality;
    public float value;
}
public struct Motivation : IBufferElementData // Shared and Common
{
    public NeedType needType;           // drives curve + spatial hash key (Interaction mode)
    public float    value;              // current urgency [-100, 100]
    public float    decayRate;          // units per second
}
public struct Faction : IComponentData
{
    public FactionType factionType;
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
    public float  staleTimer;        // refreshed on each hit; entry is removed once it reaches 0
}
public struct RecentWaypoint : IBufferElementData
{
    public Entity entity;
}
public struct RecentInteraction : IBufferElementData
{
    public Entity entity;
    public float  cooldownEndTime;   // ElapsedTime when this cooldown expires
}


// World Context
public struct Interaction : IComponentData, IEnableableComponent
{
    public ActionType action;
}

[MaterialProperty("_IsInteractable")]
public struct InteractableVisual : IComponentData
{
    public float value; // 0 = not interactable, 1 = interactable
}
public struct FactionRegistry : IComponentData
{
    public NativeParallelMultiHashMap<byte, Entity> entities;
}
public struct GlobalContext : IComponentData
{
    public int playerWantedLevel;
    public int weather;
    public float timerOfDay;
}

public struct AngeryMobSignal : IComponentData
{
    public Entity targetEntity;
    public float radius;
}

public struct DangerSignal : IComponentData
{
    public float3 position;
    public float radius;
    public float intensity; // Decays over time via a separate system
}






public struct SocialValidationRequest : IComponentData, IEnableableComponent { } // legacy pre-migration — unused, removal candidate

// Talk invitation written by the RequestSocialResponse behavior command (initiator → target).
// SocialResponseSystem consumes it: accept = invitee's StateMachine is set to Talk targeting the
// initiator (direct when idle, pending fields when busy); decline = invite disabled, the initiator
// exits its WaitTime via the TargetNotEngagedWithSelf qualifier.
public struct SocialInvite : IComponentData, IEnableableComponent
{
    public Entity initiator;
}

public struct MotivationChangeRequest : IBufferElementData
{
    public NeedType             needType;
    public MotivationChangeType changeType;  // Add: += value (clamped 0–100); Set: = value (clamped 0–100)
    public float                value;
}
public struct SwapBrainRequest : IComponentData, IEnableableComponent
{
    public UnitType newUnit;
}












