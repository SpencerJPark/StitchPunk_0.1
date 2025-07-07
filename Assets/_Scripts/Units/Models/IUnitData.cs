using UnityEngine;

public interface IUnitData
{
    string UnitName { get; }
    int MaxHealth { get; }
    int AttackDamage { get; }
    MovementType Movement { get; }
    float MoveSpeed { get; }
    float Gravity { get; }
    float MaxFallSpeed { get; }
    float GroundCheckDistance { get; }
    LayerMask GroundLayer { get; }
    float GravityMultiplier { get; }
}
