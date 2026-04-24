using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

public struct Unit : IComponentData { }
public struct UnitData : IComponentData
{
    public UnitType unitType;
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
public struct Alive : IComponentData, IEnableableComponent { }
public struct Dead : IComponentData, IEnableableComponent { }
public struct Hurt : IBufferElementData
{
    public Entity attackerEntity;
    public AttackType attackType;
    public float distance;
    public int damageAmount;
    // World-X of the hit source — used by ragdoll init for fall direction.
    // Works even when attackerEntity is Null (thrown items).
    public float hitSourceX;
    // Scales ragdoll violence (joint flail speed, body fall speed). 1 = baseline.
    public float ragdollForce;
    // Per-attack launch arc forces. 0 = no arc (character just tips over).
    public float launchForceY;
    public float launchForceX;
}
public struct Health : IComponentData
{
    public int healthAmount;
    public int healthAmountMax;
    // Snapshot of the killing blow — written by DamageApplicationJob before
    // the Hurt buffer is cleared, then read by Ragdoll2DInitSystem.
    public float killSourceX;
    public float killRagdollForce;
    public float killLaunchForceY;
    public float killLaunchForceX;
    public AttackType killAttackType;
}
public struct Heal : IComponentData, IEnableableComponent
{
    public int healAmount;
}
public struct HealthBar : IComponentData {
    public Entity barVisualEntity;
    public Entity healthEntity;
}

// Prevents the player from targeting this entity with attacks.
// Use on friendly vehicles, allied units, or anything else that shouldn't take player damage.
public struct PlayerImmune : IComponentData, IEnableableComponent { }




public struct Attack : IComponentData, IEnableableComponent { }
public struct AttackFaction : IBufferElementData
{
    public FactionType faction;
}
public struct AttackData : IComponentData
{
    public AttackType attackType;
}
public struct AttackCooldown : IComponentData
{
    public float timer;
}



// Player actions
public struct Undead : IComponentData, IEnableableComponent { }
public struct Revive : IComponentData, IEnableableComponent { }
public struct Minion: IComponentData, IEnableableComponent { }
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

