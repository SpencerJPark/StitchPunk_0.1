using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

public struct Unit : IComponentData { }
public struct UnitData : IComponentData
{
    public UnitType unitType;
    public int unitIndex;
}
public struct UnitAction : IComponentData
{
    public ActionType current;    
}
public struct Target : IComponentData, IEnableableComponent
{
    public Entity entity;
}

// Life-state is a single enableable: Dead enabled = dead, Dead disabled = alive.
public struct Dead : IComponentData, IEnableableComponent { }
public struct Health : IComponentData, IPersist
{
    public int healthAmount;
    public int healthAmountMax;
    // Killing blow snapshot: written by DamageEventSystem, read by RagdollLaunchInitSystem.
    public float3 killSourcePosition;
    public float killRagdollForce;
    public float killLaunchForceY;
    public float killLaunchForceX;
    public float killFlailIntensity;
    public float killSpin;
    public float killRestitution;
    public DamageSource killDamageSource;
}

public struct HealRequest : IComponentData, IEnableableComponent
{
    public int healAmount;
}

public struct HealthBar : IComponentData {
    public Entity barVisualEntity;
    public Entity healthEntity;
}

[MaterialProperty("_SelectionColor")]
public struct HitVisual : IComponentData
{
    public float Value;
}

// Player attacks cannot target this entity (friendly vehicles, allied units).
public struct PlayerImmune : IComponentData, IEnableableComponent { }

public struct LocomotionStance : IComponentData
{
    public StanceType stance;
}

public struct Undead : IComponentData, IEnableableComponent { }
public struct ReviveRequest : IComponentData, IEnableableComponent { }

// Converts a LIVING unit to its zombie form, consumed by ZombifySystem. Corpses use ReviveRequest instead.
// targetUnitType None = use the authored UnitSO.becomesUnitType.
public struct ZombifyRequest : IComponentData, IEnableableComponent
{
    public UnitType targetUnitType;
    // Conversion applies when this reaches zero (0 = instant).
    public float delaySeconds;
}
public struct Minion: IComponentData, IEnableableComponent, IPersist { }
public struct Selected : IComponentData, IEnableableComponent
{
    public Entity visualEntity;
    public float showScale;
    // Written by MinionCommandSystem on a new order, read by SelectedVisualSystem for ring color.
    public CommandType commandType;
}

// Lives on the selection ring visual entity.
[MaterialProperty("_SelectionColor")]
public struct SelectionColor : IComponentData
{
    public float4 Value; // RGBA
}

// State durations are seconds (-1 = permanent); UnitStateExpirySystem disables the component at 0.
// CombatAwarenessSystem refreshes AggressiveState.duration each frame a hostile is in range.
public struct AggressiveState : IComponentData, IEnableableComponent { public float duration; }
public struct DrunkState      : IComponentData, IEnableableComponent { public float duration; }
public struct StunnedState    : IComponentData, IEnableableComponent { public float duration; }
// Short slide (e.g. shoved by the caravan). Applied by StunAndShoveSystem; added on first use.
public struct ShoveMotion     : IComponentData, IEnableableComponent { public float3 velocity; public float remainingSeconds; }
public struct PanickedState   : IComponentData, IEnableableComponent { public float duration; }
public struct BerserkState    : IComponentData, IEnableableComponent { public float duration; }

