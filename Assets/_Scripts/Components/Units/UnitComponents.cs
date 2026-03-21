using Unity.Entities;

public struct Unit : IComponentData { }
public struct UnitData : IComponentData
{
    public UnitType unitType;
}
public struct UnitStateData : IComponentData
{
    public UnitState state;
}
public struct Undead : IComponentData, IEnableableComponent { }


public struct UnitAction : IComponentData
{
    public ActionType current;    
}

public struct Alive : IComponentData, IEnableableComponent { }
public struct Dead : IComponentData, IEnableableComponent { }



public struct Hurt : IBufferElementData
{
    public Entity attackerEntity;
    public float distance;
    public int damageAmount;
}

public struct CombatTarget : IComponentData
{
    public Entity entity;
}

public struct AttackCooldown : IComponentData
{
    public float timer;
}
public struct Health : IComponentData {
    
    public int healthAmount;
    public int healthAmountMax; // SHould be able to move
}
public struct HealthBar : IComponentData {
    public Entity barVisualEntity;
    public Entity healthEntity;
}

// Prevents the player from targeting this entity with attacks.
// Use on friendly vehicles, allied units, or anything else that shouldn't take player damage.
public struct PlayerImmune : IComponentData { }

// Actions
public struct Heal : IComponentData, IEnableableComponent
{
    public int healAmount;
}


public struct Attack : IComponentData, IEnableableComponent { }
public struct AttackData : IComponentData
{
    public AttackType attackType;
}
// interact

// Player actions
public struct Selected : IComponentData, IEnableableComponent {

    public Entity visualEntity;
    public float showScale;

    public bool onSelected;
    public bool onDeselected;
}

// Design
