using Unity.Entities;

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
    public float distance;
    public int damageAmount;
}
public struct Health : IComponentData {
    
    public int healthAmount;
    public int healthAmountMax; // SHould be able to move
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

// Actions



public struct Attack : IComponentData, IEnableableComponent { }
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

    public bool onSelected;
    public bool onDeselected;
}
