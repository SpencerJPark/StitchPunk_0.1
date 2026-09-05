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

// Health
// Life-state is a single enableable: Dead enabled = dead, Dead disabled = alive.
// (The former `Alive` component was deprecated — see MinionRevival_System.)
public struct Dead : IComponentData, IEnableableComponent { }
public struct Health : IComponentData, IPersist
{
    public int healthAmount;
    public int healthAmountMax;
    // Snapshot of the killing blow — written by DamageEventSystem on the lethal
    // DamageEvent, then read by RagdollLaunchInitSystem.
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

// Prevents the player from targeting this entity with attacks.
// Use on friendly vehicles, allied units, or anything else that shouldn't take player damage.
public struct PlayerImmune : IComponentData, IEnableableComponent { }


public struct LocomotionStance : IComponentData
{
    public StanceType stance;
}





// Player actions
public struct Undead : IComponentData, IEnableableComponent { }
public struct ReviveRequest : IComponentData, IEnableableComponent { }

// Turns a LIVING unit into its converted (zombie) form. Enabled by any trigger — debug menu,
// narrative ZombifyAction, a future bite hook — and consumed by ZombifySystem, which composes the
// brain swap and the re-skin that already exist. Corpses convert through ReviveRequest instead.
// targetUnitType None = use the unit's authored UnitSO.becomesUnitType, the same field the revive
// path converts through, so a trigger never has to know the human→zombie mapping.
public struct ZombifyRequest : IComponentData, IEnableableComponent
{
    public UnitType targetUnitType;
    // Counts down inside the request; conversion applies on the frame it reaches zero (0 = instant).
    public float delaySeconds;
}
public struct Minion: IComponentData, IEnableableComponent, IPersist { }
public struct Selected : IComponentData, IEnableableComponent
{
    public Entity visualEntity;
    public float showScale;
    // Updated by MinionCommandSystem when a new order is issued.
    // Read by SelectedVisualSystem to drive ring color.
    public CommandType commandType;

    public bool onSelected;
    public bool onDeselected;
}

// Lives on the selection ring visual entity. Drives the _SelectionColor shader property.
// Add this component (alongside LocalTransform) to the selection ring prefab quad.
[MaterialProperty("_SelectionColor")]
public struct SelectionColor : IComponentData
{
    public float4 Value; // RGBA
}

// ─── Behavioral State Components ──────────────────────────────────────────────
// Each state is an IEnableableComponent with a duration (seconds; -1 = permanent).
// UnitStateExpirySystem ticks duration and disables the component when it hits 0.
// CombatAwarenessSystem refreshes AggressiveState.duration each frame a hostile is in range.

public struct AggressiveState : IComponentData, IEnableableComponent { public float duration; }
public struct DrunkState      : IComponentData, IEnableableComponent { public float duration; }
public struct StunnedState    : IComponentData, IEnableableComponent { public float duration; }
public struct PanickedState   : IComponentData, IEnableableComponent { public float duration; }
public struct BerserkState    : IComponentData, IEnableableComponent { public float duration; }

