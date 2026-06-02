using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Rendering;


public struct UtilityAction : IComponentData
{
    public Entity ownerEntity;
    public Entity targetEntity;
    public ActionType action;
    public int priority;
    public int actionDefIndex;       // index into BrainLibraryBlob.actionDefs
    public ActionContextData contextData;
    public float totalUtility;
}

// --- Utility AI v2 ---

// Gate tag. Add to unit prefabs that run the new pipeline (legacy AIBrain units are unaffected).
public struct UtilityBrainV2 : IComponentData, IEnableableComponent { }

// On unit: throttles how often the selection pipeline runs.
public struct DecideCadence : IComponentData
{
    public float interval;    // seconds between decision polls
    public float remaining;   // countdown; selection runs when <= 0
}

// On action entity: pool enable/disable toggle. Disabled = pooled/inactive; no structural change needed.
public struct ActionActive : IComponentData, IEnableableComponent { }

// On unit: sensed targets written each frame by v2 perception systems.
// BehaviorExecutionSystem reads this to pick a concrete waypoint for Self-targeting actions.
public struct AwarenessTarget : IBufferElementData
{
    public Entity targetEntity;
    public float  distanceToTarget;
    public bool   inLineOfSight;
}

public struct ConsiderationElement : IBufferElementData
{
    public ConsiderationType consideration;
    public CurveType curve;
    public float4 CurveParams; // Custom tweaking values (e.g., min, max, exponent/slope, Y-Offset)
    public float Weight;       // How important this specific consideration is (0.0 - 1.0)
    
    // The system writes the output here
    public float FinalScore;   
}

// Lives on Unit
public struct CurrentUnitAction : IComponentData
{
    public Entity targetEntity;
    public ActionType action;
    public int priority;
}

public struct UnitActionOptions : IBufferElementData
{
    public ActionType action;
}

public struct UnitMotivations : IBufferElementData
{
    public ConsiderationType consideration;           // drives curve + spatial hash key (Interaction mode)
    public float    value;              // current urgency [-100, 100]
    public float    decayRate;   
}

public struct ActionContextData
{
    // Flattened Unit Stats
    public float ownerHealthRatio;
    
    public float ownerNeedRatio;   
    
    // Target / Spatial Context
    public float distanceToTarget;
    public bool targetInAwarenessRange;
    public bool targerInLineOfSight;
    public int targetFactionRelation;  // -1 Enemy, 0 Neutral, 1 Ally
    
    // World/Signal Flags
    public float recentThreatSignalIntensity; 
    
    // Trait Multipliers (Calculated once from the trait buffer)
    public float traitModifier;        // e.g., 1.5x score modifier if Trait matches Action
}

// Global Context
public struct GlobalContext : IComponentData
{
    public int playerWantedLevel;
    public int weather;
    public float timerOfDay;
}

// World Context are singals about dynamic events spawned into the world
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

// Unit Context

// Health