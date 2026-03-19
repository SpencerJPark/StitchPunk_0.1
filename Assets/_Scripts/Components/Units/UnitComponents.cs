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
public struct UnitAction : IComponentData
{
    public ActionType current;    
}



public struct Alive : IComponentData, IEnableableComponent { }
public struct Undead : IComponentData, IEnableableComponent { }
public struct Hurt : IBufferElementData
{
    public Entity attackerEntity;
    public float distance;
}
public struct Health : IComponentData {
    
    public int healthAmount;
    public int healthAmountMax; // SHould be able to move
}
public struct HealthBar : IComponentData {
    public Entity barVisualEntity;
    public Entity healthEntity;
}
public struct Attack : IComponentData
{
    public AttackType attackType;
}


public struct Selected : IComponentData, IEnableableComponent {

    public Entity visualEntity;
    public float showScale;

    public bool onSelected;
    public bool onDeselected;
}

