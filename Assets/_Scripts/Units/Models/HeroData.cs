using UnityEngine;

public class HeroData : MonoBehaviour, IUnitData
{
    private readonly UnitBaseData _so;
    private readonly MovementProfile _mp;
    public HeroData(UnitBaseData so, MovementProfile mp)
    {
        _so = so;
        _mp = mp;
    }

// Immutable Universal Data from Scriptable Object
    public string UnitName => _so.UnitName;
    public int MaxHealth => _so.MaxHealth;
    public int AttackDamage => _so.AttackDamage;
    public MovementType Movement => _mp.movementType;
    public float MoveSpeed => _mp.MoveSpeed;
    public float Gravity => _mp.gravity;
    public float MaxFallSpeed => _mp.maxFallSpeed;
    public float GroundCheckDistance => _mp.groundCheckDistance;
    public LayerMask GroundLayer => _mp.groundLayer;
    public float GravityMultiplier => _mp.gravityMultiplier;

// Mutable Data
    

}